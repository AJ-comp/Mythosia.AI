using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        // Both transports use the same request snapshot, history and function execution policy.
        // Only their wire representations differ. DeepSeek does not store Responses conversations.
        private HttpRequestMessage CreateDeepSeekResponsesRequest(Dictionary<string, object> chatBody)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(chatBody));
            var chat = document.RootElement;
            var input = new List<object>();
            foreach (var message in chat.GetProperty("messages").EnumerateArray())
            {
                var role = message.GetProperty("role").GetString();
                if (role == "tool")
                {
                    input.Add(new { type = "function_call_output", call_id = message.GetProperty("tool_call_id").GetString(),
                        output = ConvertDeepSeekResponsesContent(message.GetProperty("content"), false) });
                    continue;
                }
                if (message.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                    input.Add(new { type = "reasoning", content = new[] { new { type = "reasoning_text", text = reasoning.GetString() } } });
                var content = message.GetProperty("content");
                var hasCalls = message.TryGetProperty("tool_calls", out var calls) && calls.GetArrayLength() > 0;
                if (!hasCalls || content.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(content.GetString()))
                    input.Add(new { type = "message", role, content = ConvertDeepSeekResponsesContent(content, role == "assistant") });
                if (hasCalls)
                    foreach (var call in calls.EnumerateArray())
                    {
                        var function = call.GetProperty("function");
                        input.Add(new { type = "function_call", call_id = call.GetProperty("id").GetString(),
                            name = function.GetProperty("name").GetString(), arguments = function.GetProperty("arguments").GetString() });
                    }
            }
            var thinking = GetEffectiveThinkingOptions(CurrentRequestFeatures);
            var body = new Dictionary<string, object>
            {
                ["model"] = chat.GetProperty("model").GetString()!, ["input"] = input,
                ["stream"] = chat.GetProperty("stream").GetBoolean(),
                ["max_output_tokens"] = chat.GetProperty("max_tokens").GetInt32()
            };
            // Omission enables provider-default thinking; explicit none disables it.
            if (!thinking.ThinkingEnabled || thinking.ReasoningEffort != Models.DeepSeekReasoning.Auto)
                body["reasoning"] = new { effort = !thinking.ThinkingEnabled ? "none" : thinking.ReasoningEffort.ToString().ToLowerInvariant() };
            foreach (var key in new[] { "temperature", "top_p" })
                if (chat.TryGetProperty(key, out var value)) body[key] = value.Clone();
            if (chat.TryGetProperty("tools", out var tools))
            {
                body["tools"] = tools.EnumerateArray().Select(tool =>
                {
                    var function = tool.GetProperty("function");
                    return new { type = "function", name = function.GetProperty("name").GetString(),
                        description = function.GetProperty("description").GetString(), parameters = function.GetProperty("parameters").Clone() };
                }).ToArray();
                var choice = chat.GetProperty("tool_choice");
                body["tool_choice"] = choice.ValueKind == JsonValueKind.String ? (object)choice.GetString()! :
                    new { type = "function", name = choice.GetProperty("function").GetProperty("name").GetString() };
            }
            if (RequestStructuredOutputSchemaJson != null)
            {
                using var schema = JsonDocument.Parse(RequestStructuredOutputSchemaJson);
                body["text"] = new { format = new { type = "json_schema", name = "response", schema = schema.RootElement.Clone() } };
            }
            var request = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Accept", RequestStream ? "text/event-stream" : "application/json");
            if (!string.IsNullOrEmpty(ApiKey)) request.Headers.Add("Authorization", "Bearer " + ApiKey);
            return request;
        }

        private static object ConvertDeepSeekResponsesContent(JsonElement content, bool assistant)
        {
            if (content.ValueKind == JsonValueKind.String) return content.GetString()!;
            if (content.ValueKind == JsonValueKind.Null) return string.Empty;
            if (content.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid DeepSeek message content.");
            return content.EnumerateArray().Select(part =>
            {
                switch (part.GetProperty("type").GetString())
                {
                    case "text": return (object)new { type = assistant ? "output_text" : "input_text", text = part.GetProperty("text").GetString() };
                    case "image_url":
                        var image = part.GetProperty("image_url");
                        return new { type = "input_image", image_url = image.GetProperty("url").GetString(), detail = image.GetProperty("detail").GetString() };
                    case "file": return new { type = "input_image", file_id = part.GetProperty("file_id").GetString() };
                    default: throw new NotSupportedException("Unsupported DeepSeek Responses content part.");
                }
            }).ToArray();
        }

        private static string ConvertDeepSeekResponseToChat(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (ReadDeepSeekString(root, "status") != "completed")
                throw new AIServiceException("DeepSeek Responses did not complete successfully; no assistant turn or tool batch was saved.");
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new AIServiceException("DeepSeek Responses returned an error.", error.GetRawText());
            if (root.TryGetProperty("incomplete_details", out var incomplete) && incomplete.ValueKind != JsonValueKind.Null)
                throw new AIServiceException("DeepSeek Responses returned incomplete details despite its completed status.");
            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array || output.GetArrayLength() == 0)
                throw new AIServiceException("DeepSeek Responses returned no output items.");
            var text = new StringBuilder();
            var reasoning = new StringBuilder();
            var hasReasoning = false;
            var hasAssistantMessage = false;
            var calls = new List<object>();
            foreach (var item in output.EnumerateArray())
            {
                var status = ReadDeepSeekString(item, "status");
                if (status != null && status != "completed") throw new AIServiceException("DeepSeek returned an unfinished output item.");
                switch (ReadDeepSeekString(item, "type"))
                {
                    case "reasoning":
                    case "message":
                        var isReasoning = ReadDeepSeekString(item, "type") == "reasoning";
                        if (!isReasoning && ReadDeepSeekString(item, "role") != "assistant")
                            throw new JsonException("DeepSeek output messages must have the assistant role.");
                        if (!isReasoning) hasAssistantMessage = true;
                        if (isReasoning) hasReasoning = true;
                        if (!item.TryGetProperty("content", out var parts) || parts.ValueKind != JsonValueKind.Array)
                            throw new JsonException("DeepSeek output content must be an array.");
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (ReadDeepSeekString(part, "type") != (isReasoning ? "reasoning_text" : "output_text"))
                                throw new AIServiceException("DeepSeek returned an unsupported output content part.");
                            var value = ReadDeepSeekString(part, "text") ?? throw new JsonException("Missing DeepSeek output text.");
                            (isReasoning ? reasoning : text).Append(value);
                        }
                        break;
                    case "function_call":
                        calls.Add(new { id = ReadDeepSeekString(item, "call_id"), type = "function", function = new
                        {
                            name = ReadDeepSeekString(item, "name"), arguments = ReadDeepSeekString(item, "arguments")
                        } });
                        break;
                    default: throw new AIServiceException("DeepSeek returned an unsupported output item; no tools were executed.");
                }
            }
            if (!hasAssistantMessage && calls.Count == 0)
                throw new AIServiceException("DeepSeek Responses completed without an assistant message or function call.");
            object? usage = null;
            if (root.TryGetProperty("usage", out var rawUsage) && rawUsage.ValueKind != JsonValueKind.Null)
            {
                var input = ReadDeepSeekTokenCount(rawUsage, "input_tokens");
                var outputTokens = ReadDeepSeekTokenCount(rawUsage, "output_tokens");
                usage = new
                {
                    prompt_tokens = input, completion_tokens = outputTokens,
                    total_tokens = ReadDeepSeekTokenCount(rawUsage, "total_tokens"),
                    prompt_tokens_details = rawUsage.TryGetProperty("input_tokens_details", out var inputDetails) ? (object)inputDetails.Clone() : null,
                    completion_tokens_details = rawUsage.TryGetProperty("output_tokens_details", out var outputDetails) ? (object)outputDetails.Clone() : null
                };
            }
            return JsonSerializer.Serialize(new
            {
                model = ReadDeepSeekString(root, "model"), usage,
                choices = new[] { new { finish_reason = calls.Count > 0 ? "tool_calls" : "stop", message = new
                {
                    content = text.ToString(), reasoning_content = hasReasoning ? reasoning.ToString() : null, tool_calls = calls.Count == 0 ? null : calls
                } } }
            });
        }

        private List<StreamingContent> ParseDeepSeekResponsesStreamEvent(string json, DeepSeekStreamState state, StreamOptions options)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var type = ReadDeepSeekString(root, "type") ?? throw new JsonException("DeepSeek SSE event has no type.");
            if (root.TryGetProperty("sequence_number", out var sequence))
            {
                if (!sequence.TryGetInt64(out var number) || number < 0 || (state.ResponseSequence.HasValue && number <= state.ResponseSequence.Value))
                    throw new JsonException("DeepSeek SSE sequence is invalid or repeated.");
                state.ResponseSequence = number;
            }
            if (type == "error" || type == "response.failed" || type == "response.incomplete")
                throw new AIServiceException("DeepSeek Responses stream failed or ended incompletely; no assistant turn or tool batch was saved.");
            var chunks = new List<StreamingContent>();
            if (type == "response.output_text.delta" || type == "response.reasoning_text.delta")
            {
                var delta = ReadDeepSeekString(root, "delta") ?? throw new JsonException("Missing DeepSeek SSE delta.");
                var reasoning = type == "response.reasoning_text.delta";
                if (reasoning) state.HasReasoning = true;
                (reasoning ? state.Reasoning : state.Text).Append(delta);
                if (delta.Length > 0 && (!reasoning || options.IncludeReasoning))
                    chunks.Add(new StreamingContent { Type = reasoning ? StreamingContentType.Reasoning : StreamingContentType.Text, Content = delta });
            }
            else if (type == "response.output_item.added" || type == "response.output_item.done")
            {
                var item = root.GetProperty("item");
                var itemType = ReadDeepSeekString(item, "type");
                if (itemType == "custom_tool_call") throw new AIServiceException("DeepSeek custom tools are not registered through this adapter.");
                if (itemType == "function_call")
                {
                    var call = GetResponsesStreamCall(root, state);
                    CaptureResponsesCallIdentity(call, ReadDeepSeekString(item, "call_id"), ReadDeepSeekString(item, "name"));
                    if (type.EndsWith(".done", StringComparison.Ordinal))
                        CaptureResponsesCallArguments(call, ReadDeepSeekString(item, "arguments") ?? throw new JsonException("Missing function arguments."));
                }
            }
            else if (type == "response.function_call_arguments.delta" || type == "response.function_call_arguments.done")
            {
                var call = GetResponsesStreamCall(root, state);
                if (type.EndsWith(".delta", StringComparison.Ordinal)) call.Arguments.Append(ReadDeepSeekString(root, "delta") ?? throw new JsonException("Missing function argument delta."));
                else CaptureResponsesCallArguments(call, ReadDeepSeekString(root, "arguments") ?? throw new JsonException("Missing function arguments."));
            }
            else if (type == "response.completed")
            {
                var response = root.GetProperty("response");
                var converted = ConvertDeepSeekResponseToChat(response.GetRawText());
                using var chat = JsonDocument.Parse(converted);
                var choice = chat.RootElement.GetProperty("choices")[0];
                var message = choice.GetProperty("message");
                AppendResponsesTerminalText(state.Text, ReadDeepSeekString(message, "content")!, false, chunks, options);
                var reasoning = ReadDeepSeekString(message, "reasoning_content");
                if (reasoning != null) { state.HasReasoning = true; AppendResponsesTerminalText(state.Reasoning, reasoning, true, chunks, options); }
                else if (state.HasReasoning) throw new JsonException("DeepSeek terminal response omitted streamed reasoning.");
                var observed = new HashSet<int>();
                var outputIndex = 0;
                foreach (var item in response.GetProperty("output").EnumerateArray())
                {
                    if (ReadDeepSeekString(item, "type") == "function_call")
                    {
                        var call = state.ResponseCalls.TryGetValue(outputIndex, out var streamed) ? streamed : new DeepSeekStreamCall();
                        CaptureResponsesCallIdentity(call, ReadDeepSeekString(item, "call_id"), ReadDeepSeekString(item, "name"));
                        CaptureResponsesCallArguments(call, ReadDeepSeekString(item, "arguments") ?? throw new JsonException("Missing terminal function arguments."));
                        observed.Add(outputIndex);
                        state.Calls.Add(outputIndex, call);
                    }
                    outputIndex++;
                }
                if (state.ResponseCalls.Keys.Any(index => !observed.Contains(index)))
                    throw new JsonException("DeepSeek terminal response omitted a streamed tool call.");
                state.Model = ReadDeepSeekString(chat.RootElement, "model");
                state.Usage = ParseDeepSeekUsage(chat.RootElement.GetProperty("usage"));
                state.FinishReason = ReadDeepSeekString(choice, "finish_reason");
            }
            return chunks;
        }

        private static DeepSeekStreamCall GetResponsesStreamCall(JsonElement root, DeepSeekStreamState state)
        {
            if (!root.TryGetProperty("output_index", out var raw) || !raw.TryGetInt32(out var index) || index < 0)
                throw new JsonException("DeepSeek function event requires a nonnegative output index.");
            if (!state.ResponseCalls.TryGetValue(index, out var call)) state.ResponseCalls.Add(index, call = new DeepSeekStreamCall());
            return call;
        }

        private static void CaptureResponsesCallIdentity(DeepSeekStreamCall call, string? id, string? name)
        {
            if (id != null) { if (call.Id != null && call.Id != id) throw new JsonException("DeepSeek changed a function call ID."); call.Id = id; }
            if (name != null) { if (call.Name != null && call.Name != name) throw new JsonException("DeepSeek changed a function name."); call.Name = name; }
        }

        private static void CaptureResponsesCallArguments(DeepSeekStreamCall call, string arguments)
        {
            if (call.Arguments.Length > 0 && !string.Equals(call.Arguments.ToString(), arguments, StringComparison.Ordinal))
                throw new JsonException("DeepSeek terminal function arguments do not match streamed arguments.");
            call.Arguments.Clear().Append(arguments);
        }

        private static void AppendResponsesTerminalText(StringBuilder streamed, string terminal, bool reasoning,
            List<StreamingContent> chunks, StreamOptions options)
        {
            if (!terminal.StartsWith(streamed.ToString(), StringComparison.Ordinal))
                throw new JsonException("DeepSeek terminal text does not match streamed content.");
            var tail = terminal.Substring(streamed.Length);
            streamed.Append(tail);
            if (tail.Length > 0 && (!reasoning || options.IncludeReasoning))
                chunks.Add(new StreamingContent { Type = reasoning ? StreamingContentType.Reasoning : StreamingContentType.Text, Content = tail });
        }
    }
}
