using Google.Protobuf;
using Grpc.Core;
using Mythosia.VectorDb.Qdrant;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Mythosia.VectorDb.Tests.Qdrant;

[TestClass]
public class QdrantSearchContractTests
{
    [TestMethod]
    public async Task TextSearch_SendsOnlySparseVectorAndPreservesFilterAndPayload()
    {
        var (store, calls) = CreateStore();
        calls.TextResults.Add(Point("doc", .7f));
        var filter = new VectorFilter().Where("tenant", "company-a")
            .Or(group => group.WhereIn("kind", "manual", "faq").Where("public", "true"))
            .WithMinScore(.6);

        var result = await store.TextSearchAsync("hello world", 7, filter);

        var request = calls.Queries.Single();
        Assert.AreEqual("text-sparse", request.Using);
        Assert.IsNotNull(request.Query.Nearest.Sparse);
        Assert.IsNull(request.Query.Nearest.Dense);
        Assert.IsGreaterThan(0, request.Query.Nearest.Sparse.Indices.Count);
        Assert.AreEqual(7UL, request.Limit);
        Assert.AreEqual("\"meta.tenant\"", request.Filter.Must[0].Field.Key);
        Assert.AreEqual("company-a", request.Filter.Must[0].Field.Match.Keyword);
        Assert.AreEqual(2, request.Filter.Must[1].Filter.Should.Count);
        Assert.IsTrue(request.WithPayload.Enable);
        Assert.IsTrue(request.WithVectors.Enable);
        Assert.AreEqual("doc", result.Single().Record.Id);
        Assert.AreEqual("hello world", result[0].Record.Content);
        Assert.AreEqual("company-a", result[0].Record.Metadata["tenant"]);
        Assert.AreEqual("__mythosia_schema__", request.Filter.MustNot.Single().Field.Match.Keyword);
    }

    [TestMethod]
    public async Task TextSearch_EmptyAnalysisReturnsEmptyWithoutNetwork()
    {
        var (store, calls) = CreateStore();
        Assert.AreEqual(0, (await store.TextSearchAsync(" ! ")).Count);
        Assert.AreEqual(0, calls.TotalCalls);
    }

    [TestMethod]
    public async Task Hybrid_WeightsChangeRankingAndAllOptionsReachFusion()
    {
        var (store, calls) = CreateStore();
        calls.DenseResults.AddRange([Point("dense-first", .0001f), Point("text-first", .00001f)]);
        calls.TextResults.AddRange([Point("text-first", 100f), Point("dense-first", 90f)]);
        var options = new HybridSearchOptions { VectorWeight = .8f, CandidateMultiplier = 3, RrfK = 4 };

        var densePreferred = await store.HybridSearchAsync([1, 0, 0], "hello", options, 2);
        options.VectorWeight = .2f;
        var textPreferred = await store.HybridSearchAsync([1, 0, 0], "hello", options, 2);

        Assert.AreEqual("dense-first", densePreferred[0].Record.Id);
        Assert.AreEqual("text-first", textPreferred[0].Record.Id);
        Assert.AreEqual(4, calls.Queries.Count);
        Assert.IsTrue(calls.Queries.All(query => query.Limit == 6));
        Assert.AreEqual(5 * (.8 / 5 + .2 / 6), densePreferred[0].Score, .000001);
        Assert.IsTrue(calls.Queries.All(query => !query.HasScoreThreshold));
    }

