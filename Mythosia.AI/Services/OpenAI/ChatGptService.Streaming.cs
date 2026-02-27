using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Utilities;
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
                    if (policy.EnableLogging)
                        Console.WriteLine($"[Stream Round {round + 1}/{policy.MaxRounds}]");

                    // 1. Create and send HTTP request
                    var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
                    var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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

                    // 2. Read stream and yield chunks in real-time
                    var streamData = new StreamData();
                    bool functionCallEventSent = false;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
                        {
                            if (!line.StartsWith("data:")) continue;

                            var jsonData = line.Substring(5).Trim();
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

                            var chunk = ParseStreamChunk(jsonData, options);

                            // Text — yield immediately
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

                            // Reasoning — yield immediately
                            if (chunk.Reasoning != null && options.IncludeReasoning)
                            {
                                streamData.ReasoningBuffer.Append(chunk.Reasoning);
                                yield return new StreamingContent
                                {
                                    Type = StreamingContentType.Reasoning,
                                    Content = chunk.Reasoning,
                                    Metadata = chunk.Metadata
                                };
                            }

                            // Function call — collect for post-processing
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

                            // Completion event
                            if (chunk.IsCompletion && options.IncludeMetadata)
                            {
                                var completionMeta = chunk.Metadata ?? new Dictionary<string, object>();
                                completionMeta["total_length"] = streamData.TextBuffer.Length;
                                completionMeta["model"] = streamData.Model ?? Model;
                                yield return new StreamingContent
                                {
                                    Type = StreamingContentType.Completion,
                                    Metadata = completionMeta
                                };
                            }
                        }
                    }

                    // 3. Save assistant message
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

                    // 4. Execute function if detected
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

                        continue; // next round
                    }

                    yield break; // text-only response complete
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

                // New API format (o3, GPT-5, etc.)
                if (root.TryGetProperty("type", out var typeProp))
                {
                    ParseNewApiStreamChunk(root, typeProp.GetString(), chunk);
                }
                // Legacy format (GPT-4o, etc.)
                else if (root.TryGetProperty("choices", out var choices))
                {
                    ParseLegacyStreamChunk(choices, chunk);
                }
            }
            catch { }

            return chunk;
        }

        private void ParseNewApiStreamChunk(JsonElement root, string type, StreamChunk chunk)
        {
            switch (type)
            {
                // 텍스트 델타
                case "response.output_text.delta":
                    ParseStreamTextDelta(root, chunk);
                    break;

                // 함수 호출 이벤트
                case "response.function_call":
                case "response.function_call.arguments.delta":
                    ParseStreamFunctionCallEvent(root, type, chunk);
                    break;

                // 출력 아이템 이벤트 (텍스트 또는 함수 호출 포함)
                case "response.output_item.added":
                case "response.output_item.delta":
                    ParseStreamOutputItemEvent(root, chunk);
                    break;

                // 추론 요약 이벤트
                case "response.reasoning_summary_text.delta":
                case "response.reasoning_summary_part.added":
                case "response.reasoning_summary_part.done":
                    ParseStreamReasoningEvent(root, type, chunk);
                    break;

                // 응답 생명주기 이벤트
                case "response.created":
                    ParseStreamCreatedEvent(root, chunk);
                    break;

                // 스트리밍 완료 이벤트
                case "response.done":
                case "response.completed":
                    ParseStreamCompletionEvent(root, chunk);
                    break;
            }
        }

        /// <summary>
        /// response.output_text.delta 파싱
        /// </summary>
        private void ParseStreamTextDelta(JsonElement root, StreamChunk chunk)
        {
            if (root.TryGetProperty("delta", out var delta))
            {
                chunk.Text = delta.ValueKind == JsonValueKind.String
                    ? delta.GetString()
                    : delta.TryGetProperty("text", out var t) ? t.GetString() : null;
            }
        }

        /// <summary>
        /// 함수 호출 관련 이벤트 파싱 (response.function_call, response.function_call.arguments.delta)
        /// </summary>
        private void ParseStreamFunctionCallEvent(JsonElement root, string type, StreamChunk chunk)
        {
            if (type == "response.function_call")
            {
                chunk.FunctionCall = new FunctionCall { Source = IdSource.OpenAI };
                if (root.TryGetProperty("function_call", out var fc))
                {
                    if (fc.TryGetProperty("name", out var n))
                        chunk.FunctionCall.Name = n.GetString();
                    if (fc.TryGetProperty("id", out var id))
                        chunk.FunctionCall.Id = id.GetString();
                }
            }
            else // response.function_call.arguments.delta
            {
                if (root.TryGetProperty("delta", out var argDelta))
                {
                    chunk.FunctionCall = new FunctionCall
                    {
                        Arguments = new Dictionary<string, object>
                        {
                            ["_partial"] = argDelta.GetString()
                        },
                        Source = IdSource.OpenAI
                    };
                }
            }
        }

        /// <summary>
        /// 출력 아이템 이벤트 파싱 (response.output_item.added, response.output_item.delta)
        /// </summary>
        private void ParseStreamOutputItemEvent(JsonElement root, StreamChunk chunk)
        {
            if (!root.TryGetProperty("item", out var item))
                return;

            // function_call 타입 아이템
            if (item.TryGetProperty("type", out var itemType) &&
                itemType.GetString() == "function_call")
            {
                chunk.FunctionCall = new FunctionCall { Source = IdSource.OpenAI };

                if (item.TryGetProperty("name", out var fname))
                    chunk.FunctionCall.Name = fname.GetString();
                if (item.TryGetProperty("call_id", out var cid))
                    chunk.FunctionCall.Id = cid.GetString();
                if (item.TryGetProperty("arguments", out var args))
                {
                    chunk.FunctionCall.Arguments = new Dictionary<string, object>
                    {
                        ["_partial"] = args.GetString()
                    };
                }
                return;
            }

            // message 타입 아이템에서 텍스트 추출
            if (item.TryGetProperty("message", out var messageObj) &&
                messageObj.TryGetProperty("content", out var content))
            {
                chunk.Text = ExtractTextFromContent(content);
            }
        }

        /// <summary>
        /// 추론 요약 이벤트 파싱 (response.reasoning_summary_text.delta, response.reasoning_summary_part.*)
        /// </summary>
        private void ParseStreamReasoningEvent(JsonElement root, string type, StreamChunk chunk)
        {
            if (type == "response.reasoning_summary_text.delta" &&
                root.TryGetProperty("delta", out var reasoningDelta))
            {
                chunk.Reasoning = reasoningDelta.ValueKind == JsonValueKind.String
                    ? reasoningDelta.GetString()
                    : null;
            }
            // response.reasoning_summary_part.added / done - 무시
        }

        /// <summary>
        /// response.created 이벤트 파싱
        /// </summary>
        private void ParseStreamCreatedEvent(JsonElement root, StreamChunk chunk)
        {
            if (root.TryGetProperty("response", out var createdResp))
            {
                if (createdResp.TryGetProperty("model", out var createdModel))
                    chunk.Model = createdModel.GetString();
                if (createdResp.TryGetProperty("id", out var createdId))
                {
                    chunk.Metadata ??= new Dictionary<string, object>();
                    chunk.Metadata["response_id"] = createdId.GetString();
                }
            }
        }

        /// <summary>
        /// 스트리밍 완료 이벤트 파싱 (response.done, response.completed)
        /// </summary>
        private void ParseStreamCompletionEvent(JsonElement root, StreamChunk chunk)
        {
            chunk.IsCompletion = true;
            if (root.TryGetProperty("response", out var doneResp))
            {
                chunk.Metadata ??= new Dictionary<string, object>();
                chunk.Metadata["finish_reason"] = "stop";
                if (doneResp.TryGetProperty("usage", out var usage))
                    chunk.Metadata["usage"] = usage.GetRawText();
                if (doneResp.TryGetProperty("model", out var doneModel))
                    chunk.Model = doneModel.GetString();
                if (doneResp.TryGetProperty("id", out var doneId))
                    chunk.Metadata["response_id"] = doneId.GetString();
            }
        }

        private void ParseLegacyStreamChunk(JsonElement choices, StreamChunk chunk)
        {
            if (choices.GetArrayLength() == 0) return;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var legacyDelta)) return;

            if (legacyDelta.TryGetProperty("content", out var legacyContent))
                chunk.Text = legacyContent.GetString();

            if (legacyDelta.TryGetProperty("function_call", out var legacyFc))
            {
                chunk.FunctionCall = new FunctionCall
                {
                    Source = IdSource.OpenAI
                };

                if (legacyFc.TryGetProperty("name", out var name))
                {
                    chunk.FunctionCall.Name = name.GetString();
                    // Legacy API doesn't have call_id, generate one
                    chunk.FunctionCall.Id = $"call_{Guid.NewGuid().ToString().Substring(0, 20)}";
                }

                if (legacyFc.TryGetProperty("arguments", out var args))
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

        // ExtractTextFromContent 메서드 제거 - Parsing.cs에 있는 것 사용

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

                // Ensure ID is set
                if (FunctionCall != null && string.IsNullOrEmpty(FunctionCall.Id))
                {
                    FunctionCall.Id = $"call_{Guid.NewGuid().ToString().Substring(0, 20)}";
                }
            }
        }

        private class StreamChunk
        {
            public string Text { get; set; }
            public string? Reasoning { get; set; }
            public bool IsCompletion { get; set; }
            public FunctionCall FunctionCall { get; set; }
            public string Model { get; set; }
            public Dictionary<string, object> Metadata { get; set; }
        }

        #endregion
    }
}