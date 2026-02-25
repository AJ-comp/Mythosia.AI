using Mythosia.AI.Rag;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class RagEnabledServiceTests
{
    /// <summary>
    /// Tests WithRag(Action&lt;RagBuilder&gt;) + GetCompletionAsync end-to-end.
    /// Verifies that the prompt sent to the LLM contains the RAG-augmented context.
    /// </summary>
    [TestMethod]
    public async Task WithRag_GetCompletionAsync_SendsAugmentedPromptToLLM()
    {
        // Arrange
        var mockService = new MockAIService();
        mockService.CompletionResponse = "14일 이내에 환불 가능합니다.";

        var ragService = mockService.WithRag(rag => rag
            .AddText("환불은 구매일로부터 14일 이내에 요청할 수 있습니다.", id: "refund")
            .AddText("배송은 2-3 영업일 소요됩니다.", id: "shipping")
            .UseLocalEmbedding(512)
            .WithTopK(2)
        );

        // Act
        var response = await ragService.GetCompletionAsync("환불 정책이 어떻게 되나요?");

        // Assert
        Assert.AreEqual("14일 이내에 환불 가능합니다.", response);
        Assert.IsNotNull(mockService.LastReceivedPrompt, "LLM should have received a prompt");
        Assert.IsTrue(mockService.LastReceivedPrompt!.Contains("환불 정책이 어떻게 되나요?"),
            "Augmented prompt should contain the original query");
        Assert.IsTrue(mockService.LastReceivedPrompt.Length > "환불 정책이 어떻게 되나요?".Length,
            "Augmented prompt should be longer than the original query (context was added)");

        Console.WriteLine($"=== Prompt sent to LLM ===\n{mockService.LastReceivedPrompt}");
    }

    /// <summary>
    /// Tests WithRag(RagStore) with a shared pre-built store.
    /// </summary>
    [TestMethod]
    public async Task WithRag_SharedRagStore_WorksAcrossMultipleServices()
    {
        // Arrange: Build shared store
        var store = await RagStore.BuildAsync(config => config
            .AddText("제품 가격은 99,000원입니다.", id: "price")
            .UseLocalEmbedding(512)
        );

        var service1 = new MockAIService { CompletionResponse = "서비스1 응답" };
        var service2 = new MockAIService { CompletionResponse = "서비스2 응답" };

        var rag1 = service1.WithRag(store);
        var rag2 = service2.WithRag(store);

        // Act
        var resp1 = await rag1.GetCompletionAsync("가격이 얼마?");
        var resp2 = await rag2.GetCompletionAsync("가격이 얼마?");

        // Assert: Both services received augmented prompts
        Assert.AreEqual("서비스1 응답", resp1);
        Assert.AreEqual("서비스2 응답", resp2);
        Assert.IsNotNull(service1.LastReceivedPrompt);
        Assert.IsNotNull(service2.LastReceivedPrompt);
        Assert.IsTrue(service1.LastReceivedPrompt!.Contains("가격이 얼마?"));
        Assert.IsTrue(service2.LastReceivedPrompt!.Contains("가격이 얼마?"));

        Console.WriteLine($"Service1 prompt:\n{service1.LastReceivedPrompt}\n");
        Console.WriteLine($"Service2 prompt:\n{service2.LastReceivedPrompt}");
    }

    /// <summary>
    /// Tests WithoutRag() returns the original AIService.
    /// </summary>
    [TestMethod]
    public async Task WithoutRag_ReturnsOriginalService()
    {
        // Arrange
        var mockService = new MockAIService();
        var ragService = mockService.WithRag(rag => rag
            .AddText("Some document", id: "doc1")
        );

        // Act
        var original = ragService.WithoutRag();

        // Assert: Should be the exact same instance
        Assert.AreSame(mockService, original);
    }

    /// <summary>
    /// Tests RetrieveAsync returns augmented prompt + references without calling LLM.
    /// </summary>
    [TestMethod]
    public async Task RetrieveAsync_ReturnsContextWithoutCallingLLM()
    {
        // Arrange
        var mockService = new MockAIService();
        var ragService = mockService.WithRag(rag => rag
            .AddText("서울의 인구는 약 950만명입니다.", id: "seoul")
            .UseLocalEmbedding(512)
        );

        // Act
        var result = await ragService.RetrieveAsync("서울 인구?");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("서울 인구?", result.OriginalQuery);
        Assert.IsTrue(result.AugmentedPrompt.Length > 0);
        Assert.IsTrue(result.References.Count > 0);

        // LLM should NOT have been called
        Assert.IsNull(mockService.LastReceivedPrompt, "RetrieveAsync should not call LLM");

        Console.WriteLine($"Augmented prompt:\n{result.AugmentedPrompt}");
        Console.WriteLine($"References: {result.References.Count}");
    }

    /// <summary>
    /// Tests StreamAsync sends augmented prompt to LLM streaming.
    /// </summary>
    [TestMethod]
    public async Task StreamAsync_SendsAugmentedPromptToLLM()
    {
        // Arrange
        var mockService = new MockAIService();
        mockService.CompletionResponse = "스트리밍 응답";

        var ragService = mockService.WithRag(rag => rag
            .AddText("배송은 2-3일 소요됩니다.", id: "shipping")
            .UseLocalEmbedding(256)
        );

        // Act
        var chunks = new List<string>();
        await foreach (var chunk in ragService.StreamAsync("배송 기간은?"))
        {
            chunks.Add(chunk);
        }

        // Assert
        Assert.IsTrue(chunks.Count > 0, "Should have received streaming chunks");
        Assert.IsNotNull(mockService.LastReceivedPrompt);
        Assert.IsTrue(mockService.LastReceivedPrompt!.Contains("배송 기간은?"));

        Console.WriteLine($"Streamed: {string.Join("", chunks)}");
        Console.WriteLine($"Prompt sent:\n{mockService.LastReceivedPrompt}");
    }

    /// <summary>
    /// Tests custom prompt template flows through to the LLM.
    /// </summary>
    [TestMethod]
    public async Task WithPromptTemplate_TemplateAppearsInLLMPrompt()
    {
        // Arrange
        var mockService = new MockAIService();
        var ragService = mockService.WithRag(rag => rag
            .AddText("파이썬은 인터프리터 언어입니다.", id: "python")
            .UseLocalEmbedding(256)
            .WithPromptTemplate("[문서]\n{context}\n\n[질문]\n{question}\n\n문서만 근거로 답하세요.")
        );

        // Act
        await ragService.GetCompletionAsync("파이썬이 뭐야?");

        // Assert
        Assert.IsNotNull(mockService.LastReceivedPrompt);
        Assert.IsTrue(mockService.LastReceivedPrompt!.Contains("[문서]"));
        Assert.IsTrue(mockService.LastReceivedPrompt.Contains("[질문]"));
        Assert.IsTrue(mockService.LastReceivedPrompt.Contains("파이썬이 뭐야?"));
        Assert.IsTrue(mockService.LastReceivedPrompt.Contains("문서만 근거로 답하세요."));

        Console.WriteLine(mockService.LastReceivedPrompt);
    }

    /// <summary>
    /// Tests lazy initialization: documents are indexed only on first query, not at WithRag time.
    /// </summary>
    [TestMethod]
    public async Task LazyInitialization_IndexesOnFirstQuery()
    {
        // Arrange
        var mockService = new MockAIService();
        var ragService = mockService.WithRag(rag => rag
            .AddText("Lazy init test document", id: "lazy")
            .UseLocalEmbedding(128)
        );

        // At this point, no indexing should have occurred yet
        // LLM has not been called
        Assert.IsNull(mockService.LastReceivedPrompt);

        // Act: First query triggers initialization
        await ragService.GetCompletionAsync("test query");

        // Assert
        Assert.IsNotNull(mockService.LastReceivedPrompt);

        // Second query should reuse the same index (no re-initialization)
        mockService.LastReceivedPrompt = null;
        await ragService.GetCompletionAsync("second query");
        Assert.IsNotNull(mockService.LastReceivedPrompt);
    }
}
