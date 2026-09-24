using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        protected override CapabilitySupport ResolveSpeedSupport(InferenceSpeed speed)
        {
            if (speed == InferenceSpeed.ProviderDefault) return CapabilitySupport.Supported;
            var endpoint = HttpClient.BaseAddress;
            // This adapter uses Developer API generateContent/streamGenerateContent. Vertex
            // Priority PayGo has a different request header and response contract.
            var supported = endpoint != null && endpoint.Scheme == Uri.UriSchemeHttps && endpoint.IsDefaultPort &&
                string.Equals(endpoint.Host, "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) &&
                endpoint.AbsolutePath == "/" && KnownGeminiChatModels.Contains(RequestModel);
            return supported && (speed == InferenceSpeed.Standard || speed == InferenceSpeed.Fast)
                ? CapabilitySupport.Supported : CapabilitySupport.Unsupported;
        }

        private void ApplyGeminiSpeed(Dictionary<string, object> body)
        {
            // A top-level GenerateContentRequest field, not a generationConfig setting.
            if (RequestSpeed == InferenceSpeed.Standard) body["serviceTier"] = "standard";
            else if (RequestSpeed == InferenceSpeed.Fast) body["serviceTier"] = "priority";
        }

        private static InferenceSpeed? MapGeminiSpeed(string? tier) => tier == "priority"
            ? InferenceSpeed.Fast : tier == "standard" ? InferenceSpeed.Standard : (InferenceSpeed?)null;

        private static void RecordGeminiProcessingHeaders(HttpResponseMessage response, ProcessingObservation observation)
        {
            if (response.Headers.TryGetValues("x-gemini-service-tier", out var values))
            {
                var tier = values.FirstOrDefault();
                observation.Record(tier, MapGeminiSpeed(tier));
            }
        }

        private static void RecordGeminiProcessing(string response, ProcessingObservation observation)
        {
            try
            {
                using var document = JsonDocument.Parse(response);
                RecordGeminiProcessing(document.RootElement, observation);
            }
            catch (JsonException) { } // Keep the existing response-validation behavior.
        }

        private static void RecordGeminiProcessing(JsonElement root, ProcessingObservation observation)
        {
            if (root.ValueKind != JsonValueKind.Object) return;
            string? tier = null;
            if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object &&
                usage.TryGetProperty("serviceTier", out var value) && value.ValueKind == JsonValueKind.String)
                tier = value.GetString();
            var responseId = root.TryGetProperty("responseId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() : null;
            observation.Record(tier, MapGeminiSpeed(tier), responseId);
        }
    }
}
