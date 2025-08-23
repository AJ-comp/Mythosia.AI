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

            return new
            {
                model = ActivateChat.Model,
                messages = messagesList,
                top_p = ActivateChat.TopP,
                temperature = ActivateChat.Temperature,
                frequency_penalty = ActivateChat.FrequencyPenalty,
                presence_penalty = ActivateChat.PresencePenalty,
                max_tokens = ActivateChat.MaxTokens,
                stream = ActivateChat.Stream
            };
        }

        private object BuildGpt5ResponsesBody()
        {
            var inputList = new List<object>();

            // System message를 instructions로 처리
            string? instructions = !string.IsNullOrEmpty(ActivateChat.SystemMessage)
                ? ActivateChat.SystemMessage
                : null;

            // 대화 메시지들을 input 배열로 변환
            foreach (var message in ActivateChat.GetLatestMessages())
            {
                var messageParts = new List<object>();

                if (!message.HasMultimodalContent)
                {
                    // Role에 따라 다른 type 사용
                    string textType = message.Role == ActorRole.Assistant ? "output_text" : "input_text";

                    messageParts.Add(new
                    {
                        type = textType,
                        text = message.Content ?? string.Empty
                    });
                }
                else
                {
                    // 멀티모달 메시지
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

            // GPT-5 전용 파라미터
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = ActivateChat.Model,
                ["input"] = inputList,
                ["text"] = new { verbosity = "medium" },
                ["reasoning"] = new { effort = "medium" },
                ["max_output_tokens"] = (int)ActivateChat.MaxTokens
            };

            if (!string.IsNullOrEmpty(instructions))
            {
                requestBody["instructions"] = instructions;
            }

            if (ActivateChat.Stream)
            {
                requestBody["stream"] = true;
            }

            return requestBody;
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
                bool isGpt5Model = ActivateChat.Model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

                if (isGpt5Model)
                {
                    return ExtractGpt5Response(responseContent);
                }
                else
                {
                    return ExtractChatCompletionsResponse(responseContent);
                }
            }
            catch (Exception ex)
            {
                throw new AIServiceException($"Failed to parse OpenAI response: {ex.Message}", responseContent);
            }
        }

        private string ExtractGpt5Response(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                if (!root.TryGetProperty("output", out var outputArray))
                {
                    throw new AIServiceException("GPT-5 response missing 'output' field");
                }

                foreach (var outputItem in outputArray.EnumerateArray())
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

                throw new AIServiceException("No message text found in GPT-5 response");
            }
            catch (JsonException ex)
            {
                throw new AIServiceException($"GPT-5 JSON parsing error: {ex.Message}", responseContent);
            }
        }

        private string ExtractChatCompletionsResponse(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                var choices = root.GetProperty("choices");
                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");
                var content = message.GetProperty("content");

                return content.GetString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new AIServiceException($"Chat Completions parsing error: {ex.Message}", responseContent);
            }
        }

        protected override string StreamParseJson(string jsonData)
        {
            try
            {
                bool isGpt5Model = ActivateChat.Model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

                if (isGpt5Model)
                {
                    return StreamParseGpt5(jsonData);
                }
                else
                {
                    return StreamParseChatCompletions(jsonData);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private string StreamParseGpt5(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp))
                {
                    var typeStr = typeProp.GetString();

                    if (typeStr == "content_delta" &&
                        root.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var deltaText))
                    {
                        return deltaText.GetString() ?? string.Empty;
                    }

                    if (typeStr == "output_text" &&
                        root.TryGetProperty("text", out var directText))
                    {
                        return directText.GetString() ?? string.Empty;
                    }
                }

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
            catch
            {
                return string.Empty;
            }
        }

        private string StreamParseChatCompletions(string jsonData)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
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