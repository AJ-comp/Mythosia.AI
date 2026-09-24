using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        // Explicitly documented Fast mode models. Unknown models and fine-tunes must not
        // inherit a paid processing capability from a model-name prefix.
        private static readonly HashSet<string> FastModeModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-5.6", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.3-codex"
        };

        private readonly AsyncLocal<SpeedStreamState?> _speedStreamState = new AsyncLocal<SpeedStreamState?>();

        protected override CapabilitySupport ResolveSpeedSupport(InferenceSpeed speed)
        {
            if (speed == InferenceSpeed.ProviderDefault) return CapabilitySupport.Supported;
            var endpoint = HttpClient.BaseAddress;
            if (endpoint == null || endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.IsDefaultPort ||
                !string.Equals(endpoint.AbsolutePath, "/v1/", StringComparison.Ordinal) ||
                !string.Equals(endpoint.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase))
                return CapabilitySupport.Unknown;
            if (!IsKnownOpenAIChatModel(RequestModel)) return CapabilitySupport.Unknown;
            if (speed == InferenceSpeed.Standard) return CapabilitySupport.Supported;
            return speed == InferenceSpeed.Fast && FastModeModels.Contains(RequestModel)
                ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
        }

        private void ApplySpeedParameter(IDictionary<string, object> body)
        {
            if (RequestSpeed == InferenceSpeed.Standard) body["service_tier"] = "default";
            else if (RequestSpeed == InferenceSpeed.Fast) body["service_tier"] = "fast";
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
            if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
                root = response;
            var raw = root.TryGetProperty("service_tier", out var tier) && tier.ValueKind == JsonValueKind.String
                ? tier.GetString() : null;
            var id = root.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.String
                ? responseId.GetString() : null;
            InferenceSpeed? applied = raw == "default" ? InferenceSpeed.Standard
                : raw == "fast" || raw == "priority" ? InferenceSpeed.Fast : (InferenceSpeed?)null;
            observation.Record(raw, applied, id);
        }

        private sealed class SpeedStreamState
        {
            public ProcessingObservation? Observation { get; set; }
        }
    }
}
