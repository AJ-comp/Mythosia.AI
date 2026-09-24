using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        private const int MaximumDeepSeekFileBytes = 64 * 1024 * 1024;

        /// <summary>Uploads a JPEG, PNG, GIF or WebP image for reuse by its file ID.</summary>
        /// <param name="imagePath">Local image file to upload.</param>
        /// <param name="expiresAfterSeconds">Lifetime from 3600 to 2592000 seconds, or null to keep permanently.</param>
        /// <param name="cancellationToken">Cancels local file reading, upload and response reading.</param>
        public async Task<DeepSeekFile> UploadFileAsync(string imagePath, int? expiresAfterSeconds = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(imagePath)) throw new ArgumentException("An image path is required.", nameof(imagePath));
            ValidateDeepSeekFileExpiration(expiresAfterSeconds);
            using var image = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            return await UploadFileAsync(image, Path.GetFileName(imagePath), expiresAfterSeconds, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Uploads an image from the stream's current position. The caller retains ownership of the stream.</summary>
        /// <remarks>Reads at most 64 MiB into a temporary buffer before sending. The actual image signature determines its format.</remarks>
        public async Task<DeepSeekFile> UploadFileAsync(Stream image, string fileName, int? expiresAfterSeconds = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (!image.CanRead) throw new ArgumentException("The image stream must be readable.", nameof(image));
            ValidateDeepSeekFileName(fileName);
            ValidateDeepSeekFileExpiration(expiresAfterSeconds);
            if (image.CanSeek && image.Length - image.Position > MaximumDeepSeekFileBytes)
                throw new ArgumentException("DeepSeek image uploads cannot exceed 64 MiB.", nameof(image));

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Read one extra byte to reject an oversized non-seekable source without buffering it in full.
                var count = Math.Min(chunk.Length, MaximumDeepSeekFileBytes - (int)buffer.Length + 1);
                var read = await image.ReadAsync(chunk, 0, count, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > MaximumDeepSeekFileBytes)
                    throw new ArgumentException("DeepSeek image uploads cannot exceed 64 MiB.", nameof(image));
                await buffer.WriteAsync(chunk, 0, read, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var mediaType = DetectDeepSeekImageMediaType(buffer.GetBuffer(), (int)buffer.Length);
            buffer.Position = 0;
            using var request = CreateDeepSeekFileRequest(HttpMethod.Post, "files");
            var form = new MultipartFormDataContent();
            request.Content = form;
            form.Add(new StringContent("user_data"), "purpose");
            if (expiresAfterSeconds.HasValue)
            {
                form.Add(new StringContent("created_at"), "expires_after[anchor]");
                form.Add(new StringContent(expiresAfterSeconds.Value.ToString(CultureInfo.InvariantCulture)), "expires_after[seconds]");
            }
            var file = new StreamContent(buffer);
            file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            form.Add(file, "file", fileName);
            using var document = await SendDeepSeekFileRequestAsync(request, cancellationToken).ConfigureAwait(false);
            return ParseDeepSeekFile(document.RootElement);
        }

        /// <summary>Gets metadata for an uploaded image. The Files API does not document a content download endpoint.</summary>
        public async Task<DeepSeekFile> GetFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeepSeekImageFileContent.ValidateFileId(fileId, nameof(fileId));
            using var request = CreateDeepSeekFileRequest(HttpMethod.Get, "files/" + Uri.EscapeDataString(fileId));
            using var document = await SendDeepSeekFileRequestAsync(request, cancellationToken).ConfigureAwait(false);
            var file = ParseDeepSeekFile(document.RootElement);
            if (file.Id != fileId) throw InvalidDeepSeekFileResponse("The returned file ID does not match the requested file.");
            return file;
        }

        /// <summary>Lists one page of uploaded images. Use the returned LastId as After to read the next page.</summary>
        public async Task<DeepSeekFileList> ListFilesAsync(DeepSeekFileListOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var after = options?.After;
            var limit = options?.Limit;
            var order = options?.Order ?? DeepSeekFileOrder.Ascending;
            if (after != null) DeepSeekImageFileContent.ValidateFileId(after, nameof(options.After));
            if (limit.HasValue && (limit.Value < 1 || limit.Value > 1000))
                throw new ArgumentOutOfRangeException(nameof(options.Limit), "The page size must be between 1 and 1000.");
            if (!Enum.IsDefined(typeof(DeepSeekFileOrder), order)) throw new ArgumentOutOfRangeException(nameof(options.Order));
            var path = "files?purpose=user_data&order=" + (order == DeepSeekFileOrder.Descending ? "desc" : "asc");
            if (after != null) path += "&after=" + Uri.EscapeDataString(after);
            if (limit.HasValue) path += "&limit=" + limit.Value.ToString(CultureInfo.InvariantCulture);
            using var request = CreateDeepSeekFileRequest(HttpMethod.Get, path);
            using var document = await SendDeepSeekFileRequestAsync(request, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            RequireDeepSeekFileString(root, "object", "list");
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw InvalidDeepSeekFileResponse("Missing or invalid file list data.");
            var files = new List<DeepSeekFile>();
            foreach (var item in data.EnumerateArray()) files.Add(ParseDeepSeekFile(item));
            var first = ReadDeepSeekFileCursor(root, "first_id");
            var last = ReadDeepSeekFileCursor(root, "last_id");
            var hasMore = RequireDeepSeekFileBoolean(root, "has_more");
            if (hasMore && (files.Count == 0 || last == null))
                throw InvalidDeepSeekFileResponse("A file list with more pages must contain a file and a last_id cursor.");
            if ((first != null && (files.Count == 0 || first != files[0].Id)) ||
                (last != null && (files.Count == 0 || last != files[files.Count - 1].Id)))
                throw InvalidDeepSeekFileResponse("The file list cursors do not match its contents.");
            return new DeepSeekFileList(files.AsReadOnly(), first, last, hasMore);
        }

        /// <summary>Deletes the specified stored image and returns the server's acknowledgement.</summary>
        public async Task<DeepSeekFileDeletion> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeepSeekImageFileContent.ValidateFileId(fileId, nameof(fileId));
            using var request = CreateDeepSeekFileRequest(HttpMethod.Delete, "files/" + Uri.EscapeDataString(fileId));
            using var document = await SendDeepSeekFileRequestAsync(request, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            RequireDeepSeekFileString(root, "object", "file");
            var id = RequireDeepSeekFileId(root, "id");
            if (id != fileId) throw InvalidDeepSeekFileResponse("The deleted file ID does not match the requested file.");
            return new DeepSeekFileDeletion(id, RequireDeepSeekFileBoolean(root, "deleted"));
        }

        private HttpRequestMessage CreateDeepSeekFileRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            return request;
        }

        private async Task<JsonDocument> SendDeepSeekFileRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var json = await ReadCompletionResponseBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, json);
            cancellationToken.ThrowIfCancellationRequested();
            try { return JsonDocument.Parse(json); }
            catch (JsonException exception) { throw new AIServiceException("DeepSeek returned an invalid Files API JSON response.", exception); }
        }

        private static DeepSeekFile ParseDeepSeekFile(JsonElement root)
        {
            RequireDeepSeekFileString(root, "object", "file");
            RequireDeepSeekFileString(root, "purpose", "user_data");
            var id = RequireDeepSeekFileId(root, "id");
            var bytes = RequireDeepSeekFileInteger(root, "bytes");
            var createdAt = RequireDeepSeekFileInteger(root, "created_at");
            var filename = RequireDeepSeekFileString(root, "filename");
            long? expiresAt = null;
            if (root.TryGetProperty("expires_at", out var expiration) && expiration.ValueKind != JsonValueKind.Null)
                expiresAt = RequireDeepSeekFileInteger(root, "expires_at");
            return new DeepSeekFile(id, bytes, createdAt, filename, expiresAt);
        }

        private static string RequireDeepSeekFileString(JsonElement root, string name, string? expected = null)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) ||
                (expected != null && value.GetString() != expected))
                throw InvalidDeepSeekFileResponse($"Missing or invalid '{name}'.");
            return value.GetString()!;
        }

        private static string RequireDeepSeekFileId(JsonElement root, string name)
        {
            var id = RequireDeepSeekFileString(root, name);
            try { DeepSeekImageFileContent.ValidateFileId(id, name); }
            catch (ArgumentException) { throw InvalidDeepSeekFileResponse($"Invalid '{name}' file ID."); }
            return id;
        }

        private static string? ReadDeepSeekFileCursor(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
            return RequireDeepSeekFileId(root, name);
        }

        private static long RequireDeepSeekFileInteger(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < 0)
                throw InvalidDeepSeekFileResponse($"Missing or invalid '{name}'.");
            return number;
        }

        private static bool RequireDeepSeekFileBoolean(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) ||
                (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
                throw InvalidDeepSeekFileResponse($"Missing or invalid '{name}'.");
            return value.GetBoolean();
        }

        private static AIServiceException InvalidDeepSeekFileResponse(string detail)
            => new AIServiceException("DeepSeek returned an invalid Files API response. " + detail);

        private static void ValidateDeepSeekFileExpiration(int? expiresAfterSeconds)
        {
            if (expiresAfterSeconds.HasValue && (expiresAfterSeconds.Value < 3600 || expiresAfterSeconds.Value > 2592000))
                throw new ArgumentOutOfRangeException(nameof(expiresAfterSeconds), "Expiration must be between 3600 and 2592000 seconds.");
        }

        private static void ValidateDeepSeekFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 512)
                throw new ArgumentException("A file name from 1 to 512 characters is required.", nameof(fileName));
            foreach (var character in fileName)
                if (char.IsControl(character) || character == '/' || character == '\\' || character == '"')
                    throw new ArgumentException("The file name cannot contain directory separators, quotes or control characters.", nameof(fileName));
        }

        private static string DetectDeepSeekImageMediaType(byte[] data, int length)
        {
            if (length >= 8 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 &&
                data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10) return "image/png";
            if (length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255) return "image/jpeg";
            if (length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8' &&
                (data[4] == '7' || data[4] == '9') && data[5] == 'a') return "image/gif";
            if (length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return "image/webp";
            throw new ArgumentException("DeepSeek file uploads support only JPEG, PNG, GIF and WebP image content.", "image");
        }
    }
}
