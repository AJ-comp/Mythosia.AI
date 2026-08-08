using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private const int DefaultCandidateCount = 1;
        private const string DefaultImageMimeType = "image/jpeg";

        #region Request Body Building

        private object BuildRequestBody(bool includeThoughts = false)
        {
            var contentsList = new List<object>();
            var messages = GetLatestMessagesWithFunctionFallback().ToList();
            EnsureUserFirstMessage(messages);
            foreach (var message in messages)
            {
                contentsList.Add(ConvertMessageForGemini(message));
            }

            var generationConfig = new Dictionary<string, object>();
            ApplyTextGenerationConfig(
                generationConfig,
                includeCandidateCount: true,
                includeThoughts);

            var requestBody = new Dictionary<string, object>
            {
                ["contents"] = contentsList,
                ["generationConfig"] = generationConfig
            };

            ApplySystemInstruction(requestBody);
            ApplySafetySettings(requestBody);

            return requestBody;
        }

        private static void ApplyIncludeThoughtsConfig(Dictionary<string, object> generationConfig, bool includeThoughts)
        {
            if (!includeThoughts)
                return;

            if (generationConfig.TryGetValue("thinkingConfig", out var existingConfigObj) &&
                existingConfigObj is Dictionary<string, object> existingThinkingConfig)
            {
                existingThinkingConfig["includeThoughts"] = true;
                return;
            }

            generationConfig["thinkingConfig"] = new Dictionary<string, object>
            {
                ["includeThoughts"] = true
            };
        }

        private object ConvertMessageForGemini(Message message)
        {
            var role = GetGeminiRole(message);
            var thoughtSig = ExtractThoughtSignature(message);

            if (!message.HasMultimodalContent)
                return BuildTextOnlyGeminiContent(role, message.Content ?? "", thoughtSig);

            return BuildMultimodalGeminiContent(role, message, thoughtSig);
        }

        private static string GetGeminiRole(Message message)
        {
            return message.Role == ActorRole.Assistant ? "model" : "user";
        }

        private static string? ExtractThoughtSignature(Message message)
        {
            if (message.Metadata == null) return null;
            if (!message.Metadata.TryGetValue(MessageMetadataKeys.ThoughtSignature, out var sigObj)) return null;

            return sigObj?.ToString();
        }

        private static Dictionary<string, object> BuildTextOnlyGeminiContent(string role, string text, string? thoughtSig)
        {
            var textPart = new Dictionary<string, object> { ["text"] = text };
            if (thoughtSig != null)
            {
                textPart["thoughtSignature"] = thoughtSig;
            }

            return new Dictionary<string, object>
            {
                ["role"] = role,
                ["parts"] = new[] { textPart }
            };
        }

        private Dictionary<string, object> BuildMultimodalGeminiContent(string role, Message message, string? thoughtSig)
        {
            var parts = new List<object>();
            bool sigAttached = false;

            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent)
                {
                    var part = new Dictionary<string, object> { ["text"] = textContent.Text };
                    if (thoughtSig != null && !sigAttached)
                    {
                        part["thoughtSignature"] = thoughtSig;
                        sigAttached = true;
                    }
                    parts.Add(part);
                }
                else if (content is ImageContent imageContent)
                {
                    parts.Add(ConvertImageForGemini(imageContent));
                }
                else
                {
                    throw new NotSupportedException(
                        $"Gemini message conversion does not support content type '{content.Type}'.");
                }
            }

            if (thoughtSig != null && !sigAttached && parts.Count > 0 && parts[0] is Dictionary<string, object> firstPart)
            {
                firstPart["thoughtSignature"] = thoughtSig;
            }

            return new Dictionary<string, object>
            {
                ["role"] = role,
                ["parts"] = parts
            };
        }

        private static object ConvertImageForGemini(ImageContent imageContent)
        {
            if (imageContent.Data != null)
            {
                return new Dictionary<string, object>
                {
                    ["inlineData"] = new Dictionary<string, object>
                    {
                        ["mimeType"] = imageContent.MimeType ?? DefaultImageMimeType,
                        ["data"] = Convert.ToBase64String(imageContent.Data)
                    }
                };
            }

            if (!string.IsNullOrEmpty(imageContent.Url))
                throw new NotSupportedException("Gemini API requires base64 encoded images. Please download the image and provide as byte array.");

            throw new ArgumentException("Image content must have either Data or Url");
        }

        private void ApplySafetySettings(Dictionary<string, object> requestBody)
        {
            var settings = new List<object>();
            AddSafetySetting(settings, "HARM_CATEGORY_HARASSMENT", HarassmentSafetyThreshold);
            AddSafetySetting(settings, "HARM_CATEGORY_HATE_SPEECH", HateSpeechSafetyThreshold);
            AddSafetySetting(settings, "HARM_CATEGORY_SEXUALLY_EXPLICIT", SexuallyExplicitSafetyThreshold);
            AddSafetySetting(settings, "HARM_CATEGORY_DANGEROUS_CONTENT", DangerousContentSafetyThreshold);

            if (settings.Count > 0)
                requestBody["safetySettings"] = settings;
        }

        private static void AddSafetySetting(
            List<object> settings,
            string category,
            GeminiSafetyThreshold threshold)
        {
            if (threshold == GeminiSafetyThreshold.ProviderDefault)
                return;

            settings.Add(new Dictionary<string, object>
            {
                ["category"] = category,
                ["threshold"] = ToGeminiSafetyThreshold(threshold)
            });
        }

        private static string ToGeminiSafetyThreshold(GeminiSafetyThreshold threshold)
        {
            return threshold switch
            {
                GeminiSafetyThreshold.Off => "OFF",
                GeminiSafetyThreshold.BlockNone => "BLOCK_NONE",
                GeminiSafetyThreshold.BlockOnlyHigh => "BLOCK_ONLY_HIGH",
                GeminiSafetyThreshold.BlockMediumAndAbove => "BLOCK_MEDIUM_AND_ABOVE",
                GeminiSafetyThreshold.BlockLowAndAbove => "BLOCK_LOW_AND_ABOVE",
                _ => throw new ArgumentOutOfRangeException(nameof(threshold), threshold, null)
            };
        }

        /// <summary>
        /// Applies the appropriate thinking configuration based on the current model.
        /// Gemini 3: uses thinkingLevel (string). Gemini 2.5: uses thinkingBudget (int).
        /// </summary>
        private void ApplyThinkingConfig(Dictionary<string, object> generationConfig)
        {
            if (IsGemini3Model())
            {
                if (ThinkingLevel == GeminiThinkingLevel.Minimal &&
                    Model != null &&
                    Model.Contains("-pro", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ThinkingLevel),
                        ThinkingLevel,
                        "Gemini 3 Pro models support Low, Medium, and High thinking levels.");
                }

                if (ThinkingLevel != GeminiThinkingLevel.Auto)
                {
                    generationConfig["thinkingConfig"] = new Dictionary<string, object>
                    {
                        ["thinkingLevel"] = ThinkingLevel.ToString().ToUpperInvariant()
                    };
                }
                return;
            }

            ValidateThinkingBudget();

            generationConfig["thinkingConfig"] = new Dictionary<string, object>
            {
                ["thinkingBudget"] = ThinkingBudget
            };
        }

        private void ValidateThinkingBudget()
        {
            if (ThinkingBudget < -1)
                throw new ArgumentOutOfRangeException(
                    nameof(ThinkingBudget),
                    ThinkingBudget,
                    "Gemini thinking budget must be -1, zero where supported, or a model-supported positive budget.");

            if (Model == null || !Model.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase))
                return;

            if (Model.Contains("-pro", StringComparison.OrdinalIgnoreCase))
            {
                if (ThinkingBudget != -1 && (ThinkingBudget < 128 || ThinkingBudget > 32768))
                    throw new ArgumentOutOfRangeException(
                        nameof(ThinkingBudget),
                        ThinkingBudget,
                        "Gemini 2.5 Pro accepts -1 or a budget from 128 through 32768.");
                return;
            }

            if (Model.Contains("flash-lite", StringComparison.OrdinalIgnoreCase))
            {
                if (ThinkingBudget != -1 && ThinkingBudget != 0 &&
                    (ThinkingBudget < 512 || ThinkingBudget > 24576))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ThinkingBudget),
                        ThinkingBudget,
                        "Gemini 2.5 Flash-Lite accepts -1, 0, or a budget from 512 through 24576.");
                }
                return;
            }

            if (ThinkingBudget > 24576)
                throw new ArgumentOutOfRangeException(
                    nameof(ThinkingBudget),
                    ThinkingBudget,
                    "Gemini 2.5 Flash accepts a budget no greater than 24576.");
        }

        #endregion

        #region Response Parsing

        private bool TryGetFirstCandidateParts(JsonElement root, out JsonElement parts)
        {
            parts = default;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
                return false;

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var contentObj) ||
                !contentObj.TryGetProperty("parts", out parts) ||
                parts.ValueKind != JsonValueKind.Array)
                return false;

            return true;
        }

        protected override string ExtractResponseContent(string responseContent)
        {
            var (text, _, _) = ExtractResponseContentWithSignature(responseContent);
            return text;
        }

        /// <summary>
        /// Extracts response text and thought signature from a Gemini response.
        /// Returns (text, thinking, thoughtSignature) where thinking is reserved for future use.
        /// </summary>
        private (string text, string? thinking, string? thoughtSignature) ExtractResponseContentWithSignature(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                ValidateCompletedGeminiResponse(doc.RootElement);

                if (!TryGetFirstCandidateParts(doc.RootElement, out var partsArr))
                    return (string.Empty, null, null);

                var textParts = new StringBuilder();
                var thinkingParts = new StringBuilder();
                string? lastSignature = null;

                foreach (var part in partsArr.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElem))
                    {
                        var isThought = part.TryGetProperty("thought", out var thoughtElement) &&
                                        thoughtElement.ValueKind == JsonValueKind.True;
                        if (isThought)
                            thinkingParts.Append(textElem.GetString());
                        else
                            textParts.Append(textElem.GetString());
                    }

                    if (part.TryGetProperty("thoughtSignature", out var sigElem))
                        lastSignature = sigElem.GetString();
                }

                return (
                    textParts.ToString(),
                    thinkingParts.Length == 0 ? null : thinkingParts.ToString(),
                    lastSignature);
            }
            catch (AIServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse Gemini response", ex);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            using var doc = JsonDocument.Parse(jsonData);

            if (!TryGetFirstCandidateParts(doc.RootElement, out var partsArr))
                return string.Empty;

            var textParts = new StringBuilder();
            foreach (var part in partsArr.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElem) &&
                    !(part.TryGetProperty("thought", out var thoughtElement) &&
                      thoughtElement.ValueKind == JsonValueKind.True))
                {
                    textParts.Append(textElem.GetString());
                }
            }

            return textParts.ToString();
        }

        /// <summary>
        /// Extracts every function call and its part-local thought signature from a Gemini response.
        /// The original ordered parts are retained so the continuation request can replay them exactly.
        /// </summary>
        private (string content, string? thinking, FunctionCallBatch functionCalls, string? thoughtSignature) ExtractFunctionCallsWithSignature(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                ValidateCompletedGeminiResponse(doc.RootElement);

                if (!TryGetFirstCandidateParts(doc.RootElement, out var parts))
                    return (string.Empty, null, new FunctionCallBatch(), null);

                var content = new StringBuilder();
                var thinking = new StringBuilder();
                var functionCalls = new List<FunctionCall>();
                var responseParts = new List<JsonElement>();
                string? thoughtSignature = null;
                var partIndex = 0;

                foreach (var part in parts.EnumerateArray())
                {
                    responseParts.Add(part.Clone());

                    if (part.TryGetProperty("text", out var textElement))
                    {
                        var isThought = part.TryGetProperty("thought", out var thoughtElement) &&
                                        thoughtElement.ValueKind == JsonValueKind.True;
                        if (isThought)
                            thinking.Append(textElement.GetString());
                        else
                            content.Append(textElement.GetString());
                    }

                    if (part.TryGetProperty("functionCall", out _))
                    {
                        functionCalls.Add(ParseGeminiFunctionCallPart(
                            part,
                            functionCalls.Count,
                            partIndex,
                            IsGemini3Model()));
                    }

                    if (part.TryGetProperty("thoughtSignature", out var signatureElement) &&
                        signatureElement.ValueKind == JsonValueKind.String)
                    {
                        thoughtSignature = signatureElement.GetString();
                    }

                    partIndex++;
                }

                var batch = new FunctionCallBatch(functionCalls);
                if (functionCalls.Count > 0)
                {
                    batch.Metadata = new Dictionary<string, object>
                    {
                        [GeminiResponsePartsMetadataKey] = responseParts
                    };
                }

                return (
                    content.ToString(),
                    thinking.Length == 0 ? null : thinking.ToString(),
                    batch,
                    thoughtSignature);
            }
            catch (AIServiceException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is KeyNotFoundException ||
                exception is InvalidOperationException)
            {
                throw new AIServiceException("Failed to parse Gemini function-call response", exception);
            }
        }

        private static FunctionCall ParseGeminiFunctionCallPart(
            JsonElement part,
            int callIndex,
            int partIndex,
            bool requireProviderCallId)
        {
            if (!part.TryGetProperty("functionCall", out var functionCallElement) ||
                functionCallElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Gemini function-call part is missing its functionCall object.");
            }

            if (!functionCallElement.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                throw new InvalidOperationException($"Gemini function call at index {callIndex} is missing a name.");
            }

            if (!functionCallElement.TryGetProperty("args", out var argumentsElement) ||
                argumentsElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Gemini function call '{nameElement.GetString()}' at index {callIndex} has invalid arguments.");
            }

            if (part.TryGetProperty("thoughtSignature", out var signatureElement) &&
                signatureElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"Gemini function call '{nameElement.GetString()}' at index {callIndex} has an invalid thought signature.");
            }

            var hasProviderCallId =
                functionCallElement.TryGetProperty("id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(idElement.GetString());
            if (!hasProviderCallId && requireProviderCallId)
            {
                throw new InvalidOperationException(
                    $"Gemini 3 function call '{nameElement.GetString()}' at index {callIndex} is missing its provider ID.");
            }

            var functionCall = new FunctionCall
            {
                Id = hasProviderCallId
                    ? idElement.GetString()!
                    : $"call_{Guid.NewGuid():N}",
                Source = IdSource.Gemini,
                Name = nameElement.GetString()!,
                Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    argumentsElement.GetRawText()) ?? new Dictionary<string, object>(),
                Index = callIndex,
                Metadata = new Dictionary<string, object>
                {
                    [GeminiPartIndexMetadataKey] = partIndex,
                    [GeminiProviderCallIdMetadataKey] = hasProviderCallId
                }
            };

            if (part.TryGetProperty("thoughtSignature", out signatureElement) &&
                signatureElement.ValueKind == JsonValueKind.String &&
                signatureElement.GetString() != null)
            {
                functionCall.Metadata[MessageMetadataKeys.ThoughtSignature] = signatureElement.GetString()!;
            }

            return functionCall;
        }

        private IReadOnlyList<StreamingContent> ParseGeminiStreamChunk(
            string jsonData,
            StreamOptions options,
            GeminiFunctionCallCollector functionCalls)
        {
            using var doc = JsonDocument.Parse(jsonData);
            functionCalls.BeginChunk();
            var root = doc.RootElement;
            var parsedContents = new List<StreamingContent>();
            var usage = TryParseUsageMetadata(root);

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                var usageContent = BuildUsageOnlyStatusContent(root, options, new StreamingContent());
                if (usageContent != null)
                    parsedContents.Add(usageContent);
                return parsedContents;
            }

            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var contentObject) &&
                contentObject.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (functionCalls.TryCollectPart(part, out var functionCall))
                    {
                        if (functionCall != null)
                        {
                            var functionContent = new StreamingContent
                            {
                                Type = StreamingContentType.FunctionCall,
                                FunctionCall = functionCall,
                                FunctionCallBatchId = functionCalls.BatchId
                            };

                            if (options.IncludeMetadata)
                            {
                                functionContent.Metadata = new Dictionary<string, object>
                                {
                                    ["function_calling"] = true,
                                    ["function_name"] = functionCall.Name,
                                    ["function_index"] = functionCall.Index,
                                    ["status"] = "complete"
                                };
                            }

                            parsedContents.Add(functionContent);
                        }

                        continue;
                    }

                    var textContent = TryParseTextPart(
                        part,
                        candidate,
                        root,
                        options,
                        new StreamingContent());
                    if (textContent != null)
                        parsedContents.Add(textContent);
                }
            }

            if (usage != null && parsedContents.Count > 0 && parsedContents.All(content => content.Usage == null))
                parsedContents[parsedContents.Count - 1].Usage = usage;

            if (parsedContents.Count == 0 &&
                candidate.TryGetProperty("finishReason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason != null && (options.IncludeMetadata || usage != null))
                {
                    var statusContent = new StreamingContent
                    {
                        Type = StreamingContentType.Status,
                        Usage = usage
                    };
                    if (options.IncludeMetadata)
                    {
                        statusContent.Metadata = new Dictionary<string, object>
                        {
                            ["finish_reason"] = reason
                        };
                    }
                    parsedContents.Add(statusContent);
                }
            }

            return parsedContents;
        }

        private StreamingContent? TryParseTextPart(
            JsonElement part,
            JsonElement candidate,
            JsonElement root,
            StreamOptions options,
            StreamingContent content)
        {
            if (!part.TryGetProperty("text", out var textElem))
                return null;

            var text = textElem.GetString();
            if (!string.IsNullOrEmpty(text))
                return BuildTextStreamingContent(part, candidate, root, options, content, text);

            return TryBuildEmptyTextSignatureContent(part, content);
        }

        private StreamingContent? BuildTextStreamingContent(
            JsonElement part,
            JsonElement candidate,
            JsonElement root,
            StreamOptions options,
            StreamingContent content,
            string text)
        {
            bool isThought = part.TryGetProperty("thought", out var thoughtElem) && thoughtElem.GetBoolean();

            if (isThought && !options.IncludeReasoning)
                return null;

            content.Type = isThought ? StreamingContentType.Reasoning : StreamingContentType.Text;
            content.Content = text;
            content.Usage = TryParseUsageMetadata(root);

            if (options.IncludeMetadata)
            {
                content.Metadata = new Dictionary<string, object>();

                if (candidate.TryGetProperty("safetyRatings", out var safetyRatings))
                    content.Metadata["safety_ratings"] = safetyRatings.GetRawText();

                if (candidate.TryGetProperty("finishReason", out var textFinishReason))
                    content.Metadata["finish_reason"] = textFinishReason.GetString() ?? string.Empty;

                if (part.TryGetProperty("thoughtSignature", out var textSigElem))
                    content.Metadata[MessageMetadataKeys.ThoughtSignature] = textSigElem.GetString() ?? string.Empty;
            }

            return content;
        }

        private static StreamingContent? TryBuildEmptyTextSignatureContent(JsonElement part, StreamingContent content)
        {
            if (!part.TryGetProperty("thoughtSignature", out var emptySigElem))
                return null;

            content.Type = StreamingContentType.Status;
            content.Metadata = new Dictionary<string, object>
            {
                [MessageMetadataKeys.ThoughtSignature] = emptySigElem.GetString() ?? string.Empty
            };
            return content;
        }

        private static TokenUsage? TryParseUsageMetadata(JsonElement root)
        {
            if (!root.TryGetProperty("usageMetadata", out var usageMetadata))
                return null;

            var usage = new TokenUsage();
            if (usageMetadata.TryGetProperty("promptTokenCount", out var promptTokens))
                usage.InputTokens = promptTokens.GetInt32();
            if (usageMetadata.TryGetProperty("toolUsePromptTokenCount", out var toolUsePromptTokens))
                usage.InputTokens += toolUsePromptTokens.GetInt32();
            if (usageMetadata.TryGetProperty("candidatesTokenCount", out var outputTokens))
                usage.OutputTokens = outputTokens.GetInt32();

            if (usageMetadata.TryGetProperty("cachedContentTokenCount", out var cachedTokens))
                usage.CachedInputTokens = cachedTokens.GetInt32();
            if (usageMetadata.TryGetProperty("thoughtsTokenCount", out var thoughtsTokens))
            {
                usage.ReasoningTokens = thoughtsTokens.GetInt32();
                usage.OutputTokens += usage.ReasoningTokens;
            }

            if (usageMetadata.TryGetProperty("totalTokenCount", out var totalTokens))
                usage.TotalTokens = totalTokens.GetInt32();
            else
                usage.TotalTokens = usage.InputTokens + usage.OutputTokens;

            return usage;
        }

        private StreamingContent? BuildUsageOnlyStatusContent(
            JsonElement root,
            StreamOptions options,
            StreamingContent content)
        {
            var usage = TryParseUsageMetadata(root);
            if (usage != null)
            {
                content.Type = StreamingContentType.Status;
                content.Metadata = options.IncludeMetadata ? new Dictionary<string, object>() : null;
                content.Usage = usage;
                return content;
            }

            return null;
        }

        #endregion
    }
}
