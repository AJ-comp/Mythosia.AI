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

namespace Mythosia.AI.Services.Anthropic
{
    public partial class ClaudeService
    {
        #region Streaming Implementation

        public override async IAsyncEnumerable<StreamingContent> StreamAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
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

                var streamQueue = new Queue<StreamingContent>();

                for (int round = 0; round < policy.MaxRounds; round++)
                {
                    if (policy.EnableLogging)
                        Console.WriteLine($"[Claude Stream Round {round + 1}/{policy.MaxRounds}]");

                    var request = useFunctions ? CreateFunctionMessageRequest() : CreateMessageRequest();
                    var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        streamQueue.Enqueue(new StreamingContent
                        {
                            Type = StreamingContentType.Error,
                            Metadata = new Dictionary<string, object>
                            {
                                ["error"] = error,
                                ["status_code"] = (int)response.StatusCode
                            }
                        });

                        while (streamQueue.Count > 0)
                            yield return streamQueue.Dequeue();
                        yield break;
                    }

                    // 스트림 처리
                    var (continueLoop, functionExecuted) = await ProcessClaudeStreamResponse(
                        response, options, policy, streamQueue, cancellationToken);

                    // 큐에 있는 항목들을 yield
                    while (streamQueue.Count > 0)
                    {
                        yield return streamQueue.Dequeue();
                    }

                    if (!continueLoop)
                        break;

