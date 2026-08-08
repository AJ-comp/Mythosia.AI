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
        /// Gemini 3.6 Flash and Gemini 3.5 Flash-Lite ignore legacy sampling controls.
        /// Google has also announced that later models may reject these fields outright.
        /// </summary>
        private bool UsesLatestSamplingContract()
        {
            return Model != null &&
                   (Model.StartsWith(AIModels.Google.Gemini3_6Flash, StringComparison.OrdinalIgnoreCase) ||
                    Model.StartsWith(AIModels.Google.Gemini3_5FlashLite, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyTextGenerationConfig(
            Dictionary<string, object> generationConfig,
            bool includeCandidateCount,
            bool includeThoughts)
        {
            generationConfig["maxOutputTokens"] = (int)GetEffectiveMaxTokens();

            if (!UsesLatestSamplingContract())
            {
                generationConfig["temperature"] = Temperature;
                generationConfig["topP"] = TopP;
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
            if (_structuredOutputSchemaJson == null)
                return;

            try
            {
                using var schemaDocument = JsonDocument.Parse(_structuredOutputSchemaJson);
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
