using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip.Compression;
using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Loaders
{
    /// <summary>Decodes a buffered HTTP document without accepting an unfinished compression stream.</summary>
    internal static class UrlDocumentContent
    {
        private const int BufferSize = 81920;

        internal static async Task<string> ReadAsync(HttpContent content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? coding = null;
            foreach (var value in content.Headers.ContentEncoding)
            {
                if (coding != null)
                    throw new InvalidDataException("The URL response uses nested Content-Encoding values, which are not supported.");
                coding = value;
            }

            if (coding == null || string.Equals(coding, "identity", StringComparison.OrdinalIgnoreCase))
            {
                var text = await content.ReadAsStringAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return text;
            }

            var normalized = coding.ToLowerInvariant();
            if (normalized != "gzip" && normalized != "deflate" && normalized != "br")
                throw new InvalidDataException("The URL response uses an unsupported Content-Encoding.");

            // The caller has already buffered the HTTP body with the cancellation token.
            var encoded = await content.ReadAsByteArrayAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded.Length == 0)
                throw new InvalidDataException("The URL response declares compression but contains no compressed stream.");

            byte[] decoded;
            try
            {
                decoded = normalized == "gzip" ? DecodeGzip(encoded, cancellationToken)
                    : normalized == "deflate" ? DecodeDeflate(encoded, cancellationToken)
                    : DecodeBrotli(encoded, cancellationToken);
            }
            catch (SharpZipBaseException ex)
            {
                throw new InvalidDataException("The URL response contains invalid compressed content.", ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var decodedContent = new ByteArrayContent(decoded);
            // Reuse HttpContent's charset/BOM rules after decompression, rather than assuming UTF-8.
            decodedContent.Headers.ContentType = content.Headers.ContentType;
            var result = await decodedContent.ReadAsStringAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static byte[] DecodeGzip(byte[] encoded, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            var buffer = new byte[BufferSize];
            int offset = 0;
            // GZipInputStream deliberately tolerates a broken header after a complete member.
            // Validate RFC 1952 framing for every member; delegate actual DEFLATE to the inflater.
            while (offset < encoded.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int headerStart = offset;
                RequireBytes(encoded, offset, 10);
                if (encoded[offset] != 0x1f || encoded[offset + 1] != 0x8b || encoded[offset + 2] != 8)
                    throw new InvalidDataException("The URL response contains an invalid gzip member header.");
                byte flags = encoded[offset + 3];
                if ((flags & 0xe0) != 0)
                    throw new InvalidDataException("The URL response contains reserved gzip header flags.");
                offset += 10;

                if ((flags & 4) != 0) // FEXTRA
                {
                    RequireBytes(encoded, offset, 2);
                    int length = encoded[offset] | (encoded[offset + 1] << 8);
                    offset += 2;
                    RequireBytes(encoded, offset, length);
                    offset += length;
                }
                if ((flags & 8) != 0) SkipGzipString(encoded, ref offset, cancellationToken); // FNAME
                if ((flags & 16) != 0) SkipGzipString(encoded, ref offset, cancellationToken); // FCOMMENT
                if ((flags & 2) != 0) // FHCRC: low 16 bits of header CRC32, little endian.
                {
                    RequireBytes(encoded, offset, 2);
                    var headerCrc = new Crc32();
                    for (int position = headerStart; position < offset;)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = Math.Min(BufferSize, offset - position);
                        headerCrc.Update(new ArraySegment<byte>(encoded, position, count));
                        position += count;
                    }
                    int expected = encoded[offset] | (encoded[offset + 1] << 8);
                    if ((headerCrc.Value & 0xffff) != expected)
                        throw new InvalidDataException("The URL response contains an invalid gzip header checksum.");
                    offset += 2;
                }

                var inflater = new Inflater(noHeader: true);
                inflater.SetInput(encoded, offset, encoded.Length - offset);
                var crc = new Crc32();
                while (!inflater.IsFinished)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = inflater.Inflate(buffer);
                    crc.Update(new ArraySegment<byte>(buffer, 0, count));
                    WriteDecoded(output, buffer, count);
                    if (count == 0 && !inflater.IsFinished)
                        throw new InvalidDataException("The URL response contains an incomplete gzip member.");
                }
                offset = encoded.Length - inflater.RemainingInput;
                RequireBytes(encoded, offset, 8);
                if (ReadUInt32(encoded, offset) != (uint)crc.Value ||
                    ReadUInt32(encoded, offset + 4) != unchecked((uint)inflater.TotalOut))
                    throw new InvalidDataException("The URL response contains an invalid gzip checksum or size.");
                offset += 8;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return output.ToArray();
        }

        private static void SkipGzipString(byte[] encoded, ref int offset, CancellationToken cancellationToken)
        {
            while (offset < encoded.Length)
            {
                if ((offset & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (encoded[offset++] == 0) return;
            }
            throw new InvalidDataException("The URL response contains an incomplete gzip header string.");
        }

        private static void RequireBytes(byte[] encoded, int offset, int count)
        {
            if (count > encoded.Length - offset)
                throw new InvalidDataException("The URL response contains an incomplete gzip header or footer.");
        }

        private static uint ReadUInt32(byte[] bytes, int offset) =>
            (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));

        private static void WriteDecoded(MemoryStream output, byte[] bytes, int count)
        {
            // Match the buffered HTTP reader's maximum representable document size.
            if (output.Length > int.MaxValue - count)
                throw new InvalidDataException("The decompressed URL document exceeds the supported buffered size.");
            output.Write(bytes, 0, count);
        }

        private static byte[] DecodeDeflate(byte[] encoded, CancellationToken cancellationToken)
        {
            // HTTP "deflate" includes the RFC 1950 zlib wrapper and its Adler-32 checksum.
            var inflater = new Inflater(noHeader: false);
            inflater.SetInput(encoded);
            using var output = new MemoryStream();
            var buffer = new byte[BufferSize];
            while (!inflater.IsFinished)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = inflater.Inflate(buffer);
                WriteDecoded(output, buffer, count);
                if (count == 0 && !inflater.IsFinished)
                    throw new InvalidDataException("The URL response contains an incomplete or unsupported deflate stream.");
            }
            if (inflater.RemainingInput != 0)
                throw new InvalidDataException("The URL response contains trailing bytes after its deflate stream.");
            cancellationToken.ThrowIfCancellationRequested();
            return output.ToArray();
        }

        private static byte[] DecodeBrotli(byte[] encoded, CancellationToken cancellationToken)
        {
            var decoder = new BrotliDecoder();
            try
            {
                using var output = new MemoryStream();
                var buffer = new byte[BufferSize];
                int offset = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = decoder.Decompress(encoded.AsSpan(offset), buffer, out int consumed, out int written);
                    offset += consumed;
                    WriteDecoded(output, buffer, written);
                    if (status == OperationStatus.Done)
                    {
                        if (offset != encoded.Length)
                            throw new InvalidDataException("The URL response contains trailing bytes after its Brotli stream.");
                        cancellationToken.ThrowIfCancellationRequested();
                        return output.ToArray();
                    }
                    if (status != OperationStatus.DestinationTooSmall || (consumed == 0 && written == 0))
                        throw new InvalidDataException("The URL response contains an invalid or incomplete Brotli stream.");
                }
            }
            finally
            {
                decoder.Dispose();
            }
        }
    }
}
