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

                // Main loop - same as GetCompletionAsync
                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    if (policy.EnableLogging)
                        Console.WriteLine($"[Stream Round {round + 1}/{policy.MaxRounds}]");

                    // Process single round and get result
                    var roundResult = await ProcessStreamRoundAsync(
                        useFunctions, options, policy, cancellationToken);

                    // Yield all content from this round
                    foreach (var content in roundResult.Contents)
                    {
                        yield return content;
                    }

                    // Check if we should continue
                    if (!roundResult.ContinueToNextRound)
                        yield break;
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

        #endregion

        #region Private Methods

        private async Task<StreamRoundResult> ProcessStreamRoundAsync(
            bool useFunctions,
            StreamOptions options,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            var result = new StreamRoundResult();

            // 1. Create and send HTTP request (new for each round)
            var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
            var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                result.Contents.Add(new StreamingContent
                {
                    Type = StreamingContentType.Error,
                    Metadata = new Dictionary<string, object> { ["error"] = error }
                });
                return result;
            }

            // 2. Read and process stream
            var streamData = await ReadStreamAsync(response, options, cancellationToken);
            result.Contents.AddRange(streamData.Contents);

            // 3. Save assistant message
            if (streamData.HasContent || streamData.FunctionCall != null)
            {
                var assistantMsg = new Message(ActorRole.Assistant, streamData.TextContent);
                if (streamData.FunctionCall != null)
                {
                    assistantMsg.Metadata = new Dictionary<string, object>
                    {
                        ["type"] = "function_call",
                        ["call_id"] = streamData.FunctionCall.CallId ?? Guid.NewGuid().ToString(),
                        ["function_name"] = streamData.FunctionCall.Name,
                        ["arguments"] = JsonSerializer.Serialize(streamData.FunctionCall.Arguments)
                    };
                }
                ActivateChat.Messages.Add(assistantMsg);
            }

            // 4. Execute function if detected
            if (streamData.FunctionCall != null && useFunctions)
            {
                if (policy.EnableLogging)
                    Console.WriteLine($"  Executing function: {streamData.FunctionCall.Name}");

                var functionResult = await ProcessFunctionCallAsync(
                    streamData.FunctionCall.Name,
                    streamData.FunctionCall.Arguments);

                // Save function result message
                ActivateChat.Messages.Add(new Message(ActorRole.Function, functionResult)
                {
                    Metadata = new Dictionary<string, object>
                    {
                        ["function_name"] = streamData.FunctionCall.Name,
                        ["call_id"] = streamData.FunctionCall.CallId ?? ""
                    }
                });

                // Add function result event
                result.Contents.Add(new StreamingContent
                {
                    Type = StreamingContentType.FunctionResult,
                    Metadata = new Dictionary<string, object>
                    {
                        ["function_name"] = streamData.FunctionCall.Name,
                        ["status"] = "completed",
                        ["result"] = functionResult
                    }
                });

                result.ContinueToNextRound = true;
            }

            return result;
        }

        private async Task<StreamData> ReadStreamAsync(
            HttpResponseMessage response,
            StreamOptions options,
            CancellationToken cancellationToken)
        {
            var streamData = new StreamData();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            string line;
            bool functionCallEventSent = false;

            while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
            {
                if (!line.StartsWith("data:")) continue;

                var jsonData = line.Substring(5).Trim();
                if (jsonData == "[DONE]")
                {
                    if (options.IncludeMetadata && streamData.HasContent)
                    {
                        streamData.Contents.Add(new StreamingContent
                        {
                            Type = StreamingContentType.Completion,
                            Metadata = new Dictionary<string, object>
                            {
                                ["total_length"] = streamData.TextBuffer.Length,
                                ["model"] = streamData.Model ?? ActivateChat.Model
                            }
                        });
                    }
                    break;
                }

                var chunk = ParseStreamChunk(jsonData, options);

                // Update stream data
                if (chunk.Text != null)
                {
                    streamData.TextBuffer.Append(chunk.Text);
                    streamData.Contents.Add(new StreamingContent
                    {
                        Type = StreamingContentType.Text,
                        Content = chunk.Text,
                        Metadata = chunk.Metadata
                    });
                }

                if (chunk.FunctionCall != null)
                {
                    streamData.UpdateFunctionCall(chunk.FunctionCall);

                    // Send function call event once
                    if (!functionCallEventSent && options.IncludeFunctionCalls && streamData.FunctionCall?.Name != null)
                    {
                        functionCallEventSent = true;
                        streamData.Contents.Add(new StreamingContent
                        {
                            Type = StreamingContentType.FunctionCall,
                            Metadata = new Dictionary<string, object>
                            {
                                ["function_name"] = streamData.FunctionCall.Name,
                                ["status"] = "started"
                            }
                        });
                    }
                }

                if (chunk.Model != null)
                    streamData.Model = chunk.Model;
            }

            return streamData;
        }

        private StreamChunk ParseStreamChunk(string jsonData, StreamOptions options)
        {
            var chunk = new StreamChunk();

            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;

                // Extract metadata if needed
                if (options.IncludeMetadata)
                {
                    chunk.Metadata = new Dictionary<string, object>();
                    if (root.TryGetProperty("model", out var m))
                    {
                        chunk.Model = m.GetString();
                        chunk.Metadata["model"] = chunk.Model;
                    }
                    if (root.TryGetProperty("id", out var id))
                        chunk.Metadata["response_id"] = id.GetString();
                }

                // New API format (o3-mini, etc.)
                if (root.TryGetProperty("type", out var typeProp))
                {
                    ParseNewApiChunk(root, typeProp.GetString(), chunk);
                }
                // Legacy format (GPT-4o, etc.)
                else if (root.TryGetProperty("choices", out var choices))
                {
                    ParseLegacyChunk(choices, chunk);
                }
            }
            catch { }

            return chunk;
        }

        private void ParseNewApiChunk(JsonElement root, string type, StreamChunk chunk)
        {
            switch (type)
            {
                case "response.output_text.delta":
                    if (root.TryGetProperty("delta", out var delta))
                    {
                        chunk.Text = delta.ValueKind == JsonValueKind.String
                            ? delta.GetString()
                            : delta.TryGetProperty("text", out var t) ? t.GetString() : null;
                    }
                    break;

                case "response.function_call":
                    chunk.FunctionCall = new FunctionCall();
                    if (root.TryGetProperty("function_call", out var fc))
                    {
                        if (fc.TryGetProperty("name", out var n))
                            chunk.FunctionCall.Name = n.GetString();
                        if (fc.TryGetProperty("id", out var id))
                            chunk.FunctionCall.CallId = id.GetString();
                    }
                    break;

                case "response.function_call.arguments.delta":
                    if (root.TryGetProperty("delta", out var argDelta))
                    {
                        chunk.FunctionCall = new FunctionCall
                        {
                            Arguments = new Dictionary<string, object>
                            {
                                ["_partial"] = argDelta.GetString()
                            }
                        };
                    }
                    break;

                // Handle output items that might contain function calls
                case "response.output_item.added":
                    if (root.TryGetProperty("item", out var item))
                    {
                        if (item.TryGetProperty("type", out var itemType) &&
                            itemType.GetString() == "function_call")
                        {
                            chunk.FunctionCall = new FunctionCall();

                            if (item.TryGetProperty("name", out var fname))
                                chunk.FunctionCall.Name = fname.GetString();

                            if (item.TryGetProperty("call_id", out var cid))
                                chunk.FunctionCall.CallId = cid.GetString();
                        }
                    }
                    break;

                case "response.output_item.delta":
                    if (root.TryGetProperty("item", out var deltaItem))
                    {
                        // Check if this is a function call delta
                        if (deltaItem.TryGetProperty("type", out var dtype) &&
                            dtype.GetString() == "function_call")
                        {
                            if (deltaItem.TryGetProperty("arguments", out var args))
                            {
                                chunk.FunctionCall = new FunctionCall
                                {
                                    Arguments = new Dictionary<string, object>
                                    {
                                        ["_partial"] = args.GetString()
                                    }
                                };
                            }
                        }
                    }
                    break;
            }
        }

        private void ParseLegacyChunk(JsonElement choices, StreamChunk chunk)
        {
            if (choices.GetArrayLength() == 0) return;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta)) return;

            if (delta.TryGetProperty("content", out var content))
                chunk.Text = content.GetString();

            if (delta.TryGetProperty("function_call", out var fc))
            {
                chunk.FunctionCall = new FunctionCall();

                if (fc.TryGetProperty("name", out var name))
                    chunk.FunctionCall.Name = name.GetString();

                if (fc.TryGetProperty("arguments", out var args))
                {
                    var argsStr = args.GetString();
                    if (!string.IsNullOrEmpty(argsStr))
                    {
                        try
                        {
                            chunk.FunctionCall.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsStr);
                        }
                        catch
                        {
                            chunk.FunctionCall.Arguments = new Dictionary<string, object>
                            {
                                ["_partial"] = argsStr
                            };
                        }
                    }
                }
            }
        }

        #endregion

        #region Helper Classes

        private class StreamRoundResult
        {
            public List<StreamingContent> Contents { get; } = new List<StreamingContent>();
            public bool ContinueToNextRound { get; set; }
        }

        private class StreamData
        {
            public List<StreamingContent> Contents { get; } = new List<StreamingContent>();
            public StringBuilder TextBuffer { get; } = new StringBuilder();
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

                    // Try to parse complete arguments
                    var fullArgs = FunctionArgsBuffer.ToString();
                    if (fullArgs.StartsWith("{") && fullArgs.EndsWith("}"))
                    {
                        try
                        {
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
            }
        }

        private class StreamChunk
        {
            public string Text { get; set; }
            public FunctionCall FunctionCall { get; set; }
            public string Model { get; set; }
            public Dictionary<string, object> Metadata { get; set; }
        }

        #endregion
    }
}