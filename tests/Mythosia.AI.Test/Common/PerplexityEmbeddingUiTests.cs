using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Samples.ChatUi;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public sealed class PerplexityEmbeddingUiTests
{
    [TestMethod]
    [DataRow(PerplexityEmbeddingModels.Standard0_6B, 1024)]
    [DataRow(PerplexityEmbeddingModels.Standard4B, 2560)]
    public void SelectedModelAndDimension_ConstructCorrectProviderWithoutOpenAiKey(string model, int dimensions)
    {
        using var http = new HttpClient();
        var provider = ChatUiUtilityHelpers.BuildRagEmbeddingProvider("perplexity", null, "perplexity-test-key", http, model, dimensions, "");
        Assert.IsInstanceOfType<PerplexityEmbeddingProvider>(provider);
        Assert.AreEqual(model, ((PerplexityEmbeddingProvider)provider).Model);
        Assert.AreEqual(dimensions, provider.Dimensions);
    }

    [TestMethod]
    public void OpenAiKeyCannotSubstituteForMissingPerplexityKey()
    {
        using var http = new HttpClient();
        var error = Assert.Throws<InvalidOperationException>(() => ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
            "perplexity", "openai-test-key", null, http, PerplexityEmbeddingModels.Standard0_6B, 1024, ""));
        StringAssert.Contains(error.Message, "Perplexity API key is required");
    }

    [TestMethod]
    [DataRow(PerplexityEmbeddingModels.Context0_6B, 1024)]
    [DataRow(PerplexityEmbeddingModels.Standard0_6B, 1025)]
    [DataRow(PerplexityEmbeddingModels.Standard4B, 127)]
    public void InvalidEmbeddingConfiguration_FailsAtFactory(string model, int dimensions)
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
            "perplexity", null, "key", http, model, dimensions, ""));
    }

    [TestMethod]
    public void UnsupportedProviderDoesNotFallBackToOpenAi()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
            "typo", "openai-key", "perplexity-key", http, "model", 128, ""));
        Assert.IsInstanceOfType<OllamaEmbeddingProvider>(ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
            "ollama", null, null, http, "model", 128, "http://localhost:11434"));
        Assert.IsInstanceOfType<VllmEmbeddingProvider>(ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
            "vllm", null, null, http, "model", 128, "http://localhost:8002"));
    }

    [TestMethod]
    public void ReconnectionRequest_DeserializesDedicatedKeyAndPreservesEmbeddingSettings()
    {
        const string json = """
            {"provider":"qdrant","embeddingProvider":"perplexity","embeddingModel":"pplx-embed-v1-4b",
             "embeddingDimensions":512,"perplexityApiKey":"test-key","qdrantHost":"localhost","dimension":512}
            """;
        var request = JsonSerializer.Deserialize<VectorStoreConfigRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual("test-key", request.PerplexityApiKey);
        Assert.IsNull(request.OpenAiApiKey);
        Assert.AreEqual("perplexity", request.EmbeddingProvider);
        Assert.AreEqual(PerplexityEmbeddingModels.Standard4B, request.EmbeddingModel);
        Assert.AreEqual(512, request.EmbeddingDimensions);
    }

    [TestMethod]
    public void ReferenceSnippet_UsesPerplexityBuilderAndDedicatedKeyPlaceholder()
    {
        var config = new RagReferenceConfig(["synthetic.txt"], 200, 0, "character", "perplexity",
            PerplexityEmbeddingModels.Standard4B, 512, "", new RagFilter { TopK = 3 }, new RagRetrievalDerivation(), null);
        var code = ChatUiUtilityHelpers.GenerateRagReferenceCodeSnippet(config);
        StringAssert.Contains(code, ".UsePerplexityEmbedding(\"YOUR_PERPLEXITY_API_KEY\", new HttpClient(), model: \"pplx-embed-v1-4b\", dimensions: 512)");
        Assert.IsFalse(code.Contains("YOUR_OPENAI_API_KEY", StringComparison.Ordinal));
    }
}
