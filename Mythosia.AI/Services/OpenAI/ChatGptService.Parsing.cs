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
                BuildNewApiBody(requestBody);
            }
            else
            {
                BuildLegacyApiBody(requestBody);
            }

            ApplyModelSpecificParameters(requestBody);
            return requestBody;
        }

        private void BuildNewApiBody(Dictionary<string, object> requestBody)
        {
            var inputList = new List<object>();

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

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                messagesList.Add(new { role = "system", content = ActivateChat.SystemMessage });
            }

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

                if (root.TryGetProperty("output", out var output))
                {
                    return ExtractNewApiResponse(output);
                }
                else if (root.TryGetProperty("choices", out var choices))
                {
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
            var content = new StringBuilder();

            foreach (var outputItem in output.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("type", out var typeProp))
                    continue;

                if (typeProp.GetString() != "message")
                    continue;

                if (!outputItem.TryGetProperty("content", out var contentElem))
                    continue;

                content.Append(ExtractTextFromContent(contentElem));
            }

            return content.ToString();
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

        #endregion

        #region Stream Parsing

        protected override string StreamParseJson(string jsonData)
        {
            var (text, _, _) = ParseStreamChunk(jsonData, includeMetadata: false);
            return text ?? string.Empty;
        }

        private StreamingContent? ParseOpenAIStreamChunk(
            string jsonData,
            StreamOptions options,
            FunctionCallData functionCallData,
            ref string? currentModel,
            ref string? responseId)
        {
            var (text, type, metadata) = ParseStreamChunk(jsonData, includeMetadata: options.IncludeMetadata);

            if (text == null && type == StreamingContentType.Text && metadata == null)
                return null;

            var content = new StreamingContent
            {
                Type = type,
                Content = text,
                Metadata = metadata
            };

            if (metadata != null)
            {
                if (currentModel == null && metadata.TryGetValue("model", out var model))
                    currentModel = model.ToString();
                if (responseId == null && metadata.TryGetValue("response_id", out var id))
                    responseId = id.ToString();

                if (type == StreamingContentType.FunctionCall)
                {
                    if (metadata.TryGetValue("function_name", out var fname))
                        functionCallData.Name = fname.ToString();
                    if (metadata.TryGetValue("function_arguments", out var fargs))
                        functionCallData.Arguments.Append(fargs.ToString());

                    content.FunctionCallData = functionCallData;
                }
            }

            return content;
        }

        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseStreamChunk(
            string jsonData,
            bool includeMetadata = false)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                return IsNewApiModel(ActivateChat.Model)
                    ? ParseNewApiStream(root, includeMetadata)
                    : ParseLegacyApiStream(root, includeMetadata);
            }
            catch
            {
                return (null, StreamingContentType.Text, null);
            }
        }

        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiStream(
            JsonElement root,
            bool includeMetadata)
        {
            var metadata = includeMetadata ? new Dictionary<string, object>() : null;

            // Check type property first
            if (root.TryGetProperty("type", out var typeProp))
            {
                var type = typeProp.GetString();

                switch (type)
                {
                    // 🔴 o3-mini의 새로운 스트리밍 형식 추가!
                    case "response.created":
                    case "response.in_progress":
                        // 초기 응답, 텍스트 없음
                        return (null, StreamingContentType.Text, metadata);

                    case "response.content.delta":
                        // 실제 텍스트 콘텐츠
                        if (root.TryGetProperty("delta", out var contentDelta))
                        {
                            if (contentDelta.TryGetProperty("text", out var deltaText))
                            {
                                return (deltaText.GetString(), StreamingContentType.Text, metadata);
                            }
                            if (contentDelta.TryGetProperty("content", out var deltaContent))
                            {
                                return (deltaContent.GetString(), StreamingContentType.Text, metadata);
                            }
                        }
                        break;

                    case "response.content.done":
                    case "response.done":
                        // 완료 신호
                        if (metadata != null)
                            metadata["finish_reason"] = "stop";
                        return (null, StreamingContentType.Completion, metadata);

                    case "response.output.item.added":
                        // 출력 아이템 추가
                        if (root.TryGetProperty("item", out var item))
                        {
                            if (item.TryGetProperty("content", out var itemContent))
                            {
                                var extractedText = ExtractTextFromContent(itemContent);
                                if (!string.IsNullOrEmpty(extractedText))
                                {
                                    return (extractedText, StreamingContentType.Text, metadata);
                                }
                            }
                        }
                        break;

                    case "response.output.item.done":
                        // 아이템 완료 (텍스트 포함 가능)
                        if (root.TryGetProperty("item", out var doneItem))
                        {
                            if (doneItem.TryGetProperty("content", out var doneContent))
                            {
                                if (doneContent.ValueKind == JsonValueKind.Array && doneContent.GetArrayLength() > 0)
                                {
                                    var firstContent = doneContent[0];
                                    if (firstContent.TryGetProperty("text", out var text))
                                    {
                                        return (text.GetString(), StreamingContentType.Text, metadata);
                                    }
                                }
                            }
                        }
                        break;

                    // 기존 형식들
                    case "content_delta":
                        if (root.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("text", out var deltaText2))
                        {
                            return (deltaText2.GetString(), StreamingContentType.Text, metadata);
                        }
                        break;

                    case "output_text":
                        if (root.TryGetProperty("text", out var text2))
                        {
                            return (text2.GetString(), StreamingContentType.Text, metadata);
                        }
                        break;

                    case "message":
                        if (root.TryGetProperty("content", out var content))
                        {
                            var extractedText = ExtractTextFromContent(content);
                            if (!string.IsNullOrEmpty(extractedText))
                            {
                                return (extractedText, StreamingContentType.Text, metadata);
                            }
                        }
                        break;

                    case "done":
                        if (metadata != null)
                            metadata["finish_reason"] = "stop";
                        return (null, StreamingContentType.Completion, metadata);
                }
            }

            // Check direct delta (기존 코드)
            if (root.TryGetProperty("delta", out var directDelta))
            {
                if (directDelta.TryGetProperty("content", out var deltaContent))
                    return (deltaContent.GetString(), StreamingContentType.Text, metadata);
                if (directDelta.TryGetProperty("text", out var deltaText))
                    return (deltaText.GetString(), StreamingContentType.Text, metadata);
            }

            // Check output array (기존 코드)
            if (root.TryGetProperty("output", out var outputArray))
            {
                foreach (var item in outputArray.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var itemType) &&
                        itemType.GetString() == "message" &&
                        item.TryGetProperty("content", out var content))
                    {
                        var extractedText = ExtractTextFromContent(content);
                        if (!string.IsNullOrEmpty(extractedText))
                        {
                            return (extractedText, StreamingContentType.Text, metadata);
                        }
                    }
                }
            }

            return (null, StreamingContentType.Text, metadata);
        }

        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseLegacyApiStream(
            JsonElement root,
            bool includeMetadata)
        {
            var metadata = includeMetadata ? new Dictionary<string, object>() : null;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return (null, StreamingContentType.Text, metadata);

            var choice = choices[0];

            if (metadata != null)
            {
                if (root.TryGetProperty("model", out var model))
                    metadata["model"] = model.GetString();
                if (root.TryGetProperty("id", out var id))
                    metadata["response_id"] = id.GetString();
            }

            if (choice.TryGetProperty("finish_reason", out var finishReason))
            {
                var reason = finishReason.GetString();
                if (reason == "function_call")
                {
                    return (null, StreamingContentType.FunctionCall, metadata);
                }
                else if (reason != null)
                {
                    if (metadata != null)
                        metadata["finish_reason"] = reason;
                    return (null, StreamingContentType.Status, metadata);
                }
            }

            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("function_call", out var functionCall))
                {
                    if (metadata != null)
                    {
                        if (functionCall.TryGetProperty("name", out var name))
                            metadata["function_name"] = name.GetString();
                        if (functionCall.TryGetProperty("arguments", out var args))
                            metadata["function_arguments"] = args.GetString();
                    }
                    return (null, StreamingContentType.FunctionCall, metadata);
                }

                if (delta.TryGetProperty("content", out var content))
                {
                    return (content.GetString(), StreamingContentType.Text, metadata);
                }
            }

            return (null, StreamingContentType.Text, metadata);
        }

        private string ExtractTextFromContent(JsonElement content)
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var result = new StringBuilder();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var contentType))
                    {
                        var type = contentType.GetString();
                        if ((type == "text" || type == "output_text") &&
                            item.TryGetProperty("text", out var text))
                        {
                            result.Append(text.GetString());
                        }
                    }
                }
                return result.ToString();
            }

            return string.Empty;
        }

        #endregion
    }
}