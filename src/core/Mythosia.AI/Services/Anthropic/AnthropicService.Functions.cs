using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Anthropic
{
    public partial class AnthropicService
    {
        #region Function Calling Support

        protected override HttpRequestMessage CreateFunctionMessageRequest()
        {
            var requestBody = BuildRequestBodyWithFunctions();
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = content
            };

            AddClaudeHeaders(request);

            return request;
        }

        private object BuildRequestBodyWithFunctions()
        {
            var messagesList = new List<object>();
            var messages = GetLatestMessages().ToList();
            EnsureUserFirstMessage(messages);

            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];

                if (message.FunctionCallBatch != null)
                {
                    messagesList.Add(ConvertAssistantFunctionCallBatchMessage(message));
                    continue;
                }

                if (message.FunctionCallResultBatch != null)
                {
                    messagesList.Add(ConvertFunctionResultBatchMessage(message.FunctionCallResultBatch));
                    continue;
                }

                if (message.Role == ActorRole.Assistant && IsFunctionCallMessage(message) &&
                    message.Metadata?.ContainsKey(MessageMetadataKeys.OriginalContent) == true)
                {
                    var originalContent = message.Metadata[MessageMetadataKeys.OriginalContent]?.ToString();
                    messagesList.Add(ConvertAssistantFunctionCallMessage(message));

                    // Every call from the same parallel assistant turn stores the same raw content.
                    // Emit that assistant turn once and retain each internal call record for pairing.
                    while (i + 1 < messages.Count &&
                           messages[i + 1].Role == ActorRole.Assistant &&
                           IsFunctionCallMessage(messages[i + 1]) &&
                           string.Equals(
                               messages[i + 1].Metadata?.GetValueOrDefault(MessageMetadataKeys.OriginalContent)?.ToString(),
                               originalContent,
                               StringComparison.Ordinal))
                    {
                        i++;
                    }

                    continue;
                }

                if (message.Role == ActorRole.Function)
                {
                    var resultBlocks = new List<object>();
                    do
                    {
                        resultBlocks.Add(ConvertFunctionResultContent(messages[i]));
                        i++;
                    }
                    while (i < messages.Count && messages[i].Role == ActorRole.Function);

                    i--;
                    messagesList.Add(new { role = "user", content = resultBlocks });
                    continue;
                }

                messagesList.Add(ConvertMessageForFunctionCalling(message));
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["messages"] = messagesList,
                ["temperature"] = Temperature,
                ["max_tokens"] = GetEffectiveMaxTokens(),
                ["stream"] = Stream
            };

            ApplySystemMessage(requestBody);
            ApplyThinkingConfig(requestBody);
            ApplyTemperaturePolicy(requestBody);
            ApplyToolsConfig(requestBody);

            return requestBody;
        }

        private object ConvertMessageForFunctionCalling(Message message)
        {
            if (message.Role == ActorRole.Function)
                return ConvertFunctionResultMessage(message);

            if (message.Role == ActorRole.Assistant &&
                message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
                return ConvertAssistantFunctionCallMessage(message);

            return ConvertMessageForClaude(message);
        }

        private static bool IsFunctionCallMessage(Message message)
        {
            return message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() ==
                   "function_call";
        }

        private object ConvertAssistantFunctionCallBatchMessage(Message message)
        {
            var functionCalls = message.FunctionCallBatch
                ?? throw new InvalidOperationException("Assistant function-call batch is missing.");

            if (functionCalls.Metadata?.TryGetValue(
                    MessageMetadataKeys.OriginalContent,
                    out var originalContent) == true &&
                !string.IsNullOrWhiteSpace(originalContent?.ToString()))
            {
                return new
                {
                    role = "assistant",
                    content = JsonSerializer.Deserialize<JsonElement>(originalContent!.ToString()!)
                };
            }

            var content = new List<object>();
            if (!string.IsNullOrEmpty(message.Content))
                content.Add(new { type = "text", text = message.Content });

            foreach (var call in functionCalls.Calls)
            {
                if (string.IsNullOrEmpty(call.Id))
                    throw new InvalidOperationException(
                        $"Assistant function call is missing an ID. Function: {call.Name}");

                var claudeId = FunctionIdConverter.ToClaudeId(call.Id, call.Source);
                content.Add(new
                {
                    type = "tool_use",
                    id = claudeId,
                    name = call.Name,
                    input = call.Arguments ?? new Dictionary<string, object>()
                });
            }

            return new { role = "assistant", content };
        }

        private object ConvertFunctionResultBatchMessage(FunctionCallResultBatch functionResults)
        {
            var content = functionResults.Results
                .Select(ConvertFunctionResultContent)
                .ToList();

            return new { role = "user", content };
        }

        private object ConvertFunctionResultContent(FunctionCallResult result)
        {
            var call = result.Call
                ?? throw new InvalidOperationException("Function result is missing its originating call.");
            if (string.IsNullOrEmpty(call.Id))
                throw new InvalidOperationException(
                    $"Function result is missing an ID. Function: {call.Name}");

            var block = new Dictionary<string, object>
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = FunctionIdConverter.ToClaudeId(call.Id, call.Source),
                ["content"] = result.Content ?? string.Empty
            };
            if (result.IsError)
                block["is_error"] = true;

            return block;
        }

        private object ConvertFunctionResultMessage(Message message)
        {
            return new
            {
                role = "user",
                content = new[] { ConvertFunctionResultContent(message) }
            };
        }

        private object ConvertFunctionResultContent(Message message)
        {
            var functionId = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
            var functionSource = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionSource);

            if (string.IsNullOrEmpty(functionId) || functionSource == null)
            {
                throw new InvalidOperationException(
                    $"Function result message missing ID or source. Function: {message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)}"
                );
            }

            var source = (IdSource)functionSource;
            var claudeId = FunctionIdConverter.ToClaudeId(functionId, source);

            return new
            {
                type = "tool_result",
                tool_use_id = claudeId,
                content = message.Content ?? ""
            };
        }

        private object ConvertAssistantFunctionCallMessage(Message message)
        {
            var metadata = message.Metadata
                ?? throw new InvalidOperationException("Assistant function-call messages require metadata.");

            // Check if we have the original content preserved
            if (metadata.ContainsKey(MessageMetadataKeys.OriginalContent))
            {
                var originalContent = metadata[MessageMetadataKeys.OriginalContent].ToString();
                return new
                {
                    role = "assistant",
                    content = JsonSerializer.Deserialize<JsonElement>(originalContent)
                };
            }

            // Reconstruct from metadata
            var functionId = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
            var functionSource = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionSource);
            var functionName = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
            var argumentsStr = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";

            if (string.IsNullOrEmpty(functionId) || functionSource == null)
            {
                throw new InvalidOperationException("Assistant function call message missing ID or source");
            }

            var source = (IdSource)functionSource;
            var claudeId = FunctionIdConverter.ToClaudeId(functionId, source);

            var contentList = new List<object>();

            if (!string.IsNullOrEmpty(message.Content))
            {
                contentList.Add(new { type = "text", text = message.Content });
            }

            contentList.Add(new
            {
                type = "tool_use",
                id = claudeId,
                name = functionName,
                input = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsStr) ?? new Dictionary<string, object>()
            });

            return new
            {
                role = "assistant",
                content = contentList
            };
        }

        private void ApplyToolsConfig(Dictionary<string, object> requestBody)
        {
            if (!ShouldUseFunctions) return;

            requestBody["tools"] = Functions.Select(f => new
            {
                name = f.Name,
                description = f.Description,
                input_schema = new
                {
                    type = "object",
                    properties = f.Parameters.Properties,
                    required = f.Parameters.Required
                }
            }).ToList();

            if (FunctionCallMode == FunctionCallMode.None)
            {
                requestBody["tool_choice"] = new { type = "none" };
            }
            else if (!IsFunctionContinuation() &&
                     !UsesManualExtendedThinkingForRequest() &&
                     !string.IsNullOrWhiteSpace(ForceFunctionName))
            {
                // Anthropic's specific-tool form is valid for ordinary and adaptive-thinking
                // requests. Apply it only to the first round: forcing it after tool_result would
                // make the model call the same tool forever instead of producing its final answer.
                requestBody["tool_choice"] = new
                {
                    type = "tool",
                    name = ForceFunctionName
                };
            }
            else
            {
                // Manual extended thinking accepts only auto/none tool choice. Adaptive thinking
                // supports specific-tool choice, so it reaches the branch above.
                requestBody["tool_choice"] = new { type = "auto" };
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

        private bool UsesManualExtendedThinkingForRequest()
        {
            return IsThinkingEnabled && !UsesAdaptiveThinkingForRequest();
        }

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
        {
            try
            {
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                string content = string.Empty;
                var functionCalls = new List<FunctionCall>();
                string? originalContent = null;

                if (root.TryGetProperty("content", out var contentArray) &&
                    contentArray.ValueKind == JsonValueKind.Array)
                {
                    originalContent = contentArray.GetRawText();

                    foreach (var item in contentArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeElement))
                        {
                            var type = typeElement.GetString();

                            if (type == "text" && item.TryGetProperty("text", out var textElement))
                            {
                                content += textElement.GetString();
                            }
                            else if (type == "tool_use")
                            {
                                functionCalls.Add(ParseToolUse(item, functionCalls.Count));
                            }
                        }
                    }
                }

                var batch = new FunctionCallBatch(functionCalls);
                if (functionCalls.Count > 0 && !string.IsNullOrWhiteSpace(originalContent))
                {
                    batch.Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.OriginalContent] = originalContent
                    };
                }

                return (content, batch);
            }
            catch (AIServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AIServiceException(
                    "Claude returned an invalid tool-use response; no tools were executed.",
                    ex.Message,
                    nameof(AIProvider.Anthropic));
            }
        }

        private static FunctionCall ParseToolUse(JsonElement item, int index)
        {
            if (!item.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                throw new AIServiceException(
                    $"Claude returned a tool use without an ID at index {index}; no tools were executed.");
            }

            if (!item.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                throw new AIServiceException(
                    $"Claude returned a tool use without a name at index {index}; no tools were executed.");
            }

            if (!item.TryGetProperty("input", out var inputElement) ||
                inputElement.ValueKind != JsonValueKind.Object)
            {
                throw new AIServiceException(
                    $"Claude returned invalid arguments for tool '{nameElement.GetString()}' at index {index}; no tools were executed.");
            }

            Dictionary<string, object>? arguments;
            try
            {
                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(inputElement.GetRawText());
            }
            catch (JsonException ex)
            {
                throw new AIServiceException(
                    $"Claude returned invalid arguments for tool '{nameElement.GetString()}' at index {index}; no tools were executed.",
                    ex.Message,
                    nameof(AIProvider.Anthropic));
            }

            if (arguments == null)
            {
                throw new AIServiceException(
                    $"Claude returned null arguments for tool '{nameElement.GetString()}' at index {index}; no tools were executed.");
            }

            return new FunctionCall
            {
                Id = idElement.GetString()!,
                Source = IdSource.Claude,
                Name = nameElement.GetString()!,
                Arguments = arguments,
                Index = index
            };
        }

        #endregion
    }
}
