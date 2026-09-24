using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        private static readonly ChatCompletionsProtocol _protocol = ChatCompletionsProtocol.Instance;

        protected override HttpRequestMessage CreateMessageRequest() => BuildDeepSeekRequest(false);

        private HttpRequestMessage BuildDeepSeekRequest(bool useFunctions)
        {
            var thinking = GetEffectiveThinkingOptions(CurrentRequestFeatures);
            var messages = GetLatestMessages().ToList();
            foreach (var message in messages) ValidateDeepSeekMessage(message);
            var extra = new Dictionary<string, object>
            {
                ["thinking"] = new Dictionary<string, object> { ["type"] = thinking.ThinkingEnabled ? "enabled" : "disabled" }
            };
            if (thinking.ThinkingEnabled && thinking.ReasoningEffort != DeepSeekReasoning.Auto)
                extra["reasoning_effort"] = thinking.ReasoningEffort.ToString().ToLowerInvariant();
            var excluded = new HashSet<string> { "frequency_penalty", "presence_penalty" };
            if (thinking.ThinkingEnabled) excluded.Add("temperature");
            else excluded.Add("top_p");
            var parameters = new ProtocolRequestParams
            {
                Model = RequestModel,
                Messages = Array.Empty<Message>(),
                SystemMessage = GetEffectiveSystemMessageWithRequestContext(),
                Temperature = RequestTemperature,
                TopP = Math.Max(0.95f, RequestTopP),
                MaxTokens = GetEffectiveMaxTokens(),
                Stream = RequestStream,
                StructuredOutputSchemaJson = RequestStructuredOutputSchemaJson,
                ExtraParameters = extra,
                ExcludeParameters = excluded
            };
            var body = (Dictionary<string, object>)_protocol.BuildRequestBody(parameters);
            var wireMessages = (List<object>)body["messages"];
            foreach (var message in messages) AppendDeepSeekMessage(wireMessages, message, useFunctions);
            if (useFunctions)
            {
                body["tools"] = RequestFunctions.Select(function => new
                {
                    type = "function",
                    function = new
                    {
                        name = function.Name,
                        description = function.Description,
                        parameters = new
                        {
                            type = "object",
                            properties = function.Parameters.Properties.ToDictionary(pair => pair.Key, pair => ConvertDeepSeekParameter(pair.Value)),
                            required = function.Parameters.Required
                        }
                    }
                }).ToList();
                body["tool_choice"] = RequestFunctionCallMode == FunctionCallMode.None ? "none" : "auto";
                var continuation = messages.LastOrDefault()?.Role == ActorRole.Function ||
                    messages.LastOrDefault()?.FunctionCallResultBatch != null;
                if (RequestFunctionCallMode != FunctionCallMode.None && !continuation && !string.IsNullOrWhiteSpace(RequestForceFunctionName))
                {
                    if (thinking.ThinkingEnabled && !RequestUsesResponsesApi)
                        throw new NotSupportedException("DeepSeek thinking mode does not support a forced named tool. Disable thinking or use automatic tool selection.");
                    body["tool_choice"] = new { type = "function", function = new { name = RequestForceFunctionName } };
                }
            }
            return RequestUsesResponsesApi ? CreateDeepSeekResponsesRequest(body) : _protocol.CreateRequest(ApiKey, body);
        }

        private static Dictionary<string, object> ConvertDeepSeekParameter(ParameterProperty property)
        {
            var result = new Dictionary<string, object> { ["type"] = string.IsNullOrWhiteSpace(property.Type) ? "string" : property.Type };
            if (property.Description != null) result["description"] = property.Description;
            if (property.Enum != null) result["enum"] = property.Enum;
            if (property.Default != null) result["default"] = property.Default;
            if (property.Items != null) result["items"] = ConvertDeepSeekParameter(property.Items);
            return result;
        }

        private static void AppendDeepSeekMessage(List<object> target, Message message, bool includeReasoning)
        {
            if (message.FunctionCallResultBatch != null)
            {
                foreach (var result in message.FunctionCallResultBatch.Results)
                    target.Add(new { role = "tool", tool_call_id = result.Call.Id, content = result.Content });
                return;
            }
            var wire = new Dictionary<string, object>
            {
                ["role"] = message.Role == ActorRole.Function ? "tool" : message.Role.ToDescription(),
                ["content"] = ConvertDeepSeekContent(message)
            };
            if (message.Role == ActorRole.Function)
                wire["tool_call_id"] = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString() ?? string.Empty;
            if (message.FunctionCallBatch != null)
            {
                wire["tool_calls"] = message.FunctionCallBatch.Calls.Select(call => new
                {
                    id = call.Id, type = "function",
                    function = new { name = call.Name, arguments = JsonSerializer.Serialize(call.Arguments) }
                }).ToList();
            }
            else if (message.Role == ActorRole.Assistant &&
                message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)?.ToString() == "function_call")
            {
                wire["tool_calls"] = new[]
                {
                    new
                    {
                        id = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString(),
                        type = "function",
                        function = new
                        {
                            name = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString(),
                            arguments = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}"
                        }
                    }
                };
            }
            if (includeReasoning && message.Role == ActorRole.Assistant &&
                message.Metadata?.TryGetValue(ReasoningMetadataKey, out var reasoning) == true)
                wire["reasoning_content"] = reasoning;
            target.Add(wire);
        }

        private static object ConvertDeepSeekContent(Message message)
        {
            if (!message.HasMultimodalContent) return message.Content ?? string.Empty;
            var content = new List<object>();
            foreach (var part in message.Contents)
            {
                if (part is TextContent text) content.Add(new { type = "text", text = text.Text });
                else if (part is DeepSeekImageFileContent file) content.Add(new { type = "file", file_id = file.FileId });
                else if (part is ImageContent image)
                    content.Add(new { type = "image_url", image_url = new { url = image.GetBase64Url(), detail = image.IsHighDetail ? "high" : "low" } });
                else throw new MultimodalNotSupportedException("DeepSeek", part.Type);
            }
            return content;
        }

        private void ValidateDeepSeekMessage(Message message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            foreach (var part in message.Contents)
            {
                if (part is TextContent) continue;
                if (part is DeepSeekImageFileContent)
                {
                    if (message.Role != ActorRole.User && message.Role != ActorRole.Function)
                        throw new NotSupportedException("DeepSeek image files are supported only in user and tool messages.");
                    if (string.Equals(RequestModel, "deepseek-v4-pro", StringComparison.OrdinalIgnoreCase))
                        throw new MultimodalNotSupportedException("DeepSeek V4 Pro does not support image files. Use AIModels.DeepSeek.Flash.");
                    if (message.Role == ActorRole.Function && string.IsNullOrWhiteSpace(message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString()))
                        throw new ArgumentException("A DeepSeek tool image file requires its original tool-call ID.", nameof(message));
                    continue;
                }
                if (!(part is ImageContent image)) throw new MultimodalNotSupportedException(Provider, part.Type);
                if (message.Role != ActorRole.User && message.Role != ActorRole.Function)
                    throw new NotSupportedException("DeepSeek image content is supported only in user and tool messages.");
                if (message.Role == ActorRole.Function &&
                    string.IsNullOrWhiteSpace(message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionId)?.ToString()))
                    throw new ArgumentException("A DeepSeek tool image message requires its original tool-call ID.", nameof(message));
                // Flash aliases route to the current vision model; custom snapshots remain server-validated.
                if (string.Equals(RequestModel, "deepseek-v4-pro", StringComparison.OrdinalIgnoreCase))
                    throw new MultimodalNotSupportedException("DeepSeek V4 Pro does not support image input. Use AIModels.DeepSeek.Flash.");
                if (image.Data != null)
                {
                    if (image.Data.Length == 0) throw new ArgumentException("DeepSeek image input must contain nonempty data.", nameof(message));
                    var mime = image.MimeType?.ToLowerInvariant();
                    if (mime != "image/jpeg" && mime != "image/png" && mime != "image/gif" && mime != "image/webp")
                        throw new NotSupportedException("DeepSeek image inputs support JPEG, PNG, GIF, and WebP.");
                }
                else if (!Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new ArgumentException("DeepSeek image URLs must be absolute HTTP or HTTPS URLs, or use ImageContent with image bytes.", nameof(message));
            }
        }
    }
}