                    if (!functionExecuted)
                        break;
                }
            }
            finally
            {
                if (originalChat != null)
                    ActivateChat = originalChat;
            }
        }

        private async Task<(bool continueLoop, bool functionExecuted)> ProcessClaudeStreamResponse(
            HttpResponseMessage response,
            StreamOptions options,
            FunctionCallingPolicy policy,
            Queue<StreamingContent> streamQueue,
            CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var textBuffer = new StringBuilder();
            var currentToolUse = new ToolUseData();
            bool functionEventSent = false;
            string currentModel = null;

            string line;
            while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
            {
                if (!line.StartsWith("data:") && !line.StartsWith("event:"))
                    continue;

                // Event type 처리
                if (line.StartsWith("event:"))
                {
                    var eventType = line.Substring("event:".Length).Trim();
                    currentToolUse.CurrentEventType = eventType;
                    continue;
                }

                var jsonData = line.Substring("data:".Length).Trim();
                if (string.IsNullOrEmpty(jsonData))
                    continue;

                var parseResult = TryParseClaudeStreamChunk(jsonData, currentToolUse, options, policy);
                if (parseResult == null) continue;

                // Model 정보 추출
                if (currentModel == null && parseResult.Model != null)
                {
                    currentModel = parseResult.Model;
                }

                // Tool use (function call) 시작
                if (parseResult.ToolUseStarted && !functionEventSent && options.IncludeFunctionCalls)
                {
                    functionEventSent = true;
                    streamQueue.Enqueue(new StreamingContent
                    {
                        Type = StreamingContentType.FunctionCall,
                        Metadata = new Dictionary<string, object>
                        {
                            ["function_name"] = currentToolUse.Name ?? "unknown",
                            ["tool_use_id"] = currentToolUse.Id ?? "",
                            ["status"] = "started"
                        }
                    });

                    if (policy.EnableLogging)
                        Console.WriteLine($"  → Tool use detected: {currentToolUse.Name}");
                }

                // 텍스트 콘텐츠
                if (!string.IsNullOrEmpty(parseResult.TextContent))
                {
                    textBuffer.Append(parseResult.TextContent);
                    streamQueue.Enqueue(new StreamingContent
                    {
                        Type = StreamingContentType.Text,
                        Content = parseResult.TextContent,
                        Metadata = options.IncludeMetadata ? new Dictionary<string, object>
                        {
                            ["model"] = currentModel ?? ActivateChat.Model
                        } : null
                    });
                }

                // Message 완료
                if (parseResult.MessageComplete)
                {
                    if (options.IncludeMetadata)
                    {
                        streamQueue.Enqueue(new StreamingContent
                        {
                            Type = StreamingContentType.Completion,
                            Metadata = new Dictionary<string, object>
                            {
                                ["total_length"] = textBuffer.Length,
                                ["model"] = currentModel ?? ActivateChat.Model
                            }
                        });
                    }
                    break;
                }
            }

            // Tool use (function) 처리
            if (currentToolUse.IsComplete && !string.IsNullOrEmpty(currentToolUse.Name))
            {
                // Arguments 파싱
                Dictionary<string, object> arguments = new Dictionary<string, object>();
                if (currentToolUse.Arguments.Length > 0)
                {
                    arguments = TryParseArguments(currentToolUse.Arguments.ToString())
                        ?? new Dictionary<string, object>();
                }

                // 통합 ID 생성
                var unifiedId = Guid.NewGuid().ToString();

                // Assistant 메시지 저장 (tool_use 포함) - 통합 메타데이터 사용
                var assistantMsg = new Message(ActorRole.Assistant, textBuffer.ToString())
                {
                    Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.MessageType] = "function_call",
                        [MessageMetadataKeys.FunctionCallId] = unifiedId,  // 통합 ID
                        [MessageMetadataKeys.FunctionName] = currentToolUse.Name,
                        [MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(arguments),
                        [MessageMetadataKeys.ClaudeToolUseId] = currentToolUse.Id ?? Guid.NewGuid().ToString()  // Claude 전용 ID
                    }
                };
                ActivateChat.Messages.Add(assistantMsg);

                // Function 실행
                if (policy.EnableLogging)
                    Console.WriteLine($"  → Executing function: {currentToolUse.Name}");

                var result = await ProcessFunctionCallAsync(currentToolUse.Name, arguments);

                // Function result 이벤트
                streamQueue.Enqueue(new StreamingContent
                {
                    Type = StreamingContentType.FunctionResult,
                    Metadata = new Dictionary<string, object>
                    {
                        ["function_name"] = currentToolUse.Name,
                        ["tool_use_id"] = currentToolUse.Id ?? "",
                        ["status"] = "completed",
                        ["result"] = result
                    }
                });

                if (policy.EnableLogging)
                    Console.WriteLine($"  → Function result: {result}");

                // Function 결과 메시지 저장 - 통합 메타데이터 사용
                ActivateChat.Messages.Add(new Message(ActorRole.Function, result)
                {
                    Metadata = new Dictionary<string, object>
                    {
                        [MessageMetadataKeys.MessageType] = "function_result",
                        [MessageMetadataKeys.FunctionCallId] = unifiedId,  // 동일한 통합 ID
                        [MessageMetadataKeys.FunctionName] = currentToolUse.Name,
                        [MessageMetadataKeys.ClaudeToolUseId] = currentToolUse.Id ?? Guid.NewGuid().ToString()
                    }
                });

                return (true, true); // continue loop, function executed
            }
            else if (textBuffer.Length > 0)
            {
                // Function call 없이 일반 응답만 있는 경우
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, textBuffer.ToString()));
            }

            return (false, false); // don't continue loop
        }

        // 기존 callback 기반 메서드 (호환성 유지)
        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            await foreach (var content in StreamAsync(message, StreamOptions.TextOnlyOptions))
            {
                if (content.Type == StreamingContentType.Text && content.Content != null)
                    await messageReceivedAsync(content.Content);
            }
        }

        #endregion

        #region Helper Classes and Methods

        private class ToolUseData
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public StringBuilder Arguments { get; } = new StringBuilder();
            public bool IsComplete { get; set; }
            public string CurrentEventType { get; set; }
        }

        private class ClaudeStreamParseResult
        {
            public string TextContent { get; set; }
            public bool ToolUseStarted { get; set; }
            public bool MessageComplete { get; set; }
            public string Model { get; set; }
        }

        private ClaudeStreamParseResult TryParseClaudeStreamChunk(
            string jsonData,
            ToolUseData toolUseData,
            StreamOptions options,
            FunctionCallingPolicy policy)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonData);
                var root = doc.RootElement;
                var result = new ClaudeStreamParseResult();

                // Model 정보 추출
                if (root.TryGetProperty("model", out var modelElem))
                {
                    result.Model = modelElem.GetString();
                }

                // Type 기반 처리
                if (root.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();

                    switch (type)
                    {
                        case "message_start":
                            // 메시지 시작 - 메타데이터 추출 가능
                            if (root.TryGetProperty("message", out var msgStart))
                            {
                                if (msgStart.TryGetProperty("model", out var msgModel))
                                {
                                    result.Model = msgModel.GetString();
                                }
                            }
                            break;

                        case "content_block_start":
                            // 콘텐츠 블록 시작
                            if (root.TryGetProperty("content_block", out var blockElement))
                            {
                                if (blockElement.TryGetProperty("type", out var blockType))
                                {
                                    var blockTypeStr = blockType.GetString();

                                    if (blockTypeStr == "tool_use")
                                    {
                                        // Tool use 시작
                                        if (blockElement.TryGetProperty("id", out var idElem))
                                        {
                                            toolUseData.Id = idElem.GetString();
                                        }
                                        if (blockElement.TryGetProperty("name", out var nameElem))
                                        {
                                            toolUseData.Name = nameElem.GetString();
                                        }
                                        toolUseData.Arguments.Clear();
                                        result.ToolUseStarted = true;
                                    }
                                }
                            }
                            break;

                        case "content_block_delta":
                            // 콘텐츠 델타
                            if (root.TryGetProperty("delta", out var deltaElement))
                            {
                                if (deltaElement.TryGetProperty("type", out var deltaType))
                                {
                                    var deltaTypeStr = deltaType.GetString();

                                    if (deltaTypeStr == "text_delta")
                                    {
                                        if (deltaElement.TryGetProperty("text", out var textElem))
                                        {
                                            result.TextContent = textElem.GetString();
                                        }
                                    }
                                    else if (deltaTypeStr == "input_json_delta")
                                    {
                                        if (deltaElement.TryGetProperty("partial_json", out var jsonElem))
                                        {
                                            toolUseData.Arguments.Append(jsonElem.GetString());
                                        }
                                    }
                                }
                            }
                            break;

                        case "content_block_stop":
                            // 콘텐츠 블록 완료
                            if (root.TryGetProperty("index", out var indexElem))
                            {
                                // Tool use가 완료되었는지 확인
                                if (!string.IsNullOrEmpty(toolUseData.Name))
                                {
                                    toolUseData.IsComplete = true;
                                }
                            }
                            break;

                        case "message_delta":
                            // 메시지 델타 (usage 정보 등)
                            if (root.TryGetProperty("usage", out var usageElem) && options.IncludeTokenInfo)
                            {
                                // Token 정보 처리 (필요시)
                            }
                            break;

                        case "message_stop":
                            // 메시지 완료
                            result.MessageComplete = true;
                            break;

                        case "error":
                            // 에러 처리
                            if (root.TryGetProperty("error", out var errorElem))
                            {
                                if (policy.EnableLogging)
                                {
                                    Console.WriteLine($"[Claude Stream Error] {errorElem.GetRawText()}");
                                }
                            }
                            break;
                    }
                }

                return result;
            }
            catch (JsonException ex)
            {
                if (policy.EnableLogging)
                    Console.WriteLine($"[Claude Parse Error] {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, object> TryParseArguments(string argsJson)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}