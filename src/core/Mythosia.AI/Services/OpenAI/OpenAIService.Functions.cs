using Mythosia.AI.Models;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        private const string ResponsesOutputItemsMetadataKey = "openai_responses_output_items";

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var requestBody = BuildRequestWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Determine endpoint based on model
            string endpoint = IsNewApiModel(Model)
                ? (Stream ? "responses?stream=true" : "responses")
                : "chat/completions";

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        private object BuildRequestWithFunctions()
        {
            var requestBody = new Dictionary<string, object>();

            if (IsNewApiModel(Model))
            {
                // Build new API format (GPT-5, o3, GPT-4.1)
                BuildNewApiRequest(requestBody);
            }
            else
            {
                // Build legacy API format
                BuildLegacyRequest(requestBody);
            }

            // Apply model-specific parameter configurations
            ApplyModelSpecificParameters(requestBody);

            return requestBody;
        }

        /// <summary>
        /// Creates function schema that works for both old and new API
        /// </summary>
        private Dictionary<string, object> CreateFunctionParameterSchema(FunctionDefinition f, bool isNewApi = false)
        {
            var properties = new Dictionary<string, object>();
            var requiredPropertyNames = new List<string>();
            var declaredRequired = new HashSet<string>(
                f.Parameters?.Required ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            if (f.Parameters?.Properties != null && f.Parameters.Properties.Count > 0)
            {
                foreach (var prop in f.Parameters.Properties)
                {
                    var propObj = new Dictionary<string, object>();

                    // Type is required
                    propObj["type"] = !string.IsNullOrEmpty(prop.Value.Type)
                        ? prop.Value.Type
                        : "string";

                    // Description
                    if (!string.IsNullOrEmpty(prop.Value.Description))
                        propObj["description"] = prop.Value.Description;

                    // Enum values
                    if (prop.Value.Enum != null && prop.Value.Enum.Count > 0)
                        propObj["enum"] = prop.Value.Enum;

                    // JSON Schema annotation. Optionality is defined only by Parameters.Required.
                    if (prop.Value.Default != null)
                        propObj["default"] = prop.Value.Default;

                    // Array items schema
                    if (prop.Value.Items != null)
                        propObj["items"] = ConvertParameterPropertyForOpenAI(prop.Value.Items);

                    properties[prop.Key] = propObj;

                    if (declaredRequired.Contains(prop.Key))
                        requiredPropertyNames.Add(prop.Key);
                }
            }

            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = requiredPropertyNames
            };

            // New API requires additionalProperties: false
            if (isNewApi)
            {
                schema["additionalProperties"] = false;
            }

            return schema;
        }

        private static bool CanUseStrictFunctionSchema(FunctionDefinition function)
        {
            var properties = function.Parameters?.Properties;
            if (properties == null || properties.Count == 0)
                return true;

            var declaredRequired = new HashSet<string>(
                function.Parameters?.Required ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            // OpenAI strict schemas require every property to appear in `required` and every
            // object schema (including array item objects) to set additionalProperties=false.
            // ParameterProperty cannot describe nested object properties/required fields today,
            // so making such a node strict would either be rejected or incorrectly forbid every
            // object member. Keep those tools best-effort until the common schema model can express
            // the full nested contract. `default` remains an annotation and does not make a field
            // required.
            return properties.Keys.All(declaredRequired.Contains) &&
                   properties.Values.All(IsStrictCompatibleParameterProperty);
        }

        private static bool IsStrictCompatibleParameterProperty(ParameterProperty property)
        {
            if (string.Equals(property.Type, "object", StringComparison.OrdinalIgnoreCase))
                return false;

            return property.Items == null || IsStrictCompatibleParameterProperty(property.Items);
        }

        private static List<object> ConvertMessageForResponses(Message message)
        {
            var messageParts = new List<object>();

            if (!message.HasMultimodalContent)
            {
                var textType = message.Role == ActorRole.Assistant ? "output_text" : "input_text";
                messageParts.Add(new
                {
                    type = textType,
                    text = message.Content ?? string.Empty
                });

                return messageParts;
            }

            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent)
                {
                    var textType = message.Role == ActorRole.Assistant ? "output_text" : "input_text";
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

            return messageParts;
        }

        private void BuildNewApiRequest(Dictionary<string, object> requestBody)
        {
            var inputList = new List<object>();

            // Convert messages to new format
            foreach (var message in GetLatestMessages())
            {
                if (message.FunctionCallBatch != null)
                {
                    if (message.FunctionCallBatch.Metadata?.TryGetValue(
                            ResponsesOutputItemsMetadataKey,
                            out var outputItemsObject) == true &&
                        outputItemsObject is IReadOnlyList<JsonElement> outputItems)
                    {
                        foreach (var outputItem in outputItems)
                            inputList.Add(outputItem);
                    }
                    else
                    {
                        foreach (var functionCall in message.FunctionCallBatch.Calls)
                        {
                            var openAiCallId = FunctionIdConverter.ToOpenAIId(
                                functionCall.Id,
                                functionCall.Source);
                            inputList.Add(new
                            {
                                type = "function_call",
                                call_id = openAiCallId,
                                name = functionCall.Name,
                                arguments = JsonSerializer.Serialize(functionCall.Arguments)
                            });
                        }
                    }
                }
                else if (message.FunctionCallResultBatch != null)
                {
                    foreach (var result in message.FunctionCallResultBatch.Results)
                    {
                        var openAiCallId = FunctionIdConverter.ToOpenAIId(
                            result.Call.Id,
                            result.Call.Source);
                        inputList.Add(new
                        {
                            type = "function_call_output",
                            call_id = openAiCallId,
                            output = result.Content
                        });
                    }
                }
                // Legacy imported history without typed batch data.
                else if (message.Role == ActorRole.Assistant &&
                    message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    var functionId = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
                    var functionSource = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionSource);
                    var functionName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
                    var argumentsStr = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";

                    if (string.IsNullOrEmpty(functionId) || functionSource == null)
                    {
                        throw new InvalidOperationException($"Function call missing ID or source. Function: {functionName}");
                    }

                    var source = (IdSource)functionSource;

                    // Reasoning models require every output item from the tool-call response
                    // (including reasoning items) to be replayed with the function output.
                    // Prefer the exact Responses API items when they were captured locally;
                    // cross-provider/imported histories fall back to the synthesized call below.
                    var openAiCallId = FunctionIdConverter.ToOpenAIId(functionId, source);

                    inputList.Add(new
                    {
                        type = "function_call",
                        call_id = openAiCallId,
                        name = functionName,
                        arguments = argumentsStr
                    });
                }
                // Handle Function results
                else if (message.Role == ActorRole.Function)
                {
                    var functionId = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
                    var functionSource = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionSource);
                    var functionName = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();

                    if (string.IsNullOrEmpty(functionId) || functionSource == null)
                    {
                        throw new InvalidOperationException($"Function result missing ID or source. Function: {functionName}");
                    }

                    var source = (IdSource)functionSource;
                    var openAiCallId = FunctionIdConverter.ToOpenAIId(functionId, source);

                    inputList.Add(new
                    {
                        type = "function_call_output",
                        call_id = openAiCallId,
                        output = message.Content ?? ""
                    });
                }
                // Handle regular messages
                else if (message.Role != ActorRole.Assistant ||
                         message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() != "function_call")
                {
                    var role = message.Role.ToDescription();
                    inputList.Add(new
                    {
                        role,
                        content = ConvertMessageForResponses(message)
                    });
                }
            }

            // Convert functions to tools format using unified schema
            var tools = Functions.Select(f => new Dictionary<string, object>
            {
                ["type"] = "function",
                ["name"] = f.Name,
                ["description"] = f.Description,
                ["parameters"] = CreateFunctionParameterSchema(f, isNewApi: true),
                ["strict"] = CanUseStrictFunctionSchema(f)
            }).ToList();

            requestBody["model"] = Model;
            requestBody["input"] = inputList;
            requestBody["tools"] = tools;
            // This allows the model to request multiple tools in one response. Handlers are still
            // Execution order is selected locally by FunctionCallingPolicy.ExecutionMode.
            requestBody["parallel_tool_calls"] = true;

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

            // Tool choice configuration
            if (FunctionCallMode == FunctionCallMode.None)
            {
                requestBody["tool_choice"] = "none";
            }
            else if (!IsFunctionContinuation() &&
                     !string.IsNullOrWhiteSpace(ForceFunctionName))
            {
                requestBody["tool_choice"] = new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["name"] = ForceFunctionName
                };
            }
            else
            {
                requestBody["tool_choice"] = "auto";
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

        private void BuildLegacyRequest(Dictionary<string, object> requestBody)
        {
            var messagesList = new List<object>();

            // Convert messages
            foreach (var message in GetLatestMessages())
            {
                if (message.FunctionCallBatch != null)
                {
                    messagesList.Add(new
                    {
                        role = "assistant",
                        content = string.IsNullOrEmpty(message.Content) ? null : message.Content,
                        tool_calls = message.FunctionCallBatch.Calls.Select(call => new
                        {
                            id = FunctionIdConverter.ToOpenAIId(call.Id, call.Source),
                            type = "function",
                            function = new
                            {
                                name = call.Name,
                                arguments = JsonSerializer.Serialize(call.Arguments)
                            }
                        }).ToList()
                    });
                }
                else if (message.FunctionCallResultBatch != null)
                {
                    foreach (var result in message.FunctionCallResultBatch.Results)
                    {
                        messagesList.Add(new
                        {
                            role = "tool",
                            tool_call_id = FunctionIdConverter.ToOpenAIId(
                                result.Call.Id,
                                result.Call.Source),
                            content = result.Content
                        });
                    }
                }
                else if (message.Role == ActorRole.Function)
                {
                    var functionId = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString()
                        ?? $"call_{Guid.NewGuid():N}";
                    var functionSource = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionSource) is IdSource source
                        ? source
                        : IdSource.OpenAI;
                    messagesList.Add(new
                    {
                        role = "tool",
                        tool_call_id = FunctionIdConverter.ToOpenAIId(functionId, functionSource),
                        content = message.Content ?? ""
                    });
                }
                else if (message.Role == ActorRole.Assistant &&
                         message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                {
                    var functionId = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString()
                        ?? $"call_{Guid.NewGuid():N}";
                    var functionSource = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionSource) is IdSource source
                        ? source
                        : IdSource.OpenAI;
                    var functionName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
                    var argumentsStr = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";

                    messagesList.Add(new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = FunctionIdConverter.ToOpenAIId(functionId, functionSource),
                                type = "function",
                                function = new
                                {
                                    name = functionName,
                                    arguments = argumentsStr
                                }
                            }
                        }
                    });
                }
                else
                {
                    messagesList.Add(ConvertMessageForOpenAI(message));
                }
            }

            var tools = Functions.Select(f => new
            {
                type = "function",
                function = new
                {
                    name = f.Name,
                    description = f.Description,
                    parameters = CreateFunctionParameterSchema(f, isNewApi: false)
                }
            }).ToList();

            requestBody["model"] = Model;
            requestBody["messages"] = messagesList;
            requestBody["tools"] = tools;
            requestBody["temperature"] = Temperature;
            requestBody["stream"] = Stream;

            if (Stream)
            {
                requestBody["stream_options"] = new Dictionary<string, object>
                {
                    ["include_usage"] = true
                };
            }

            if (FunctionCallMode == FunctionCallMode.None)
            {
                requestBody["tool_choice"] = "none";
            }
            else if (!IsFunctionContinuation() &&
                     !string.IsNullOrWhiteSpace(ForceFunctionName))
            {
                requestBody["tool_choice"] = new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = ForceFunctionName
                    }
                };
            }
            else
            {
                requestBody["tool_choice"] = "auto";
            }
        }

        private bool IsFunctionContinuation()
        {
            var lastMessage = GetLatestMessages().LastOrDefault();
            return lastMessage?.Role == ActorRole.Function ||
                   lastMessage?.FunctionCallResultBatch != null ||
                   lastMessage?.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() ==
                       "function_result";
        }

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Check API format and extract accordingly
            if (root.TryGetProperty("output", out var output))
            {
                // New API format (GPT-5, o3, GPT-4.1)
                return ExtractNewApiFunctionCalls(output);
            }
            else if (root.TryGetProperty("choices", out var choices))
            {
                // Legacy API format
                return ExtractLegacyFunctionCalls(choices);
            }

            return (string.Empty, new FunctionCallBatch());
        }

        private (string content, FunctionCallBatch functionCalls) ExtractNewApiFunctionCalls(JsonElement output)
        {
            CaptureReasoningSummary(output);

            string content = string.Empty;
            var functionCalls = new List<FunctionCall>();
            var responseOutputItems = output
                .EnumerateArray()
                .Select(item => item.Clone())
                .ToList();

            foreach (var item in responseOutputItems)
            {
                if (!item.TryGetProperty("type", out var typeElem))
                    continue;

                var type = typeElem.GetString();

                if (type == "message")
                {
                    // Extract text content
                    if (item.TryGetProperty("content", out var messageContent))
                    {
                        if (messageContent.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var contentItem in messageContent.EnumerateArray())
                            {
                                if (contentItem.TryGetProperty("type", out var contentType) &&
                                    contentType.GetString() == "output_text" &&
                                    contentItem.TryGetProperty("text", out var textElem))
                                {
                                    content += textElem.GetString();
                                }
                            }
                        }
                        else if (messageContent.ValueKind == JsonValueKind.String)
                        {
                            content += messageContent.GetString();
                        }
                    }
                }
                else if (type == "function_call")
                {
                    var functionCall = new FunctionCall
                    {
                        Name = item.GetProperty("name").GetString() ?? string.Empty,
                        Arguments = new Dictionary<string, object>(),
                        Source = IdSource.OpenAI,
                        Index = functionCalls.Count
                    };

                    // Responses continuations require the exact provider call_id. Synthesizing one
                    // would produce a function_call_output that cannot match the server response.
                    if (!item.TryGetProperty("call_id", out var callId) ||
                        callId.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(callId.GetString()))
                    {
                        throw CreateInvalidFunctionArgumentsException(
                            functionCall.Name,
                            null,
                            "The function call is missing a non-empty call_id.");
                    }

                    functionCall.Id = callId.GetString()!;

                    if (!item.TryGetProperty("arguments", out var argsElem) ||
                        argsElem.ValueKind != JsonValueKind.String)
                    {
                        throw CreateInvalidFunctionArgumentsException(
                            functionCall.Name,
                            functionCall.Id,
                            "The arguments field is missing or is not a JSON string.");
                    }

                    functionCall.Arguments = ParseFunctionArguments(
                        argsElem.GetString(),
                        functionCall.Name,
                        functionCall.Id);
                    functionCalls.Add(functionCall);
                }
            }

            var batch = new FunctionCallBatch(functionCalls);
            if (functionCalls.Count > 0)
            {
                batch.Metadata = new Dictionary<string, object>
                {
                    [ResponsesOutputItemsMetadataKey] = responseOutputItems
                };
            }

            return (content, batch);
        }

        private (string content, FunctionCallBatch functionCalls) ExtractLegacyFunctionCalls(JsonElement choices)
        {
            if (choices.GetArrayLength() == 0)
                return (string.Empty, new FunctionCallBatch());

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message))
                return (string.Empty, new FunctionCallBatch());

            string? finishReason = null;
            if (choice.TryGetProperty("finish_reason", out var finishReasonElement) &&
                finishReasonElement.ValueKind != JsonValueKind.Null)
            {
                if (finishReasonElement.ValueKind != JsonValueKind.String)
                    throw new AIServiceException("The chat/completions finish_reason field must be a string or null.");
                finishReason = finishReasonElement.GetString();
            }

            string? content = null;
            var functionCalls = new List<FunctionCall>();
            var acceptedStopTerminatedFunctionPayload = false;

            // Extract content
            if (message.TryGetProperty("content", out var contentElement))
            {
                content = contentElement.GetString();
            }

            var hasModernToolCalls = message.TryGetProperty(
                "tool_calls",
                out var toolCallsElement) &&
                toolCallsElement.ValueKind != JsonValueKind.Null;
            if (hasModernToolCalls)
            {
                if (toolCallsElement.ValueKind != JsonValueKind.Array)
                    throw new AIServiceException("The chat/completions tool_calls field must be an array.");

                foreach (var toolCallElement in toolCallsElement.EnumerateArray())
                {
                    if (toolCallElement.ValueKind != JsonValueKind.Object)
                        throw CreateInvalidFunctionArgumentsException(null, null, "The tool call must be an object.");
                    if (!toolCallElement.TryGetProperty("function", out var functionElement) ||
                        functionElement.ValueKind != JsonValueKind.Object)
                        throw CreateInvalidFunctionArgumentsException(null, null, "The function payload is missing.");

                    if (!toolCallElement.TryGetProperty("id", out var idElement) ||
                        idElement.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(idElement.GetString()))
                    {
                        throw CreateInvalidFunctionArgumentsException(
                            null,
                            null,
                            "The tool call is missing a non-empty provider call ID.");
                    }

                    functionCalls.Add(ParseLegacyFunctionCall(
                        functionElement,
                        idElement.GetString(),
                        functionCalls.Count));
                }

                if (!string.Equals(finishReason, "tool_calls", StringComparison.Ordinal))
                {
                    if (string.Equals(finishReason, "stop", StringComparison.Ordinal))
                    {
                        acceptedStopTerminatedFunctionPayload = true;
                    }
                    else
                    {
                        throw new AIServiceException(
                            $"The response contained tool_calls but finish_reason was '{finishReason ?? "missing"}'.");
                    }
                }
            }
            else if (message.TryGetProperty("function_call", out var functionCallElement))
            {
                if (functionCallElement.ValueKind != JsonValueKind.Object)
                    throw new AIServiceException("The legacy function_call field must be an object.");
                functionCalls.Add(ParseLegacyFunctionCall(functionCallElement, null, 0));

                if (!string.Equals(finishReason, "function_call", StringComparison.Ordinal))
                {
                    if (string.Equals(finishReason, "stop", StringComparison.Ordinal))
                    {
                        acceptedStopTerminatedFunctionPayload = true;
                    }
                    else
                    {
                        throw new AIServiceException(
                            $"The response contained function_call but finish_reason was '{finishReason ?? "missing"}'.");
                    }
                }
            }

            if (string.Equals(finishReason, "tool_calls", StringComparison.Ordinal) &&
                functionCalls.Count == 0)
            {
                throw new AIServiceException(
                    "The response ended with finish_reason=tool_calls but contained no usable tool calls.");
            }
            if (string.Equals(finishReason, "function_call", StringComparison.Ordinal) &&
                functionCalls.Count == 0)
            {
                throw new AIServiceException(
                    "The response ended with finish_reason=function_call but contained no usable function call.");
            }

            var batch = new FunctionCallBatch(functionCalls);
            if (acceptedStopTerminatedFunctionPayload)
            {
                batch.Metadata = new Dictionary<string, object>
                {
                    ["function_finish_reason_mismatch"] = "stop"
                };
            }

            return (content ?? string.Empty, batch);
        }

        private static FunctionCall ParseLegacyFunctionCall(
            JsonElement functionElement,
            string? callId,
            int index)
        {
            var functionCall = new FunctionCall
            {
                Name = functionElement.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty,
                Source = IdSource.OpenAI,
                Id = string.IsNullOrWhiteSpace(callId) ? $"call_{Guid.NewGuid():N}" : callId!,
                Index = index
            };

            if (!functionElement.TryGetProperty("arguments", out var argumentsElement) ||
                argumentsElement.ValueKind != JsonValueKind.String)
            {
                throw CreateInvalidFunctionArgumentsException(
                    functionCall.Name,
                    functionCall.Id,
                    "The arguments field is missing or is not a JSON string.");
            }

            functionCall.Arguments = ParseFunctionArguments(
                argumentsElement.GetString(),
                functionCall.Name,
                functionCall.Id);
            return functionCall;
        }

        private static string? ExtractLegacyFinishReason(string response)
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("finish_reason", out var finishReason) ||
                finishReason.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (finishReason.ValueKind != JsonValueKind.String)
                throw new AIServiceException("The chat/completions finish_reason field must be a string or null.");

            return finishReason.GetString();
        }

        private static Dictionary<string, object> ParseFunctionArguments(
            string? argumentsJson,
            string? functionName,
            string? callId)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                throw CreateInvalidFunctionArgumentsException(
                    functionName,
                    callId,
                    "The arguments payload is empty.");
            }

            try
            {
                using var argumentsDocument = JsonDocument.Parse(argumentsJson);
                if (argumentsDocument.RootElement.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Function arguments must be a JSON object.");

                return JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson)
                    ?? throw new JsonException("Function arguments must be a JSON object.");
            }
            catch (JsonException exception)
            {
                throw CreateInvalidFunctionArgumentsException(
                    functionName,
                    callId,
                    "The arguments payload is not a valid JSON object.",
                    exception);
            }
        }

        private static AIServiceException CreateInvalidFunctionArgumentsException(
            string? functionName,
            string? callId,
            string reason,
            Exception? innerException = null)
        {
            var message =
                $"OpenAI returned invalid arguments for function '{functionName}' " +
                $"(call '{callId}'). {reason} The function handler was not executed.";

            return innerException == null
                ? new AIServiceException(message)
                : new AIServiceException(message, innerException);
        }

        private Dictionary<string, object> ConvertParameterPropertyForOpenAI(ParameterProperty prop)
        {
            var result = new Dictionary<string, object>
            {
                ["type"] = prop.Type ?? "string"
            };

            if (!string.IsNullOrEmpty(prop.Description))
                result["description"] = prop.Description;

            if (prop.Enum != null && prop.Enum.Count > 0)
                result["enum"] = prop.Enum;

            if (prop.Items != null)
                result["items"] = ConvertParameterPropertyForOpenAI(prop.Items);

            return result;
        }
    }
}
