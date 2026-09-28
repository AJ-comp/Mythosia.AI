using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Serving.Internal
{
    // Linked into concrete packages, not a dependency of the public contracts.
    internal sealed class ServingHttpTransport
    {
        private readonly HttpClient _client;
        private readonly string? _apiKey;
        public Uri Endpoint { get; }

        public ServingHttpTransport(string endpoint, HttpClient httpClient, string? apiKey = null, params string[] suffixes)
        {
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("Use an absolute HTTP(S) endpoint without credentials, query or fragment.", nameof(endpoint));
            var path = uri.AbsolutePath.TrimEnd('/');
            foreach (var suffix in suffixes)
            {
                if (!string.IsNullOrEmpty(suffix) && path.EndsWith("/" + suffix.Trim('/'), StringComparison.OrdinalIgnoreCase))
                { path = path.Substring(0, path.Length - suffix.Trim('/').Length - 1); break; }
            }
            Endpoint = new UriBuilder(uri) { Path = path + "/" }.Uri;
        }

        public async Task<JObject> GetObjectAsync(string path, CancellationToken cancellationToken = default) =>
            ParseObject(await GetTextAsync(path, cancellationToken).ConfigureAwait(false));

        public async Task<JObject> PostObjectAsync(string path, object? body, CancellationToken cancellationToken = default)
        {
            using var response = await OpenStreamAsync(HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
            return ParseObject(await response.ReadTextAsync(cancellationToken).ConfigureAwait(false));
        }

        public async Task<string> GetTextAsync(string path, CancellationToken cancellationToken = default)
        {
            using var response = await OpenStreamAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
            return await response.ReadTextAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> GetStatusAsync(string path, CancellationToken cancellationToken = default)
        {
            using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken, false).ConfigureAwait(false);
            return response.StatusCode;
        }

        public Task<ServingStream> OpenStreamAsync(HttpMethod method, string path, object? body,
            CancellationToken cancellationToken = default) => SendAsync(method, path, body, cancellationToken, true);

        private async Task<ServingStream> SendAsync(HttpMethod method, string path, object? body,
            CancellationToken cancellationToken, bool requireSuccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
                Uri.TryCreate(path, UriKind.Absolute, out _))
                throw new ArgumentException("A relative management route is required.", nameof(path));
            using var request = new HttpRequestMessage(method, new Uri(Endpoint, path));
            if (_apiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_client.Timeout != Timeout.InfiniteTimeSpan) lifetime.CancelAfter(_client.Timeout);
            HttpResponseMessage? response = null;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                if (requireSuccess && !response.IsSuccessStatusCode)
                    throw new ServingException("The server rejected the management request.", (int)response.StatusCode);
                return new ServingStream(response, lifetime, cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is HttpRequestException || ex is IOException)
            {
                response?.Dispose(); lifetime.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                throw new ServingException(ex is OperationCanceledException
                    ? "The management request timed out." : "The model server could not be reached.",
                    failureKind: ex is OperationCanceledException ? ServingFailureKind.Timeout : ServingFailureKind.Transport);
            }
            catch { response?.Dispose(); lifetime.Dispose(); throw; }
        }

        private static JObject ParseObject(string text)
        {
            try
            {
                using var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
                var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new ServingException("The server returned extra JSON content.", failureKind: ServingFailureKind.InvalidResponse);
                return result;
            }
            catch (JsonException) { throw new ServingException("The server returned an invalid JSON object.", failureKind: ServingFailureKind.InvalidResponse); }
        }
    }

    internal sealed class ServingStream : IDisposable
    {
        private const int MaxTextCharacters = 16 * 1024 * 1024;
        private const int MaxLineCharacters = 1024 * 1024;
        private readonly HttpResponseMessage _response;
        private readonly CancellationTokenSource _lifetime;
        private readonly CancellationToken _callerToken;
        public string? MediaType => _response.Content?.Headers.ContentType?.MediaType;
        public int StatusCode => (int)_response.StatusCode;

        public ServingStream(HttpResponseMessage response, CancellationTokenSource lifetime, CancellationToken callerToken)
        { _response = response; _lifetime = lifetime; _callerToken = callerToken; }

        private async Task<Stream> GetStreamAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Disposing the response interrupts network reads even on runtimes whose
            // HttpContent stream factory does not expose a cancellation overload.
            using var registration = token.Register(() => _response.Dispose());
            try
            {
                var stream = await _response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return stream;
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is HttpRequestException || ex is IOException || ex is ObjectDisposedException)
            { ThrowReadFailure(token); throw; }
        }

        private void ThrowReadFailure(CancellationToken token, bool invalidEncoding = false)
        {
            _callerToken.ThrowIfCancellationRequested();
            if (_lifetime.IsCancellationRequested) throw new ServingException("The management response timed out.", failureKind: ServingFailureKind.Timeout);
            token.ThrowIfCancellationRequested();
            throw new ServingException("The management response could not be read.",
                failureKind: invalidEncoding ? ServingFailureKind.InvalidResponse : ServingFailureKind.Transport);
        }

        private async Task<int> ReadAsync(StreamReader reader, char[] buffer, CancellationToken token)
        {
            try { return await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException || ex is HttpRequestException || ex is IOException || ex is ObjectDisposedException || ex is DecoderFallbackException)
            { ThrowReadFailure(token, ex is DecoderFallbackException); throw; }
        }

        public async Task<string> ReadTextAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            using var stream = await GetStreamAsync(linked.Token).ConfigureAwait(false);
            using var registration = linked.Token.Register(() => stream.Dispose());
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            var text = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await ReadAsync(reader, buffer, linked.Token).ConfigureAwait(false)) != 0)
            {
                if (text.Length > MaxTextCharacters - count) throw new ServingException("The management response exceeds the size limit.", failureKind: ServingFailureKind.InvalidResponse);
                text.Append(buffer, 0, count);
            }
            if (linked.IsCancellationRequested) ThrowReadFailure(linked.Token);
            return text.ToString();
        }

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            using var stream = await GetStreamAsync(linked.Token).ConfigureAwait(false);
            using var registration = linked.Token.Register(() => stream.Dispose());
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            var line = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await ReadAsync(reader, buffer, linked.Token).ConfigureAwait(false)) != 0)
            {
                for (var i = 0; i < count; i++)
                {
                    if (linked.IsCancellationRequested) ThrowReadFailure(linked.Token);
                    if (buffer[i] == '\n')
                    { yield return line.ToString().TrimEnd('\r'); line.Clear(); }
                    else
                    {
                        if (line.Length >= MaxLineCharacters) throw new ServingException("A management response line exceeds the size limit.", failureKind: ServingFailureKind.InvalidResponse);
                        line.Append(buffer[i]);
                    }
                }
            }
            if (linked.IsCancellationRequested) ThrowReadFailure(linked.Token);
            if (line.Length != 0) yield return line.ToString().TrimEnd('\r');
        }

        public void Dispose() { _response.Dispose(); _lifetime.Dispose(); }
    }
}
