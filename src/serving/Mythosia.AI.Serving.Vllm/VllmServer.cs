using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Serving.Vllm
{
    /// <summary>
    /// Control-plane client for a <b>running</b> vLLM server instance — model cards, health,
    /// server version, and Prometheus metrics. It talks <b>to</b> a server over HTTP; it does
    /// not start or host one.
    /// <para>
    /// Family taxonomy: <c>Mythosia.AI.Providers.*</c> = chat data plane (concrete AI services);
    /// <c>Mythosia.AI.Serving.*</c> = model-server control plane. Chat/completions against vLLM
    /// stay on Mythosia.AI (e.g. <c>QwenService</c> with <c>EndpointPlatform.Vllm</c>).
    /// </para>
    /// </summary>
    public class VllmServer
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;

        /// <summary>
        /// Normalized server <b>root</b> endpoint this client talks to (always ends with <c>/</c>).
        /// Management routes (<c>/health</c>, <c>/version</c>, <c>/metrics</c>) live at the root
        /// while <c>/v1/models</c> lives under <c>/v1</c>; the client composes both from here.
        /// </summary>
        public Uri Endpoint { get; }

        /// <summary>
        /// Creates a client for one vLLM server instance.
        /// </summary>
        /// <param name="endpoint">
        /// Server URL. Accepts either the server root (<c>http://host:8000</c>) or an OpenAI-style
        /// <c>/v1</c>-suffixed URL (<c>http://host:8000/v1</c>, as typically stored for chat clients) —
        /// a trailing <c>/v1</c> segment is stripped and the URL is normalized to the server root.
        /// </param>
        /// <param name="httpClient">
        /// HTTP client to send requests with (inject via <c>IHttpClientFactory</c> where available).
        /// Neither <see cref="HttpClient.BaseAddress"/> nor default headers are mutated, so a shared
        /// instance is safe.
        /// </param>
        /// <param name="apiKey">
        /// Optional API key, sent as <c>Authorization: Bearer</c> on every request — for servers
        /// started with <c>--api-key</c> / <c>VLLM_API_KEY</c>.
        /// </param>
        public VllmServer(string endpoint, HttpClient httpClient, string? apiKey = null)
        {
            Endpoint = NormalizeToRoot(endpoint);
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        }

        /// <summary>
        /// Lists the server's model cards via <c>GET /v1/models</c>.
        /// <para>
        /// vLLM returns one card per <c>--served-model-name</c> alias — all alias cards share an
        /// identical <see cref="VllmModelCard.Root"/> (the raw <c>--model</c> value) and
        /// <c>data[0].Id</c> is the canonical served name the server echoes in responses — plus one
        /// card per loaded LoRA adapter (<see cref="VllmModelCard.Parent"/> != null).
        /// </para>
        /// </summary>
        /// <exception cref="VllmException">Non-success response, or a response body that is not the expected model list.</exception>
        public async Task<IReadOnlyList<VllmModelCard>> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            var body = await GetStringAsync("v1/models", cancellationToken).ConfigureAwait(false);
            ModelListEnvelope? envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<ModelListEnvelope>(body);
            }
            catch (JsonException ex)
            {
                throw new VllmException(
                    $"Failed to parse the /v1/models response as a model list: {ex.Message}",
                    200, null, null, Truncate(body));
            }

            return (IReadOnlyList<VllmModelCard>?)envelope?.Data ?? Array.Empty<VllmModelCard>();
        }

        /// <summary>
        /// Returns the model card whose <see cref="VllmModelCard.Id"/> equals
        /// <paramref name="servedName"/> (ordinal comparison), or <c>null</c> if the server does
        /// not list it. Convenience over <see cref="GetModelsAsync"/> for the common
        /// "resolve the configured alias to the actual model" lookup
        /// (<c>card.DisplayModel</c>).
        /// </summary>
        public async Task<VllmModelCard?> GetModelAsync(string servedName, CancellationToken cancellationToken = default)
        {
            if (servedName == null) throw new ArgumentNullException(nameof(servedName));

            var models = await GetModelsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var model in models)
            {
                if (string.Equals(model.Id, servedName, StringComparison.Ordinal))
                    return model;
            }
            return null;
        }

        /// <summary>
        /// Returns the vLLM server version via <c>GET /version</c> (e.g. <c>"0.25.0"</c>), or
        /// <c>null</c> when the response has no <c>version</c> field.
        /// </summary>
        /// <exception cref="VllmException">Non-success response.</exception>
        public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            var body = await GetStringAsync("version", cancellationToken).ConfigureAwait(false);
            try
            {
                var obj = JObject.Parse(body);
                return obj["version"]?.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Simple boolean probe over <see cref="GetHealthAsync"/>: <c>true</c> only when the server
        /// answers <c>GET /health</c> with a success status. The only member of this client that
        /// never throws on server/network failures (caller cancellation still propagates).
        /// </summary>
        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            var report = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
            return report.Status == VllmHealthStatus.Healthy;
        }

        /// <summary>
        /// Probes <c>GET /health</c> and classifies the outcome so operators can tell a dead engine
        /// (HTTP 503) from an auth misconfiguration (401/403) from a network problem — the
        /// distinctions a connection-test UI actually needs. vLLM's <c>/health</c> returns an empty
        /// body by contract; only the status code is inspected. Never throws on server/network
        /// failures (caller cancellation still propagates).
        /// </summary>
        public async Task<VllmHealthReport> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using (var request = CreateRequest("health"))
                using (var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    var statusCode = (int)response.StatusCode;
                    if (response.IsSuccessStatusCode)
                        return new VllmHealthReport(VllmHealthStatus.Healthy, statusCode, null);
                    if (statusCode == 503)
                        return new VllmHealthReport(VllmHealthStatus.EngineDead, statusCode, response.ReasonPhrase);
                    if (statusCode == 401 || statusCode == 403)
                        return new VllmHealthReport(VllmHealthStatus.Unauthorized, statusCode, response.ReasonPhrase);
                    return new VllmHealthReport(VllmHealthStatus.Unexpected, statusCode, response.ReasonPhrase);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // caller-requested cancellation is not a health verdict
            }
            catch (OperationCanceledException ex)
            {
                // HttpClient.Timeout elapsed without caller cancellation — effectively unreachable.
                return new VllmHealthReport(VllmHealthStatus.Unreachable, null, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return new VllmHealthReport(VllmHealthStatus.Unreachable, null, ex.Message);
            }
        }

        /// <summary>
        /// Fetches <c>GET /metrics</c> (Prometheus text exposition) and parses it into
        /// label-preserving families plus typed convenience getters. The generic
        /// <see cref="VllmMetrics.Families"/> dictionary is the durable contract — metric names
        /// have been renamed across vLLM versions, in which case the typed getters return
        /// <c>null</c> while the families/raw text still carry everything the server exposed.
        /// </summary>
        /// <exception cref="VllmException">Non-success response.</exception>
        public async Task<VllmMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
        {
            var body = await GetStringAsync("metrics", cancellationToken).ConfigureAwait(false);
            return new VllmMetrics(PrometheusTextParser.Parse(body), body);
        }

        private HttpRequestMessage CreateRequest(string relativePath)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint, relativePath));
            if (_apiKey != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return request;
        }

        private async Task<string> GetStringAsync(string relativePath, CancellationToken cancellationToken)
        {
            using (var request = CreateRequest(relativePath))
            using (var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                var body = response.Content != null
                    ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                    : string.Empty;

                if (!response.IsSuccessStatusCode)
                    throw VllmException.FromResponse((int)response.StatusCode, response.ReasonPhrase, body);

                return body;
            }
        }

        private static Uri NormalizeToRoot(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint must be a non-empty absolute URL.", nameof(endpoint));

            var trimmed = endpoint.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - 3).TrimEnd('/');

            if (!Uri.TryCreate(trimmed + "/", UriKind.Absolute, out var uri))
                throw new ArgumentException($"Endpoint is not a valid absolute URL: '{endpoint}'", nameof(endpoint));

            return uri;
        }

        internal static string? Truncate(string? body)
        {
            const int maxLength = 4096;
            if (body == null || body.Length <= maxLength) return body;
            return body.Substring(0, maxLength);
        }

        private sealed class ModelListEnvelope
        {
            [JsonProperty("data")]
            public List<VllmModelCard>? Data { get; set; }
        }
    }
}
