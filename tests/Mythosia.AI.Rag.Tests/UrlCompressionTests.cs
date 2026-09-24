using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class UrlCompressionTests
{
    private const string Policy = "서울 환불 정책: 30일 이내 반품 가능합니다. 😀 NEW_POLICY";

    [TestMethod]
    [DataRow("gzip", "utf-8", false)]
    [DataRow("deflate", "utf-8", false)]
    [DataRow("br", "utf-8", false)]
    [DataRow("gzip", "utf-16", false)]
    [DataRow("deflate", "utf-16", false)]
    [DataRow("br", "utf-16", false)]
    [DataRow("gzip", "utf-8", true)]
    [DataRow("deflate", "utf-8", true)]
    [DataRow("br", "utf-8", true)]
    [DataRow("gzip", "utf-16", true)]
    [DataRow("deflate", "utf-16", true)]
    [DataRow("br", "utf-16", true)]
    public async Task CompressedResponse_DecodesBeforeCharsetAndPreservesIdentity(string coding, string charset, bool bom)
    {
        var encoding = Encoding.GetEncoding(charset);
        var content = encoding.GetBytes(Policy);
        if (bom) content = encoding.GetPreamble().Concat(content).ToArray();
        var response = new Response(Compress(content, coding), coding)
        {
            ContentType = bom ? "text/plain" : $"text/plain; charset=\"{charset}\""
        };
        await AssertSuccessful(response, Policy);
    }

    [TestMethod]
    [DataRow("gzip", false)]
    [DataRow("deflate", false)]
    [DataRow("br", false)]
    [DataRow("gzip", true)]
    [DataRow("deflate", true)]
    [DataRow("br", true)]
    public async Task ChunkedOrRedirectedCompression_DecodesEntireDocument(string coding, bool redirect)
    {
        await AssertSuccessful(new Response(Compress(Encoding.UTF8.GetBytes(Policy), coding), coding)
        {
            Chunked = true, Redirect = redirect
        }, Policy);
    }

    [TestMethod]
    [DataRow("GZIP")]
    [DataRow("DeFlAtE")]
    [DataRow("BR")]
    [DataRow("identity")]
    public async Task ContentEncoding_IsCaseInsensitiveAndIdentityIsPlainText(string coding)
    {
        var plain = Encoding.UTF8.GetBytes(Policy);
        await AssertSuccessful(new Response(coding == "identity" ? plain : Compress(plain, coding.ToLowerInvariant()), coding), Policy);
    }

    [TestMethod]
    [DataRow("gzip")]
    [DataRow("deflate")]
    [DataRow("br")]
    public async Task ValidCompressedEmptyDocument_DeletesOnlyTheSameUrl(string coding)
    {
        await AssertSuccessful(new Response(Compress([], coding), coding), "");
    }

    [TestMethod]
    public async Task ConcatenatedGzipMembers_DecodesTheWholeDocument()
    {
        var first = Compress(Encoding.UTF8.GetBytes("서울 정책: "), "gzip");
        var second = Compress(Encoding.UTF8.GetBytes(Policy), "gzip");
        await AssertSuccessful(new Response(first.Concat(second).ToArray(), "gzip"), "서울 정책: " + Policy);
    }

    [TestMethod]
    [DataRow("zstd", false)]
    [DataRow("zstd", true)]
    [DataRow("gzip, br", false)]
    [DataRow("gzip, br", true)]
    [DataRow("br, gzip", false)]
    [DataRow("br, gzip", true)]
    [DataRow("gzip, gzip", false)]
    [DataRow("gzip, gzip", true)]
    [DataRow("zstd, gzip", false)]
    [DataRow("zstd, gzip", true)]
    [DataRow("gzip, zstd", false)]
    [DataRow("gzip, zstd", true)]
    public async Task UnsupportedOrNestedEncoding_RejectsBeforeEmbeddingAndStorage(string coding, bool callback)
    {
        byte[] body = Encoding.UTF8.GetBytes(Policy);
        foreach (var layer in coding.Split(',').Select(c => c.Trim()))
            if (layer != "zstd") body = Compress(body, layer);
        await AssertRejected(new Response(body, coding), callback);
    }

    [TestMethod]
    [DataRow("gzip", false)]
    [DataRow("deflate", false)]
    [DataRow("br", false)]
    [DataRow("gzip", true)]
    [DataRow("deflate", true)]
    [DataRow("br", true)]
    public async Task InvalidCompressedData_RejectsBeforeEmbeddingAndStorage(string coding, bool callback)
    {
        await AssertRejected(new Response(Enumerable.Repeat((byte)0xff, 64).ToArray(), coding), callback);
    }

    [TestMethod]
    [DataRow("gzip")]
    [DataRow("deflate")]
    public async Task IncorrectCompressionChecksum_DoesNotReplacePreviousDocument(string coding)
    {
        var body = Compress(Encoding.UTF8.GetBytes(Policy), coding);
        body[^4] ^= 0x80;
        await AssertRejected(new Response(body, coding), callback: false);
    }

    [TestMethod]
    [DataRow("gzip")]
    [DataRow("deflate")]
    [DataRow("br")]
    public async Task InvalidCharsetInCompressedResponse_DoesNotReplacePreviousDocument(string coding)
    {
        var body = Compress(Encoding.UTF8.GetBytes(Policy), coding);
        await using var server = new DocumentServer(new Response(body, coding) { ContentType = "text/plain; charset=unknown-broken-charset" });
        using var store = new InMemoryVectorStore();
        await Seed(store, server.Url);
        var embedding = new CountingEmbedding();
        await Assert.ThrowsAsync<InvalidOperationException>(() => RagStore.BuildAsync(
            b => b.AddUrl(server.Url).UseEmbedding(embedding).UseStore(store)));
        Assert.AreEqual(0, embedding.Calls);
        await AssertOriginalIndex(store);
    }

    [TestMethod]
    [DataRow("gzip", false)]
    [DataRow("deflate", false)]
    [DataRow("br", false)]
    [DataRow("gzip", true)]
    [DataRow("deflate", true)]
    [DataRow("br", true)]
    public async Task TruncatedHttpBody_DoesNotReplacePreviousDocument(string coding, bool chunked)
    {
        var body = Compress(Encoding.UTF8.GetBytes(Policy), coding);
        await AssertRejected(new Response(body[..(body.Length / 2)], coding)
        {
            Chunked = chunked, DeclaredLength = body.Length, CompleteChunks = false
        }, callback: false);
    }

    [TestMethod]
    [DataRow("gzip", 1)]
    [DataRow("gzip", 4)]
    [DataRow("gzip", 8)]
    [DataRow("gzip", 16)]
    [DataRow("gzip", -1)]
    [DataRow("deflate", 1)]
    [DataRow("deflate", 4)]
    [DataRow("deflate", 8)]
    [DataRow("deflate", 16)]
    [DataRow("deflate", -1)]
    [DataRow("br", 1)]
    [DataRow("br", 4)]
    [DataRow("br", 8)]
    [DataRow("br", 16)]
    [DataRow("br", -1)]
    public async Task CompleteHttpWithTruncatedCompression_DoesNotReplacePreviousDocument(string coding, int removed)
    {
        var body = Compress(Encoding.UTF8.GetBytes(Policy), coding);
        int count = removed == -1 ? body.Length / 2 : removed;
        // Content-Length exactly matches the transmitted bytes. Only the compression is incomplete.
        await AssertRejected(new Response(body[..^count], coding), callback: false);
    }

    [TestMethod]
    [DataRow("gzip", false)]
    [DataRow("deflate", false)]
    [DataRow("br", false)]
    [DataRow("gzip", true)]
    [DataRow("deflate", true)]
    [DataRow("br", true)]
    public async Task EmptyRawCompressedBody_IsNotAnEmptyDocument(string coding, bool callback)
    {
        await AssertRejected(new Response([], coding), callback);
    }

    [TestMethod]
    [DataRow("gzip")]
    [DataRow("deflate")]
    [DataRow("br")]
    public async Task TrailingBytesAfterCompression_DoesNotReplacePreviousDocument(string coding)
    {
        var body = Compress(Encoding.UTF8.GetBytes(Policy), coding).Concat(new byte[] { 0xff, 0x00 }).ToArray();
        await AssertRejected(new Response(body, coding), callback: false);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)]
    [DataRow(8)] [DataRow(9)] [DataRow(10)] [DataRow(11)]
    [DataRow(12)] [DataRow(13)] [DataRow(14)] [DataRow(15)]
    [DataRow(16)] [DataRow(17)] [DataRow(18)] [DataRow(19)]
    [DataRow(20)] [DataRow(21)] [DataRow(22)] [DataRow(23)]
    [DataRow(24)] [DataRow(25)] [DataRow(26)] [DataRow(27)]
    [DataRow(28)] [DataRow(29)] [DataRow(30)] [DataRow(31)]
    public async Task GzipOptionalHeaders_AllFlagCombinationsAreDecoded(int flags)
    {
        await AssertSuccessful(new Response(GzipWithOptionalHeaders(Policy, (byte)flags), "gzip"), Policy);
    }

    [TestMethod]
    [DataRow("magic")]
    [DataRow("method")]
    [DataRow("reserved-flags")]
    [DataRow("extra-length")]
    [DataRow("name-unterminated")]
    [DataRow("comment-unterminated")]
    [DataRow("header-crc")]
    [DataRow("footer-crc")]
    [DataRow("footer-size")]
    [DataRow("second-header")]
    [DataRow("second-method")]
    [DataRow("second-body")]
    [DataRow("second-footer")]
    public async Task InvalidGzipFraming_RejectsEvenAfterACompleteMember(string fault)
    {
        var body = GzipWithOptionalHeaders(Policy, 0);
        switch (fault)
        {
            case "magic": body[0] ^= 1; break;
            case "method": body[2] = 0; break;
            case "reserved-flags": body[3] = 0x20; break;
            case "extra-length":
                body = GzipWithOptionalHeaders(Policy, 4); body[10] = body[11] = 0xff; break;
            case "name-unterminated":
            case "comment-unterminated":
                body = body[..10].Concat(Encoding.ASCII.GetBytes("no-terminator")).ToArray();
                body[3] = fault == "name-unterminated" ? (byte)8 : (byte)16;
                break;
            case "header-crc": body = GzipWithOptionalHeaders(Policy, 2); body[10] ^= 1; break;
            case "footer-crc": body[^8] ^= 1; break;
            case "footer-size": body[^4] ^= 1; break;
            case "second-header": body = body.Concat(body.Take(9)).ToArray(); break;
            case "second-method":
                var second = body.ToArray(); second[2] = 0; body = body.Concat(second).ToArray(); break;
            case "second-body": body = body.Concat(body.Take(body.Length / 2)).ToArray(); break;
            case "second-footer": body = body.Concat(body.Take(body.Length - 1)).ToArray(); break;
            default: Assert.Fail("Unknown fixture."); break;
        }
        await AssertRejected(new Response(body, "gzip"), callback: false);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task GzipEmptyMemberAlongsideContent_IsValid(bool emptyFirst)
    {
        var empty = GzipWithOptionalHeaders("", 31);
        var content = GzipWithOptionalHeaders(Policy, 31);
        var body = emptyFirst ? empty.Concat(content).ToArray() : content.Concat(empty).ToArray();
        await AssertSuccessful(new Response(body, "gzip"), Policy);
    }

    [TestMethod]
    [DataRow("gzip", false)]
    [DataRow("deflate", false)]
    [DataRow("br", false)]
    [DataRow("gzip", true)]
    [DataRow("deflate", true)]
    [DataRow("br", true)]
    public async Task CancelDuringCompressedResponse_CompletesWithoutWaitingForServer(string coding, bool redirect)
    {
        var body = Compress(Encoding.UTF8.GetBytes(Policy), coding);
        await using var server = new DocumentServer(new Response(body[..(body.Length / 2)], coding)
        {
            Chunked = true, Hold = true, Redirect = redirect
        });
        using var store = new InMemoryVectorStore();
        await Seed(store, server.Url);
        using var cancellation = new CancellationTokenSource();
        var embedding = new CountingEmbedding();
        int callbacks = 0;
        var build = RagStore.BuildAsync(b => b.AddUrl(server.Url).UseEmbedding(embedding).UseStore(store),
            onDocumentEmbedded: _ => { callbacks++; return Task.CompletedTask; }, cancellationToken: cancellation.Token);
        await server.Holding.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => build.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, embedding.Calls);
        Assert.AreEqual(0, callbacks);
        await AssertOriginalIndex(store);
    }

    private static async Task AssertSuccessful(Response response, string expected)
    {
        await using var server = new DocumentServer(response);
        using var store = new InMemoryVectorStore();
        await Seed(store, server.Url);
        await RagStore.BuildAsync(b => b.AddUrl(server.Url).UseLocalEmbedding(8).UseStore(store));
        var records = await store.ListAllRecordsAsync();
        var updated = records.Where(r => r.Metadata["document_id"] == server.Url).ToArray();
        if (expected.Length == 0)
            Assert.AreEqual(0, updated.Length);
        else
        {
            Assert.AreEqual(1, updated.Length);
            Assert.AreEqual(expected, updated[0].Content);
            Assert.AreEqual(server.Url, updated[0].Metadata["source"]);
            Assert.AreEqual(server.Url, updated[0].Metadata["url"]);
            Assert.AreEqual("url", updated[0].Metadata["type"]);
        }
        Assert.AreEqual("KEEP", records.Single(r => r.Metadata["document_id"] == "other").Content);
        Assert.AreEqual(expected.Length == 0 ? 1 : 2, records.Count);
        foreach (string request in server.Requests)
        {
            var accept = request.Split("\r\n").Single(h => h.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(accept, "gzip");
            StringAssert.Contains(accept, "deflate");
            StringAssert.Contains(accept, "br");
        }
    }

    private static async Task AssertRejected(Response response, bool callback)
    {
        await using var server = new DocumentServer(response);
        using var store = new InMemoryVectorStore();
        await Seed(store, server.Url);
        var embedding = new CountingEmbedding();
        int callbacks = 0;
        try
        {
            await RagStore.BuildAsync(b => b.AddUrl(server.Url).UseEmbedding(embedding).UseStore(store),
                onDocumentEmbedded: callback ? _ => { callbacks++; return Task.CompletedTask; } : null)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Fail("Invalid or incomplete encoded content must fail before indexing.");
        }
        // Brotli reports invalid compressed data as InvalidOperationException on .NET.
        catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or InvalidOperationException) { }
        Assert.AreEqual(0, embedding.Calls);
        Assert.AreEqual(0, callbacks);
        await AssertOriginalIndex(store);
    }

    private static async Task Seed(InMemoryVectorStore store, string url)
    {
        await store.UpsertAsync(new VectorRecord("old", new float[8], "OLD") { Metadata = { ["document_id"] = url } });
        await store.UpsertAsync(new VectorRecord("other", new float[8], "KEEP") { Metadata = { ["document_id"] = "other" } });
    }

    private static async Task AssertOriginalIndex(InMemoryVectorStore store) =>
        CollectionAssert.AreEquivalent(new[] { "OLD", "KEEP" }, (await store.ListAllRecordsAsync()).Select(r => r.Content).ToArray());

    private static byte[] Compress(byte[] bytes, string coding)
    {
        // A zero-byte write does not initialize .NET's gzip/zlib compressor. Use complete
        // empty streams (final empty DEFLATE block plus the required wrapper/checksum).
        if (bytes.Length == 0 && coding == "gzip")
            return Convert.FromHexString("1F8B080000000000000303000000000000000000");
        if (bytes.Length == 0 && coding == "deflate")
            return Convert.FromHexString("789C030000000001");
        using var output = new MemoryStream();
        using (Stream compressor = coding switch
        {
            "gzip" => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true),
            "deflate" => new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true),
            "br" => new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(coding))
        })
            compressor.Write(bytes);
        return output.ToArray();
    }

    private static byte[] GzipWithOptionalHeaders(string content, byte flags)
    {
        var original = Compress(Encoding.UTF8.GetBytes(content), "gzip");
        var header = original.Take(10).ToList();
        header[3] = flags;
        if ((flags & 4) != 0) header.AddRange(new byte[] { 3, 0, 0x11, 0x22, 0x33 });
        if ((flags & 8) != 0) header.AddRange(Encoding.ASCII.GetBytes("document.txt\0"));
        if ((flags & 16) != 0) header.AddRange(Encoding.ASCII.GetBytes("test comment\0"));
        if ((flags & 2) != 0)
        {
            // Independent RFC CRC32 oracle for the header fixture; the wire CRC16 is little endian.
            uint crc = uint.MaxValue;
            foreach (byte value in header)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            crc = ~crc;
            header.Add((byte)crc);
            header.Add((byte)(crc >> 8));
        }
        return header.Concat(original.Skip(10)).ToArray();
    }

    private sealed class CountingEmbedding : IEmbeddingProvider
    {
        private readonly LocalEmbeddingProvider inner = new(8);
        public int Dimensions => 8;
        public int Calls { get; private set; }
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        { Calls++; return inner.GetEmbeddingAsync(text, cancellationToken); }
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        { Calls++; return inner.GetEmbeddingsAsync(texts, cancellationToken); }
    }

    private sealed record Response(byte[] Body, string Coding)
    {
        public string ContentType { get; init; } = "text/plain; charset=utf-8";
        public bool Chunked { get; init; }
        public bool CompleteChunks { get; init; } = true;
        public int? DeclaredLength { get; init; }
        public bool Hold { get; init; }
        public bool Redirect { get; init; }
    }

    private sealed class DocumentServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stopping = new();
        private readonly TaskCompletionSource holding = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task serving;
        public string Url { get; }
        public List<string> Requests { get; } = [];
        public Task Holding => holding.Task;

        public DocumentServer(Response response)
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/document";
            serving = Serve(response);
        }

        private async Task Serve(Response response)
        {
            try
            {
                if (response.Redirect)
                {
                    using var redirectClient = await listener.AcceptTcpClientAsync(stopping.Token);
                    using var redirectStream = redirectClient.GetStream();
                    await ReadRequest(redirectStream);
                    await WriteAscii(redirectStream, "HTTP/1.1 302 Found\r\nLocation: /final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                }
                using var client = await listener.AcceptTcpClientAsync(stopping.Token);
                using var stream = client.GetStream();
                await ReadRequest(stream);
                string length = response.Chunked ? "Transfer-Encoding: chunked" : $"Content-Length: {response.DeclaredLength ?? response.Body.Length}";
                await WriteAscii(stream, $"HTTP/1.1 200 OK\r\nContent-Type: {response.ContentType}\r\nContent-Encoding: {response.Coding}\r\n{length}\r\nConnection: close\r\n\r\n");
                if (response.Chunked)
                {
                    foreach (byte value in response.Body)
                    {
                        await WriteAscii(stream, "1\r\n");
                        await stream.WriteAsync(new[] { value }, stopping.Token);
                        await WriteAscii(stream, "\r\n");
                    }
                }
                else await stream.WriteAsync(response.Body, stopping.Token);
                if (response.Hold)
                {
                    holding.TrySetResult();
                    await Task.Delay(Timeout.Infinite, stopping.Token);
                }
                if (response.Chunked && response.CompleteChunks)
                    await WriteAscii(stream, "0\r\nX-Completed: true\r\n\r\n");
            }
            catch (Exception ex) when (stopping.IsCancellationRequested &&
                ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
            catch (Exception ex) { holding.TrySetException(ex); throw; }
        }

        private async Task ReadRequest(NetworkStream stream)
        {
            var request = new StringBuilder();
            var buffer = new byte[1024];
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                int count = await stream.ReadAsync(buffer, stopping.Token);
                if (count == 0) throw new IOException("Request ended before headers.");
                request.Append(Encoding.ASCII.GetString(buffer, 0, count));
                if (request.Length > 16_384) throw new IOException("Unexpectedly large test request.");
            }
            Requests.Add(request.ToString());
        }

        private ValueTask WriteAscii(NetworkStream stream, string text) =>
            stream.WriteAsync(Encoding.ASCII.GetBytes(text), stopping.Token);

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            listener.Stop();
            try { await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { stopping.Dispose(); }
        }
    }
}
