using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Samples.ChatUi;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public sealed class PerplexityEmbeddingUiTests
{
    [TestMethod]
    [DataRow(PerplexityEmbeddingModels.Standard0_6B, 1024, false)]
    [DataRow(PerplexityEmbeddingModels.Standard4B, 2560, false)]
    [DataRow(PerplexityEmbeddingModels.Context0_6B, 1024, true)]
    [DataRow(PerplexityEmbeddingModels.Context4B, 2560, true)]
    public async Task SelectedModelAndDimension_ConstructCorrectProviderWithoutOpenAiKey(string model, int dimensions, bool contextual)
    {
        var calls = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            var endpoint = contextual ? "contextualizedembeddings" : "embeddings";
            Assert.AreEqual($"https://api.perplexity.ai/v1/{endpoint}", request.RequestUri!.AbsoluteUri);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual("perplexity-test-key", request.Headers.Authorization?.Parameter);
            Assert.IsTrue(cancellationToken.CanBeCanceled);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(model, body.RootElement.GetProperty("model").GetString());
            Assert.AreEqual(dimensions, body.RootElement.GetProperty("dimensions").GetInt32());
            Assert.AreEqual("base64_int8", body.RootElement.GetProperty("encoding_format").GetString());
            var input = body.RootElement.GetProperty("input");
            Assert.AreEqual(2, input.GetArrayLength());
            var texts = new[] { "synthetic document", "synthetic query" };
            for (var index = 0; index < texts.Length; index++)
            {
                if (contextual)
                {
                    Assert.AreEqual(1, input[index].GetArrayLength(), "Legacy flat inputs retain independent document contexts.");
                    Assert.AreEqual(texts[index], input[index][0].GetString());
                }
                else Assert.AreEqual(texts[index], input[index].GetString());
            }
            var bytes = new byte[dimensions];
            bytes[0] = 1;
            var embedding = Convert.ToBase64String(bytes);
            object data = contextual
                ? Enumerable.Range(0, 2).Select(index => new { index, data = new[] { new { index = 0, embedding } } }).ToArray()
                : Enumerable.Range(0, 2).Select(index => new { index, embedding }).ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { model, data }), Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        var provider = ChatUiUtilityHelpers.BuildRagEmbeddingProvider("perplexity", null, "perplexity-test-key", http, model, dimensions, "");
        if (contextual) Assert.IsInstanceOfType<IRetrievalEmbeddingProvider>(provider);
        Assert.AreEqual(dimensions, provider.Dimensions);
        var vectors = await provider.GetEmbeddingsAsync(["synthetic document", "synthetic query"]);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, vectors.Count);
        foreach (var vector in vectors)
        {
            Assert.AreEqual(dimensions, vector.Length);
            Assert.AreEqual(1f, vector[0]);
        }
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
    [DataRow("unsupported-model", 1024)]
    [DataRow(PerplexityEmbeddingModels.Context0_6B, 1025)]
    [DataRow(PerplexityEmbeddingModels.Context4B, 127)]
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
    }

    [TestMethod]
    [DataRow("ollama", "http://localhost:11434", "/api/embed")]
    [DataRow("vllm", "http://localhost:8002", "/v1/embeddings")]
    public async Task LocalProviders_UseTheirOwnEndpointsWithoutCloudKeys(string providerName, string baseUrl, string endpoint)
    {
        var calls = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual(baseUrl + endpoint, request.RequestUri!.AbsoluteUri);
            Assert.IsNull(request.Headers.Authorization);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("model", body.RootElement.GetProperty("model").GetString());
            Assert.AreEqual(128, body.RootElement.GetProperty("dimensions").GetInt32());
            Assert.AreEqual("synthetic text", body.RootElement.GetProperty("input")[0].GetString());
            var embedding = Enumerable.Repeat(0.25f, 128).ToArray();
            object response = providerName == "ollama"
                ? new { embeddings = new[] { embedding } }
                : new { data = new[] { new { index = 0, embedding } } };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        var provider = ChatUiUtilityHelpers.BuildRagEmbeddingProvider(providerName, null, null, http, "model", 128, baseUrl);
        var vector = await provider.GetEmbeddingAsync("synthetic text");
        Assert.AreEqual(1, calls);
        Assert.AreEqual(128, provider.Dimensions);
        Assert.AreEqual(128, vector.Length);
        Assert.AreEqual(0.25f, vector[0]);
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
    [DataRow(PerplexityEmbeddingModels.Standard4B, "PerplexityEmbeddingProvider")]
    [DataRow(PerplexityEmbeddingModels.Context4B, "PerplexityContextualizedEmbeddingProvider")]
    public void ReferenceSnippet_UsesPerplexityProviderAndDedicatedKeyPlaceholder(string model, string providerType)
    {
        var config = new RagReferenceConfig(["synthetic.txt"], 200, 0, "character", "perplexity",
            model, 512, "", new RagFilter { TopK = 3 }, new RagRetrievalDerivation(), null);
        var code = ChatUiUtilityHelpers.GenerateRagReferenceCodeSnippet(config);
        StringAssert.Contains(code, $".UseEmbedding(new {providerType}(\"YOUR_PERPLEXITY_API_KEY\", embeddingHttpClient, model: \"{model}\", dimensions: 512))");
        StringAssert.Contains(code, $"var embeddingHttpClient = new HttpClient {{ Timeout = TimeSpan.FromSeconds({config.EmbeddingTimeoutSeconds}) }};");
        Assert.IsFalse(code.Contains("YOUR_OPENAI_API_KEY", StringComparison.Ordinal));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
