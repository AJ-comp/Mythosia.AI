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
            AddClaudeSpeedHeader(request);

            return request;
        }

        private object BuildRequestBodyWithFunctions()
        {
            PrepareClaudeFeatureMessage();
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = RequestModel,
                ["messages"] = BuildClaudeFunctionMessages(),
                ["temperature"] = RequestTemperature,
                ["max_tokens"] = GetEffectiveMaxTokens(),
                ["stream"] = RequestStream
            };

            ApplySystemMessage(requestBody);
            ApplyClaudeThinking(requestBody);
            ApplyToolsConfig(requestBody);
            ApplyNativeClaudeTools(requestBody);
            ApplyClaudeSpeed(requestBody);

            return requestBody;
        }

        private List<object> BuildClaudeFunctionMessages(bool validateGenerationPrefill = true)
        {
            if (UsesClaudeWireHistory)
                return BuildPreservedClaudeMessages(validateGenerationPrefill);

            var projection = new ClaudeWireProjection();
            var messages = GetLatestMessages().ToList();
            EnsureUserFirstMessage(messages);
            foreach (var message in messages)
            {
                var wire = new List<object>();
                AppendClaudeEffortMarker(wire, message);
                // Convert and validate every record, including records that represent the
                // same legacy assistant turn. Projection alone owns wire grouping.
                wire.Add(ConvertPreservedClaudeMessage(message));
                projection.Append(message, wire.Select(item => JsonSerializer.SerializeToElement(item)).ToArray());
            }
            return projection.Messages;
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

            if (TryReadClaudeAssistantContent(message, out var blocks))
            {
                ValidatePreservedClaudeAssistantText(message, blocks);
                return new
                {
                    role = "assistant",
                    content = blocks
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
            if (string.IsNullOrEmpty(functionId) || !TryReadClaudeLegacySource(message, out var source))
            {
                throw new InvalidOperationException(
                    $"Function result message missing ID or source. Function: {message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)}"
                );
            }

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
            if (TryReadClaudeAssistantContent(message, out var blocks))
            {
                ValidatePreservedClaudeAssistantText(message, blocks);
                return new
                {
                    role = "assistant",
                    content = blocks
                };
            }

            // Reconstruct from metadata
            var functionId = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
            var functionName = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
            var argumentsStr = metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";

            if (string.IsNullOrEmpty(functionId) || !TryReadClaudeLegacySource(message, out var source))
            {
                throw new InvalidOperationException("Assistant function call message missing ID or source");
            }

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

            requestBody["tools"] = RequestFunctions.Select(f => new
            {
                name = f.Name,
                description = f.Description,
                input_schema = new
                {
                    type = "object",
                    properties = f.Parameters.Properties.ToDictionary(pair => pair.Key,
                        pair => BuildClaudeParameterSchema(pair.Value), StringComparer.Ordinal),
                    required = f.Parameters.Required
                }
            }).ToList();

            if (RequestFunctionCallMode == FunctionCallMode.None)
            {
                requestBody["tool_choice"] = new { type = "none" };
            }
            else if (!IsFunctionContinuation() &&
                     !UsesManualExtendedThinkingForRequest() &&
                     !string.IsNullOrWhiteSpace(RequestForceFunctionName))
            {
                // Anthropic's specific-tool form is valid for ordinary and adaptive-thinking
                // requests. Apply it only to the first round: forcing it after tool_result would
                // make the model call the same tool forever instead of producing its final answer.
                requestBody["tool_choice"] = new
                {
                    type = "tool",
                    name = RequestForceFunctionName
                };
            }
            else
            {
                // Manual extended thinking accepts only auto/none tool choice. Adaptive thinking
                // supports specific-tool choice, so it reaches the branch above.
                requestBody["tool_choice"] = new { type = "auto" };
            }
        }

        private static Dictionary<string, object> BuildClaudeParameterSchema(ParameterProperty property)
        {
            if (property == null)
                throw new ArgumentException("Function parameter schemas must not contain null entries.", nameof(property));

            var schema = new Dictionary<string, object>(StringComparer.Ordinal);
            var node = schema;
            var ancestors = new List<ParameterProperty>();
            while (true)
            {
                // Token counting can read service defaults without the request snapshot's
                // graph validation. Bound traversal and reject cycles on this path too.
                if (ancestors.Count >= 64 || ancestors.Any(ancestor => ReferenceEquals(ancestor, property)))
                    throw new ArgumentException("Function parameter schemas must be acyclic and no more than 64 levels deep.", nameof(property));
                ancestors.Add(property);

                // Only schema keywords are mapped. Parameter names and data inside a
                // default value retain their original spelling and serialization.
                if (!string.IsNullOrWhiteSpace(property.Type)) node["type"] = property.Type;
                if (property.Description != null) node["description"] = property.Description;
                if (property.Enum != null) node["enum"] = property.Enum.ToArray();
                if (property.Default != null) node["default"] = property.Default;
                if (property.Items == null) return schema;

                var items = new Dictionary<string, object>(StringComparer.Ordinal);
                node["items"] = items;
                node = items;
                property = property.Items;
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
            => ResolveClaudeThinkingPlan(preserveConversation: true).Type == "enabled";

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
