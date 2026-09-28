using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Mythosia.AI.Serving.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Serving.Vllm
{
    public partial class VllmServer : IModelServer, IModelMetricsProvider
    {
        /// <summary>
        /// Reads the server version into the common serving contract. The configured runtime
        /// is vLLM; model-list aliases alone do not establish a server mode or load state.
        /// </summary>
        public async Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        {
            var body = await GetCommonStringAsync("version", cancellationToken).ConfigureAwait(false);
            try
            {
                var response = ParseCommonObject(body, "version");
                var version = response["version"];
                if (version != null && version.Type != JTokenType.Null && version.Type != JTokenType.String)
                    throw InvalidCommonResponse("version");
                return new ServerInfo("vllm", Endpoint, (string?)version);
            }
            catch (JsonException)
            {
                throw InvalidCommonResponse("version");
            }
        }

        /// <summary>
        /// Probes model listing and metrics on this server. A valid response demonstrates
        /// support; missing routes are unsupported, while authentication, transport and
        /// malformed-response failures leave support unknown. No lifecycle operation is run.
        /// </summary>
        public async Task<ServingCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            var modelListing = await ProbeAsync(async () =>
            {
                await GetCommonModelsAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            var metrics = await ProbeAsync(async () =>
            {
                await GetCommonMetricsAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            return new ServingCapabilities(
                modelListing: modelListing,
                modelLoading: ServingFeatureSupport.Unsupported,
                modelUnloading: ServingFeatureSupport.Unsupported,
                modelDownloading: ServingFeatureSupport.Unsupported,
                metrics: metrics);
        }

        Task<IReadOnlyList<ServerModel>> IModelServer.GetModelsAsync(CancellationToken cancellationToken)
            => GetCommonModelsAsync(cancellationToken);

        async Task<ServerHealth> IModelServer.GetHealthAsync(CancellationToken cancellationToken)
        {
            var health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
            var status = health.Status switch
            {
                VllmHealthStatus.Healthy => ServerHealthStatus.Healthy,
                VllmHealthStatus.EngineDead => ServerHealthStatus.NotReady,
                VllmHealthStatus.Unauthorized => ServerHealthStatus.Unauthorized,
                VllmHealthStatus.Unreachable => ServerHealthStatus.Unreachable,
                _ => ServerHealthStatus.Unexpected,
            };
            var detail = status switch
            {
                ServerHealthStatus.Healthy => null,
                ServerHealthStatus.NotReady => "The vLLM engine is not ready.",
                ServerHealthStatus.Unauthorized => "The vLLM health endpoint requires valid authorization.",
                ServerHealthStatus.Unreachable => "The vLLM health endpoint could not be reached.",
                _ => "The vLLM health endpoint returned an unexpected status.",
            };
            return new ServerHealth(status, health.StatusCode, detail);
        }

        Task<ServerMetrics> IModelMetricsProvider.GetMetricsAsync(CancellationToken cancellationToken)
            => GetCommonMetricsAsync(cancellationToken);

        private async Task<IReadOnlyList<ServerModel>> GetCommonModelsAsync(CancellationToken cancellationToken)
        {
            var body = await GetCommonStringAsync("v1/models", cancellationToken).ConfigureAwait(false);
            try
            {
                var response = ParseCommonObject(body, "v1/models");
                if (!(response["data"] is JArray cards))
                    throw InvalidCommonResponse("v1/models");

                var models = new List<ServerModel>(cards.Count);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in cards)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!(item is JObject card) || card["id"]?.Type != JTokenType.String
                        || string.IsNullOrWhiteSpace((string?)card["id"]))
                        throw InvalidCommonResponse("v1/models");
                    if (!IsOptionalType(card["root"], JTokenType.String)
                        || !IsOptionalType(card["max_model_len"], JTokenType.Integer))
                        throw InvalidCommonResponse("v1/models");

                    var id = (string)card["id"]!;
                    if (!ids.Add(id)) throw InvalidCommonResponse("v1/models");
                    var root = (string?)card["root"];
                    int? contextLength = (int?)card["max_model_len"];
                    if (contextLength < 0) throw InvalidCommonResponse("v1/models");
                    models.Add(new ServerModel(id,
                        displayName: string.IsNullOrEmpty(root) ? id : root,
                        installationState: ModelInstallationState.Unknown,
                        loadState: ModelLoadState.Unknown,
                        contextLength: contextLength));
                }
                return models.AsReadOnly();
            }
            catch (JsonException)
            {
                throw InvalidCommonResponse("v1/models");
            }
            catch (OverflowException)
            {
                throw InvalidCommonResponse("v1/models");
            }
        }

        private async Task<ServerMetrics> GetCommonMetricsAsync(CancellationToken cancellationToken)
        {
            var body = await GetCommonStringAsync("metrics", cancellationToken).ConfigureAwait(false);
            return ServingMetricsParser.Parse(body);
        }

        private static bool IsOptionalType(JToken? value, JTokenType type)
            => value == null || value.Type == JTokenType.Null || value.Type == type;

        private static VllmException InvalidCommonResponse(string route)
            => new VllmException($"The vLLM /{route} response does not match the expected serving format.", 200, ServingFailureKind.InvalidResponse);

        private static JObject ParseCommonObject(string body, string route)
        {
            using var reader = new JsonTextReader(new StringReader(body)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            var response = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read() || (response["error"] != null && response["error"]!.Type != JTokenType.Null))
                throw InvalidCommonResponse(route);
            return response;
        }

        private async Task<string> GetCommonStringAsync(string route, CancellationToken cancellationToken)
        {
            try
            {
                // The common contract uses a bounded, cancellation-aware body lifetime.
                // Legacy concrete methods retain their existing diagnostics and parser behavior.
                var transport = new ServingHttpTransport(Endpoint.AbsoluteUri, _httpClient, _apiKey);
                return await transport.GetTextAsync(route, cancellationToken).ConfigureAwait(false);
            }
            catch (ServingException ex) when (ex.StatusCode.HasValue)
            {
                // Preserve the legacy exception shape without forwarding arbitrary server
                // error bodies or messages through the new common management surface.
                throw new VllmException($"The vLLM /{route} endpoint returned HTTP {ex.StatusCode}.", ex.StatusCode.Value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new ServingException($"The vLLM /{route} request timed out.");
            }
            catch (HttpRequestException)
            {
                throw new ServingException($"The vLLM /{route} endpoint could not be reached.");
            }
            catch (IOException)
            {
                throw new ServingException($"The vLLM /{route} response could not be read.");
            }
        }

        private static async Task<ServingFeatureSupport> ProbeAsync(Func<Task> probe, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await probe().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return ServingFeatureSupport.Supported;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ServingException ex) when (ex.StatusCode == 404 || ex.StatusCode == 405 || ex.StatusCode == 501)
            {
                return ServingFeatureSupport.Unsupported;
            }
            catch (ServingException)
            {
                return ServingFeatureSupport.Unknown;
            }
            catch (HttpRequestException)
            {
                return ServingFeatureSupport.Unknown;
            }
            catch (IOException)
            {
                return ServingFeatureSupport.Unknown;
            }
            catch (OperationCanceledException)
            {
                return ServingFeatureSupport.Unknown;
            }
        }
    }
}