    [TestMethod]
    public async Task Hybrid_FinalMinScoreDoesNotDiscardLowRawScoreCandidates()
    {
        var (store, calls) = CreateStore();
        calls.DenseResults.Add(Point("shared", .001f));
        calls.TextResults.Add(Point("shared", .002f));
        var filter = new VectorFilter().Where("tenant", "company-a").WithMinScore(.99);

        var result = await store.HybridSearchAsync([1, 0, 0], "hello", new HybridSearchOptions(), 5, filter);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1d, result[0].Score, .000001);
        Assert.IsTrue(calls.Queries.All(query => !query.HasScoreThreshold));
        Assert.IsTrue(calls.Queries.All(query => query.Filter.Must[0].Field.Match.Keyword == "company-a"));
    }

    [TestMethod]
    public async Task Hybrid_ZeroVectorWeightOnlySearchesSparseAndNeedsNoDenseVector()
    {
        var (store, calls) = CreateStore();
        calls.TextResults.Add(Point("text", .5f));
        var results = await store.HybridSearchAsync([], "hello", new HybridSearchOptions { VectorWeight = 0 });
        Assert.AreEqual("text-sparse", calls.Queries.Single().Using);
        Assert.AreEqual(1d, results.Single().Score, .000001);
    }

    [TestMethod]
    public async Task Hybrid_FullVectorWeightOnlySearchesDenseAndNeedsNoText()
    {
        var (store, calls) = CreateStore();
        calls.DenseResults.Add(Point("dense", .5f));
        var results = await store.HybridSearchAsync([1, 0, 0], null!, new HybridSearchOptions { VectorWeight = 1 });
        Assert.AreEqual("text-dense", calls.Queries.Single().Using);
        Assert.AreEqual(1d, results.Single().Score, .000001);
    }

    [TestMethod]
    public async Task Hybrid_EmptyTextWithZeroVectorWeightMakesNoNetworkCalls()
    {
        var (store, calls) = CreateStore();
        Assert.AreEqual(0, (await store.HybridSearchAsync([], "!", new HybridSearchOptions { VectorWeight = 0 })).Count);
        Assert.AreEqual(0, calls.TotalCalls);
    }

    [TestMethod]
    public async Task Hybrid_SnapshotsOptionsBeforeFirstAwait()
    {
        var (store, calls) = CreateStore();
        var options = new HybridSearchOptions { VectorWeight = .8f, CandidateMultiplier = 3, RrfK = 4 };
        calls.OnCollectionExists = () => { options.VectorWeight = 0; options.CandidateMultiplier = 20; options.RrfK = 99; };
        calls.DenseResults.Add(Point("dense", .5f));
        calls.TextResults.Add(Point("text", .5f));

        var result = await store.HybridSearchAsync([1, 0, 0], "hello", options, 2);

        Assert.AreEqual(2, calls.Queries.Count);
        Assert.IsTrue(calls.Queries.All(query => query.Limit == 6));
        Assert.AreEqual("dense", result[0].Record.Id);
        Assert.AreEqual(.8, result[0].Score, .000001);
    }

    [TestMethod]
    public async Task Search_UnsupportedNestedFilterFailsBeforeNetwork()
    {
        var (store, calls) = CreateStore();
        var filter = new VectorFilter().Where("tenant", "company-a").Or(group => group.WhereLike("name", "%secret%"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => store.TextSearchAsync("hello", filter: filter));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => store.HybridSearchAsync([1, 0, 0], "hello", new HybridSearchOptions(), filter: filter));
        Assert.AreEqual(0, calls.TotalCalls);
    }

    [TestMethod]
    public async Task TextSearch_NegatedFiltersRequireExistingKeysAndEmptyInSetMatchesNothing()
    {
        var (store, calls) = CreateStore();
        var filter = new VectorFilter().WhereNot("tenant", "other")
            .WhereNotIn("kind", "secret").WhereIn("empty").WhereExists("author").WhereNotExists("deleted");
        await store.TextSearchAsync("hello", filter: filter);
        var translated = calls.Queries.Single().Filter.Must;
        Assert.AreEqual("\"meta.tenant\"", translated[0].Filter.MustNot[0].IsEmpty.Key);
        Assert.AreEqual("\"meta.kind\"", translated[1].Filter.MustNot[0].IsEmpty.Key);
        Assert.AreEqual(translated[2].Filter.Must[0], translated[2].Filter.MustNot[0]);
        Assert.AreEqual("\"meta.author\"", translated[3].Filter.MustNot[0].IsEmpty.Key);
        Assert.AreEqual("\"meta.deleted\"", translated[4].IsEmpty.Key);
    }

    [TestMethod]
    public async Task Search_CancellationAndInvalidOptionsFailBeforeNetwork()
    {
        var (store, calls) = CreateStore();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.TextSearchAsync("!", cancellationToken: canceled.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.HybridSearchAsync([1, 0, 0], "hello", new HybridSearchOptions(), cancellationToken: canceled.Token));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => store.HybridSearchAsync([1, 0, 0], "hello", new HybridSearchOptions { VectorWeight = float.NaN }));
        Assert.AreEqual(0, calls.TotalCalls);
    }

    [TestMethod]
    public async Task LegacyHybrid_KeepsConfiguredServerFusion()
    {
        var (store, calls) = CreateStore(QdrantHybridFusionStrategy.Dbsf);
        await store.HybridSearchAsync([1, 0, 0], "hello", 3,
            new VectorFilter().Where("tenant", "company-a").WhereNotIn("kind", "private"));
        var request = calls.Queries.Single();
        Assert.AreEqual(Fusion.Dbsf, request.Query.Fusion);
        Assert.AreEqual(2, request.Prefetch.Count);
        Assert.AreEqual(6UL, request.Prefetch[0].Limit);
        Assert.IsTrue(request.Prefetch.All(prefetch =>
            prefetch.Filter.Must[0].Field.Key == "\"meta.tenant\"" &&
            prefetch.Filter.Must[1].Filter.MustNot[0].IsEmpty.Key == "\"meta.kind\""));
    }

    [TestMethod]
    public async Task Search_UnrepresentableLiteralMetadataKeyFailsBeforeNetwork()
    {
        var (store, calls) = CreateStore();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => store.TextSearchAsync("hello",
            filter: new VectorFilter().Where("key\"quoted", "value")));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => store.TextSearchAsync("hello",
            filter: new VectorFilter().Where(@"key\path", "value")));
        Assert.AreEqual(0, calls.TotalCalls);
    }

    [TestMethod]
    public async Task TextSearch_MetadataPayloadIndexUsesLiteralKey()
    {
        var calls = new RecordingInvoker();
        using var client = new QdrantClient(new QdrantGrpcClient(calls));
        using var store = new QdrantStore(new QdrantOptions
        {
            CollectionName = "contract-test", Dimension = 3, AutoCreateCollection = false,
            AdditionalPayloadIndexes = [new QdrantIndexOption("meta.tenant", PayloadSchemaType.Keyword)]
        }, client);
        await store.TextSearchAsync("hello");
        Assert.AreEqual("\"meta.tenant\"", calls.Indexes.Single().FieldName);
    }

    private static (QdrantStore Store, RecordingInvoker Calls) CreateStore(QdrantHybridFusionStrategy fusion = QdrantHybridFusionStrategy.Rrf)
    {
        var calls = new RecordingInvoker();
        var client = new QdrantClient(new QdrantGrpcClient(calls));
        var store = new QdrantStore(new QdrantOptions
        {
            CollectionName = "contract-test", Dimension = 3,
            AutoCreateCollection = false, HybridFusionStrategy = fusion
        }, client);
        return (store, calls);
    }

    private static ScoredPoint Point(string id, float score)
    {
        var point = new ScoredPoint { Id = new PointId { Num = 1 }, Score = score };
        point.Payload["_id"] = id;
        point.Payload["_content"] = "hello world";
        point.Payload["meta.tenant"] = "company-a";
        return point;
    }

    private sealed class RecordingInvoker : CallInvoker
    {
        public List<QueryPoints> Queries { get; } = [];
        public List<CreateFieldIndexCollection> Indexes { get; } = [];
        public List<ScoredPoint> DenseResults { get; } = [];
        public List<ScoredPoint> TextResults { get; } = [];
        public int TotalCalls { get; private set; }
        public Action? OnCollectionExists { get; set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            TotalCalls++;
            object response;
            if (request is CollectionExistsRequest)
            {
                OnCollectionExists?.Invoke();
                response = new CollectionExistsResponse { Result = new CollectionExists { Exists = true } };
            }
            else if (request is CreateFieldIndexCollection index)
            {
                Indexes.Add(CreateFieldIndexCollection.Parser.ParseFrom(index.ToByteArray()));
                response = new PointsOperationResponse();
            }
            else if (request is QueryPoints query)
            {
                // Round-trip the actual protobuf request to verify what the SDK puts on the wire.
                var captured = QueryPoints.Parser.ParseFrom(query.ToByteArray());
                Queries.Add(captured);
                var result = new QueryResponse();
                result.Result.AddRange(captured.Using == "text-sparse" ? TextResults : DenseResults);
                response = result;
            }
            else throw new InvalidOperationException($"Unexpected RPC: {method.FullName}");
            return new AsyncUnaryCall<TResponse>(Task.FromResult((TResponse)response),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
