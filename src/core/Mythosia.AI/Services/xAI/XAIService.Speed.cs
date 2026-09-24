using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.xAI
{
    public partial class XAIService
    {
        private readonly AsyncLocal<SpeedStreamState?> _speedStreamState = new AsyncLocal<SpeedStreamState?>();

        protected override CapabilitySupport ResolveSpeedSupport(InferenceSpeed speed)
        {
            if (speed == InferenceSpeed.ProviderDefault) return CapabilitySupport.Supported;
            var endpoint = HttpClient.BaseAddress;
            if (endpoint == null || endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.IsDefaultPort ||
                !string.Equals(endpoint.AbsolutePath, "/v1/", StringComparison.Ordinal) ||
                !(string.Equals(endpoint.Host, "api.x.ai", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(endpoint.Host, "us.api.x.ai", StringComparison.OrdinalIgnoreCase)))
                return CapabilitySupport.Unknown;
            if (!KnownGrokChatModels.Contains(RequestModel)) return CapabilitySupport.Unknown;
            if (string.Equals(endpoint.Host, "us.api.x.ai", StringComparison.OrdinalIgnoreCase) &&
                !IsGrok46Or47(GetModelFamily()))
                return CapabilitySupport.Unsupported;
            return speed == InferenceSpeed.Standard || speed == InferenceSpeed.Fast
                ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
        }

        protected override void OnStreamRoundStarting()
        {
            base.OnStreamRoundStarting();
            _speedStreamState.Value = new SpeedStreamState();
        }

        protected override Task<HttpResponseMessage> SendStreamingRequestAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_speedStreamState.Value != null)
                _speedStreamState.Value.Observation = BeginProcessingObservation();
            return base.SendStreamingRequestAsync(request, cancellationToken);
        }

        private static void CaptureProcessing(ProcessingObservation? observation, string json)
        {
            if (observation == null) return;
            try
            {
                using var document = JsonDocument.Parse(json);
                CaptureProcessing(observation, document.RootElement);
            }
            catch (JsonException) { /* The normal response parser reports malformed responses. */ }
        }

        private static void CaptureProcessing(ProcessingObservation? observation, JsonElement root)
        {
            if (observation == null || root.ValueKind != JsonValueKind.Object) return;
            var raw = root.TryGetProperty("service_tier", out var tier) && tier.ValueKind == JsonValueKind.String
                ? tier.GetString() : null;
            var id = root.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.String
                ? responseId.GetString() : null;
            InferenceSpeed? applied = raw == "default" ? InferenceSpeed.Standard
                : raw == "priority" ? InferenceSpeed.Fast : (InferenceSpeed?)null;
            observation.Record(raw, applied, id);
        }

        private sealed class SpeedStreamState
        {
            public ProcessingObservation? Observation { get; set; }
        }
    }
}
