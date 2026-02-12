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

            if (IsNewApiModel(Model))
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

            foreach (var message in GetLatestMessages())
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

            requestBody["model"] = Model;
            requestBody["input"] = inputList;

            if (!string.IsNullOrEmpty(ActivateChat.SystemMessage))
            {
                requestBody["instructions"] = ActivateChat.SystemMessage;
            }

            if (Stream)
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

            foreach (var message in GetLatestMessages())
            {
                messagesList.Add(ConvertMessageForOpenAI(message));
            }

            requestBody["model"] = Model;
            requestBody["messages"] = messagesList;
            requestBody["temperature"] = Temperature;
            requestBody["top_p"] = TopP;
            requestBody["frequency_penalty"] = FrequencyPenalty;
            requestBody["presence_penalty"] = PresencePenalty;
            requestBody["stream"] = Stream;
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

                // Responses API: check status for incomplete responses
                if (root.TryGetProperty("status", out var statusProp))
                {
                    var status = statusProp.GetString();
                    if (status == "incomplete")
                    {
                        var reason = "unknown";
                        if (root.TryGetProperty("incomplete_details", out var details) &&
                            details.TryGetProperty("reason", out var reasonProp))
                        {
                            reason = reasonProp.GetString();
                        }
                        Console.WriteLine($"[WARNING] Responses API returned incomplete status. Reason: {reason}");
                    }
                }

                // Responses API: check for convenience output_text field first
                if (root.TryGetProperty("output_text", out var outputText) &&
                    outputText.ValueKind == JsonValueKind.String)
                {
                    var text = outputText.GetString();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }

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
            catch (AIServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AIServiceException($"Failed to parse OpenAI response: {ex.Message}", responseContent);
            }
        }

        private string ExtractNewApiResponse(JsonElement output)
        {
            var content = new StringBuilder();
            bool hasReasoningOnly = false;

            foreach (var outputItem in output.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("type", out var typeProp))
                    continue;

                var itemType = typeProp.GetString();

                // Handle "message" output items (standard Responses API format)
                if (itemType == "message")
                {
                    if (outputItem.TryGetProperty("content", out var contentElem))
                    {
                        content.Append(ExtractTextFromContent(contentElem));
                    }
                }
                // Handle direct "text" output items
                else if (itemType == "text" || itemType == "output_text")
                {
                    if (outputItem.TryGetProperty("text", out var textElem))
                    {
                        content.Append(textElem.GetString());
                    }
                }
                // Extract reasoning summary and store for non-streaming access
                else if (itemType == "reasoning")
                {
                    hasReasoningOnly = true;
                    if (outputItem.TryGetProperty("summary", out var summaryElem) &&
                        summaryElem.ValueKind == JsonValueKind.Array)
                    {
                        var reasoningText = new StringBuilder();
                        foreach (var summaryItem in summaryElem.EnumerateArray())
                        {
                            if (summaryItem.TryGetProperty("type", out var sType) &&
                                sType.GetString() == "summary_text" &&
                                summaryItem.TryGetProperty("text", out var sText))
                            {
                                reasoningText.Append(sText.GetString());
                            }
                        }
                        if (reasoningText.Length > 0)
                        {
                            LastReasoningSummary = reasoningText.ToString();
                        }
                    }
                }
            }

            if (content.Length == 0 && hasReasoningOnly)
            {
                Console.WriteLine("[WARNING] GPT-5 output contains only reasoning with no text. " +
                    "This typically means max_output_tokens was too low for reasoning + text generation.");
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

                return IsNewApiModel(Model)
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
            if (root.TryGetProperty("type", out var typeProp))
            {
                var type = typeProp.GetString();

                // 텍스트 델타 이벤트
                if (type == "response.output_text.delta")
                    return ParseNewApiTextDelta(root);

                // 응답 생명주기 이벤트 (created, in_progress, content_part.added)
                if (type == "response.created" || type == "response.in_progress" || type == "response.content_part.added")
                    return ParseNewApiLifecycleEvent(root, type, includeMetadata);

                // 출력 아이템 이벤트 (added, delta, done)
                if (type == "response.output_item.added" || type == "response.output.item.delta" || type == "response.output_item.done")
                    return ParseNewApiOutputItemEvent(root);

                // 스트리밍 완료 이벤트 (response.done, response.completed)
                if (type == "response.done" || type == "response.completed")
                    return ParseNewApiCompletionEvent(root, type, includeMetadata);

                // 기존 형식들 (GPT-4, GPT-5 등)
                if (type == "content_delta" || type == "output_text" || type == "message" || type == "done")
                    return ParseNewApiLegacyTypeEvent(root, type, includeMetadata);
            }

            // Fallback: 직접 delta 또는 output array 처리
            return ParseNewApiFallback(root);
        }

        /// <summary>
        /// response.output_text.delta 이벤트 파싱 (o3, GPT-5 등 새로운 스트리밍 형식)
        /// </summary>
        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiTextDelta(
            JsonElement root)
        {
            if (root.TryGetProperty("delta", out var deltaElem))
            {
                // delta가 문자열인 경우
                if (deltaElem.ValueKind == JsonValueKind.String)
                {
                    var text = deltaElem.GetString();
                    if (!string.IsNullOrEmpty(text))
                        return (text, StreamingContentType.Text, null);
                }
                // delta가 객체인 경우 (text 속성 포함)
                else if (deltaElem.ValueKind == JsonValueKind.Object &&
                         deltaElem.TryGetProperty("text", out var textElem))
                {
                    var text = textElem.GetString();
                    if (!string.IsNullOrEmpty(text))
                        return (text, StreamingContentType.Text, null);
                }
            }

            return (null, StreamingContentType.Text, null);
        }

        /// <summary>
        /// 응답 생명주기 이벤트 파싱 (response.created, response.in_progress, response.content_part.added)
        /// </summary>
        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiLifecycleEvent(
            JsonElement root,
            string type,
            bool includeMetadata)
        {
            if (type == "response.created" && includeMetadata &&
                root.TryGetProperty("response", out var responseObj))
            {
                var metadata = new Dictionary<string, object>();
                if (responseObj.TryGetProperty("model", out var modelElem))
                    metadata["model"] = modelElem.GetString();
                if (responseObj.TryGetProperty("id", out var idElem))
                    metadata["response_id"] = idElem.GetString();
                return (null, StreamingContentType.Text, metadata);
            }

            return (null, StreamingContentType.Text, null);
        }

        /// <summary>
        /// 출력 아이템 이벤트 파싱 (response.output_item.added, response.output.item.delta, response.output_item.done)
        /// </summary>
        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiOutputItemEvent(
            JsonElement root)
        {
            if (!root.TryGetProperty("item", out var itemElem))
                return (null, StreamingContentType.Text, null);

            // message 타입의 아이템에서 텍스트 추출
            if (itemElem.TryGetProperty("type", out var itemType) && itemType.GetString() == "message" &&
                itemElem.TryGetProperty("message", out var messageObj) &&
                messageObj.TryGetProperty("content", out var content))
            {
                var extractedText = ExtractTextFromContent(content);
                if (!string.IsNullOrEmpty(extractedText))
                    return (extractedText, StreamingContentType.Text, null);
            }

            // message 프로퍼티가 직접 있는 경우 (output.item.delta, output_item.done)
            if (itemElem.TryGetProperty("message", out var directMessage) &&
                directMessage.TryGetProperty("content", out var directContent))
            {
                var extractedText = ExtractTextFromContent(directContent);
                if (!string.IsNullOrEmpty(extractedText))
                    return (extractedText, StreamingContentType.Text, null);
            }

            return (null, StreamingContentType.Text, null);
        }

        /// <summary>
        /// 스트리밍 완료 이벤트 파싱 (response.done, response.completed)
        /// </summary>
        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiCompletionEvent(
            JsonElement root,
            string type,
            bool includeMetadata)
        {
            Dictionary<string, object>? metadata = null;

            if (includeMetadata)
            {
                metadata = new Dictionary<string, object> { ["finish_reason"] = "stop" };

                if (root.TryGetProperty("response", out var finalResponse))
                {
                    if (finalResponse.TryGetProperty("usage", out var usage))
                        metadata["usage"] = usage.GetRawText();

                    if (type == "response.completed" &&
                        finalResponse.TryGetProperty("id", out var idElem))
                        metadata["response_id"] = idElem.GetString();
                }
            }

            return (null, StreamingContentType.Completion, metadata);
        }

        /// <summary>
        /// 기존 형식 타입 이벤트 파싱 (content_delta, output_text, message, done)
        /// </summary>
        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiLegacyTypeEvent(
            JsonElement root,
            string type,
            bool includeMetadata)
        {
            switch (type)
            {
                case "content_delta":
                    if (root.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var deltaText))
                        return (deltaText.GetString(), StreamingContentType.Text, null);
                    break;

                case "output_text":
                    if (root.TryGetProperty("text", out var text))
                        return (text.GetString(), StreamingContentType.Text, null);
                    break;

                case "message":
                    if (root.TryGetProperty("content", out var content))
                    {
                        var extractedText = ExtractTextFromContent(content);
                        if (!string.IsNullOrEmpty(extractedText))
                            return (extractedText, StreamingContentType.Text, null);
                    }
                    break;

                case "done":
                    Dictionary<string, object>? metadata = null;
                    if (includeMetadata)
                    {
                        metadata = new Dictionary<string, object> { ["finish_reason"] = "stop" };
                    }
                    return (null, StreamingContentType.Completion, metadata);
            }

            return (null, StreamingContentType.Text, null);
        }

        /// <summary>
        /// Fallback 파싱: 직접 delta 또는 output array 처리
        /// </summary>
        private (string? text, StreamingContentType type, Dictionary<string, object>? metadata) ParseNewApiFallback(
            JsonElement root)
        {
            // Check direct delta
            if (root.TryGetProperty("delta", out var directDelta))
            {
                if (directDelta.TryGetProperty("content", out var deltaContent))
                    return (deltaContent.GetString(), StreamingContentType.Text, null);
                if (directDelta.TryGetProperty("text", out var deltaText))
                    return (deltaText.GetString(), StreamingContentType.Text, null);
            }

            // Check output array
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
                            return (extractedText, StreamingContentType.Text, null);
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