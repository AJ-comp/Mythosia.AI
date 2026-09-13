using System.Net;
using System.Text.Json.Nodes;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb.InMemory;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class PerplexityEmbeddingProviderTests
{
    [TestMethod]
    [DataRow(PerplexityEmbeddingModels.Standard0_6B, false, 1024)]
    [DataRow(PerplexityEmbeddingModels.Standard4B, false, 2560)]
    [DataRow(PerplexityEmbeddingModels.Context0_6B, true, 1024)]
    [DataRow(PerplexityEmbeddingModels.Context4B, true, 2560)]
    public async Task AllModels_DeriveFullDimensionsAndUseCorrectEndpoint(string model, bool context, int dimensions)
    {
        using var handler = new PerplexityApiTestTransport { Reply = body => Reply(body) };
        using var http = new HttpClient(handler);
        float[] result;
        if (context)
        {
            var provider = new PerplexityContextualizedEmbeddingProvider("key", http, model);
            Assert.AreEqual(dimensions, provider.Dimensions);
            result = await provider.GetQueryEmbeddingAsync("query");
        }
        else
        {
            var provider = new PerplexityEmbeddingProvider("key", http, model);
            Assert.AreEqual(dimensions, provider.Dimensions);
            result = await provider.GetEmbeddingAsync("query");
        }
        Assert.AreEqual(dimensions, result.Length);
        Assert.AreEqual("https://api.perplexity.ai/v1/" + (context ? "contextualizedembeddings" : "embeddings"), handler.Uris[0].AbsoluteUri);
        Assert.AreEqual(model, handler.Bodies[0]["model"]!.GetValue<string>());
        Assert.AreEqual("base64_int8", handler.Bodies[0]["encoding_format"]!.GetValue<string>());
        Assert.AreEqual("Bearer key", handler.Authorization[0]);
        Assert.IsTrue(handler.Contents.Single().WasDisposed);
    }

    [TestMethod]
    public async Task SignedInt8_NormalizesAndReordersEveryResponseIndex()
    {
        using var handler = new PerplexityApiTestTransport { Reply = body => Reply(body, (text, dimensions) => Vector(dimensions, text == "first" ? (sbyte)-128 : (sbyte)127, 64), reverse: true) };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://caller.example/") };
        http.DefaultRequestHeaders.Add("X-Caller", "preserved");
        var provider = new PerplexityEmbeddingProvider("key", http, dimensions: 129);
        var vectors = await provider.GetEmbeddingsAsync(new[] { "first", "second" });
        var norm = Math.Sqrt(128 * 128 + 64 * 64);
        Assert.AreEqual((float)(-128 / norm), vectors[0][0], 0.000001f);
        Assert.AreEqual((float)(64 / norm), vectors[0][1], 0.000001f);
        Assert.IsGreaterThan(0f, vectors[1][0]);
        foreach (var vector in vectors) Assert.AreEqual(1d, vector.Sum(value => (double)value * value), 0.000001);
        Assert.AreEqual("https://caller.example/", http.BaseAddress.AbsoluteUri);
        Assert.IsNull(http.DefaultRequestHeaders.Authorization);
        Assert.AreEqual("preserved", http.DefaultRequestHeaders.GetValues("X-Caller").Single());
        await provider.GetEmbeddingAsync("caller owns reusable client");
        Assert.IsTrue(handler.Contents.All(content => content.WasDisposed));
    }

    [TestMethod]
    public async Task ContextualizedDocuments_PreserveGroupsChunkOrderAndQuerySpace()
    {
        using var handler = new PerplexityApiTestTransport
        {
            Reply = body => Reply(body, (text, dimensions) => Vector(dimensions, (sbyte)text.Length, -10), reverse: true)
        };
        using var http = new HttpClient(handler);
        var provider = new PerplexityContextualizedEmbeddingProvider("key", http, PerplexityEmbeddingModels.Context4B, 128);
        var groups = await provider.GetDocumentEmbeddingsAsync(new[] { new[] { "a", "longer" }, new[] { "second-document" } });
        Assert.HasCount(2, groups);
        Assert.HasCount(2, groups[0]);
        Assert.HasCount(1, groups[1]);
        Assert.IsLessThan(groups[0][1][0], groups[0][0][0]);
        Assert.IsLessThan(groups[1][0][0], groups[0][1][0]);
        Assert.AreEqual("longer", handler.Bodies[0]["input"]![0]![1]!.GetValue<string>());
        Assert.AreEqual("second-document", handler.Bodies[0]["input"]![1]![0]!.GetValue<string>());
        await provider.GetQueryEmbeddingAsync("query");
        Assert.AreEqual(PerplexityEmbeddingModels.Context4B, handler.Bodies[1]["model"]!.GetValue<string>());
        Assert.AreEqual("query", handler.Bodies[1]["input"]![0]![0]!.GetValue<string>());
        Assert.HasCount(1, handler.Bodies[1]["input"]!.AsArray());
        Assert.IsFalse(typeof(IEmbeddingProvider).IsAssignableFrom(typeof(PerplexityContextualizedEmbeddingProvider)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PackedBinary_PreservesBitsAndUsesHammingDistance(bool context)
    {
        using var handler = new PerplexityApiTestTransport
        {
            Reply = body => Reply(body, (text, dimensions) => Enumerable.Repeat(text == "zero" ? (byte)0 : (byte)255, dimensions / 8).ToArray())
        };
        using var http = new HttpClient(handler);
        IReadOnlyList<PerplexityBinaryEmbedding> values;
        if (context)
        {
            var provider = new PerplexityContextualizedEmbeddingProvider("key", http, dimensions: 128);
            values = (await provider.GetBinaryDocumentEmbeddingsAsync(new[] { new[] { "zero", "ones" } }))[0];
            var query = await provider.GetBinaryQueryEmbeddingAsync("zero");
            Assert.AreEqual(0, query.HammingDistance(values[0]));
        }
        else
        {
            var provider = new PerplexityEmbeddingProvider("key", http, dimensions: 128);
            values = await provider.GetBinaryEmbeddingsAsync(new[] { "zero", "ones" });
            var query = await provider.GetBinaryEmbeddingAsync("zero");
            Assert.AreEqual(0, query.HammingDistance(values[0]));
        }
        Assert.AreEqual(128, values[0].Dimensions);
        Assert.HasCount(16, values[0].ToArray());
        Assert.AreEqual(128, values[0].HammingDistance(values[1]));
        Assert.AreEqual(128, values[1].HammingDistance(values[0]));
        var copy = values[0].ToArray();
        copy[0] = 255;
        Assert.AreEqual(0, values[0].ToArray()[0]);
        Assert.IsTrue(handler.Bodies.All(body => body["encoding_format"]!.GetValue<string>() == "base64_binary"));
    }

    [TestMethod]
    public async Task PackedBinary_CountsPartialByteDifferencesAndRejectsDimensionMismatch()
    {
        byte handlerByte = 0;
        using var handler = new PerplexityApiTestTransport
        {
            Reply = body => Reply(body, (_, dimensions) => { var bytes = new byte[dimensions / 8]; bytes[0] = handlerByte; return bytes; })
        };
        using var http = new HttpClient(handler);
        var zero = await new PerplexityEmbeddingProvider("key", http, dimensions: 128).GetBinaryEmbeddingAsync("zero");
        handlerByte = 0b01010101;
        var fourBits = await new PerplexityEmbeddingProvider("key", http, dimensions: 128).GetBinaryEmbeddingAsync("four");
        Assert.AreEqual(4, zero.HammingDistance(fourBits));
        var larger = await new PerplexityEmbeddingProvider("key", http, dimensions: 256).GetBinaryEmbeddingAsync("larger");
        Assert.Throws<ArgumentException>(() => zero.HammingDistance(larger));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BinaryNonByteAlignedDimensions_RejectsBeforeHttp(bool context)
    {
        using var handler = new PerplexityApiTestTransport();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            if (context) await new PerplexityContextualizedEmbeddingProvider("key", http, dimensions: 129).GetBinaryQueryEmbeddingAsync("query");
            else await new PerplexityEmbeddingProvider("key", http, dimensions: 129).GetBinaryEmbeddingAsync("query");
        });
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow(127)]
    [DataRow(1025)]
    [DataRow(0)]
    public void InvalidDimensions_RejectsBeforeHttp(int dimensions)
    {
        using var http = new HttpClient(new PerplexityApiTestTransport());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerplexityEmbeddingProvider("key", http, dimensions: dimensions));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerplexityContextualizedEmbeddingProvider("key", http, dimensions: dimensions));
    }

    [TestMethod]
    public void ModelsCannotBeMixedAcrossStandardAndContextualizedApis()
    {
        using var http = new HttpClient(new PerplexityApiTestTransport());
        Assert.Throws<ArgumentException>(() => new PerplexityEmbeddingProvider("key", http, PerplexityEmbeddingModels.Context4B));
        Assert.Throws<ArgumentException>(() => new PerplexityContextualizedEmbeddingProvider("key", http, PerplexityEmbeddingModels.Standard4B));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerplexityEmbeddingProvider("key", http, PerplexityEmbeddingModels.Standard4B, 2561));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerplexityContextualizedEmbeddingProvider("key", http, PerplexityEmbeddingModels.Context4B, 2561));
    }

    [TestMethod]
    public async Task EmptyBatches_AreEmptyWithoutRequests_EmptyTextsAndDocumentsAreRejected()
    {
        using var handler = new PerplexityApiTestTransport();
        using var http = new HttpClient(handler);
        var standard = new PerplexityEmbeddingProvider("key", http);
        var context = new PerplexityContextualizedEmbeddingProvider("key", http);
        Assert.IsEmpty(await standard.GetEmbeddingsAsync(Array.Empty<string>()));
        Assert.IsEmpty(await standard.GetBinaryEmbeddingsAsync(Array.Empty<string>()));
        Assert.IsEmpty(await context.GetDocumentEmbeddingsAsync(Array.Empty<string[]>()));
        Assert.IsEmpty(await context.GetBinaryDocumentEmbeddingsAsync(Array.Empty<string[]>()));
        await Assert.ThrowsAsync<ArgumentException>(() => standard.GetEmbeddingAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => context.GetQueryEmbeddingAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => context.GetDocumentEmbeddingsAsync(new[] { Array.Empty<string>() }));
        await Assert.ThrowsAsync<ArgumentException>(() => standard.GetEmbeddingsAsync(Enumerable.Repeat("text", 513)));
        await Assert.ThrowsAsync<ArgumentException>(() => context.GetDocumentEmbeddingsAsync(Enumerable.Repeat(new[] { "text" }, 513)));
        await Assert.ThrowsAsync<ArgumentException>(() => context.GetDocumentEmbeddingsAsync(new[] { Enumerable.Repeat("a", 8001), Enumerable.Repeat("b", 8000) }));
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow("missing-index")]
    [DataRow("duplicate-index")]
    [DataRow("out-of-range")]
    [DataRow("wrong-count")]
    [DataRow("wrong-model")]
    [DataRow("wrong-dimension")]
    [DataRow("bad-base64")]
    [DataRow("float-array")]
    [DataRow("zero-vector")]
    public async Task MalformedStandardResponse_FailsAtomically(string fault)
    {
        using var handler = new PerplexityApiTestTransport { Reply = body => Corrupt(Reply(body), fault) };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PerplexityEmbeddingProvider("key", http, dimensions: 128).GetEmbeddingsAsync(new[] { "a", "b" }));
        Assert.IsTrue(handler.Contents.Single().WasDisposed);
    }

    [TestMethod]
    [DataRow("document-count")]
    [DataRow("document-index")]
    [DataRow("chunk-count")]
    [DataRow("chunk-index")]
    public async Task MalformedContextResponse_FailsWithoutLosingDocumentBoundaries(string fault)
    {
        using var handler = new PerplexityApiTestTransport
        {
            Reply = body =>
            {
                var root = JsonNode.Parse(Reply(body))!;
                var data = root["data"]!.AsArray();
                if (fault == "document-count") data.RemoveAt(1);
                if (fault == "document-index") data[1]!["index"] = 0;
                if (fault == "chunk-count") data[0]!["data"]!.AsArray().RemoveAt(1);
                if (fault == "chunk-index") data[0]!["data"]![1]!["index"] = 0;
                return root.ToJsonString();
            }
        };
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PerplexityContextualizedEmbeddingProvider("key", http, dimensions: 128)
            .GetDocumentEmbeddingsAsync(new[] { new[] { "a", "b" }, new[] { "c" } }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HttpFailureAndCancellation_ArePropagated(bool context)
    {
        using var handler = new PerplexityApiTestTransport { Status = HttpStatusCode.TooManyRequests };
        using var http = new HttpClient(handler);
        Task Call(CancellationToken token) => context
            ? new PerplexityContextualizedEmbeddingProvider("key", http).GetQueryEmbeddingAsync("query", token)
            : new PerplexityEmbeddingProvider("key", http).GetEmbeddingAsync("query", token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Call(default));
        StringAssert.Contains(error.Message, "429");
        Assert.IsTrue(handler.Contents.Single().WasDisposed);
        handler.WaitForCancellation = true;
        using var cancellation = new CancellationTokenSource();
        var pending = Call(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task StandardProvider_RagIndexAndQuery_RetrieveSignedNegativeVector()
    {
        using var handler = new PerplexityApiTestTransport
        {
            Reply = body => Reply(body, (text, dimensions) => text.Contains("refund", StringComparison.Ordinal)
                ? Vector(dimensions, -128, 64) : Vector(dimensions, 127, 64))
        };
        using var http = new HttpClient(handler);
        var embedding = new PerplexityEmbeddingProvider("key", http, dimensions: 128);
        var pipeline = new RagPipeline(embedding, new InMemoryVectorStore(),
            new CharacterTextSplitter(200, 0), new DefaultContextBuilder(),
            new RagPipelineOptions { DefaultQuery = new RagQueryOptions { FinalFilter = new RagFilter { TopK = 1 } } });
        await pipeline.IndexDocumentAsync(new RagDocument { Id = "refund", Source = "refund.txt", Content = "Our refund policy grants fourteen days." });
        await pipeline.IndexDocumentAsync(new RagDocument { Id = "shipping", Source = "shipping.txt", Content = "Shipping takes three days." });
        var result = await pipeline.QueryAsync("refund eligibility");
        Assert.HasCount(1, result.SearchResults);
        StringAssert.Contains(result.SearchResults[0].Record.Content, "fourteen days");
        StringAssert.Contains(result.Context, "fourteen days");
        Assert.IsGreaterThan(0.99d, result.SearchResults[0].Score);
        Assert.HasCount(3, handler.Bodies);
    }

    internal static byte[] Vector(int dimensions, sbyte first = 100, sbyte second = -50)
    {
        var bytes = new byte[dimensions];
        bytes[0] = unchecked((byte)first); bytes[1] = unchecked((byte)second);
        return bytes;
    }

    internal static string Reply(JsonObject body, Func<string, int, byte[]>? encode = null, bool reverse = false)
    {
        encode ??= (_, dimensions) => Vector(dimensions);
        var dimensions = body["dimensions"]!.GetValue<int>();
        var model = body["model"]!.GetValue<string>();
        var input = body["input"]!.AsArray();
        JsonArray Items(JsonArray texts)
        {
            var values = texts.Select((text, index) => (JsonNode)new JsonObject
            { ["object"] = "embedding", ["index"] = index, ["embedding"] = Convert.ToBase64String(encode(text!.GetValue<string>(), dimensions)) }).ToArray();
            if (reverse) Array.Reverse(values);
            return new JsonArray(values);
        }
        JsonArray data;
        if (input.Count > 0 && input[0] is JsonArray)
        {
            var groups = input.Select((document, index) => (JsonNode)new JsonObject
            { ["object"] = "list", ["index"] = index, ["data"] = Items(document!.AsArray()) }).ToArray();
            if (reverse) Array.Reverse(groups);
            data = new JsonArray(groups);
        }
        else data = Items(input);
        return new JsonObject { ["object"] = "list", ["model"] = model, ["data"] = data }.ToJsonString();
    }

    private static string Corrupt(string json, string fault)
    {
        var root = JsonNode.Parse(json)!;
        var data = root["data"]!.AsArray();
        switch (fault)
        {
            case "missing-index": data[1]!.AsObject().Remove("index"); break;
            case "duplicate-index": data[1]!["index"] = 0; break;
            case "out-of-range": data[1]!["index"] = 2; break;
            case "wrong-count": data.RemoveAt(1); break;
            case "wrong-model": root["model"] = PerplexityEmbeddingModels.Standard4B; break;
            case "wrong-dimension": data[1]!["embedding"] = Convert.ToBase64String(Vector(129)); break;
            case "bad-base64": data[1]!["embedding"] = "%%%"; break;
            case "float-array": data[1]!["embedding"] = new JsonArray(0.1, 0.2); break;
            case "zero-vector": data[1]!["embedding"] = Convert.ToBase64String(new byte[128]); break;
        }
        return root.ToJsonString();
    }
}
