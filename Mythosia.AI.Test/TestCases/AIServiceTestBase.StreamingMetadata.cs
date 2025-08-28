using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 기본 메타데이터 포함 스트리밍 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingWithMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService)
            {
                Assert.Inconclusive("Metadata streaming requires AIService base class");
                return;
            }

            var options = new StreamOptions
            {
                IncludeMetadata = true,
                IncludeTokenInfo = true,
                TextOnly = false
            };

            var metadataReceived = new List<Dictionary<string, object>>();
            var contentTypes = new List<StreamingContentType>();
            var textChunks = new List<string>();

            var message = new Message(ActorRole.User, "Tell me a short story about AI");

            await foreach (var content in aiService.StreamAsync(message, options))
            {
                contentTypes.Add(content.Type);

                if (content.Metadata != null)
                {
                    metadataReceived.Add(content.Metadata);
                    Console.WriteLine($"[Metadata] Type: {content.Type}, Keys: {string.Join(", ", content.Metadata.Keys)}");
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

            Assert.IsTrue(metadataReceived.Count > 0 || textChunks.Count > 0);
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
    [TestCategory("Common")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingModelMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService)
            {
                Assert.Inconclusive("Metadata streaming requires AIService base class");
                return;
            }

            var options = StreamOptions.FullOptions;

            string capturedModel = null;
            string responseId = null;
            var timestamps = new List<DateTime>();

            string prompt = "What is 2+2?";

            await foreach (var content in aiService.StreamAsync(prompt, options))
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

            if (capturedModel != null)
            {
                Console.WriteLine($"[Validation] Model captured: {capturedModel}");
            }

            if (responseId != null)
            {
                Console.WriteLine($"[Validation] Response ID format: {responseId}");
                Assert.IsTrue(responseId.Length > 0);
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
    [TestCategory("Common")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingCompletionMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService)
            {
                Assert.Inconclusive("Metadata streaming requires AIService base class");
                return;
            }

            var options = new StreamOptions
            {
                IncludeMetadata = true,
                TextOnly = false
            };

            StreamingContent completionContent = null;
            string finishReason = null;
            int totalLength = 0;
            var allContent = new List<StreamingContent>();

            await foreach (var content in aiService.StreamAsync("Say hello", options))
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

            Assert.IsTrue(allContent.Any(c => c.Type == StreamingContentType.Text));

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
    [TestCategory("Common")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingErrorMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService)
            {
                Assert.Inconclusive("Metadata streaming requires AIService base class");
                return;
            }

            var options = StreamOptions.FullOptions;
            var originalModel = AI.ActivateChat.Model;
            StreamingContent errorContent = null;

            try
            {
                AI.ActivateChat.ChangeModel("invalid-model-xyz");

                await foreach (var content in aiService.StreamAsync("Test", options))
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
                Assert.IsNotNull(errorContent.Metadata);
                Assert.IsTrue(errorContent.Metadata.ContainsKey("error"));
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
    [TestCategory("Common")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingTextOnlyVsMetadataTest()
    {
        try
        {
            var prompt = "Count from 1 to 3";

            // 1. 텍스트 전용 모드
            Console.WriteLine("[Text Only Mode]");
            var textOnlyContent = new List<string>();

            await foreach (var chunk in AI.StreamOnceAsync(prompt))
            {
                textOnlyContent.Add(chunk);
                Console.Write(chunk);
            }

            Console.WriteLine($"\n  Text chunks: {textOnlyContent.Count}");

            // 2. 전체 메타데이터 모드
            if (AI is Mythosia.AI.Services.Base.AIService aiService)
            {
                Console.WriteLine("\n[Full Metadata Mode]");
                var fullOptions = StreamOptions.FullOptions;
                var fullContent = new List<StreamingContent>();

                await foreach (var content in aiService.StreamAsync(prompt, fullOptions))
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

                Assert.IsTrue(fullContent.Any(c => c.Metadata != null));
            }

            Assert.IsTrue(textOnlyContent.Count > 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Comparison Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 스트리밍 중 취소와 메타데이터 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingCancellationWithMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService)
            {
                Assert.Inconclusive("Metadata streaming requires AIService base class");
                return;
            }

            var options = StreamOptions.FullOptions;
            var cts = new CancellationTokenSource();

            var collectedMetadata = new List<Dictionary<string, object>>();
            int chunksBeforeCancellation = 0;

            try
            {
                var message = new Message(ActorRole.User, "Write a very long essay about computing");

                await foreach (var content in aiService.StreamAsync(message, options, cts.Token))
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
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Cancellation] Stream cancelled as expected");
            }

            Console.WriteLine($"[Cancellation Results]");
            Console.WriteLine($"  Chunks before cancellation: {chunksBeforeCancellation}");
            Console.WriteLine($"  Metadata entries collected: {collectedMetadata.Count}");

            Assert.AreEqual(5, chunksBeforeCancellation);
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
    [TestCategory("Common")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingWithImageMetadataTest()
    {
        await RunIfSupported(
            () => SupportsMultimodal(),
            async () =>
            {
                if (AI is not Mythosia.AI.Services.Base.AIService aiService)
                {
                    Assert.Inconclusive("Metadata streaming requires AIService base class");
                    return;
                }

                // Vision 지원 모델로 변경
                if (AI is Mythosia.AI.Services.OpenAI.ChatGptService && AI.ActivateChat.Model.Contains("mini"))
                {
                    AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                }

                var options = new StreamOptions
                {
                    IncludeMetadata = true,
                    IncludeTokenInfo = true
                };

                var message = Mythosia.AI.Builders.MessageBuilder.Create()
                    .WithRole(ActorRole.User)
                    .AddText("What's in this image?")
                    .AddImage(TestImagePath)
                    .Build();

                var metadataTypes = new Dictionary<string, int>();

                await foreach (var content in aiService.StreamAsync(message, options))
                {
                    if (content.Metadata != null)
                    {
                        foreach (var key in content.Metadata.Keys)
                        {
                            if (!metadataTypes.ContainsKey(key))
                                metadataTypes[key] = 0;
                            metadataTypes[key]++;
                        }

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

                Assert.IsTrue(metadataTypes.Count > 0);
            },
            "Streaming with Image Metadata"
        );
    }
}