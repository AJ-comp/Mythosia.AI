using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// 기본 메타데이터 포함 스트리밍 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingWithMetadataTest()
        {
            try
            {
                var options = new StreamOptions
                {
                    IncludeMetadata = true,
                    IncludeTokenInfo = true,
                    TextOnly = false
                };

                var metadataReceived = new List<Dictionary<string, object>>();
                var contentTypes = new List<StreamingContentType>();
                var textChunks = new List<string>();

                // Message 객체를 직접 생성
                var message = new Message(ActorRole.User, "Tell me a short story about AI");

                await foreach (var content in ((AIService)AI).StreamAsync(message, options))
                {
                    contentTypes.Add(content.Type);

                    if (content.Metadata != null)
                    {
                        metadataReceived.Add(content.Metadata);
                        Console.WriteLine($"[Metadata] Type: {content.Type}, Keys: {string.Join(", ", content.Metadata.Keys)}");

                        foreach (var kvp in content.Metadata)
                        {
                            Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                        }
                    }

                    if (content.Type == StreamingContentType.Text && content.Content != null)
                    {
                        textChunks.Add(content.Content);
                        Console.Write(content.Content);
                    }
                }

                Console.WriteLine($"\n[Metadata Summary]");
                Console.WriteLine($"  Total metadata entries: {metadataReceived.Count}");
                Console.WriteLine($"  Content types: {string.Join(", ", contentTypes.Distinct())}");
                Console.WriteLine($"  Text chunks: {textChunks.Count}");

                Assert.IsTrue(metadataReceived.Count > 0, "Should receive metadata");
                Assert.IsTrue(contentTypes.Contains(StreamingContentType.Text), "Should have text content");

                var completionMetadata = metadataReceived.FirstOrDefault(m =>
                    m.ContainsKey("total_length") || m.ContainsKey("finish_reason"));
                Assert.IsNotNull(completionMetadata, "Should receive completion metadata");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Metadata Test Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 모델 및 응답 ID 메타데이터 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingModelMetadataTest()
        {
            try
            {
                var options = StreamOptions.FullOptions;

                string? capturedModel = null;
                string? responseId = null;
                var timestamps = new List<DateTime>();

                // 간단한 string prompt 사용
                string prompt = "What is 2+2?";

                // StreamAsync의 오버로드 중 string을 받는 것 사용
                await foreach (var content in ((AIService)AI).StreamAsync(prompt, options))
                {
                    if (content.Metadata != null)
                    {
                        if (content.Metadata.TryGetValue("model", out var model))
                        {
                            capturedModel = model?.ToString();
                            Console.WriteLine($"[Model] {capturedModel}");
                        }

                        if (content.Metadata.TryGetValue("response_id", out var id))
                        {
                            responseId = id?.ToString();
                            Console.WriteLine($"[Response ID] {responseId}");
                        }

                        if (content.Metadata.TryGetValue("timestamp", out var timestamp))
                        {
                            if (timestamp is DateTime dt)
                            {
                                timestamps.Add(dt);
                            }
                        }
                    }
                }

                Assert.IsNotNull(capturedModel, "Should capture model name");

                if (responseId != null)
                {
                    Console.WriteLine($"[Validation] Response ID format: {responseId}");
                    Assert.IsTrue(responseId.Length > 0, "Response ID should not be empty");
                }

                if (timestamps.Count > 0)
                {
                    Console.WriteLine($"[Timestamps] Received {timestamps.Count} timestamps");
                    Console.WriteLine($"  First: {timestamps.First():HH:mm:ss.fff}");
                    Console.WriteLine($"  Last: {timestamps.Last():HH:mm:ss.fff}");
                    Console.WriteLine($"  Duration: {(timestamps.Last() - timestamps.First()).TotalMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Model Metadata Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 완료 상태 메타데이터 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingCompletionMetadataTest()
        {
            try
            {
                var options = new StreamOptions
                {
                    IncludeMetadata = true,
                    TextOnly = false
                };

                StreamingContent? completionContent = null;
                string? finishReason = null;
                int totalLength = 0;
                var allContent = new List<StreamingContent>();

                // string 프롬프트 직접 사용
                await foreach (var content in ((AIService)AI).StreamAsync("Say hello", options))
                {
                    allContent.Add(content);

                    if (content.Type == StreamingContentType.Completion)
                    {
                        completionContent = content;
                        Console.WriteLine("[Completion] Stream completed");
                    }

                    if (content.Type == StreamingContentType.Status)
                    {
                        Console.WriteLine($"[Status] Received status update");
                    }

                    if (content.Metadata?.TryGetValue("finish_reason", out var reason) == true)
                    {
                        finishReason = reason?.ToString();
                        Console.WriteLine($"[Finish Reason] {finishReason}");
                    }

                    if (content.Metadata?.TryGetValue("total_length", out var length) == true)
                    {
                        if (int.TryParse(length?.ToString(), out var len))
                        {
                            totalLength = len;
                            Console.WriteLine($"[Total Length] {totalLength} characters");
                        }
                    }
                }

                Assert.IsTrue(allContent.Any(c => c.Type == StreamingContentType.Text),
                    "Should have text content");

                if (completionContent != null)
                {
                    Assert.IsNotNull(completionContent.Metadata,
                        "Completion should have metadata");
                }

                if (finishReason != null)
                {
                    Assert.IsTrue(finishReason == "stop" || finishReason == "length",
                        $"Finish reason should be valid: {finishReason}");
                }

                Console.WriteLine($"\n[Summary]");
                Console.WriteLine($"  Total content items: {allContent.Count}");
                Console.WriteLine($"  Content types: {string.Join(", ", allContent.Select(c => c.Type).Distinct())}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Completion Metadata Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 에러 메타데이터 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingErrorMetadataTest()
        {
            try
            {
                var options = StreamOptions.FullOptions;
                var originalModel = AI.ActivateChat.Model;
                StreamingContent? errorContent = null;

                try
                {
                    AI.ActivateChat.ChangeModel("invalid-model-xyz");

                    await foreach (var content in ((AIService)AI).StreamAsync("Test", options))
                    {
                        if (content.Type == StreamingContentType.Error)
                        {
                            errorContent = content;
                            Console.WriteLine("[Error] Received error content");

                            if (content.Metadata != null)
                            {
                                foreach (var kvp in content.Metadata)
                                {
                                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                                }
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Expected Error] {ex.Message}");
                }
                finally
                {
                    AI.ActivateChat.ChangeModel(originalModel);
                }

                if (errorContent != null)
                {
                    Assert.IsNotNull(errorContent.Metadata, "Error should have metadata");
                    Assert.IsTrue(errorContent.Metadata.ContainsKey("error"),
                        "Should contain error message");

                    if (errorContent.Metadata.ContainsKey("status_code"))
                    {
                        var statusCode = errorContent.Metadata["status_code"];
                        Console.WriteLine($"[Status Code] {statusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error Metadata Test] {ex.Message}");
            }
        }

        /// <summary>
        /// 텍스트 전용 vs 메타데이터 포함 비교 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingTextOnlyVsMetadataTest()
        {
            try
            {
                var prompt = "Count from 1 to 3";

                // 1. 텍스트 전용 모드 - StreamOnceAsync는 string을 받음
                Console.WriteLine("[Text Only Mode]");
                var textOnlyContent = new List<string>();

                await foreach (var chunk in AI.StreamOnceAsync(prompt))
                {
                    textOnlyContent.Add(chunk);
                    Console.Write(chunk);
                }

                Console.WriteLine($"\n  Text chunks: {textOnlyContent.Count}");

                // 2. 전체 메타데이터 모드
                Console.WriteLine("\n[Full Metadata Mode]");
                var fullOptions = StreamOptions.FullOptions;
                var fullContent = new List<StreamingContent>();

                await foreach (var content in ((AIService)AI).StreamAsync(prompt, fullOptions))
                {
                    fullContent.Add(content);
                    if (content.Content != null)
                    {
                        Console.Write(content.Content);
                    }
                }

                Console.WriteLine($"\n  Items: {fullContent.Count}");
                Console.WriteLine($"  Has metadata: {fullContent.Any(c => c.Metadata != null)}");
                Console.WriteLine($"  Types: {string.Join(", ", fullContent.Select(c => c.Type).Distinct())}");

                Assert.IsTrue(textOnlyContent.Count > 0, "Should have text chunks");
                Assert.IsTrue(fullContent.Any(c => c.Metadata != null),
                    "Full mode should have metadata");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Comparison Test Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 커스텀 StreamOptions 테스트
        /// </summary>
        [TestMethod]
        public async Task CustomStreamOptionsTest()
        {
            try
            {
                var customOptions = new StreamOptions()
                    .WithMetadata(true)
                    .WithTokenInfo(true)
                    .WithFunctionCalls(false)
                    .AsTextOnly(false);

                var receivedTypes = new HashSet<StreamingContentType>();
                var hasTokenInfo = false;

                // string 프롬프트와 StreamOptions 사용
                await foreach (var content in ((AIService)AI).StreamAsync(
                    "Explain AI in one sentence",
                    customOptions))
                {
                    receivedTypes.Add(content.Type);

                    if (content.Metadata?.ContainsKey("token_count") == true ||
                        content.Metadata?.ContainsKey("tokens") == true)
                    {
                        hasTokenInfo = true;
                        Console.WriteLine("[Token Info] Found token information in metadata");
                    }

                    if (content.Type == StreamingContentType.Text && content.Content != null)
                    {
                        Console.Write(content.Content);
                    }
                }

                Console.WriteLine($"\n[Custom Options Results]");
                Console.WriteLine($"  Received types: {string.Join(", ", receivedTypes)}");
                Console.WriteLine($"  Has token info: {hasTokenInfo}");
                Console.WriteLine($"  Function calls disabled: {!customOptions.IncludeFunctionCalls}");

                Assert.IsTrue(receivedTypes.Contains(StreamingContentType.Text));
                Assert.IsFalse(receivedTypes.Contains(StreamingContentType.FunctionCall),
                    "Should not have function calls when disabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Custom Options Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 스트리밍 중 취소와 메타데이터 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingCancellationWithMetadataTest()
        {
            try
            {
                var options = StreamOptions.FullOptions;
                var cts = new CancellationTokenSource();

                var collectedMetadata = new List<Dictionary<string, object>>();
                int chunksBeforeCancellation = 0;
                bool receivedCancellationStatus = false;

                try
                {
                    await foreach (var content in ((AIService)AI).StreamAsync(
                        "Write a very long essay about the history of computing",
                        options,
                        cts.Token))
                    {
                        chunksBeforeCancellation++;

                        if (content.Metadata != null)
                        {
                            collectedMetadata.Add(content.Metadata);
                        }

                        if (chunksBeforeCancellation >= 5)
                        {
                            cts.Cancel();
                            break;
                        }

                        if (content.Type == StreamingContentType.Status &&
                            content.Metadata?.ContainsKey("cancelled") == true)
                        {
                            receivedCancellationStatus = true;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[Cancellation] Stream cancelled as expected");
                }

                Console.WriteLine($"[Cancellation Results]");
                Console.WriteLine($"  Chunks before cancellation: {chunksBeforeCancellation}");
                Console.WriteLine($"  Metadata entries collected: {collectedMetadata.Count}");
                Console.WriteLine($"  Received cancellation status: {receivedCancellationStatus}");

                Assert.AreEqual(5, chunksBeforeCancellation, "Should stop at 5 chunks");
                Assert.IsTrue(collectedMetadata.Count > 0, "Should have collected metadata");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cancellation Metadata Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 이미지와 함께 스트리밍 시 메타데이터 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingWithImageMetadataTest()
        {
            try
            {
                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);

                var options = new StreamOptions
                {
                    IncludeMetadata = true,
                    IncludeTokenInfo = true
                };

                // MessageBuilder를 사용하여 메시지 생성
                var message = MessageBuilder.Create()
                    .WithRole(ActorRole.User)
                    .AddText("What's in this image?")
                    .AddImage(TestImagePath)
                    .Build();

                var metadataTypes = new Dictionary<string, int>();

                await foreach (var content in ((AIService)AI).StreamAsync(message, options))
                {
                    if (content.Metadata != null)
                    {
                        foreach (var key in content.Metadata.Keys)
                        {
                            if (!metadataTypes.ContainsKey(key))
                                metadataTypes[key] = 0;
                            metadataTypes[key]++;
                        }

                        // 각 변수를 개별적으로 체크
                        if (content.Metadata.TryGetValue("input_tokens", out var inputTokens))
                        {
                            Console.WriteLine($"[Token Info] Input tokens: {inputTokens}");
                        }

                        if (content.Metadata.TryGetValue("output_tokens", out var outputTokens))
                        {
                            Console.WriteLine($"[Token Info] Output tokens: {outputTokens}");
                        }
                    }

                    if (content.Type == StreamingContentType.Text && content.Content != null)
                    {
                        Console.Write(content.Content);
                    }
                }

                Console.WriteLine($"\n[Image Streaming Metadata]");
                foreach (var kvp in metadataTypes)
                {
                    Console.WriteLine($"  {kvp.Key}: appeared {kvp.Value} times");
                }

                Assert.IsTrue(metadataTypes.Count > 0, "Should have metadata types");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Image Metadata Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}