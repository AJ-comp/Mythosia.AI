// ChatGptService.Parsing.cs 전체 코드

using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            // 디버깅용 코드
            Console.WriteLine($"[DEBUG ParseOpenAIStreamChunk] text={text != null}, type={type}, metadata={metadata != null}");

            // Completion 타입은 항상 처리
            if (type == StreamingContentType.Completion)
            {
                return new StreamingContent
                {
                    Type = type,
                    Content = null,
                    Metadata = metadata
                };
            }

            // response.created 같은 초기 이벤트는 스킵
            if (text == null && type == StreamingContentType.Text && metadata == null)
            {
                Console.WriteLine($"[DEBUG ParseOpenAIStreamChunk] Returning null - skipping empty event");
                return null;
            }

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
            catch (Exception ex)
            {
                // 디버깅용 코드
                Debug.WriteLine($"[DEBUG ParseStreamChunk Exception] {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"[DEBUG ParseStreamChunk JSON] {jsonData.Substring(0, Math.Min(200, jsonData.Length))}");
                return (null, StreamingContentType.Text, null);
            }
        }

        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiStream(
           JsonElement root,
           bool includeMetadata)
        {
            Dictionary<string, object>? metadata = null;

            // Check type property first
            if (root.TryGetProperty("type", out var typeProp))
            {
                var type = typeProp.GetString();

                switch (type)
                {
                    // o3-mini의 새로운 스트리밍 형식
                    case "response.output_text.delta":
                        // delta가 문자열로 직접 오는 경우
                        if (root.TryGetProperty("delta", out var deltaElem))
                        {
                            // delta가 문자열인 경우
                            if (deltaElem.ValueKind == JsonValueKind.String)
                            {
                                var text = deltaElem.GetString();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    return (text, StreamingContentType.Text, null);
                                }
                            }
                            // delta가 객체인 경우 (text 속성 포함)
                            else if (deltaElem.ValueKind == JsonValueKind.Object)
                            {
                                if (deltaElem.TryGetProperty("text", out var textElem))
                                {
                                    var text = textElem.GetString();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        return (text, StreamingContentType.Text, null);
                                    }
                                }
                            }
                        }
                        return (null, StreamingContentType.Text, null);

                    case "response.created":
                        // 초기 응답 - 메타데이터 추출 가능
                        if (includeMetadata && root.TryGetProperty("response", out var responseObj))
                        {
                            metadata = new Dictionary<string, object>();
                            if (responseObj.TryGetProperty("model", out var modelElem))
                                metadata["model"] = modelElem.GetString();
                            if (responseObj.TryGetProperty("id", out var idElem))
                                metadata["response_id"] = idElem.GetString();
                            return (null, StreamingContentType.Text, metadata);
                        }
                        return (null, StreamingContentType.Text, null);

                    case "response.in_progress":
                        // 진행 중 상태 - 일반적으로 무시
                        return (null, StreamingContentType.Text, null);

                    case "response.output_item.added":
                        // 새 출력 아이템 추가됨
                        if (root.TryGetProperty("item", out var item))
                        {
                            if (item.TryGetProperty("type", out var itemType) &&
                                itemType.GetString() == "message")
                            {
                                if (item.TryGetProperty("message", out var messageObj))
                                {
                                    if (messageObj.TryGetProperty("content", out var content))
                                    {
                                        var extractedText = ExtractTextFromContent(content);
                                        if (!string.IsNullOrEmpty(extractedText))
                                        {
                                            return (extractedText, StreamingContentType.Text, null);
                                        }
                                    }
                                }
                            }
                        }
                        return (null, StreamingContentType.Text, null);

                    case "response.output.item.delta":
                        // 증분 텍스트 스트리밍
                        if (root.TryGetProperty("item", out var deltaItem))
                        {
                            if (deltaItem.TryGetProperty("message", out var deltaMessage))
                            {
                                if (deltaMessage.TryGetProperty("content", out var deltaContent))
                                {
                                    var extractedText = ExtractTextFromContent(deltaContent);
                                    if (!string.IsNullOrEmpty(extractedText))
                                    {
                                        return (extractedText, StreamingContentType.Text, null);
                                    }
                                }
                            }
                        }
                        return (null, StreamingContentType.Text, null);

                    case "response.output_item.done":
                        // 아이템 완료 - 최종 텍스트 포함 가능
                        if (root.TryGetProperty("item", out var doneItem))
                        {
                            if (doneItem.TryGetProperty("message", out var doneMessage))
                            {
                                if (doneMessage.TryGetProperty("content", out var doneContent))
                                {
                                    var extractedText = ExtractTextFromContent(doneContent);
                                    if (!string.IsNullOrEmpty(extractedText))
                                    {
                                        return (extractedText, StreamingContentType.Text, null);
                                    }
                                }
                            }
                        }
                        return (null, StreamingContentType.Text, null);

                    case "response.content_part.added":
                        // 컨텐츠 파트 추가됨 - 일반적으로 무시
                        return (null, StreamingContentType.Text, null);

                    case "response.done":
                        // 스트리밍 완료
                        if (includeMetadata)
                        {
                            metadata = new Dictionary<string, object>();
                            metadata["finish_reason"] = "stop";
                            if (root.TryGetProperty("response", out var finalResponse))
                            {
                                if (finalResponse.TryGetProperty("usage", out var usage))
                                {
                                    metadata["usage"] = usage.GetRawText();
                                }
                            }
                            return (null, StreamingContentType.Completion, metadata);
                        }
                        return (null, StreamingContentType.Completion, null);

                    case "response.completed":
                        // 스트리밍 완료
                        if (includeMetadata)
                        {
                            metadata = new Dictionary<string, object>();
                            metadata["finish_reason"] = "stop";
                            if (root.TryGetProperty("response", out var finalResponse))
                            {
                                if (finalResponse.TryGetProperty("usage", out var usage))
                                {
                                    metadata["usage"] = usage.GetRawText();
                                }
                                if (finalResponse.TryGetProperty("id", out var idElem))
                                {
                                    metadata["response_id"] = idElem.GetString();
                                }
                            }
                        }
                        return (null, StreamingContentType.Completion, metadata);

                    // 기존 형식들 (GPT-4, GPT-5 등)
                    case "content_delta":
                        if (root.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("text", out var deltaText2))
                        {
                            return (deltaText2.GetString(), StreamingContentType.Text, null);
                        }
                        break;

                    case "output_text":
                        if (root.TryGetProperty("text", out var text2))
                        {
                            return (text2.GetString(), StreamingContentType.Text, null);
                        }
                        break;

                    case "message":
                        if (root.TryGetProperty("content", out var content2))
                        {
                            var extractedText2 = ExtractTextFromContent(content2);
                            if (!string.IsNullOrEmpty(extractedText2))
                            {
                                return (extractedText2, StreamingContentType.Text, null);
                            }
                        }
                        break;

                    case "done":
                        if (includeMetadata)
                        {
                            metadata = new Dictionary<string, object>();
                            metadata["finish_reason"] = "stop";
                        }
                        return (null, StreamingContentType.Completion, metadata);
                }
            }

            // Check direct delta (기존 코드)
            if (root.TryGetProperty("delta", out var directDelta))
            {
                if (directDelta.TryGetProperty("content", out var deltaContent))
                    return (deltaContent.GetString(), StreamingContentType.Text, null);
                if (directDelta.TryGetProperty("text", out var deltaText))
                    return (deltaText.GetString(), StreamingContentType.Text, null);
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
                            return (extractedText, StreamingContentType.Text, null);
                        }
                    }
                }
            }

            return (null, StreamingContentType.Text, null);
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
                        if ((type == "text" || type == "output_text" || type == "input_text") &&
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