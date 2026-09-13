using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private const string NativeGeminiPartsKey = "mythosia_google_native_parts";

        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            var reasoning = features.Reasoning;
            if (reasoning != null)
            {
                if (reasoning.Cache == CachePreservation.Required)
                    throw new NotSupportedException("Gemini does not support cache-preserving per-message reasoning changes.");
                var level = reasoning.Level;
                if (!GetCommonGeminiReasoningLevels().Contains(level))
                    throw new NotSupportedException(
                        $"Gemini model '{RequestModel}' does not support reasoning level {level}. Use its provider-specific ThinkingBudget where supported.");
            }

            if (features.WebSearch == null && features.FileSearch == null)
                return;
            if (!IsGeminiSearchAdapterModel())
                throw new NotSupportedException($"Gemini model '{RequestModel}' is not supported by the native search adapter.");
            if (features.WebSearch?.AllowedDomains?.Count > 0)
                throw new NotSupportedException("Gemini Google Search does not expose an allowed-domain filter.");
            if (features.WebSearch != null && features.FileSearch != null)
                throw new NotSupportedException("Gemini File Search and Google Search cannot be used in the same request.");
            if (ShouldUseFunctions && !IsGemini3Model())
                throw new NotSupportedException("Combining Gemini built-in search with client functions requires a Gemini 3 model.");
            if (features.FileSearch != null)
            {
                if (features.FileSearch.Stores == null || features.FileSearch.Stores.Count == 0)
                    throw new ArgumentException("Gemini File Search requires at least one file search store.");
                foreach (var store in features.FileSearch.Stores)
                {
                    if (!string.Equals(store.Provider, Provider, StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Gemini File Search requires Google file search stores.");
                    if (!store.Id.StartsWith("fileSearchStores/", StringComparison.Ordinal) ||
                        store.Id.Length == "fileSearchStores/".Length)
                        throw new ArgumentException("Google file search store IDs must use fileSearchStores/{id}.");
                }
                if (!IsGemini3Model() && RequestStructuredOutputSchemaJson != null)
                    throw new NotSupportedException("Gemini File Search with structured output requires a Gemini 3 model.");
            }
        }

        private bool HasNativeGeminiSearch =>
            CurrentRequestFeatures.WebSearch != null || CurrentRequestFeatures.FileSearch != null;

        private bool ApplyCommonGeminiReasoning(Dictionary<string, object> generationConfig)
        {
            var reasoning = CurrentRequestFeatures.Reasoning;
            if (reasoning == null || reasoning.Level == ReasoningLevel.Auto)
                return false;
            generationConfig["thinkingConfig"] = reasoning.Level == ReasoningLevel.None
                ? new Dictionary<string, object> { ["thinkingBudget"] = 0 }
                : new Dictionary<string, object> { ["thinkingLevel"] = reasoning.Level.ToString().ToUpperInvariant() };
            return true;
        }

        private void ApplyNativeGeminiTools(Dictionary<string, object> requestBody)
        {
            if (!HasNativeGeminiSearch) return;
            var tools = requestBody.TryGetValue("tools", out var existing) && existing is IEnumerable<object> declared
                ? declared.ToList()
                : new List<object>();
            if (CurrentRequestFeatures.WebSearch != null)
                tools.Add(new Dictionary<string, object> { ["googleSearch"] = new Dictionary<string, object>() });
            if (CurrentRequestFeatures.FileSearch != null)
                tools.Add(new Dictionary<string, object>
                {
                    ["fileSearch"] = new Dictionary<string, object>
                    {
                        ["fileSearchStoreNames"] = CurrentRequestFeatures.FileSearch.Stores.Select(store => store.Id).ToArray()
                    }
                });
            requestBody["tools"] = tools;
            if (ShouldUseFunctions)
            {
                var config = requestBody.TryGetValue("toolConfig", out var existingConfig) &&
                             existingConfig is Dictionary<string, object> dictionary
                    ? dictionary
                    : new Dictionary<string, object>();
                // Circulate provider-owned toolCall/toolResponse parts as well as client function calls.
                config["includeServerSideToolInvocations"] = true;
                requestBody["toolConfig"] = config;
            }
        }

        private void RecordGeminiCitations(string response)
        {
            try
            {
                using var document = JsonDocument.Parse(response);
                foreach (var citation in ExtractGeminiCitations(document.RootElement))
                    RecordCitation(citation);
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException)
            {
                // The existing response validator owns malformed-envelope errors and their
                // AIServiceException contract. Optional grounding metadata must not bypass it.
            }
        }

        private IEnumerable<AICitation> ExtractGeminiCitations(JsonElement root)
        {
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
                yield break;
            var candidate = candidates[0];
            if (!candidate.TryGetProperty("groundingMetadata", out var grounding) ||
                !grounding.TryGetProperty("groundingChunks", out var chunks) || chunks.ValueKind != JsonValueKind.Array)
                yield break;
            var responseId = ReadGeminiString(root, "responseId");
            var referenced = new HashSet<int>();
            if (grounding.TryGetProperty("groundingSupports", out var supports) && supports.ValueKind == JsonValueKind.Array)
            {
                foreach (var support in supports.EnumerateArray())
                {
                    if (!support.TryGetProperty("groundingChunkIndices", out var indices) ||
                        indices.ValueKind != JsonValueKind.Array) continue;
                    support.TryGetProperty("segment", out var segment);
                    foreach (var indexElement in indices.EnumerateArray())
                    {
                        if (!indexElement.TryGetInt32(out var index) || index < 0 || index >= chunks.GetArrayLength())
                            continue;
                        referenced.Add(index);
                        var citation = CreateGeminiCitation(chunks[index], responseId);
                        citation.ContentIndex = ReadGeminiInt(segment, "partIndex");
                        citation.StartIndex = ReadGeminiInt(segment, "startIndex");
                        citation.EndIndex = ReadGeminiInt(segment, "endIndex");
                        citation.Text = ReadGeminiString(segment, "text") ?? citation.Text;
                        yield return citation;
                    }
                }
            }
            for (var index = 0; index < chunks.GetArrayLength(); index++)
                if (!referenced.Contains(index))
                    yield return CreateGeminiCitation(chunks[index], responseId);
        }

        private AICitation CreateGeminiCitation(JsonElement chunk, string? responseId)
        {
            var web = chunk.TryGetProperty("web", out var source);
            if (!web) chunk.TryGetProperty("retrievedContext", out source);
            return new AICitation
            {
                Provider = Provider,
                ResponseId = responseId,
                OutputIndex = 0,
                Url = ReadGeminiString(source, "uri"),
                FileId = web ? null : ReadGeminiString(source, "fileSearchStore") ?? ReadGeminiString(source, "uri"),
                Title = ReadGeminiString(source, "title"),
                Text = ReadGeminiString(source, "text")
            };
        }

        private static string? ReadGeminiString(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static int? ReadGeminiInt(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
            value.TryGetInt32(out var number) ? number : (int?)null;

        private void PreserveNativeGeminiParts(Message message, string response)
        {
            if (!HasNativeGeminiSearch) return;
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts))
            {
                message.Metadata ??= new Dictionary<string, object>();
                message.Metadata[NativeGeminiPartsKey] = parts.GetRawText();
            }
        }
    }
}
