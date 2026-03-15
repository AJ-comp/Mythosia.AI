using Mythosia.AI.Models;
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

namespace Mythosia.AI.Providers.Alibaba
{
    public partial class QwenService
    {
        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            await foreach (var content in StreamAsync(message, StreamOptions.TextOnlyOptions))
            {
                if (content.Type == StreamingContentType.Text && content.Content != null)
                    await messageReceivedAsync(content.Content);
            }
        }

        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            bool useFunctions = options.IncludeFunctionCalls &&
                                ShouldUseFunctions &&
                                !FunctionsDisabled;

            ChatBlock originalChat = null;
            if (StatelessMode)
            {
                originalChat = ActivateChat;
                ActivateChat = new ChatBlock { SystemMessage = ActivateChat.SystemMessage };
            }

            try
            {
                Stream = true;
                ActivateChat.Messages.Add(message);

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
                    var response = await HttpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.Error,
                            Content = $"API error ({(int)response.StatusCode}): {error}",
                            Metadata = new Dictionary<string, object> { ["error"] = error }
                        };
                        yield break;
                    }

                    var streamData = new QwenStreamData();
                    bool functionCallEventSent = false;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream))
                    {
                        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync();
                            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                                continue;

                            var jsonData = line.Substring("data:".Length).Trim();
                            if (jsonData == "[DONE]")
                            {
                                if (options.IncludeMetadata && streamData.HasContent)
                                {
                                    yield return new StreamingContent
                                    {
                                        Type = StreamingContentType.Completion,
                                        Metadata = new Dictionary<string, object>
                                        {
                                            ["total_length"] = streamData.TextBuffer.Length,
                                            ["model"] = streamData.Model ?? Model
                                        }
                                    };
                                }
                                break;
                            }

                            QwenStreamChunk chunk;
                            try
                            {
                                chunk = ParseQwenStreamChunk(jsonData, options);
                            }
                            catch
                            {
                                continue;
                            }

                            if (chunk.ReasoningText != null && options.IncludeReasoning)
                            {
                                streamData.ReasoningBuffer.Append(chunk.ReasoningText);
                                yield return new StreamingContent
                                {
                                    Type = StreamingContentType.Reasoning,
                                    Content = chunk.ReasoningText,
                                    Metadata = chunk.Metadata
                                };
                            }

                            if (chunk.Text != null)
                            {
                                streamData.TextBuffer.Append(chunk.Text);
                                yield return new StreamingContent
                                {
                                    Type = StreamingContentType.Text,
                                    Content = chunk.Text,
                                    Metadata = chunk.Metadata
                                };
                            }

                            if (chunk.FunctionCall != null)
                            {
                                streamData.UpdateFunctionCall(chunk.FunctionCall);

                                if (!functionCallEventSent && options.IncludeFunctionCalls && streamData.FunctionCall?.Name != null)
                                {
                                    functionCallEventSent = true;
                                    yield return new StreamingContent
                                    {
                                        Type = StreamingContentType.FunctionCall,
                                        Metadata = new Dictionary<string, object>
                                        {
                                            ["function_name"] = streamData.FunctionCall.Name,
                                            ["status"] = "started"
                                        }
                                    };
                                }
                            }

                            if (chunk.Model != null)
                                streamData.Model = chunk.Model;
                        }
                    }

                    if (streamData.HasContent || streamData.FunctionCall != null)
                    {
                        var assistantMsg = new Message(ActorRole.Assistant, streamData.TextContent);

                        if (streamData.FunctionCall != null)
                        {
                            assistantMsg.Metadata = new Dictionary<string, object>
                            {
                                [MessageMetadataKeys.MessageType] = "function_call",
                                [MessageMetadataKeys.FunctionId] = streamData.FunctionCall.Id,
                                [MessageMetadataKeys.FunctionSource] = streamData.FunctionCall.Source,
                                [MessageMetadataKeys.FunctionName] = streamData.FunctionCall.Name,
                                [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(streamData.FunctionCall.Arguments)
                            };
                        }

                        ActivateChat.Messages.Add(assistantMsg);
                    }

                    if (streamData.FunctionCall != null && useFunctions)
                    {
                        if (policy.EnableLogging)
                            Console.WriteLine($"  Executing function: {streamData.FunctionCall.Name}");

                        var functionResult = await ProcessFunctionCallAsync(
                            streamData.FunctionCall.Name,
                            streamData.FunctionCall.Arguments);

                        var resultMetadata = new Dictionary<string, object>
                        {
                            [MessageMetadataKeys.MessageType] = "function_result",
                            [MessageMetadataKeys.FunctionId] = streamData.FunctionCall.Id,
                            [MessageMetadataKeys.FunctionSource] = streamData.FunctionCall.Source,
                            [MessageMetadataKeys.FunctionName] = streamData.FunctionCall.Name
                        };

                        ActivateChat.Messages.Add(new Message(ActorRole.Function, functionResult)
                        {
                            Metadata = resultMetadata
                        });

                        yield return new StreamingContent
                        {
                            Type = StreamingContentType.FunctionResult,
                            Metadata = new Dictionary<string, object>
                            {
                                ["function_name"] = streamData.FunctionCall.Name,
                                ["status"] = "completed",
                                ["result"] = functionResult
                            }
                        };

                        continue;
                    }

                    yield break;
                }
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        private QwenStreamChunk ParseQwenStreamChunk(string jsonData, StreamOptions options)
        {
            var chunk = new QwenStreamChunk();

            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            if (options.IncludeMetadata)
            {
                chunk.Metadata = new Dictionary<string, object>();
                if (root.TryGetProperty("model", out var m))
                {
                    chunk.Model = m.GetString();
                    chunk.Metadata["model"] = chunk.Model!;
                }
            }

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0)
                return chunk;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                return chunk;

            if (delta.TryGetProperty("content", out var contentElem) &&
                contentElem.ValueKind == JsonValueKind.String)
            {
                var text = contentElem.GetString();
                if (!string.IsNullOrEmpty(text))
                    chunk.Text = text;
            }

            if (delta.TryGetProperty("reasoning_content", out var reasoningElem) &&
                reasoningElem.ValueKind == JsonValueKind.String)
            {
                chunk.ReasoningText = reasoningElem.GetString();
            }
            else if (delta.TryGetProperty("reasoning", out var ollamaReasoningElem) &&
                     ollamaReasoningElem.ValueKind == JsonValueKind.String)
            {
                chunk.ReasoningText = ollamaReasoningElem.GetString();
            }

            if (delta.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array &&
                toolCalls.GetArrayLength() > 0)
            {
                var tc = toolCalls[0];
                chunk.FunctionCall = new FunctionCall { Source = IdSource.OpenAI };

                if (tc.TryGetProperty("id", out var idElem))
                    chunk.FunctionCall.Id = idElem.GetString();

                if (tc.TryGetProperty("function", out var funcElem))
                {
                    if (funcElem.TryGetProperty("name", out var nameElem))
                        chunk.FunctionCall.Name = nameElem.GetString();

                    if (funcElem.TryGetProperty("arguments", out var argsElem))
                    {
                        var argsStr = argsElem.GetString();
                        if (!string.IsNullOrEmpty(argsStr))
                        {
                            chunk.FunctionCall.Arguments = new Dictionary<string, object>
                            {
                                ["_partial"] = argsStr
                            };
                        }
                    }
                }
            }

            return chunk;
        }

        private class QwenStreamData
        {
            public StringBuilder TextBuffer { get; } = new StringBuilder();
            public StringBuilder ReasoningBuffer { get; } = new StringBuilder();
            public StringBuilder FunctionArgsBuffer { get; } = new StringBuilder();
            public FunctionCall FunctionCall { get; set; }
            public string Model { get; set; }
            public bool HasContent => TextBuffer.Length > 0;
            public string TextContent => TextBuffer.ToString();

            public void UpdateFunctionCall(FunctionCall fc)
            {
                if (fc == null) return;

                if (!string.IsNullOrEmpty(fc.Name))
                {
                    FunctionCall = fc;
                    FunctionArgsBuffer.Clear();
                }

                if (fc.Arguments?.ContainsKey("_partial") == true)
                {
                    FunctionArgsBuffer.Append(fc.Arguments["_partial"]);

                    var fullArgs = FunctionArgsBuffer.ToString();
                    if (fullArgs.StartsWith("{") && fullArgs.EndsWith("}"))
                    {
                        try
                        {
                            if (FunctionCall != null)
                                FunctionCall.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(fullArgs);
                        }
                        catch { }
                    }
                }
                else if (fc.Arguments != null)
                {
                    if (FunctionCall == null) FunctionCall = fc;
                    else FunctionCall.Arguments = fc.Arguments;
                }

                if (FunctionCall != null && string.IsNullOrEmpty(FunctionCall.Id))
                {
                    FunctionCall.Id = $"call_{Guid.NewGuid().ToString().Substring(0, 20)}";
                }
            }
        }

        private class QwenStreamChunk
        {
            public string Text { get; set; }
            public string ReasoningText { get; set; }
            public FunctionCall FunctionCall { get; set; }
            public string Model { get; set; }
            public Dictionary<string, object> Metadata { get; set; }
        }
    }
}
