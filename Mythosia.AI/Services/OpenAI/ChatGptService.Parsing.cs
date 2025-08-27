using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        #region Request Body Building

        private object BuildRequestBody()
        {
            var requestBody = new Dictionary<string, object>();

            if (IsNewApiModel(ActivateChat.Model))
            {
                // Build for new API (GPT-5, o3, etc.)
                BuildNewApiBody(requestBody);
            }
            else
            {
                // Build for legacy API
                BuildLegacyApiBody(requestBody);
            }

            // Apply model-specific parameter configurations
            ApplyModelSpecificParameters(requestBody);

            return requestBody;
        }

        private void BuildNewApiBody(Dictionary<string, object> requestBody)
        {
            var inputList = new List<object>();

            // Convert messages to new API format
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                var messageParts = new List<object>();

                if (!message.HasMultimodalContent)
                {
                    string textType = message.Role == ActorRole.Assistant ? "output_text" : "input_text";
                    messageParts.Add(new
                    {
                        type = textType,
                        text = message.Content ?? string.Empty
                    });
                }
                else
                {
                    foreach (var content in message.Contents)
                    {
                        if (content is TextContent textContent)
                        {
                            string textType = message.Role == ActorRole.Assistant ? "output_text" : "input_text";
                            messageParts.Add(new
                            {
                                type = textType,
                                text = textContent.Text ?? string.Empty
                            });
                        }
                        else if (content is ImageContent imageContent)
                        {
                            messageParts.Add(new
                            {
                                type = "input_image",
                                image_url = imageContent.GetBase64Url()
                            });
                        }
                    }
                }

                inputList.Add(new
                {
                    role = message.Role.ToDescription(),
                    content = messageParts
                });
            }

            requestBody["model"] = ActivateChat.Model;
            requestBody["input"] = inputList;

            // Add instructions if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                requestBody["instructions"] = ActivateChat.SystemMessage;
            }

            if (ActivateChat.Stream)
            {
                requestBody["stream"] = true;
            }
        }

        private void BuildLegacyApiBody(Dictionary<string, object> requestBody)
        {
            var messagesList = new List<object>();

            // Add system message if present
            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

            // Add conversation messages
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForOpenAI(message));
            }

            requestBody["model"] = ActivateChat.Model;
            requestBody["messages"] = messagesList;
            requestBody["temperature"] = ActivateChat.Temperature;
            requestBody["top_p"] = ActivateChat.TopP;
            requestBody["frequency_penalty"] = ActivateChat.FrequencyPenalty;
            requestBody["presence_penalty"] = ActivateChat.PresencePenalty;
            requestBody["stream"] = ActivateChat.Stream;
        }

        private object ConvertMessageForOpenAI(Message message)
        {
            if (!message.HasMultimodalContent)
            {
                return new { role = message.Role.ToDescription(), content = message.Content };
            }

            // Handle multimodal content
            var contentList = new List<object>();
            foreach (var content in message.Contents)
            {
                contentList.Add(content.ToRequestFormat(Provider));
            }

            return new
            {
                role = message.Role.ToDescription(),
                content = contentList
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

                // Check response format to determine API version
                if (root.TryGetProperty("output", out var output))
                {
                    // New API format (GPT-5, o3, etc.)
                    return ExtractNewApiResponse(output);
                }
                else if (root.TryGetProperty("choices", out var choices))
                {
                    // Legacy API format
                    return ExtractLegacyApiResponse(choices);
                }

                throw new AIServiceException("Unrecognized response format");
            }
            catch (Exception ex)
            {
                throw new AIServiceException($"Failed to parse OpenAI response: {ex.Message}", responseContent);
            }
        }

        private string ExtractNewApiResponse(JsonElement output)
        {
            foreach (var outputItem in output.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("type", out var typeProp))
                    continue;

                if (typeProp.GetString() != "message")
                    continue;

                if (!outputItem.TryGetProperty("content", out var contentArray))
                    continue;

                foreach (var contentItem in contentArray.EnumerateArray())
                {
                    if (!contentItem.TryGetProperty("type", out var contentTypeProp))
                        continue;

                    if (contentTypeProp.GetString() != "output_text")
                        continue;

                    if (contentItem.TryGetProperty("text", out var textProp))
                    {
                        return textProp.GetString() ?? string.Empty;
                    }
                }
            }

            throw new AIServiceException("No message text found in response");
        }

        private string ExtractLegacyApiResponse(JsonElement choices)
        {
            if (choices.GetArrayLength() == 0)
                throw new AIServiceException("No choices in response");

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message))
                throw new AIServiceException("No message in choice");

            if (!message.TryGetProperty("content", out var content))
                throw new AIServiceException("No content in message");

            return content.GetString() ?? string.Empty;
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                // Determine format and parse accordingly
                if (root.TryGetProperty("type", out var typeProp))
                {
                    // New API streaming format
                    return StreamParseNewApi(root, typeProp.GetString());
                }
                else if (root.TryGetProperty("choices", out var choices))
                {
                    // Legacy API streaming format
                    return StreamParseLegacyApi(choices);
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string StreamParseNewApi(JsonElement root, string? type)
        {
            if (type == "content_delta" &&
                root.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("text", out var deltaText))
            {
                return deltaText.GetString() ?? string.Empty;
            }

            if (type == "output_text" &&
                root.TryGetProperty("text", out var directText))
            {
                return directText.GetString() ?? string.Empty;
            }

            // Check for nested output structure
            if (root.TryGetProperty("output", out var outputArray))
            {
                foreach (var item in outputArray.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var itemType) &&
                        itemType.GetString() == "message" &&
                        item.TryGetProperty("content", out var contentArray))
                    {
                        foreach (var contentItem in contentArray.EnumerateArray())
                        {
                            if (contentItem.TryGetProperty("type", out var contentType) &&
                                contentType.GetString() == "output_text" &&
                                contentItem.TryGetProperty("text", out var textProp))
                            {
                                return textProp.GetString() ?? string.Empty;
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }

        private string StreamParseLegacyApi(JsonElement choices)
        {
            if (choices.GetArrayLength() == 0)
                return string.Empty;

            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private StreamingContent? ParseOpenAIStreamChunk(
            string jsonData,
            StreamOptions options,
            FunctionCallData functionCallData,
            ref string? currentModel,
            ref string? responseId)
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            // Extract metadata on first chunk
            if (currentModel == null && root.TryGetProperty("model", out var modelElem))
            {
                currentModel = modelElem.GetString();
            }
            if (responseId == null && root.TryGetProperty("id", out var idElem))
            {
                responseId = idElem.GetString();
            }

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];
            var content = new StreamingContent();

            // Check for finish reason
            if (choice.TryGetProperty("finish_reason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason == "function_call")
                {
                    functionCallData.IsComplete = true;
                    content.Type = StreamingContentType.FunctionCall;
                    content.FunctionCallData = functionCallData;

                    if (options.IncludeMetadata)
                    {
                        content.Metadata = new Dictionary<string, object>
                        {
                            ["function_calling"] = true,
                            ["function_name"] = functionCallData.Name ?? "",
                            ["status"] = "complete",
                            ["finish_reason"] = reason
                        };
                    }
                    return content;
                }
                else if (reason != null && options.IncludeMetadata)
                {
                    content.Type = StreamingContentType.Status;
                    content.Metadata = new Dictionary<string, object>
                    {
                        ["finish_reason"] = reason
                    };
                    return content;
                }
            }

            // Check for delta content
            if (choice.TryGetProperty("delta", out var delta))
            {
                // Check for function call
                if (delta.TryGetProperty("function_call", out var functionCall))
                {
                    content.Type = StreamingContentType.FunctionCall;

                    if (functionCall.TryGetProperty("name", out var nameElem))
                    {
                        functionCallData.Name = nameElem.GetString();

                        if (options.IncludeMetadata)
                        {
                            content.Metadata = new Dictionary<string, object>
                            {
                                ["function_calling"] = true,
                                ["function_name"] = functionCallData.Name ?? "",
                                ["status"] = "started"
                            };
                        }
                    }

                    if (functionCall.TryGetProperty("arguments", out var argsElem))
                    {
                        var argChunk = argsElem.GetString();
                        if (!string.IsNullOrEmpty(argChunk))
                        {
                            functionCallData.Arguments.Append(argChunk);

                            if (options.IncludeMetadata)
                            {
                                content.Metadata = new Dictionary<string, object>
                                {
                                    ["function_calling"] = true,
                                    ["function_name"] = functionCallData.Name ?? "",
                                    ["status"] = "accumulating"
                                };
                            }
                        }
                    }

                    content.FunctionCallData = functionCallData;
                    return content;
                }

                // Check for text content
                if (delta.TryGetProperty("content", out var textContent))
                {
                    var text = textContent.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        content.Type = StreamingContentType.Text;
                        content.Content = text;

                        if (options.IncludeMetadata)
                        {
                            content.Metadata = new Dictionary<string, object>();
                            if (currentModel != null)
                                content.Metadata["model"] = currentModel;
                            if (responseId != null)
                                content.Metadata["response_id"] = responseId;
                        }

                        return content;
                    }
                }
            }

            return null;
        }

        #endregion
    }
}