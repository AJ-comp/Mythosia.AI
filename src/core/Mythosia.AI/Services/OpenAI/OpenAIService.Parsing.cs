// OpenAIService.Parsing.cs 전체 코드

using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
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
    public partial class OpenAIService
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

            foreach (var message in GetLatestMessagesWithFunctionFallback())
            {
                var messageParts = new List<object>();
                var role = message.Role.ToDescription();

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
                                image_url = imageContent.GetBase64Url(),
                                detail = imageContent.IsHighDetail ? "high" : "low"
                            });
                        }
                    }
                }

                inputList.Add(new
                {
                    role,
                    content = messageParts
                });
            }

            requestBody["model"] = Model;
            requestBody["input"] = inputList;

            var instructions = GetEffectiveSystemMessageWithRequestContext();

            if (!string.IsNullOrEmpty(instructions))
            {
                requestBody["instructions"] = instructions;
            }

            if (_structuredOutputSchemaJson != null)
            {
                var schemaElement = JsonDocument.Parse(_structuredOutputSchemaJson).RootElement.Clone();
                requestBody["text"] = new Dictionary<string, object>
                {
                    ["format"] = new Dictionary<string, object>
                    {
                        ["type"] = "json_schema",
                        ["name"] = "structured_output",
                        ["strict"] = true,
                        ["schema"] = schemaElement
                    }
                };
            }

            if (Stream)
            {
                requestBody["stream"] = true;
                if (!IsNewApiModel(Model))
                {
                    requestBody["stream_options"] = new Dictionary<string, object>
                    {
                        ["include_usage"] = true
                    };
                }
            }
        }

        private void BuildLegacyApiBody(Dictionary<string, object> requestBody)
        {
            var messagesList = new List<object>();

            var systemMsg = GetEffectiveSystemMessageWithRequestContext();

            if (!string.IsNullOrEmpty(systemMsg))
            {
                messagesList.Add(new { role = "system", content = systemMsg });
            }

            foreach (var message in GetLatestMessagesWithFunctionFallback())
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

            if (Stream)
            {
                requestBody["stream_options"] = new Dictionary<string, object>
                {
                    ["include_usage"] = true
                };
            }

            if (_structuredOutputSchemaJson != null)
            {
                requestBody["response_format"] = new Dictionary<string, object> { ["type"] = "json_object" };
            }
        }

        private object ConvertMessageForOpenAI(Message message)
        {
            var role = message.Role.ToDescription();

            if (!message.HasMultimodalContent)
            {
                return new { role, content = message.Content };
            }

            var contentList = new List<object>();
            foreach (var content in message.Contents)
            {
                contentList.Add(content.ToRequestFormat(Provider));
            }

            return new
            {
                role,
                content = contentList
            };
        }

        #endregion

        #region Response Parsing

        private void EnsureCompletedResponsesApiResponse(string responseContent)
        {
            try
            {
                using var document = JsonDocument.Parse(responseContent);
                var root = document.RootElement;
                var status = root.TryGetProperty("status", out var statusElement) &&
                             statusElement.ValueKind == JsonValueKind.String
                    ? statusElement.GetString()
                    : null;

                if (!string.Equals(status, "completed", StringComparison.Ordinal))
                {
                    var reason = ExtractResponsesFailureReason(root);
                    throw CreateResponsesTerminalException(
                        status ?? "missing",
                        reason,
                        "OpenAI Responses API did not complete successfully; the partial response was not saved and no tools were executed.");
                }

                var refusal = FindResponsesRefusal(root);
                if (refusal != null)
                {
                    throw CreateResponsesTerminalException(
                        "completed",
                        refusal,
                        "OpenAI Responses API refused the request; the response was not saved and no tools were executed.",
                        isRefusal: true);
                }
            }
            catch (AIServiceException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw new AIServiceException(
                    "Failed to parse the OpenAI Responses API result; the response was not saved and no tools were executed.",
                    JsonSerializer.Serialize(new
                    {
                        status = "malformed",
                        parse_error = exception.Message
                    }),
                    nameof(AIProvider.OpenAI));
            }
        }

        private static AIServiceException CreateResponsesTerminalException(
            string status,
            string? reason,
            string message,
            bool isRefusal = false)
        {
            return new AIServiceException(
                message,
                JsonSerializer.Serialize(new
                {
                    status,
                    reason,
                    refusal = isRefusal
                }),
                nameof(AIProvider.OpenAI));
        }

        private static string? ExtractResponsesFailureReason(JsonElement response)
        {
            if (response.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }

                if (error.TryGetProperty("code", out var code) &&
                    code.ValueKind == JsonValueKind.String)
                {
                    return code.GetString();
                }
            }

            if (response.TryGetProperty("incomplete_details", out var incompleteDetails) &&
                incompleteDetails.ValueKind == JsonValueKind.Object &&
                incompleteDetails.TryGetProperty("reason", out var reason) &&
                reason.ValueKind == JsonValueKind.String)
            {
                return reason.GetString();
            }

            if (response.TryGetProperty("message", out var directMessage) &&
                directMessage.ValueKind == JsonValueKind.String)
            {
                return directMessage.GetString();
            }

            if (response.TryGetProperty("code", out var directCode) &&
                directCode.ValueKind == JsonValueKind.String)
            {
                return directCode.GetString();
            }

            return null;
        }

        private static string? FindResponsesRefusal(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (element.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                string.Equals(type.GetString(), "refusal", StringComparison.Ordinal))
            {
                if (element.TryGetProperty("refusal", out var refusal) &&
                    refusal.ValueKind == JsonValueKind.String)
                {
                    return refusal.GetString() ?? string.Empty;
                }

                if (element.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }

                return string.Empty;
            }

            foreach (var propertyName in new[] { "output", "content" })
            {
                if (!element.TryGetProperty(propertyName, out var children) ||
                    children.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var child in children.EnumerateArray())
                {
                    var refusal = FindResponsesRefusal(child);
                    if (refusal != null)
                        return refusal;
                }
            }

            return null;
        }

        protected override string ExtractResponseContent(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;

                string? convenienceOutputText = null;
                if (root.TryGetProperty("output_text", out var outputText) &&
                    outputText.ValueKind == JsonValueKind.String)
                {
                    convenienceOutputText = outputText.GetString();
                }

                if (root.TryGetProperty("output", out var output))
                {
                    var extractedText = ExtractNewApiResponse(output);
                    return !string.IsNullOrEmpty(convenienceOutputText)
                        ? convenienceOutputText
                        : extractedText;
                }

                if (!string.IsNullOrEmpty(convenienceOutputText))
                    return convenienceOutputText;

                if (root.TryGetProperty("choices", out var choices))
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
            CaptureReasoningSummary(output);

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
                else if (itemType == "reasoning")
                {
                    hasReasoningOnly = true;
                }
            }

            if (content.Length == 0 && hasReasoningOnly)
            {
                Console.WriteLine("[WARNING] GPT-5 output contains only reasoning with no text. " +
                    "This typically means max_output_tokens was too low for reasoning + text generation.");
            }

            return content.ToString();
        }

        private void CaptureReasoningSummary(JsonElement output)
        {
            if (output.ValueKind != JsonValueKind.Array)
                return;

            var reasoningText = new StringBuilder();
            foreach (var outputItem in output.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("type", out var typeProp) ||
                    typeProp.GetString() != "reasoning" ||
                    !outputItem.TryGetProperty("summary", out var summaryElement) ||
                    summaryElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var summaryItem in summaryElement.EnumerateArray())
                {
                    if (summaryItem.TryGetProperty("type", out var summaryType) &&
                        summaryType.GetString() == "summary_text" &&
                        summaryItem.TryGetProperty("text", out var summaryText) &&
                        summaryText.ValueKind == JsonValueKind.String)
                    {
                        reasoningText.Append(summaryText.GetString());
                    }
                }
            }

            if (reasoningText.Length > 0)
                LastReasoningSummary = reasoningText.ToString();
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
                {
                    var model = modelElem.GetString();
                    if (model != null)
                        metadata["model"] = model;
                }
                if (responseObj.TryGetProperty("id", out var idElem))
                {
                    var responseId = idElem.GetString();
                    if (responseId != null)
                        metadata["response_id"] = responseId;
                }
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
                    if (finalResponse.TryGetProperty("usage", out var usage) &&
                        usage.ValueKind == JsonValueKind.Object)
                    {
                        var tokenUsage = ParseOpenAICompatibleUsage(usage);
                        if (tokenUsage != null)
                            metadata["_token_usage"] = tokenUsage;
                    }

                    if (type == "response.completed" &&
                        finalResponse.TryGetProperty("id", out var idElem))
                    {
                        var responseId = idElem.GetString();
                        if (responseId != null)
                            metadata["response_id"] = responseId;
                    }
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
                {
                    var modelName = model.GetString();
                    if (modelName != null)
                        metadata["model"] = modelName;
                }
                if (root.TryGetProperty("id", out var id))
                {
                    var responseId = id.GetString();
                    if (responseId != null)
                        metadata["response_id"] = responseId;
                }
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
                        {
                            var functionName = name.GetString();
                            if (functionName != null)
                                metadata["function_name"] = functionName;
                        }
                        if (functionCall.TryGetProperty("arguments", out var args))
                        {
                            var functionArguments = args.GetString();
                            if (functionArguments != null)
                                metadata["function_arguments"] = functionArguments;
                        }
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
