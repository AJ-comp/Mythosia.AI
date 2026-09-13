using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        protected override HttpRequestMessage CreateMessageRequest() => CreateAgentRequest(false);

        protected override string? GetRunRequestedModel()
            => ResolveAgentRequestedModel(RequestModel, EffectiveAgentOptions());

        private static string? ResolveAgentRequestedModel(string model, PerplexityAgentOptions options)
            => options.Models != null ? null : options.ModelOverride ??
                (options.Preset.HasValue || options.Profile != null ? null : model);

        private HttpRequestMessage CreateAgentRequest(bool useFunctions)
            => CreateAgentHttpRequest(BuildAgentRequestBody(GetLatestMessages().ToList(),
                GetEffectiveSystemMessageWithRequestContext(), RequestModel, EffectiveAgentOptions(),
                SuppressAgentTools ? new AIRequestFeatures() : CurrentRequestFeatures, useFunctions, RequestStream));

        internal HttpRequestMessage CreateAgentHttpRequest(Dictionary<string, object> body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "v1/agent")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");
            return request;
        }

        internal Dictionary<string, object> BuildAgentRequestBody(
            IReadOnlyList<Message> messages, string? instructions, string model,
            PerplexityAgentOptions options, AIRequestFeatures features, bool useFunctions, bool stream)
        {
            ValidateAgentOptions(options);
            var input = new List<object>();
            foreach (var message in messages)
            {
                ValidateAgentMessage(message);
                AppendAgentMessage(input, message);
            }
            var body = new Dictionary<string, object>
            {
                ["input"] = input, ["stream"] = stream, ["max_output_tokens"] = GetEffectiveMaxTokens()
            };
            // Defaults retain preset/model sampling; explicit settings are forwarded by the gateway.
            if (RequestTemperature != 1.0f) body["temperature"] = RequestTemperature;
            if (RequestTopP != 1.0f) body["top_p"] = RequestTopP;
            if (options.Preset.HasValue) body["preset"] = PresetWireName(options.Preset.Value);
            var requestedModel = ResolveAgentRequestedModel(model, options);
            if (requestedModel != null) body["model"] = requestedModel;
            if (!string.IsNullOrEmpty(instructions)) body["instructions"] = instructions;
            if (options.MaxSteps > 0) body["max_steps"] = options.MaxSteps;
            var level = features.Reasoning?.Level ?? ReasoningLevel.Auto;
            if (level == ReasoningLevel.Auto) level = options.ReasoningEffort;
            if (DisableAgentReasoning) level = MinimumAgentReasoning(options.ModelOverride ?? model);
            if (level != ReasoningLevel.Auto)
            {
                ValidateAgentReasoning(level, options.ModelOverride ??
                    (options.Preset.HasValue || options.Profile != null || options.Models != null ? null : model));
                body["reasoning"] = new Dictionary<string, object> { ["effort"] = level.ToString().ToLowerInvariant() };
            }

            var tools = options.Tools.Select(tool =>
            {
                var result = ObjectGraphSnapshot.CloneDictionary(tool.Parameters);
                result["type"] = tool.Type;
                return result;
            }).ToList();
            var web = tools.FirstOrDefault(tool => (string)tool["type"] == "web_search");
            if (web == null && (features.WebSearch != null || (!options.DisableWebSearch && !options.Preset.HasValue && options.Profile == null)))
            {
                web = new Dictionary<string, object> { ["type"] = "web_search" };
                tools.Add(web);
            }
            if (features.WebSearch?.AllowedDomains != null)
            {
                if (features.WebSearch.AllowedDomains.Any(domain => domain.StartsWith("-", StringComparison.Ordinal)))
                    throw new NotSupportedException("WithWebSearch AllowedDomains must be an allowlist. Use native filters for a denylist.");
                Dictionary<string, object> filters;
                if (web!.TryGetValue("filters", out var value))
                {
                    if (value is Dictionary<string, object> existing) filters = ObjectGraphSnapshot.CloneDictionary(existing);
                    else
                    {
                        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
                        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Web search filters must be an object.");
                        filters = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone());
                    }
                }
                else filters = new Dictionary<string, object>();
                filters["search_domain_filter"] = features.WebSearch.AllowedDomains.ToArray();
                web["filters"] = filters;
            }
            if (useFunctions && RequestFunctionCallMode != FunctionCallMode.None)
                tools.AddRange(RequestFunctions.Select(BuildAgentFunctionTool));
            // Preset/profile tools are merged by the server. An empty array does not promise to remove them.
            if (tools.Count > 0 || options.DisableWebSearch) body["tools"] = tools;
            if (RequestStructuredOutputSchemaJson != null)
            {
                using var schema = JsonDocument.Parse(RequestStructuredOutputSchemaJson);
                body["response_format"] = new
                {
                    type = "json_schema",
                    json_schema = new { name = "structuredoutput", schema = schema.RootElement.Clone() }
                };
            }
            ApplyAdvancedAgentOptions(body, options);
            return body;
        }

        internal static string PresetWireName(PerplexityPreset preset)
            => preset == PerplexityPreset.WideResearch ? "wide-research" : preset.ToString().ToLowerInvariant();

        private static void ValidateAgentReasoning(ReasoningLevel level, string? model)
        {
            if (!Enum.IsDefined(typeof(ReasoningLevel), level)) throw new ArgumentOutOfRangeException(nameof(level));
            if (level == ReasoningLevel.None) throw new NotSupportedException("Perplexity Agent does not support reasoning effort None.");
            if (!HasConfigurableAgentReasoning(model))
                throw new NotSupportedException("Perplexity does not document configurable reasoning effort for the Sonar model. Select a reasoning-capable Agent model.");
            // The gateway validates the selected model's native effort capabilities. Do not silently
            // remap levels between providers or infer support from a preset's changing model.
        }

        private static ReasoningLevel MinimumAgentReasoning(string model)
        {
            if (model.StartsWith("openai/gpt-5", StringComparison.OrdinalIgnoreCase)) return ReasoningLevel.Minimal;
            if (model.StartsWith("google/", StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase) ||
                model.StartsWith("xai/", StringComparison.OrdinalIgnoreCase)) return ReasoningLevel.Low;
            return ReasoningLevel.Auto;
        }

        private static void AppendAgentMessage(List<object> target, Message message)
        {
            if (message.FunctionCallResultBatch != null)
            {
                foreach (var result in message.FunctionCallResultBatch.Results)
                    target.Add(new { type = "function_call_output", call_id = result.Call.Id, output = result.Content });
                return;
            }
            if (message.Role == ActorRole.Assistant &&
                message.Metadata?.TryGetValue(AgentOutputMetadataKey, out var preserved) == true &&
                preserved is IEnumerable<JsonElement> items)
            {
                // Hosted traces remain in public metadata. The Agent input union only accepts
                // messages and custom function inputs/results, not output-only tool or reasoning items.
                AppendPreservedAgentOutput(target, message, items.Where(IsAgentReplayInput).ToList());
                return;
            }
            if (message.FunctionCallBatch != null)
            {
                if (!string.IsNullOrEmpty(message.Content))
                    target.Add(new { type = "message", role = "assistant", content = message.Content });
                foreach (var call in message.FunctionCallBatch.Calls)
                {
                    var item = new Dictionary<string, object>
                    {
                        ["type"] = "function_call", ["call_id"] = call.Id, ["name"] = call.Name,
                        ["arguments"] = JsonSerializer.Serialize(call.Arguments)
                    };
                    if (call.Metadata?.TryGetValue("thought_signature", out var signature) == true)
                        item["thought_signature"] = signature;
                    target.Add(item);
                }
                return;
            }
            if (message.Role == ActorRole.Function)
            {
                var id = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A tool result requires its original call ID.");
                target.Add(new { type = "function_call_output", call_id = id, output = message.Content ?? string.Empty });
                return;
            }
            if (message.Role == ActorRole.Assistant &&
                message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
            {
                var id = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString();
                var name = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Imported function calls require a name and call ID.");
                target.Add(new { type = "function_call", call_id = id, name,
                    arguments = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}" });
                return;
            }
            if (!message.HasMultimodalContent)
            {
                target.Add(new { type = "message", role = message.Role.ToDescription(), content = message.Content ?? string.Empty });
                return;
            }
            var content = new List<object>();
            if (!string.IsNullOrEmpty(message.Content) && !message.Contents.OfType<TextContent>().Any())
                content.Add(new { type = "input_text", text = message.Content });
            foreach (var part in message.Contents)
            {
                if (part is TextContent text) content.Add(new { type = "input_text", text = text.Text });
                else if (part is ImageContent image) content.Add(new { type = "input_image", image_url = image.GetBase64Url() });
            }
            target.Add(new { type = "message", role = message.Role.ToDescription(), content });
        }

        private static void AppendPreservedAgentOutput(List<object> target, Message message, IReadOnlyList<JsonElement> items)
        {
            var original = new StringBuilder();
            foreach (var item in items)
                if (ReadAgentString(item, "type") == "message")
                    foreach (var part in item.GetProperty("content").EnumerateArray())
                        if (IsAgentTextPart(part))
                            original.Append(ReadAgentString(part, ReadAgentString(part, "type") == "refusal" ? "refusal" : "text"));
            var originalText = original.ToString();
            var currentText = message.Content ?? string.Empty;
            var explicitText = message.Contents.OfType<TextContent>().ToArray();
            if (explicitText.Length > 0)
            {
                var partsText = string.Concat(explicitText.Select(part => part.Text));
                if (currentText != originalText && partsText != originalText && currentText != partsText)
                    throw new InvalidOperationException("The edited assistant Content and TextContent parts disagree. Set one text representation or make them identical.");
                if (partsText != originalText) currentText = partsText;
            }
            if (currentText == originalText)
            {
                foreach (var item in items) target.Add(item.Clone());
                return;
            }

            // One public Message represents all text in this response. An edit occupies the first
            // text slot; later slots are cleared while native item IDs and custom calls survive.
            var inserted = false;
            foreach (var item in items)
            {
                if (ReadAgentString(item, "type") != "message")
                {
                    target.Add(item.Clone());
                    continue;
                }
                var content = new List<object>();
                foreach (var part in item.GetProperty("content").EnumerateArray())
                {
                    if (!IsAgentTextPart(part))
                    {
                        content.Add(part.Clone());
                        continue;
                    }
                    var edited = part.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone());
                    edited["type"] = "output_text";
                    edited["text"] = inserted ? string.Empty : currentText;
                    edited.Remove("refusal");
                    // Citation offsets and log probabilities belong to the original text.
                    edited["annotations"] = Array.Empty<object>();
                    if (edited.ContainsKey("logprobs")) edited["logprobs"] = Array.Empty<object>();
                    content.Add(edited);
                    inserted = true;
                }
                var rewritten = item.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone());
                rewritten["content"] = content;
                target.Add(rewritten);
            }
            if (!inserted)
                target.Add(new { type = "message", role = "assistant", status = "completed",
                    content = new[] { new { type = "output_text", text = currentText, annotations = Array.Empty<object>() } } });
        }

        private static bool IsAgentTextPart(JsonElement part)
        {
            var type = ReadAgentString(part, "type");
            return type == "output_text" || type == "text" || type == "refusal";
        }

        private static bool IsAgentReplayInput(JsonElement item)
        {
            var type = ReadAgentString(item, "type");
            return type == "message" || type == "function_call" || type == "function_call_output";
        }

        private static void ValidateAgentMessage(Message message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            foreach (var part in message.Contents)
            {
                if (part is TextContent) continue;
                if (!(part is ImageContent image)) throw new MultimodalNotSupportedException("Perplexity", part.Type);
                if (message.Role != ActorRole.User) throw new NotSupportedException("Agent image input is supported in user messages.");
                if (image.Data != null)
                {
                    if (image.Data.Length == 0 || image.Data.LongLength > 50L * 1024 * 1024)
                        throw new ArgumentException("Agent image data must be nonempty and at most 50 MiB.");
                    var mime = image.MimeType?.ToLowerInvariant();
                    if (mime != "image/png" && mime != "image/jpeg" && mime != "image/gif" && mime != "image/webp")
                        throw new NotSupportedException("Agent images support PNG, JPEG, GIF and WebP.");
                }
                else if (!Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new ArgumentException("Agent image URLs must use HTTPS; use ImageContent bytes for base64 images.");
            }
        }
    }
}
