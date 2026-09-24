using System.Net;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Rag.Embeddings;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class OpenAIEmbeddingProviderTests
{
    private const string Ada = "text-embedding-ada-002";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Ada_SingleInputOmitsDimensionsAndReturnsFixedSize(bool explicitDimensions)
    {
        using var handler = new EmbeddingHandler();
        using var http = new HttpClient(handler);
        var provider = explicitDimensions
            ? new OpenAIEmbeddingProvider("test-key", http, Ada, 1536)
            : new OpenAIEmbeddingProvider("test-key", http, Ada);

        var vector = await provider.GetEmbeddingAsync("single input");

        Assert.AreEqual(1536, provider.Dimensions);
        Assert.AreEqual(1536, vector.Length);
        Assert.AreEqual(1f, vector[0]);
        var body = handler.Requests.Single();
        Assert.AreEqual(Ada, body.GetProperty("model").GetString());
        Assert.IsFalse(body.TryGetProperty("dimensions", out _));
        Assert.AreEqual("single input", body.GetProperty("input")[0].GetString());
    }

    [TestMethod]
    public async Task Ada_BatchOmitsDimensionsAndPreservesInputOrder()
    {
        using var handler = new EmbeddingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAIEmbeddingProvider("test-key", http, Ada);

        var vectors = await provider.GetEmbeddingsAsync(new[] { "first", "second" });

        Assert.AreEqual(2, vectors.Count);
        Assert.IsTrue(vectors.All(vector => vector.Length == 1536));
        Assert.AreEqual(1f, vectors[0][0]);
        Assert.AreEqual(2f, vectors[1][0]);
        Assert.IsFalse(handler.Requests.Single().TryGetProperty("dimensions", out _));
        CollectionAssert.AreEqual(new[] { "first", "second" }, handler.Requests.Single()
            .GetProperty("input").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(1024)]
    [DataRow(1535)]
    [DataRow(1537)]
    [DataRow(3072)]
    public void Ada_InvalidDimensionsAreRejectedBeforeHttp(int dimensions)
    {
        using var handler = new EmbeddingHandler();
        using var http = new HttpClient(handler);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OpenAIEmbeddingProvider("test-key", http, Ada, dimensions));

        Assert.AreEqual("dimensions", exception.ParamName);
        Assert.AreEqual(dimensions, exception.ActualValue);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow("text-embedding-3-small", 1536)]
    [DataRow("text-embedding-3-small", 128)]
    [DataRow("text-embedding-3-large", 3072)]
    [DataRow("text-embedding-3-large", 128)]
    [DataRow("custom-embedding-model", 64)]
    public async Task OtherModels_KeepSendingConfiguredDimensions(string model, int dimensions)
    {
        using var handler = new EmbeddingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAIEmbeddingProvider("test-key", http, model, dimensions);

        var vector = await provider.GetEmbeddingAsync("input");

        var body = handler.Requests.Single();
        Assert.AreEqual(model, body.GetProperty("model").GetString());
        Assert.AreEqual(dimensions, body.GetProperty("dimensions").GetInt32());
        Assert.AreEqual(dimensions, provider.Dimensions);
        Assert.AreEqual(dimensions, vector.Length);
    }

    [TestMethod]
    public async Task DefaultModelAndDimensionsAreUnchanged()
    {
        using var handler = new EmbeddingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAIEmbeddingProvider("test-key", http);

        var vector = await provider.GetEmbeddingAsync("input");

        var body = handler.Requests.Single();
        Assert.AreEqual("text-embedding-3-small", body.GetProperty("model").GetString());
        Assert.AreEqual(1536, body.GetProperty("dimensions").GetInt32());
        Assert.AreEqual(1536, vector.Length);
    }

    [TestMethod]
    public async Task Ada_StillRejectsResponseWithWrongVectorSize()
    {
        using var handler = new EmbeddingHandler { ResponseDimensions = 1535 };
        using var http = new HttpClient(handler);
        var provider = new OpenAIEmbeddingProvider("test-key", http, Ada);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetEmbeddingAsync("input"));

        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsFalse(handler.Requests.Single().TryGetProperty("dimensions", out _));
    }

    [TestMethod]
    public async Task Ada_EmptyAndCanceledRequestsDoNotSendHttp()
    {
        using var handler = new EmbeddingHandler();
        using var http = new HttpClient(handler);
        var provider = new OpenAIEmbeddingProvider("test-key", http, Ada);
        Assert.AreEqual(0, (await provider.GetEmbeddingsAsync(Array.Empty<string>())).Count);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetEmbeddingAsync("input", canceled.Token));

        Assert.AreEqual(0, handler.Requests.Count);
    }

    private sealed class EmbeddingHandler : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = new();
        public int? ResponseDimensions { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://api.openai.com/v1/embeddings", request.RequestUri!.AbsoluteUri);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = json.RootElement.Clone();
            Requests.Add(body);
            var isAda = body.GetProperty("model").GetString() == Ada;
            if (isAda && body.TryGetProperty("dimensions", out _))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("dimensions is not supported for this model")
                };

            var dimensions = ResponseDimensions ?? (isAda ? 1536 : body.GetProperty("dimensions").GetInt32());
            var data = body.GetProperty("input").EnumerateArray().Select((_, index) =>
            {
                var vector = new float[dimensions];
                vector[0] = index + 1;
                return new { index, embedding = vector };
            }).Reverse().ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { data }), Encoding.UTF8, "application/json")
            };
        }
    }
}
