using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class ChatGptService
    {
        #region Streaming Implementation

        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            bool useFunctions = options.IncludeFunctionCalls &&
                               ActivateChat.ShouldUseFunctions &&
                               !FunctionsDisabled;

            ChatBlock originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = ActivateChat.CloneWithoutMessages();
            }

            try
            {
                ActivateChat.Stream = true;
                ActivateChat.Messages.Add(message);

                // Function calling loop
                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    if (policy.EnableLogging)
                        Console.WriteLine($"[Round {round + 1}/{policy.MaxRounds}]");

                    var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
                    var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.Error,
                            Metadata = new Dictionary<string, object> { ["error"] = error }
                        };
                        yield break;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream);

                    var textBuffer = new StringBuilder();
                    FunctionCall functionCall = null;
                    string currentModel = null;
                    string line;

                    while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
                    {
                        if (!line.StartsWith("data:")) continue;

                        var jsonData = line.Substring(5).Trim();
                        if (jsonData == "[DONE]")
                        {
                            // Send completion metadata if enabled
                            if (options.IncludeMetadata && textBuffer.Length > 0)
                            {
                                yield return new StreamingContent
                                {
                                    Type = StreamingContentType.Completion,
                                    Metadata = new Dictionary<string, object>
                                    {
                                        ["total_length"] = textBuffer.Length,
                                        ["model"] = currentModel ?? ActivateChat.Model
                                    }
                                };
                            }
                            break;
                        }

                        var (text, fc, metadata) = ParseStreamChunkWithMetadata(jsonData, options.IncludeMetadata);

                        // Extract model from metadata if available
                        if (metadata != null && currentModel == null && metadata.TryGetValue("model", out var model))
                            currentModel = model.ToString();

                        if (!string.IsNullOrEmpty(text))
                        {
                            textBuffer.Append(text);
                            var content = new StreamingContent
                            {
                                Type = StreamingContentType.Text,
                                Content = text
                            };

                            if (options.IncludeMetadata && metadata != null)
                                content.Metadata = metadata;

                            yield return content;
                        }

                        if (fc != null)
                            functionCall = fc;
                    }

                    // Save message
                    if (textBuffer.Length > 0 || functionCall != null)
                    {
                        var assistantMsg = new Message(ActorRole.Assistant, textBuffer.ToString());
                        if (functionCall != null)
                        {
                            assistantMsg.Metadata = new Dictionary<string, object>
                            {
                                ["type"] = "function_call",
                                ["call_id"] = functionCall.CallId ?? Guid.NewGuid().ToString(),
                                ["function_name"] = functionCall.Name,
                                ["arguments"] = JsonSerializer.Serialize(functionCall.Arguments)
                            };
                        }
                        ActivateChat.Messages.Add(assistantMsg);
                    }

                    // Handle function call
                    if (functionCall != null && useFunctions)
                    {
                        var result = await ProcessFunctionCallAsync(functionCall.Name, functionCall.Arguments);
                        ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
                        {
                            Metadata = new Dictionary<string, object>
                            {
                                ["function_name"] = functionCall.Name,
                                ["call_id"] = functionCall.CallId ?? Guid.NewGuid().ToString()
                            }
                        });
                        continue; // Next round
                    }

                    yield break; // No function call, done
                }
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            await foreach (var content in StreamAsync(message, StreamOptions.TextOnlyOptions))
            {
                if (content.Type == StreamingContentType.Text && content.Content != null)
                    await messageReceivedAsync(content.Content);
            }
        }

        private (string text, FunctionCall functionCall, Dictionary<string, object> metadata) ParseStreamChunkWithMetadata(
            string jsonData, bool includeMetadata)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;
                Dictionary<string, object> metadata = null;

                // Extract metadata if requested
                if (includeMetadata)
                {
                    metadata = new Dictionary<string, object>();

                    // Get model info
                    if (root.TryGetProperty("model", out var modelElem))
                        metadata["model"] = modelElem.GetString();

                    // Get id
                    if (root.TryGetProperty("id", out var idElem))
                        metadata["response_id"] = idElem.GetString();

                    // Get timestamp if available
                    if (root.TryGetProperty("created", out var createdElem))
                        metadata["created"] = createdElem.GetInt64();
                }

                // Try new API format first
                if (root.TryGetProperty("type", out var typeProp))
                {
                    var type = typeProp.GetString();
                    if (type == "response.output_text.delta" && root.TryGetProperty("delta", out var delta))
                    {
                        if (delta.ValueKind == JsonValueKind.String)
                            return (delta.GetString(), null, metadata);
                        if (delta.TryGetProperty("text", out var text))
                            return (text.GetString(), null, metadata);
                    }
                }

                // Legacy format
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];

                    // Add choice metadata if requested
                    if (includeMetadata)
                    {
                        if (choice.TryGetProperty("index", out var indexElem))
                            metadata["choice_index"] = indexElem.GetInt32();

                        if (choice.TryGetProperty("finish_reason", out var finishElem))
                        {
                            var finishReason = finishElem.GetString();
                            if (!string.IsNullOrEmpty(finishReason))
                                metadata["finish_reason"] = finishReason;
                        }
                    }

                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        string content = null;
                        FunctionCall fc = null;

                        if (delta.TryGetProperty("content", out var contentElem))
                            content = contentElem.GetString();

                        if (delta.TryGetProperty("function_call", out var funcCall))
                        {
                            fc = new FunctionCall { Name = funcCall.GetProperty("name").GetString() };
                            if (funcCall.TryGetProperty("arguments", out var args))
                            {
                                fc.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(args.GetString())
                                    ?? new Dictionary<string, object>();
                            }
                        }

                        return (content, fc, metadata);
                    }
                }

                return (null, null, metadata);
            }
            catch
            {
                return (null, null, null);
            }
        }

        #endregion
    }
}