using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        /// <summary>
        /// Gemini 3.6 Flash and Gemini 3.5 Flash-Lite ignore legacy sampling controls;
        /// migration guidance for Gemini 3.7 and 3.8 Flash requires omitting them.
        /// </summary>
        private bool UsesLatestSamplingContract()
        {
            return RequestModel != null &&
                   (RequestModel.StartsWith(AIModels.Google.Gemini3_6Flash, StringComparison.OrdinalIgnoreCase) ||
                    RequestModel.StartsWith(AIModels.Google.Gemini3_5FlashLite, StringComparison.OrdinalIgnoreCase) ||
                    IsGemini37Or38FlashModel());
        }

        private bool IsGemini37Or38FlashModel() =>
            IsModelOrSnapshot(AIModels.Google.Gemini3_7Flash) ||
            IsModelOrSnapshot(AIModels.Google.Gemini3_8Flash);

        private bool IsModelOrSnapshot(string model) =>
            string.Equals(RequestModel, model, StringComparison.OrdinalIgnoreCase) ||
            (RequestModel?.StartsWith(model + "-", StringComparison.OrdinalIgnoreCase) ?? false);

        private bool HasLowThinkingFloor() =>
            IsGemini3Model() &&
            (RequestModel.Contains("-pro", StringComparison.OrdinalIgnoreCase) || IsGemini37Or38FlashModel());

        private void ApplyTextGenerationConfig(
            Dictionary<string, object> generationConfig,
            bool includeCandidateCount,
            bool includeThoughts)
        {
            generationConfig["maxOutputTokens"] = (int)GetEffectiveMaxTokens();

            if (!UsesLatestSamplingContract())
            {
                generationConfig["temperature"] = RequestTemperature;
                generationConfig["topP"] = RequestTopP;
                generationConfig["topK"] = DefaultTopK;
            }

            if (includeCandidateCount && !IsGemini3Model())
                generationConfig["candidateCount"] = DefaultCandidateCount;

            ApplyThinkingConfig(generationConfig);
            ApplyIncludeThoughtsConfig(generationConfig, includeThoughts);
            ApplyStructuredOutputConfig(generationConfig);
        }

        private void ApplyStructuredOutputConfig(Dictionary<string, object> generationConfig)
        {
            if (RequestStructuredOutputSchemaJson == null)
                return;

            try
            {
                using var schemaDocument = JsonDocument.Parse(RequestStructuredOutputSchemaJson);
                generationConfig["responseFormat"] = new Dictionary<string, object>
                {
                    ["text"] = new Dictionary<string, object>
                    {
                        ["mimeType"] = "APPLICATION_JSON",
                        ["schema"] = schemaDocument.RootElement.Clone()
                    }
                };
            }
            catch (JsonException exception)
            {
                throw new AIServiceException(
                    "The structured-output schema could not be serialized for Gemini.",
                    exception);
            }
        }

        private HttpRequestMessage CreateGoogleRequest(
            HttpMethod method,
            string endpoint,
            HttpContent content)
        {
            var request = new HttpRequestMessage(method, endpoint)
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("x-goog-api-key", ApiKey);
            return request;
        }
    }
}
