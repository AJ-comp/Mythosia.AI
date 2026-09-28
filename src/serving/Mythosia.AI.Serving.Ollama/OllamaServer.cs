using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Serving.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mythosia.AI.Serving.Ollama
{
    /// <summary>
    /// Management client for an existing Ollama server. Discovery does not run inference;
    /// loading, unloading and downloading are explicit operations. Does not host a server.
    /// </summary>
    public sealed class OllamaServer : IModelServer, IModelLifecycle, IModelDownloader
    {
        private readonly ServingHttpTransport _transport;

        public Uri Endpoint => _transport.Endpoint;

        /// <summary>
        /// Accepts a server root or a trailing /api or /v1 endpoint. Authentication is sent
        /// per request without changing or taking ownership of the supplied HTTP client.
        /// </summary>
        public OllamaServer(string endpoint, HttpClient httpClient, string? apiKey = null)
        {
            _transport = new ServingHttpTransport(endpoint, httpClient, apiKey, "api", "v1");
        }

        public async Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken = default)
            => new ServerInfo("ollama", Endpoint, await GetVersionAsync(cancellationToken).ConfigureAwait(false));

        public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            var response = await _transport.GetObjectAsync("api/version", cancellationToken).ConfigureAwait(false);
            ThrowIfError(response);
            var version = OllamaModelParser.OptionalString(response, "version");
            if (string.IsNullOrWhiteSpace(version))
                throw OllamaModelParser.Invalid("The version response must contain a non-empty version string.");
            return version!;
        }

        /// <summary>
        /// Probes /api/version. Healthy means the management API answered correctly; it
        /// does not guarantee that any particular model can perform inference.
        /// </summary>
        public async Task<ServerHealth> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await GetVersionAsync(cancellationToken).ConfigureAwait(false);
                return new ServerHealth(ServerHealthStatus.Healthy, 200);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                return new ServerHealth(ServerHealthStatus.Unreachable, detail: "The management request timed out.");
            }
            catch (HttpRequestException)
            {
                return new ServerHealth(ServerHealthStatus.Unreachable, detail: "The management endpoint could not be reached.");
            }
            catch (ServingException ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = ex.FailureKind == ServingFailureKind.Transport || ex.FailureKind == ServingFailureKind.Timeout
                    ? ServerHealthStatus.Unreachable :
                    ex.StatusCode == 401 || ex.StatusCode == 403 ? ServerHealthStatus.Unauthorized :
                    ex.StatusCode == 503 ? ServerHealthStatus.NotReady : ServerHealthStatus.Unexpected;
                return new ServerHealth(status, ex.StatusCode, "The management endpoint did not return a valid successful response.");
            }
        }

        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
            => (await GetHealthAsync(cancellationToken).ConfigureAwait(false)).Status == ServerHealthStatus.Healthy;

        /// <summary>
        /// Combines /api/tags and /api/ps. Installed means registered in Ollama, which may
        /// include a remote-model manifest. Loaded is reported only from /api/ps; these
        /// two requests form an observation rather than an atomic server snapshot.
        /// </summary>
        public async Task<IReadOnlyList<ServerModel>> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            var registered = await _transport.GetObjectAsync("api/tags", cancellationToken).ConfigureAwait(false);
            ThrowIfError(registered);
            var running = await _transport.GetObjectAsync("api/ps", cancellationToken).ConfigureAwait(false);
            ThrowIfError(running);
            cancellationToken.ThrowIfCancellationRequested();
            return OllamaModelParser.Merge(registered, running, cancellationToken);
        }

        /// <summary>
        /// Observes the version and inventory endpoints without mutating server state.
        /// Lifecycle and download support are inferred from a valid Ollama protocol
        /// observation, not proved by executing those operations or checking every model.
        /// </summary>
        public async Task<ServingCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await GetVersionAsync(cancellationToken).ConfigureAwait(false); }
            catch (ServingException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ServingCapabilities();
            }
            try { await GetModelsAsync(cancellationToken).ConfigureAwait(false); }
            catch (ServingException ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ServingCapabilities(
                    modelListing: ex.StatusCode == 404 ? ServingFeatureSupport.Unsupported : ServingFeatureSupport.Unknown,
                    metrics: ServingFeatureSupport.Unsupported);
            }
            return new ServingCapabilities(
                modelListing: ServingFeatureSupport.Supported,
                modelLoading: ServingFeatureSupport.Supported,
                modelUnloading: ServingFeatureSupport.Supported,
                modelDownloading: ServingFeatureSupport.Supported,
                metrics: ServingFeatureSupport.Unsupported);
        }

        /// <summary>
        /// Explicitly preloads a compatible model through an empty /api/generate request.
        /// Ollama's default keep-alive applies. Completion acknowledges this request; it
        /// does not promise indefinite residency or local execution of remote models.
        /// </summary>
        public Task LoadModelAsync(string modelId, CancellationToken cancellationToken = default)
            => ChangeModelLifetimeAsync(modelId, false, cancellationToken);

        /// <summary>Explicitly requests unloading with an empty prompt and keep_alive=0. Does not delete model files.</summary>
        public Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default)
            => ChangeModelLifetimeAsync(modelId, true, cancellationToken);

        private async Task ChangeModelLifetimeAsync(string modelId, bool unload, CancellationToken cancellationToken)
        {
            ValidateModelId(modelId);
            var body = new JObject { ["model"] = modelId, ["prompt"] = "", ["stream"] = false };
            if (unload) body["keep_alive"] = 0;
            var response = await _transport.PostObjectAsync("api/generate", body, cancellationToken).ConfigureAwait(false);
            ThrowIfError(response);
            var reason = OllamaModelParser.OptionalString(response, "done_reason");
            if (response["done"]?.Type != JTokenType.Boolean || response["done"]!.Value<bool>() != true ||
                string.IsNullOrWhiteSpace(OllamaModelParser.OptionalString(response, "model")) ||
                !string.IsNullOrEmpty(OllamaModelParser.OptionalString(response, "response")) ||
                (!string.IsNullOrEmpty(reason) && reason != (unload ? "unload" : "load")))
                throw OllamaModelParser.Invalid("The model lifecycle request was not acknowledged as complete.");
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Downloads a model on the server and reports NDJSON progress. Byte counters are
        /// for the current artifact, not the entire model. Completion requires the final
        /// success message. Canceling this client request does not roll back cached layers.
        /// </summary>
        public async Task DownloadModelAsync(string modelId, IProgress<ModelDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateModelId(modelId);
            using var stream = await _transport.OpenStreamAsync(HttpMethod.Post, "api/pull",
                new { model = modelId, stream = true }, cancellationToken).ConfigureAwait(false);
            ModelDownloadProgress? completed = null;
            await foreach (var line in stream.ReadLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (completed != null)
                    throw OllamaModelParser.Invalid("The download stream contained data after its success message.");
                JObject item;
                try
                {
                    item = JObject.Parse(line, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                }
                catch (JsonException)
                {
                    throw OllamaModelParser.Invalid("The download stream contained malformed JSON.");
                }
                ThrowIfError(item);
                var stage = OllamaModelParser.OptionalString(item, "status");
                if (string.IsNullOrWhiteSpace(stage))
                    throw OllamaModelParser.Invalid("The download stream did not contain a status string.");
                var done = OllamaModelParser.OptionalLong(item, "completed");
                var total = OllamaModelParser.OptionalLong(item, "total");
                if (done.HasValue && total.HasValue && done > total)
                    throw OllamaModelParser.Invalid("Download progress exceeds its artifact size.");
                var update = new ModelDownloadProgress(modelId, stage!, done, total,
                    OllamaModelParser.OptionalString(item, "digest"));
                if (stage == "success") completed = update;
                else progress?.Report(update);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (completed == null)
                throw OllamaModelParser.Invalid("The download stream ended before success was confirmed.");
            progress?.Report(completed);
        }

        private static void ThrowIfError(JObject response)
        {
            if (response["error"] != null && response["error"]!.Type != JTokenType.Null)
                throw new ServingException("Ollama reported a server-side operation error.");
        }

        private static void ValidateModelId(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                throw new ArgumentException("A non-empty model identifier is required.", nameof(modelId));
        }
    }
}
