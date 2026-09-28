using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Serving.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mythosia.AI.Serving.LlamaCpp
{
    /// <summary>
    /// Management client for an existing llama.cpp server. It does not start a server or perform chat inference.
    /// Router-only commands require an explicit router identity from the read-only properties endpoint.
    /// </summary>
    public sealed class LlamaCppServer : IModelServer, IModelLifecycle, IModelDownloader, IModelMetricsProvider
    {
        private readonly ServingHttpTransport _transport;

        /// <summary>The normalized server root, including any reverse-proxy path prefix.</summary>
        public Uri Endpoint => _transport.Endpoint;

        /// <summary>Creates a client without changing or taking ownership of the supplied HTTP client.</summary>
        /// <param name="endpoint">HTTP(S) server root, optionally ending with /v1.</param>
        /// <param name="httpClient">Caller-owned HTTP client. Its timeout also applies to streamed bodies.</param>
        /// <param name="apiKey">Optional per-request Bearer credential.</param>
        public LlamaCppServer(string endpoint, HttpClient httpClient, string? apiKey = null)
        {
            _transport = new ServingHttpTransport(endpoint, httpClient, apiKey, "v1");
        }

        /// <summary>Reads the native server role and optional build information from /props.</summary>
        public async Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            var props = await _transport.GetObjectAsync("props", cancellationToken).ConfigureAwait(false);
            return new ServerInfo("llama.cpp", Endpoint,
                LlamaCppModelParser.OptionalString(props["build_info"]), LlamaCppModelParser.GetMode(props));
        }

        /// <summary>Classifies health without exposing response text, credentials or network exception details.</summary>
        public async Task<ServerHealth> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var health = await _transport.GetObjectAsync("health", cancellationToken).ConfigureAwait(false);
                return new ServerHealth(!LlamaCppModelParser.HasError(health) && health["status"]?.Type == JTokenType.String && (string?)health["status"] == "ok"
                    ? ServerHealthStatus.Healthy : ServerHealthStatus.Unexpected, 200);
            }
            catch (ServingException ex)
            {
                var status = ex.StatusCode;
                return new ServerHealth(status == 401 || status == 403 ? ServerHealthStatus.Unauthorized
                    : status == 503 ? ServerHealthStatus.NotReady
                    : ex.FailureKind == ServingFailureKind.Transport || ex.FailureKind == ServingFailureKind.Timeout ? ServerHealthStatus.Unreachable
                    : ServerHealthStatus.Unexpected, status);
            }
        }

        /// <summary>
        /// Lists advertised models. Router status distinguishes downloaded inventory from loaded models.
        /// Optional /props inspection resolves single-model sleeping state without waking the model.
        /// </summary>
        public async Task<IReadOnlyList<ServerModel>> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            var models = await _transport.GetObjectAsync("v1/models", cancellationToken).ConfigureAwait(false);
            var props = await TryPropertiesAsync(cancellationToken).ConfigureAwait(false);
            return LlamaCppModelParser.ParseModels(models, props);
        }

        /// <summary>
        /// Uses only GET probes. Unknown means insufficient evidence, including authorization or server failures.
        /// Router SSE availability alone cannot prove that a particular build implements POST /models downloads.
        /// </summary>
        public async Task<ServingCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            var props = await TryPropertiesAsync(cancellationToken).ConfigureAwait(false);
            var mode = props == null ? ServerMode.Unknown : LlamaCppModelParser.GetMode(props);
            var listing = ServingFeatureSupport.Unknown;
            try
            {
                var models = await _transport.GetObjectAsync("v1/models", cancellationToken).ConfigureAwait(false);
                LlamaCppModelParser.ParseModels(models, props);
                listing = ServingFeatureSupport.Supported;
            }
            catch (ServingException ex) { listing = MissingOrUnknown(ex); }

            var lifecycle = mode == ServerMode.Router ? ServingFeatureSupport.Supported
                : mode == ServerMode.SingleModel ? ServingFeatureSupport.Unsupported : ServingFeatureSupport.Unknown;
            var downloading = mode == ServerMode.SingleModel ? ServingFeatureSupport.Unsupported : ServingFeatureSupport.Unknown;
            var metrics = mode == ServerMode.Router ? ServingFeatureSupport.Unsupported : ServingFeatureSupport.Unknown;
            if (mode == ServerMode.Router)
            {
                try
                {
                    using (var stream = await _transport.OpenStreamAsync(HttpMethod.Get, "models/sse", null, cancellationToken).ConfigureAwait(false))
                    {
                        RequireEventStream(stream);
                    }
                }
                catch (ServingException ex) { downloading = MissingOrUnknown(ex); }
            }
            else if (mode == ServerMode.SingleModel)
            {
                try
                {
                    var text = await _transport.GetTextAsync("metrics", cancellationToken).ConfigureAwait(false);
                    ServingMetricsParser.Parse(text);
                    metrics = ServingFeatureSupport.Supported;
                }
                catch (ServingException ex) { metrics = MissingOrUnknown(ex); }
            }
            return new ServingCapabilities(listing, lifecycle, lifecycle, downloading, metrics);
        }

        /// <summary>Submits an explicit router load command. Success acknowledges the command, not readiness.</summary>
        public Task LoadModelAsync(string modelId, CancellationToken cancellationToken = default) =>
            SendLifecycleAsync("models/load", modelId, cancellationToken);

        /// <summary>Submits an explicit router unload command. It may also stop a router-managed download.</summary>
        public Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default) =>
            SendLifecycleAsync("models/unload", modelId, cancellationToken);

        /// <summary>
        /// Subscribes to SSE before starting a router download and waits for that model's download_finished event.
        /// Cancellation stops this client's requests and observation; it does not submit an unload or cancel remote work.
        /// </summary>
        public async Task DownloadModelAsync(string modelId, IProgress<ModelDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateModelId(modelId);
            cancellationToken.ThrowIfCancellationRequested();
            await RequireRouterAsync(cancellationToken).ConfigureAwait(false);
            using (var stream = await _transport.OpenStreamAsync(HttpMethod.Get, "models/sse", null, cancellationToken).ConfigureAwait(false))
            {
                RequireEventStream(stream);
                var accepted = await _transport.PostObjectAsync("models", new { model = modelId }, cancellationToken).ConfigureAwait(false);
                RequireSuccess(accepted);
                progress?.Report(new ModelDownloadProgress(modelId, "accepted"));
                var data = new StringBuilder();
                await foreach (var line in stream.ReadLinesAsync(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line.Length == 0)
                    {
                        if (data.Length == 0) continue;
                        var finished = ProcessDownloadEvent(data.ToString(), modelId, progress);
                        data.Clear();
                        if (finished)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return;
                        }
                    }
                    else if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        var value = line.Substring(5);
                        if (value.StartsWith(" ", StringComparison.Ordinal)) value = value.Substring(1);
                        if (data.Length + value.Length > 1024 * 1024)
                            throw new ServingException("The llama.cpp download event exceeded the supported size.", failureKind: ServingFailureKind.InvalidResponse);
                        if (data.Length != 0) data.Append('\n');
                        data.Append(value);
                    }
                    // Standard SSE comments, id, event and retry lines do not contain the native JSON event.
                }
                throw new ServingException("The llama.cpp download stream ended before model completion was confirmed.");
            }
        }

        /// <summary>Reads single-model server metrics. Router metrics require the model-specific overload.</summary>
        public async Task<ServerMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
        {
            var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
            if (info.Mode == ServerMode.Router)
                throw new NotSupportedException("Router metrics require an explicit model. Use the model-specific metrics overload.");
            if (info.Mode != ServerMode.SingleModel)
                throw new ServingException("The server mode could not be verified for a metrics request.");
            return ServingMetricsParser.Parse(await _transport.GetTextAsync("metrics", cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Reads one router model's metrics with autoload=false, so inspection never loads a model.</summary>
        public async Task<ServerMetrics> GetMetricsAsync(string modelId, CancellationToken cancellationToken = default)
        {
            ValidateModelId(modelId);
            await RequireRouterAsync(cancellationToken).ConfigureAwait(false);
            var path = "metrics?model=" + Uri.EscapeDataString(modelId) + "&autoload=false";
            return ServingMetricsParser.Parse(await _transport.GetTextAsync(path, cancellationToken).ConfigureAwait(false));
        }

        private async Task SendLifecycleAsync(string path, string modelId, CancellationToken cancellationToken)
        {
            ValidateModelId(modelId);
            cancellationToken.ThrowIfCancellationRequested();
            await RequireRouterAsync(cancellationToken).ConfigureAwait(false);
            RequireSuccess(await _transport.PostObjectAsync(path, new { model = modelId }, cancellationToken).ConfigureAwait(false));
        }

        private async Task RequireRouterAsync(CancellationToken cancellationToken)
        {
            var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
            if (info.Mode == ServerMode.SingleModel)
                throw new NotSupportedException("This operation requires llama.cpp router mode.");
            if (info.Mode != ServerMode.Router)
                throw new ServingException("The server could not be verified as a llama.cpp router; no management command was sent.");
        }

        private async Task<JObject?> TryPropertiesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var props = await _transport.GetObjectAsync("props", cancellationToken).ConfigureAwait(false);
                LlamaCppModelParser.GetMode(props);
                return props;
            }
            catch (ServingException) { return null; }
        }

        private static ServingFeatureSupport MissingOrUnknown(ServingException error) =>
            error.StatusCode == 404 || error.StatusCode == 405 || error.StatusCode == 501
                ? ServingFeatureSupport.Unsupported : ServingFeatureSupport.Unknown;

        private static void RequireSuccess(JObject response)
        {
            if (LlamaCppModelParser.HasError(response) || response["success"]?.Type != JTokenType.Boolean || !(bool)response["success"]!)
                throw new ServingException("The llama.cpp server did not acknowledge the management command.");
        }

        private static void RequireEventStream(ServingStream stream)
        {
            if (!string.Equals(stream.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                throw new ServingException("The llama.cpp server did not provide an SSE event stream; no download was started.");
        }

        private static void ValidateModelId(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 4096)
                throw new ArgumentException("A non-empty model identifier of at most 4096 characters is required.", nameof(modelId));
        }

        private static bool ProcessDownloadEvent(string json, string modelId, IProgress<ModelDownloadProgress>? progress)
        {
            JObject item;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
                {
                    item = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw LlamaCppModelParser.InvalidResponse();
                }
            }
            catch (JsonException) { throw LlamaCppModelParser.InvalidResponse(); }
            var eventModel = LlamaCppModelParser.RequiredString(item["model"]);
            var eventName = LlamaCppModelParser.RequiredString(item["event"]);
            if (!string.Equals(eventModel, modelId, StringComparison.Ordinal)) return false;
            if (LlamaCppModelParser.HasError(item) || eventName == "download_failed")
                throw new ServingException("The llama.cpp server reported that the model download failed.");
            if (eventName == "download_finished")
            {
                progress?.Report(new ModelDownloadProgress(modelId, "completed"));
                return true;
            }
            if (eventName != "download_progress") return false;
            if (!(item["data"] is JObject data)) throw LlamaCppModelParser.InvalidResponse();
            // Current native source wraps progress; the official README also documents the direct map.
            var files = data["progress"] == null ? data : data["progress"] as JObject ?? throw LlamaCppModelParser.InvalidResponse();
            if (files.Count == 0 || files.Count > 10000) throw LlamaCppModelParser.InvalidResponse();
            var updates = new List<ModelDownloadProgress>(files.Count);
            foreach (var file in files.Properties())
            {
                if (!(file.Value is JObject counts)) throw LlamaCppModelParser.InvalidResponse();
                var done = LlamaCppModelParser.OptionalCount(counts["done"]);
                var total = LlamaCppModelParser.OptionalCount(counts["total"]);
                if (!done.HasValue || !total.HasValue || (total > 0 && done > total))
                    throw LlamaCppModelParser.InvalidResponse();
                // Zero means the native downloader has not learned the content length yet.
                updates.Add(new ModelDownloadProgress(modelId, "downloading", done, total == 0 ? null : total,
                    artifact: file.Name));
            }
            foreach (var update in updates) progress?.Report(update);
            return false;
        }
    }
}
