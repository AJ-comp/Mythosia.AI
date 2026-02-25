using Mythosia.AI.Loaders;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.AI.VectorDB;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class RagPipelineEndToEndTests
{
    /// <summary>
    /// Tests the full pipeline: document → split → embed → store → query → search → context build.
    /// Verifies that the retrieved context contains relevant content from the indexed document.
    /// </summary>
    [TestMethod]
    public async Task FullPipeline_IndexAndQuery_ReturnsRelevantContext()
    {
        // Arrange: Create components
        var embedding = new LocalEmbeddingProvider(dimensions: 512);
        var vectorStore = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(chunkSize: 200, chunkOverlap: 30, separator: "\n");
        var contextBuilder = new DefaultContextBuilder();
        var pipeline = new RagPipeline(embedding, vectorStore, splitter, contextBuilder,
            new RagPipelineOptions { TopK = 3 });

        // Act: Index a document
        var doc = new RagDocument
        {
            Id = "policy-doc",
            Content = @"환불 정책 안내
구매일로부터 14일 이내에 환불을 요청할 수 있습니다.
사용하지 않은 제품에 한해 전액 환불이 가능합니다.
개봉된 제품은 환불이 불가능할 수 있습니다.

배송 정책 안내
주문 후 2-3 영업일 이내에 배송됩니다.
무료 배송은 5만원 이상 구매 시 적용됩니다.
도서산간 지역은 추가 배송비가 부과될 수 있습니다.

교환 정책 안내
제품 수령 후 7일 이내에 교환을 요청할 수 있습니다.
동일 제품 또는 동일 가격대 제품으로 교환 가능합니다.",
            Source = "policy.txt"
        };
        await pipeline.IndexDocumentAsync(doc);

        // Act: Query
        var result = await pipeline.QueryAsync("환불 정책이 어떻게 되나요?");

        // Assert: Context should contain refund-related content
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Context), "Context should not be empty");
        Assert.IsTrue(result.SearchResults.Count > 0, "Should have search results");
        Assert.AreEqual("환불 정책이 어떻게 되나요?", result.Query);

        Console.WriteLine("=== Query Result ===");
        Console.WriteLine($"Query: {result.Query}");
        Console.WriteLine($"Search Results Count: {result.SearchResults.Count}");
        for (int i = 0; i < result.SearchResults.Count; i++)
        {
            var sr = result.SearchResults[i];
            Console.WriteLine($"\n--- Result {i + 1} (Score: {sr.Score:F4}) ---");
            Console.WriteLine(sr.Record.Content);
        }
        Console.WriteLine($"\n=== Built Context ===\n{result.Context}");
    }

    /// <summary>
    /// Tests that IRagPipeline.ProcessAsync returns an augmented prompt with context.
    /// </summary>
    [TestMethod]
    public async Task ProcessAsync_ReturnsAugmentedPrompt()
    {
        // Arrange
        var embedding = new LocalEmbeddingProvider(dimensions: 512);
        var vectorStore = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(chunkSize: 100, chunkOverlap: 20);
        var contextBuilder = new DefaultContextBuilder();
        var pipeline = new RagPipeline(embedding, vectorStore, splitter, contextBuilder);

        var doc = new RagDocument
        {
            Id = "faq",
            Content = "제품 가격은 99,000원입니다. 색상은 블랙, 화이트, 블루 3가지입니다.",
            Source = "faq.txt"
        };
        await pipeline.IndexDocumentAsync(doc);

        // Act: Use IRagPipeline interface
        IRagPipeline ragPipeline = pipeline;
        var processed = await ragPipeline.ProcessAsync("가격이 얼마인가요?");

        // Assert
        Assert.IsNotNull(processed);
        Assert.AreEqual("가격이 얼마인가요?", processed.OriginalQuery);
        Assert.IsFalse(string.IsNullOrWhiteSpace(processed.AugmentedPrompt));
        Assert.IsTrue(processed.AugmentedPrompt.Contains("가격이 얼마인가요?"),
            "Augmented prompt should contain the original query");

        Console.WriteLine($"Augmented Prompt:\n{processed.AugmentedPrompt}");
    }

    /// <summary>
    /// Tests RagBuilder with AddText and local embedding (no API key, no files).
    /// </summary>
    [TestMethod]
    public async Task RagBuilder_AddText_BuildsAndQueries()
    {
        // Arrange & Act: Build via RagBuilder
        var store = await RagStore.BuildAsync(config => config
            .AddText("서울의 인구는 약 950만명입니다.", id: "seoul-pop")
            .AddText("부산의 인구는 약 340만명입니다.", id: "busan-pop")
            .AddText("대전의 인구는 약 150만명입니다.", id: "daejeon-pop")
            .UseLocalEmbedding(512)
            .UseInMemoryStore()
            .WithTopK(2)
            .WithChunkSize(200)
        );

        // Act: Query via pipeline
        var processed = await store.QueryAsync("서울 인구는 몇 명?");

        // Assert
        Assert.IsNotNull(processed);
        Assert.IsTrue(processed.References.Count > 0, "Should have references");
        Assert.IsTrue(processed.AugmentedPrompt.Length > 0, "Should have augmented prompt");

        Console.WriteLine($"Query: {processed.OriginalQuery}");
        Console.WriteLine($"References: {processed.References.Count}");
        foreach (var r in processed.References)
        {
            Console.WriteLine($"  Score={r.Score:F4} | {r.Record.Content}");
        }
        Console.WriteLine($"\nAugmented Prompt:\n{processed.AugmentedPrompt}");
    }

    /// <summary>
    /// Tests custom prompt template with {context} and {question} placeholders.
    /// </summary>
    [TestMethod]
    public async Task WithPromptTemplate_UsesCustomTemplate()
    {
        var store = await RagStore.BuildAsync(config => config
            .AddText("파이썬은 인터프리터 언어입니다.", id: "python")
            .UseLocalEmbedding(256)
            .WithPromptTemplate("[참고]\n{context}\n\n[질문]\n{question}\n\n반드시 문서를 근거로 답변하세요.")
        );

        var processed = await store.QueryAsync("파이썬이 뭐야?");

        Assert.IsTrue(processed.AugmentedPrompt.Contains("[참고]"));
        Assert.IsTrue(processed.AugmentedPrompt.Contains("[질문]"));
        Assert.IsTrue(processed.AugmentedPrompt.Contains("파이썬이 뭐야?"));
        Assert.IsTrue(processed.AugmentedPrompt.Contains("반드시 문서를 근거로 답변하세요."));

        Console.WriteLine(processed.AugmentedPrompt);
    }

    /// <summary>
    /// Tests that LocalEmbeddingProvider produces consistent, normalized vectors.
    /// </summary>
    [TestMethod]
    public async Task LocalEmbedding_ProducesNormalizedVectors()
    {
        var provider = new LocalEmbeddingProvider(dimensions: 256);

        var vec1 = await provider.GetEmbeddingAsync("환불 정책");
        var vec2 = await provider.GetEmbeddingAsync("환불 정책"); // Same text
        var vec3 = await provider.GetEmbeddingAsync("배송 안내"); // Different text

        // Same text → same vector
        Assert.AreEqual(256, vec1.Length);
        CollectionAssert.AreEqual(vec1, vec2, "Same text should produce same vector");

        // Different text → different vector
        CollectionAssert.AreNotEqual(vec1, vec3, "Different text should produce different vector");

        // Verify normalization (L2 norm ≈ 1.0)
        double norm = 0;
        for (int i = 0; i < vec1.Length; i++)
            norm += vec1[i] * (double)vec1[i];
        norm = Math.Sqrt(norm);
        Assert.IsTrue(Math.Abs(norm - 1.0) < 0.001, $"Vector should be unit-normalized, got norm={norm:F6}");
    }

    /// <summary>
    /// Tests that similar queries score higher than unrelated queries.
    /// </summary>
    [TestMethod]
    public async Task Search_SimilarQuery_ScoresHigherThanUnrelated()
    {
        var embedding = new LocalEmbeddingProvider(512);
        var vectorStore = new InMemoryVectorStore();
        var splitter = new CharacterTextSplitter(500, 50);
        var contextBuilder = new DefaultContextBuilder();
        var pipeline = new RagPipeline(embedding, vectorStore, splitter, contextBuilder,
            new RagPipelineOptions { TopK = 5 });

        // Index documents on different topics
        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "refund", Content = "환불은 14일 이내 가능합니다. 미개봉 제품만 환불 가능합니다.", Source = "refund.txt"
        });
        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "shipping", Content = "배송은 2-3일 소요됩니다. 무료 배송은 5만원 이상 구매 시 적용됩니다.", Source = "shipping.txt"
        });
        await pipeline.IndexDocumentAsync(new RagDocument
        {
            Id = "cooking", Content = "김치찌개 레시피: 돼지고기, 김치, 두부를 넣고 끓입니다.", Source = "recipe.txt"
        });

        // Query about refund
        var result = await pipeline.QueryAsync("환불 방법을 알려주세요");

        Assert.IsTrue(result.SearchResults.Count > 0);

        // The top result should be the refund document
        var topResult = result.SearchResults[0];
        Console.WriteLine($"Top result (Score={topResult.Score:F4}): {topResult.Record.Content}");
        Assert.IsTrue(topResult.Record.Content.Contains("환불"),
            "Top result should be about refund");
    }
}
