using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GeminiService
    {
        private const int DefaultCandidateCount = 1;
        private const string DefaultImageMimeType = "image/jpeg";

        #region Request Body Building

        private object BuildRequestBody()
        {
            var contentsList = new List<object>();
            foreach (var message in GetLatestMessagesWithFunctionFallback())
            {
                contentsList.Add(ConvertMessageForGemini(message));
            }

            var generationConfig = new Dictionary<string, object>
            {
                ["temperature"] = Temperature,
                ["topP"] = TopP,
                ["topK"] = DefaultTopK,
                ["maxOutputTokens"] = (int)GetEffectiveMaxTokens(),
                ["candidateCount"] = DefaultCandidateCount
            };

            ApplyThinkingConfig(generationConfig);

            var requestBody = new Dictionary<string, object>
            {
                ["contents"] = contentsList,
                ["generationConfig"] = generationConfig,
                ["safetySettings"] = GetSafetySettings()
            };

            ApplySystemInstruction(requestBody);

            return requestBody;
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

        private object[] GetSafetySettings()
        {
            return new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" }
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
                if (ThinkingLevel != GeminiThinkingLevel.Auto)
                {
                    generationConfig["thinkingConfig"] = new Dictionary<string, object>
                    {
                        ["thinkingLevel"] = ThinkingLevel.ToString().ToUpperInvariant()
                    };
                }
                return;
            }

            if (ThinkingBudget >= 0)
            {
                generationConfig["thinkingConfig"] = new Dictionary<string, object>
                {
                    ["thinkingBudget"] = ThinkingBudget
                };
            }
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

                if (!TryGetFirstCandidateParts(doc.RootElement, out var partsArr))
                    return (string.Empty, null, null);

                var textParts = new StringBuilder();
                string? lastSignature = null;

                foreach (var part in partsArr.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElem))
                        textParts.Append(textElem.GetString());

                    if (part.TryGetProperty("thoughtSignature", out var sigElem))
                        lastSignature = sigElem.GetString();
                }

                return (textParts.ToString(), null, lastSignature);
            }
            catch (Exception ex)
            {
                throw new AIServiceException("Failed to parse Gemini response", ex);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);

                if (!TryGetFirstCandidateParts(doc.RootElement, out var partsArr))
                    return string.Empty;

                var textParts = new StringBuilder();
                foreach (var part in partsArr.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElem))
                        textParts.Append(textElem.GetString());
                }

                return textParts.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Extracts function call info and thought signature from a Gemini response.
        /// Gemini 3 includes thoughtSignature on functionCall parts which must be circulated back.
        /// </summary>
        private (string content, FunctionCall? functionCall, string? thoughtSignature) ExtractFunctionCallWithSignature(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);

                if (!TryGetFirstCandidateParts(doc.RootElement, out var parts))
                    return (string.Empty, null, null);

                string content = string.Empty;
                FunctionCall? functionCall = null;
                string? thoughtSignature = null;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement))
                    {
                        content += textElement.GetString();
                    }
                    else if (part.TryGetProperty("functionCall", out var functionCallElement))
                    {
                        functionCall = new FunctionCall
                        {
                            Id = Guid.NewGuid().ToString(),
                            Source = IdSource.Gemini,
                            Name = functionCallElement.GetProperty("name").GetString(),
                            Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                functionCallElement.GetProperty("args").GetRawText())
                        };
                    }

                    if (part.TryGetProperty("thoughtSignature", out var sigElem))
                        thoughtSignature = sigElem.GetString();
                }

                return (content, functionCall, thoughtSignature);
            }
            catch
            {
                return (string.Empty, null, null);
            }
        }

        private StreamingContent? ParseGeminiStreamChunk(
            string jsonData,
            StreamOptions options,
            FunctionCallData functionCallData)
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            var content = new StreamingContent();

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return BuildUsageOnlyStatusContent(root, options, content);

            var candidate = candidates[0];

            if (candidate.TryGetProperty("content", out var contentObj) &&
                contentObj.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (TryParseFunctionCallPart(part, options, functionCallData, content, out var functionContent))
                        return functionContent;

                    var textContent = TryParseTextPart(part, candidate, root, options, content);
                    if (textContent != null)
                        return textContent;
                }
            }

            if (options.IncludeMetadata &&
                candidate.TryGetProperty("finishReason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason != null)
                {
                    content.Type = StreamingContentType.Status;
                    content.Metadata = new Dictionary<string, object> { ["finish_reason"] = reason };
                    AddUsageMetadata(content.Metadata, root);
                    return content;
                }
            }

            if (options.IncludeMetadata)
                return BuildUsageOnlyStatusContent(root, options, content);

            return null;
        }

        private bool TryParseFunctionCallPart(
            JsonElement part,
            StreamOptions options,
            FunctionCallData functionCallData,
            StreamingContent content,
            out StreamingContent? parsedContent)
        {
            parsedContent = null;

            if (!part.TryGetProperty("functionCall", out var functionCallElement))
                return false;

            content.Type = StreamingContentType.FunctionCall;

            if (functionCallElement.TryGetProperty("name", out var nameElem))
                functionCallData.Name = nameElem.GetString();

            if (functionCallElement.TryGetProperty("args", out var argsElem))
            {
                functionCallData.Arguments.Clear();
                functionCallData.Arguments.Append(argsElem.GetRawText());
                functionCallData.IsComplete = true;
            }

            if (part.TryGetProperty("thoughtSignature", out var fcSigElem))
                functionCallData.ThoughtSignature = fcSigElem.GetString();

            content.FunctionCallData = functionCallData;

            if (options.IncludeMetadata)
            {
                content.Metadata = new Dictionary<string, object>
                {
                    ["function_calling"] = true,
                    ["function_name"] = functionCallData.Name ?? "",
                    ["status"] = functionCallData.IsComplete ? "complete" : "accumulating"
                };
            }

            parsedContent = content;
            return true;
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

            if (options.IncludeMetadata)
            {
                content.Metadata = new Dictionary<string, object>();

                if (candidate.TryGetProperty("safetyRatings", out var safetyRatings))
                    content.Metadata["safety_ratings"] = safetyRatings.GetRawText();

                if (candidate.TryGetProperty("finishReason", out var textFinishReason))
                    content.Metadata["finish_reason"] = textFinishReason.GetString();

                AddUsageMetadata(content.Metadata, root);

                if (part.TryGetProperty("thoughtSignature", out var textSigElem))
                    content.Metadata[MessageMetadataKeys.ThoughtSignature] = textSigElem.GetString();
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
                [MessageMetadataKeys.ThoughtSignature] = emptySigElem.GetString()
            };
            return content;
        }

        private void AddUsageMetadata(Dictionary<string, object> metadata, JsonElement root)
        {
            if (!root.TryGetProperty("usageMetadata", out var usageMetadata))
                return;

            if (usageMetadata.TryGetProperty("promptTokenCount", out var promptTokens))
                metadata["input_tokens"] = promptTokens.GetInt32();
            if (usageMetadata.TryGetProperty("candidatesTokenCount", out var outputTokens))
                metadata["output_tokens"] = outputTokens.GetInt32();
            if (usageMetadata.TryGetProperty("totalTokenCount", out var totalTokens))
                metadata["total_tokens"] = totalTokens.GetInt32();
        }

        private StreamingContent? BuildUsageOnlyStatusContent(
            JsonElement root,
            StreamOptions options,
            StreamingContent content)
        {
            if (!options.IncludeMetadata)
                return null;

            content.Type = StreamingContentType.Status;
            content.Metadata = new Dictionary<string, object>();

            AddUsageMetadata(content.Metadata, root);

            return content.Metadata.Count > 0 ? content : null;
        }

        #endregion
    }
}