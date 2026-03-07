using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// 스트리밍 메타데이터 테스트.
/// 원본: AIServiceTestBase.StreamingMetadata.cs
/// </summary>
[TestClass]
public abstract class StreamingMetadataTestModule : TestModuleBase
{
    [TestCategory("StreamingMetadata"), TestMethod]
    public async Task StreamingWithMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService) { Assert.Inconclusive("Metadata streaming requires AIService base class"); return; }
            var options = new StreamOptions { IncludeMetadata = true, IncludeTokenInfo = true, TextOnly = false };
            var metadataReceived = new List<Dictionary<string, object>>();
            var contentTypes = new List<StreamingContentType>();
            var textChunks = new List<string>();
            var message = new Message(ActorRole.User, "Tell me a short story about AI");
            await foreach (var content in aiService.StreamAsync(message, options))
            {
                contentTypes.Add(content.Type);
                if (content.Metadata != null) { metadataReceived.Add(content.Metadata); Console.WriteLine($"[Metadata] Type: {content.Type}, Keys: {string.Join(", ", content.Metadata.Keys)}"); }
                if (content.Type == StreamingContentType.Text && content.Content != null) { textChunks.Add(content.Content); Console.Write(content.Content); }
            }
            Console.WriteLine($"\n[Metadata Summary] Total metadata entries: {metadataReceived.Count}, Content types: {string.Join(", ", contentTypes.Distinct())}, Text chunks: {textChunks.Count}");
            Assert.IsTrue(metadataReceived.Count > 0 || textChunks.Count > 0);
        }
        catch (Exception ex) { Console.WriteLine($"[Metadata Test Error] {ex.Message}"); Assert.Fail(ex.Message); }
    }

    [TestCategory("StreamingMetadata"), TestMethod]
    public async Task StreamingModelMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService) { Assert.Inconclusive("Metadata streaming requires AIService base class"); return; }
            var options = StreamOptions.FullOptions;
            string? capturedModel = null;
            string? responseId = null;
            var timestamps = new List<DateTime>();
            await foreach (var content in aiService.StreamAsync("What is 2+2?", options))
            {
                if (content.Metadata != null)
                {
                    if (content.Metadata.TryGetValue("model", out var model)) { capturedModel = model?.ToString(); Console.WriteLine($"[Model] {capturedModel}"); }
                    if (content.Metadata.TryGetValue("response_id", out var id)) { responseId = id?.ToString(); Console.WriteLine($"[Response ID] {responseId}"); }
                    if (content.Metadata.TryGetValue("timestamp", out var timestamp) && timestamp is DateTime dt) timestamps.Add(dt);
                }
            }
            if (capturedModel != null) Console.WriteLine($"[Validation] Model captured: {capturedModel}");
            if (responseId != null) { Console.WriteLine($"[Validation] Response ID format: {responseId}"); Assert.IsTrue(responseId.Length > 0); }
            if (timestamps.Count > 0) { Console.WriteLine($"[Timestamps] Received {timestamps.Count}, First: {timestamps.First():HH:mm:ss.fff}, Last: {timestamps.Last():HH:mm:ss.fff}, Duration: {(timestamps.Last() - timestamps.First()).TotalMilliseconds}ms"); }
        }
        catch (Exception ex) { Console.WriteLine($"[Model Metadata Error] {ex.Message}"); Assert.Fail(ex.Message); }
    }

    [TestCategory("StreamingMetadata"), TestMethod]
    public async Task StreamingCompletionMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService) { Assert.Inconclusive("Metadata streaming requires AIService base class"); return; }
            var options = new StreamOptions { IncludeMetadata = true, TextOnly = false };
            StreamingContent? completionContent = null;
            string? finishReason = null;
            var allContent = new List<StreamingContent>();
            await foreach (var content in aiService.StreamAsync("Say hello", options))
            {
                allContent.Add(content);
                if (content.Type == StreamingContentType.Completion) { completionContent = content; Console.WriteLine("[Completion] Stream completed"); }
                if (content.Type == StreamingContentType.Status) Console.WriteLine($"[Status] Received status update");
                if (content.Metadata?.TryGetValue("finish_reason", out var reason) == true) { finishReason = reason?.ToString(); Console.WriteLine($"[Finish Reason] {finishReason}"); }
                if (content.Metadata?.TryGetValue("total_length", out var length) == true && int.TryParse(length?.ToString(), out var len)) Console.WriteLine($"[Total Length] {len} characters");
            }
            Assert.IsTrue(allContent.Any(c => c.Type == StreamingContentType.Text));
            Console.WriteLine($"\n[Summary] Total content items: {allContent.Count}, Content types: {string.Join(", ", allContent.Select(c => c.Type).Distinct())}");
        }
        catch (Exception ex) { Console.WriteLine($"[Completion Metadata Error] {ex.Message}"); Assert.Fail(ex.Message); }
    }

    [TestCategory("StreamingMetadata"), TestMethod]
    public async Task StreamingErrorMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService) { Assert.Inconclusive("Metadata streaming requires AIService base class"); return; }
            var options = StreamOptions.FullOptions;
            var originalModel = AI.Model;
            StreamingContent? errorContent = null;
            try
            {
                AI.ChangeModel("invalid-model-xyz");
                await foreach (var content in aiService.StreamAsync("Test", options))
                {
                    if (content.Type == StreamingContentType.Error)
                    {
                        errorContent = content;
                        Console.WriteLine("[Error] Received error content");
                        if (content.Metadata != null) foreach (var kvp in content.Metadata) Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                        break;
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Expected Error] {ex.Message}"); }
            finally { AI.ChangeModel(originalModel); }
            if (errorContent != null) { Assert.IsNotNull(errorContent.Metadata); Assert.IsTrue(errorContent.Metadata.ContainsKey("error")); }
        }
        catch (Exception ex) { Console.WriteLine($"[Error Metadata Test] {ex.Message}"); }
    }

    [TestCategory("StreamingMetadata"), TestMethod]
    public async Task StreamingTextOnlyVsMetadataTest()
    {
        try
        {
            var prompt = "Count from 1 to 3";
            Console.WriteLine("[Text Only Mode]");
            var textOnlyContent = new List<string>();
            await foreach (var chunk in AI.StreamOnceAsync(prompt)) { textOnlyContent.Add(chunk); Console.Write(chunk); }
            Console.WriteLine($"\n  Text chunks: {textOnlyContent.Count}");

            if (AI is Mythosia.AI.Services.Base.AIService aiService)
            {
                Console.WriteLine("\n[Full Metadata Mode]");
                var fullOptions = StreamOptions.FullOptions;
                var fullContent = new List<StreamingContent>();
                await foreach (var content in aiService.StreamAsync(prompt, fullOptions)) { fullContent.Add(content); if (content.Content != null) Console.Write(content.Content); }
                Console.WriteLine($"\n  Items: {fullContent.Count}, Has metadata: {fullContent.Any(c => c.Metadata != null)}, Types: {string.Join(", ", fullContent.Select(c => c.Type).Distinct())}");
                Assert.IsTrue(fullContent.Any(c => c.Metadata != null));
            }
            Assert.IsTrue(textOnlyContent.Count > 0);
        }
        catch (Exception ex) { Console.WriteLine($"[Comparison Test Error] {ex.Message}"); Assert.Fail(ex.Message); }
    }

    [TestCategory("StreamingMetadata"), TestMethod]
    public async Task StreamingCancellationWithMetadataTest()
    {
        try
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService) { Assert.Inconclusive("Metadata streaming requires AIService base class"); return; }
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
                    if (content.Metadata != null) collectedMetadata.Add(content.Metadata);
                    if (chunksBeforeCancellation >= 5) { cts.Cancel(); break; }
                }
            }
            catch (OperationCanceledException) { Console.WriteLine("[Cancellation] Stream cancelled as expected"); }
            Console.WriteLine($"[Cancellation Results] Chunks before cancellation: {chunksBeforeCancellation}, Metadata entries collected: {collectedMetadata.Count}");
            Assert.AreEqual(5, chunksBeforeCancellation);
        }
        catch (Exception ex) { Console.WriteLine($"[Cancellation Metadata Error] {ex.Message}"); Assert.Fail(ex.Message); }
    }

    [TestCategory("StreamingMetadata"), TestMethod]
    public async Task StreamingWithImageMetadataTest()
    {
        await RunIfSupported(() => SupportsMultimodal(), async () =>
        {
            if (AI is not Mythosia.AI.Services.Base.AIService aiService) { Assert.Inconclusive("Metadata streaming requires AIService base class"); return; }
            if (AI is Mythosia.AI.Services.OpenAI.ChatGptService && AI.Model.Contains("mini")) AI.ChangeModel(Mythosia.AI.Models.Enums.AIModel.Gpt4oLatest);
            var options = new StreamOptions { IncludeMetadata = true, IncludeTokenInfo = true };
            var message = Mythosia.AI.Builders.MessageBuilder.Create().WithRole(ActorRole.User).AddText("What's in this image?").AddImage(TestImagePath).Build();
            var metadataTypes = new Dictionary<string, int>();
            await foreach (var content in aiService.StreamAsync(message, options))
            {
                if (content.Metadata != null)
                {
                    foreach (var key in content.Metadata.Keys) { if (!metadataTypes.ContainsKey(key)) metadataTypes[key] = 0; metadataTypes[key]++; }
                    if (content.Metadata.TryGetValue("input_tokens", out var inputTokens)) Console.WriteLine($"[Token Info] Input tokens: {inputTokens}");
                    if (content.Metadata.TryGetValue("output_tokens", out var outputTokens)) Console.WriteLine($"[Token Info] Output tokens: {outputTokens}");
                }
                if (content.Type == StreamingContentType.Text && content.Content != null) Console.Write(content.Content);
            }
            Console.WriteLine($"\n[Image Streaming Metadata]");
            foreach (var kvp in metadataTypes) Console.WriteLine($"  {kvp.Key}: appeared {kvp.Value} times");
            Assert.IsTrue(metadataTypes.Count > 0);
        }, "Streaming with Image Metadata");
    }
}
