using System.Text.Json;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Evaluation;

namespace Mythosia.AI.Rag.Evaluation.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class DenseCacheTests
{
    private string root = "";

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "Mythosia.Evaluation.DenseCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var prefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mythosia.Evaluation.DenseCacheTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(root).StartsWith(prefix, StringComparison.Ordinal) && Directory.Exists(root)) Directory.Delete(root, true);
    }

    [TestMethod]
    public async Task CacheHit_DeduplicatesTextsAndSuppressesProviderCallsAcrossRuns()
    {
        var provider = new FakeEmbeddingProvider();
        var first = new DenseEmbeddingCache(root);
        var generated = await first.GetAsync(["same", "same", "other"], "provider/model/2", provider, default);
        Assert.AreEqual(1, provider.Calls);
        CollectionAssert.AreEqual(new[] { "same", "other" }, provider.Batches.Single());
        Assert.AreEqual(2, first.GeneratedVectors);
        Assert.AreEqual(0, first.CacheHits);
        generated["same"][0] = 777;
        var offline = new FakeEmbeddingProvider { Generate = (_, _) => throw new AssertFailedException("Cache hit must not call the external provider.") };
        var second = new DenseEmbeddingCache(root);
        var cached = await second.GetAsync(["same", "other", "same"], "provider/model/2", offline, default);
        Assert.AreEqual(0, offline.Calls);
        Assert.AreEqual(2, second.CacheHits);
        Assert.AreEqual(0, second.GeneratedVectors);
        Assert.AreEqual(1f, cached["same"][0], "Mutating returned vectors must not mutate cached data.");
    }

    [TestMethod]
    public async Task DifferentProviderIdentity_DoesNotReuseAnotherModelsVectors()
    {
        var provider = new FakeEmbeddingProvider();
        await new DenseEmbeddingCache(root).GetAsync(["same"], "provider/model-a/2", provider, default);
        var second = new DenseEmbeddingCache(root);
        await second.GetAsync(["same"], "provider/model-b/2", provider, default);
        Assert.AreEqual(2, provider.Calls);
        Assert.AreEqual(0, second.CacheHits);
        Assert.AreEqual(2, Directory.GetFiles(root, "*.json").Length);
    }

    [TestMethod]
    public async Task InvalidVector_PreventsPublishingAnyEntryFromItsBatch()
    {
        foreach (var invalid in new float[][] { [0, 0], [1], [float.NaN, 1], [float.PositiveInfinity, 1] })
        {
            var cacheDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var provider = new FakeEmbeddingProvider { Generate = (_, _) => Task.FromResult<IReadOnlyList<float[]>>([[1, 0], invalid]) };
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new DenseEmbeddingCache(cacheDirectory).GetAsync(["valid", "invalid"], "model", provider, default));
            Assert.AreEqual(0, Directory.GetFiles(cacheDirectory).Length, "Invalid batches must not produce partial cache entries.");
        }
    }

    [TestMethod]
    public async Task WrongEmbeddingCount_DoesNotPublishCacheEntries()
    {
        var provider = new FakeEmbeddingProvider { Generate = (_, _) => Task.FromResult<IReadOnlyList<float[]>>([[1, 0]]) };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new DenseEmbeddingCache(root).GetAsync(["one", "two"], "model", provider, default));
        Assert.AreEqual(0, Directory.GetFiles(root).Length);
    }

    [TestMethod]
    public async Task CorruptedCachedIdentity_IsRejectedWithoutCallingProvider()
    {
        var identity = "provider/model/2";
        var text = "same";
        var filename = DenseEmbeddingCache.Hash(identity + "\0" + DenseEmbeddingCache.Hash(text)) + ".json";
        File.WriteAllText(Path.Combine(root, filename), JsonSerializer.Serialize(new { SchemaVersion = 1, Identity = "another-provider", TextHash = DenseEmbeddingCache.Hash(text), Vector = new[] { 1f, 0f } }));
        var provider = new FakeEmbeddingProvider();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new DenseEmbeddingCache(root).GetAsync([text], identity, provider, default));
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    public async Task CachedWrongDimension_IsRejectedWithoutCallingProvider()
    {
        var identity = "provider/model";
        await new DenseEmbeddingCache(root).GetAsync(["same"], identity, new FakeEmbeddingProvider(), default);
        var provider = new FakeEmbeddingProvider { Dimensions = 3 };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new DenseEmbeddingCache(root).GetAsync(["same"], identity, provider, default));
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    public async Task PreCancelledEmptyRequest_StillHonorsCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var provider = new FakeEmbeddingProvider();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new DenseEmbeddingCache(root).GetAsync([], "model", provider, cancelled.Token));
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    public async Task ProviderCancellation_IsPropagatedWithoutCacheFiles()
    {
        using var cancelled = new CancellationTokenSource();
        var provider = new FakeEmbeddingProvider
        {
            Generate = (_, token) =>
            {
                Assert.AreEqual(cancelled.Token, token);
                cancelled.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<float[]>>([]);
            }
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() => new DenseEmbeddingCache(root).GetAsync(["question"], "model", provider, cancelled.Token));
        Assert.AreEqual(0, Directory.GetFiles(root).Length);
    }

    [TestMethod]
    public async Task ProviderReturningAfterCancellation_DoesNotPublishVectors()
    {
        using var cancelled = new CancellationTokenSource();
        var provider = new FakeEmbeddingProvider
        {
            Generate = (_, _) =>
            {
                cancelled.Cancel();
                return Task.FromResult<IReadOnlyList<float[]>>([[1, 0]]);
            }
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() => new DenseEmbeddingCache(root).GetAsync(["question"], "model", provider, cancelled.Token));
        Assert.AreEqual(0, Directory.GetFiles(root).Length);
    }

    [TestMethod]
    public async Task LargeCorpus_UsesBoundedBatchesAndReusesEveryGeneratedVector()
    {
        var texts = Enumerable.Range(0, 67).Select(index => "text-" + index).ToArray();
        var provider = new FakeEmbeddingProvider();
        var first = new DenseEmbeddingCache(root);
        await first.GetAsync(texts, "model", provider, default);
        CollectionAssert.AreEqual(new[] { 32, 32, 3 }, provider.Batches.Select(batch => batch.Length).ToArray());
        Assert.AreEqual(67, first.GeneratedVectors);
        var second = new DenseEmbeddingCache(root);
        await second.GetAsync(texts.Reverse(), "model", provider, default);
        Assert.AreEqual(3, provider.Calls);
        Assert.AreEqual(67, second.CacheHits);
    }

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public int Dimensions { get; init; } = 2;
        public int Calls { get; private set; }
        public List<string[]> Batches { get; } = [];
        public Func<string[], CancellationToken, Task<IReadOnlyList<float[]>>> Generate { get; init; }
            = (texts, _) => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 1f, 0f }).ToArray());
        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("The cache must use the batch embedding API.");
        public Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            Calls++;
            var batch = texts.ToArray();
            Batches.Add(batch);
            return Generate(batch, cancellationToken);
        }
    }
}
