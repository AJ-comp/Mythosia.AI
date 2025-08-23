using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
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
        #region Request Body Building

        private object BuildRequestBody()
        {
            var contentsList = new List<object>();

            // Add system message as first user message if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                contentsList.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = ActivateChat.SystemMessage } }
                });

                // Add a model response to balance the conversation
                contentsList.Add(new
                {
                    role = "model",
                    parts = new[] { new { text = "Understood. I'll follow these instructions." } }
                });
            }

            // Add conversation messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                contentsList.Add(ConvertMessageForGemini(message));
            }

            var generationConfig = new
            {
                temperature = ActivateChat.Temperature,
                topP = ActivateChat.TopP,
                topK = 40,
                maxOutputTokens = (int)ActivateChat.MaxTokens,
                candidateCount = 1
            };

            return new
            {
                contents = contentsList,
                generationConfig = generationConfig,
                safetySettings = GetSafetySettings()
            };
        }

        private object ConvertMessageForGemini(Message message)
        {
            // Gemini uses "model" instead of "assistant"
            var role = message.Role == ActorRole.Assistant ? "model" : "user";

            if (!message.HasMultimodalContent)
            {
                return new
                {
                    role = role,
                    parts = new[] { new { text = message.Content } }
                };
            }

            // Handle multimodal content
            var parts = new List<object>();
            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent)
                {
                    parts.Add(new { text = textContent.Text });
                }
                else if (content is ImageContent imageContent)
                {
                    parts.Add(ConvertImageForGemini(imageContent));
                }
            }

            return new
            {
                role = role,
                parts = parts
            };
        }

        private object ConvertImageForGemini(ImageContent imageContent)
        {
            if (imageContent.Data != null)
            {
                return new
                {
                    inline_data = new
                    {
                        mime_type = imageContent.MimeType ?? "image/jpeg",
                        data = Convert.ToBase64String(imageContent.Data)
                    }
                };
            }
            else if (!string.IsNullOrEmpty(imageContent.Url))
            {
                // Gemini doesn't support URLs directly, need to download
                throw new NotSupportedException("Gemini API requires base64 encoded images. Please download the image and provide as byte array.");
            }

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

        #endregion

        #region Response Parsing

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var partsArr) &&
                        partsArr.ValueKind == JsonValueKind.Array)
                    {
                        var textParts = new StringBuilder();
                        foreach (var part in partsArr.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElem))
                            {
                                textParts.Append(textElem.GetString());
                            }
                        }
                        return textParts.ToString();
                    }
                }

                return string.Empty;
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
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    var firstCandidate = candidates[0];
                    if (firstCandidate.TryGetProperty("content", out var contentObj) &&
                        contentObj.TryGetProperty("parts", out var partsArr) &&
                        partsArr.ValueKind == JsonValueKind.Array)
                    {
                        var textParts = new StringBuilder();
                        foreach (var part in partsArr.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textElem))
                            {
                                textParts.Append(textElem.GetString());
                            }
                        }
                        return textParts.ToString();
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
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

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];

                // Check for finish reason
                if (candidate.TryGetProperty("finishReason", out var finishReason))
                {
                    var reason = finishReason.GetString();
                    if (reason != null && options.IncludeMetadata)
                    {
                        content.Type = StreamingContentType.Status;
                        content.Metadata = new Dictionary<string, object>
                        {
                            ["finish_reason"] = reason
                        };
                        return content;
                    }
                }

                // Check for content
                if (candidate.TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        // Check for function call
                        if (part.TryGetProperty("functionCall", out var functionCallElement))
                        {
                            content.Type = StreamingContentType.FunctionCall;

                            if (functionCallElement.TryGetProperty("name", out var nameElem))
                            {
                                functionCallData.Name = nameElem.GetString();
                            }

                            if (functionCallElement.TryGetProperty("args", out var argsElem))
                            {
                                // Gemini sends complete args in one chunk
                                functionCallData.Arguments.Clear();
                                functionCallData.Arguments.Append(argsElem.GetRawText());
                                functionCallData.IsComplete = true;
                            }

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

                            return content;
                        }

                        // Check for text content
                        if (part.TryGetProperty("text", out var textElem))
                        {
                            var text = textElem.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                content.Type = StreamingContentType.Text;
                                content.Content = text;

                                if (options.IncludeMetadata)
                                {
                                    content.Metadata = new Dictionary<string, object>();

                                    // Add safety ratings if present
                                    if (candidate.TryGetProperty("safetyRatings", out var safetyRatings))
                                    {
                                        content.Metadata["safety_ratings"] = safetyRatings.GetRawText();
                                    }
                                }

                                return content;
                            }
                        }
                    }
                }
            }

            return null;
        }

        #endregion
    }
}