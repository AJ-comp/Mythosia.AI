using Mythosia.AI.Samples.ChatUi;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public sealed class OpenAIEmbeddingUiTests
{
    [TestMethod]
    [DataRow("text-embedding-ada-002", 1536)]
    [DataRow("text-embedding-3-small", 512)]
    [DataRow("text-embedding-3-large", 3072)]
    public async Task SubmittedSettings_PreserveModelDimensions(string model, int dimensions)
    {
        var calls = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            calls++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://api.openai.com/v1/embeddings", request.RequestUri!.AbsoluteUri);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual("test-key", request.Headers.Authorization?.Parameter);
            Assert.IsTrue(cancellationToken.CanBeCanceled);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(model, body.RootElement.GetProperty("model").GetString());
            CollectionAssert.AreEqual(new[] { "synthetic document", "synthetic query" },
                body.RootElement.GetProperty("input").EnumerateArray().Select(value => value.GetString()).ToArray());
            if (model == "text-embedding-ada-002")
                Assert.IsFalse(body.RootElement.TryGetProperty("dimensions", out _));
            else
                Assert.AreEqual(dimensions, body.RootElement.GetProperty("dimensions").GetInt32());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    data = Enumerable.Range(0, 2).Select(index => new
                    {
                        index,
                        embedding = Enumerable.Repeat(0.25f, dimensions).ToArray()
                    }).ToArray()
                }), Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        var provider = ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
            "openai", "test-key", null, http, model, dimensions, "");

        Assert.AreEqual(dimensions, provider.Dimensions);
        var vectors = await provider.GetEmbeddingsAsync(["synthetic document", "synthetic query"]);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, vectors.Count);
        foreach (var vector in vectors)
        {
            Assert.AreEqual(dimensions, vector.Length);
            Assert.AreEqual(0.25f, vector[0]);
        }
    }

    [TestMethod]
    [DataRow(512)]
    [DataRow(3072)]
    public void AdaIncompatibleDatabaseDimensions_AreRejectedByServerFactory(int dimensions)
    {
        using var http = new HttpClient();
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ChatUiUtilityHelpers.BuildRagEmbeddingProvider(
                "openai", "test-key", null, http, "text-embedding-ada-002", dimensions, ""));

        Assert.AreEqual("dimensions", error.ParamName);
        StringAssert.Contains(error.Message, "1536");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
