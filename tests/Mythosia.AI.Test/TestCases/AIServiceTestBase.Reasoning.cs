using Mythosia.AI.Models.Streaming;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// Reasoning summary 추출 테스트 (비스트리밍 + 스트리밍)
    /// reasoning.summary = "auto" 설정으로 reasoning 데이터가 반환되는지 확인
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Reasoning")]
    [TestMethod]
    public async Task ReasoningSummaryTest()
    {
        await RunIfSupported(
            () => SupportsReasoning(),
            async () =>
            {
                // 1. 스트리밍: StreamingContentType.Reasoning 이벤트 확인
                var options = new StreamOptions().WithReasoning().WithMetadata();

                var reasoningChunks = new List<string>();
                var textChunks = new List<string>();

                await foreach (var content in AI.StreamAsync("What is 15 * 17? Show your reasoning.", options))
                {
                    if (content.Type == StreamingContentType.Reasoning && content.Content != null)
                    {
                        reasoningChunks.Add(content.Content);
                        Console.Write($"[R]{content.Content}");
                    }
                    else if (content.Type == StreamingContentType.Text && content.Content != null)
                    {
                        textChunks.Add(content.Content);
                        Console.Write(content.Content);
                    }
                }

                Console.WriteLine($"\n[Streaming] Reasoning chunks: {reasoningChunks.Count}, Text chunks: {textChunks.Count}");
                Assert.IsTrue(textChunks.Count > 0, "Should have received text chunks from streaming");

                if (reasoningChunks.Count > 0)
                {
                    var fullReasoning = string.Join("", reasoningChunks);
                    Console.WriteLine($"[OK] Streaming reasoning captured ({fullReasoning.Length} chars)");
                }
                else
                {
                    Console.WriteLine("[INFO] No streaming reasoning chunks received (may vary by model).");
                }

                // 2. 비스트리밍: 일반 응답이 정상 동작하는지 확인
                var response = await AI.GetCompletionAsync("What is 15 * 17? Show just the answer.");
                Assert.IsNotNull(response, "Response should not be null");
                Assert.IsTrue(response.Length > 0, "Response should not be empty");
                Console.WriteLine($"[Non-Streaming Response] {response}");
            },
            "Reasoning"
        );
    }

    /// <summary>
    /// 추론 모드 스트리밍 테스트 — reasoning + text 청크가 모두 수신되는지 검증
    /// 에러 발생 시 즉시 실패 처리하여 empty response 문제를 조기 감지
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Reasoning")]
    [TestMethod]
    public async Task ReasoningStreamingTest()
    {
        await RunIfSupported(
            () => SupportsReasoning(),
            async () =>
            {
                var options = new StreamOptions().WithReasoning().WithMetadata(false);

                var reasoningChunks = new List<string>();
                var textChunks = new List<string>();
                var receivedTypes = new HashSet<StreamingContentType>();

                await foreach (var content in AI.StreamAsync(
                    "What is 15 * 17? Explain your reasoning.", options))
                {
                    receivedTypes.Add(content.Type);

                    if (content.Type == StreamingContentType.Reasoning && content.Content != null)
                    {
                        reasoningChunks.Add(content.Content);
                        Console.Write($"[R]{content.Content}");
                    }
                    else if (content.Type == StreamingContentType.Text && content.Content != null)
                    {
                        textChunks.Add(content.Content);
                        Console.Write(content.Content);
                    }
                    else if (content.Type == StreamingContentType.Error)
                    {
                        Console.WriteLine($"\n[ERROR] {content.Content}");
                        Assert.Fail($"Received error during reasoning streaming: {content.Content}");
                    }
                }

                var fullReasoning = string.Join("", reasoningChunks);
                var fullText = string.Join("", textChunks);

                Console.WriteLine($"\n[Reasoning Stream] Reasoning chunks: {reasoningChunks.Count} ({fullReasoning.Length} chars)");
                Console.WriteLine($"[Reasoning Stream] Text chunks: {textChunks.Count} ({fullText.Length} chars)");
                Console.WriteLine($"[Reasoning Stream] Received types: {string.Join(", ", receivedTypes)}");

                Assert.IsTrue(textChunks.Count > 0, "Should have received text chunks");
                Assert.IsTrue(fullText.Length > 0, "Text response should not be empty");
                Assert.IsTrue(reasoningChunks.Count > 0,
                    "Should have received reasoning chunks when reasoning mode is enabled");
            },
            "Reasoning Streaming"
        );
    }
}
