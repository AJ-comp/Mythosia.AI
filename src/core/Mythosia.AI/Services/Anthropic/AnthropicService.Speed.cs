using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        private const string ClaudeFastModeBeta = "fast-mode-2026-02-01";

        protected override CapabilitySupport ResolveSpeedSupport(InferenceSpeed speed)
        {
            if (speed == InferenceSpeed.ProviderDefault) return CapabilitySupport.Supported;
            var endpoint = HttpClient.BaseAddress;
            if (endpoint == null || endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.IsDefaultPort ||
                !string.Equals(endpoint.Host, "api.anthropic.com", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(endpoint.AbsolutePath, "/v1/", StringComparison.Ordinal))
                return CapabilitySupport.Unsupported;

            if (speed == InferenceSpeed.Standard)
                return IsKnownClaudeModel(RequestModel) ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;

            // Fast is a model- and platform-specific research preview. In particular, Opus 4.6
            // silently serves standard speed and Opus 4.7 rejects fast, so neither is supported here.
            return speed == InferenceSpeed.Fast && SupportsClaudeSpeedParameter()
                ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
        }

        private bool SupportsClaudeSpeedParameter() =>
            string.Equals(RequestModel, AIModels.Anthropic.ClaudeOpus4_8, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(RequestModel, AIModels.Anthropic.ClaudeOpus5, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(RequestModel, AIModels.Anthropic.ClaudeOpus5_5, StringComparison.OrdinalIgnoreCase);

        private void ApplyClaudeSpeed(Dictionary<string, object> body)
        {
            // Other known Claude models only offer normal inference and reject the speed
            // parameter, even with the beta header. Standard uses their normal request.
            if (!SupportsClaudeSpeedParameter()) return;
            if (RequestSpeed == InferenceSpeed.Standard) body["speed"] = "standard";
            else if (RequestSpeed == InferenceSpeed.Fast) body["speed"] = "fast";
        }

        // Called only for generation requests; token counting must not opt in to fast mode.
        private void AddClaudeSpeedHeader(HttpRequestMessage request)
        {
            // Both explicit speed values belong to the beta Messages schema. Without the
            // header the API rejects speed:"standard" as an unknown input as well.
            if (SupportsClaudeSpeedParameter() &&
                (RequestSpeed == InferenceSpeed.Standard || RequestSpeed == InferenceSpeed.Fast))
                request.Headers.TryAddWithoutValidation("anthropic-beta", ClaudeFastModeBeta);
        }

        private static InferenceSpeed? MapClaudeSpeed(string? speed) => speed == "fast"
            ? InferenceSpeed.Fast : speed == "standard" ? InferenceSpeed.Standard : (InferenceSpeed?)null;

        private static void RecordClaudeProcessing(string response, ProcessingObservation observation)
        {
            try
            {
                using var document = JsonDocument.Parse(response);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                var speed = root.TryGetProperty("usage", out var usage) ? ReadClaudeString(usage, "speed") : null;
                observation.Record(speed, MapClaudeSpeed(speed), ReadClaudeString(root, "id"));
            }
            catch (JsonException) { } // Existing response validation remains responsible for malformed bodies.
        }
    }
}
