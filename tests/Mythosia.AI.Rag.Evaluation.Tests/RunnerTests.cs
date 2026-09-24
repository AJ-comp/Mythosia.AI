using System.Text.Json;
using Mythosia.AI.Rag.Evaluation;
using Mythosia.VectorDb;

namespace Mythosia.AI.Rag.Evaluation.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RunnerTests
{
    private string root = "";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [TestInitialize]
    public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "Mythosia.Evaluation.RunnerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var prefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mythosia.Evaluation.RunnerTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(root).StartsWith(prefix, StringComparison.Ordinal) && Directory.Exists(root)) Directory.Delete(root, true);
    }

    [TestMethod]
    public async Task ExistingReport_IsNotOverwritten()
    {
        var options = Options();
        await EvaluationRunner.RunAsync(options);
        var original = File.ReadAllBytes(Path.Combine(options.Output, "results.json"));
        await Assert.ThrowsExactlyAsync<IOException>(() => EvaluationRunner.RunAsync(options));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(options.Output, "results.json")));
    }

    [TestMethod]
    public async Task ChangedRetrievalSettings_CannotPassBaselineGate()
    {
        var original = Options();
        await EvaluationRunner.RunAsync(original);
        var changed = original with { Output = NewOutput(), ChunkOverlap = 1, Baseline = Path.Combine(original.Output, "results.json") };
        var result = await EvaluationRunner.RunAsync(changed);
        Assert.IsFalse(result.Passed);
        using var report = Report(changed);
        Assert.IsFalse(report.RootElement.GetProperty("regression").GetProperty("isCompatible").GetBoolean());
    }

    [TestMethod]
    public async Task UnchangedConditions_PassBaselineGateDespiteDifferentRepetitions()
    {
        var original = Options();
        await EvaluationRunner.RunAsync(original);
        var repeated = original with { Output = NewOutput(), Warmup = 2, Repeat = 3, Baseline = Path.Combine(original.Output, "results.json") };
        var result = await EvaluationRunner.RunAsync(repeated);
        Assert.IsTrue(result.Passed);
    }

    [TestMethod]
    public async Task CustomAdapter_WarmupsDoNotAffectQuality_AndAllMeasuredRepeatsAreRecorded()
    {
        var options = Options() with { Methods = ["custom"], Warmup = 2, Repeat = 3 };
        RecordingMethod? adapter = null;
        await EvaluationRunner.RunAsync(options, registry => registry.Register("custom", context => adapter = new(context,
            (self, _) => [Hit(self.Context, self.SearchCalls == 3 ? "a" : "b")])));
        Assert.IsNotNull(adapter);
        Assert.AreEqual(5, adapter.SearchCalls);
        Assert.AreEqual(1, adapter.InitializeCalls);
        Assert.AreEqual(1, adapter.DisposeCalls);
        using var report = Report(options);
        var method = report.RootElement.GetProperty("methods")[0];
        Assert.AreEqual(1d, method.GetProperty("quality").GetProperty("recallAtK").GetDouble());
        Assert.AreEqual(1, method.GetProperty("quality").GetProperty("caseCount").GetInt32());
        Assert.IsFalse(method.GetProperty("rankingsStableAcrossRepeats").GetBoolean());
        var rows = report.RootElement.GetProperty("measurements").EnumerateArray().ToArray();
        Assert.AreEqual(3, rows.Length);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(row => row.GetProperty("iteration").GetInt32()).ToArray());
        Assert.AreEqual(0d, rows[1].GetProperty("metrics").GetProperty("recallAtK").GetDouble());
    }

    [TestMethod]
    public async Task AdapterReturningAnotherTenant_IsRejectedAndDisposed()
    {
        var options = Options(ValidDataset() with
        {
            Cases = [new() { Id = "q", Query = "alpha", Filter = new() { ["tenant"] = "A" }, Judgments = new() { ["a"] = 1 } }]
        }) with { Methods = ["leaky"] };
        RecordingMethod? adapter = null;
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => EvaluationRunner.RunAsync(options,
            registry => registry.Register("leaky", context => adapter = new(context, (self, _) => [Hit(self.Context, "b")]))));
        StringAssert.Contains(error.Message, "outside the query filter");
        Assert.AreEqual(1, adapter!.DisposeCalls);
        Assert.IsFalse(File.Exists(Path.Combine(options.Output, "results.json")));
    }

    [TestMethod]
    public async Task AdapterReturningInvalidScores_IsRejected()
    {
        var options = Options() with { Methods = ["bad-score"] };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => EvaluationRunner.RunAsync(options,
            registry => registry.Register("bad-score", context => new RecordingMethod(context,
                (self, _) => [new VectorSearchResult(Hit(self.Context, "a").Record, double.NaN)]))));
    }

    [TestMethod]
    public async Task AdapterExceedingCandidateBudget_IsRejected()
    {
        var options = Options() with { Methods = ["too-many"], CandidateLimit = 1, RecallK = 1, RankK = 1 };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => EvaluationRunner.RunAsync(options,
            registry => registry.Register("too-many", context => new RecordingMethod(context,
                (self, _) => [Hit(self.Context, "a"), Hit(self.Context, "b")]))));
    }

    [TestMethod]
    public async Task CancellationDuringAdapterInitialization_DisposesTheAdapter()
    {
        var options = Options() with { Methods = ["cancelled"] };
        using var cancelled = new CancellationTokenSource();
        RecordingMethod? adapter = null;
        await Assert.ThrowsAsync<OperationCanceledException>(() => EvaluationRunner.RunAsync(options,
            registry => registry.Register("cancelled", context => adapter = new(context, (_, _) => [])
            {
                InitializeAction = () => { cancelled.Cancel(); cancelled.Token.ThrowIfCancellationRequested(); }
            }), cancelled.Token));
        Assert.AreEqual(1, adapter!.DisposeCalls);
        Assert.IsFalse(File.Exists(Path.Combine(options.Output, "results.json")));
    }

    [TestMethod]
    public async Task EveryAdapterIsDisposed_EvenWhenAnotherDisposeThrows()
    {
        var options = Options() with { Methods = ["first", "second"] };
        RecordingMethod? first = null;
        RecordingMethod? second = null;
        var failed = false;
        try
        {
            await EvaluationRunner.RunAsync(options, registry =>
            {
                registry.Register("first", context => first = new(context, (self, _) => [Hit(self.Context, "a")]));
                registry.Register("second", context => second = new(context, (self, _) => [Hit(self.Context, "a")]) { ThrowOnDispose = true });
            });
        }
        catch (Exception) { failed = true; }
        Assert.IsTrue(failed, "Disposal failures must not be silently ignored.");
        Assert.AreEqual(1, second!.DisposeCalls);
        Assert.AreEqual(1, first!.DisposeCalls, "A failing adapter must not prevent other adapters from being disposed.");
        Assert.IsFalse(File.Exists(Path.Combine(options.Output, "results.json")), "Failed cleanup must not leave a successful run report.");
    }

    [TestMethod]
    public async Task ReservedDocumentIdMetadata_IsRejectedInsteadOfSilentlyChangingFilters()
    {
        var dataset = ValidDataset() with
        {
            Documents = [new() { Id = "a", Text = "alpha cancellation", Metadata = new() { ["document_id"] = "external-business-id" } }],
            Cases = [new() { Id = "q", Query = "alpha", Judgments = new() { ["a"] = 1 }, Filter = new() { ["document_id"] = "external-business-id" } }]
        };
        var options = Options(dataset);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => EvaluationRunner.RunAsync(options));
        Assert.IsFalse(Directory.Exists(options.Output));
    }

    [TestMethod]
    public async Task DenseAdapterWithoutDenseConfiguration_FailsBeforeProducingReports()
    {
        var options = Options() with { Methods = ["custom-dense"], Dense = "none" };
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => EvaluationRunner.RunAsync(options,
            registry => registry.Register("custom-dense", context => new RecordingMethod(context, (_, _) => []), requiresDense: true)));
        Assert.IsFalse(Directory.Exists(options.Output));
    }

    [TestMethod]
    public async Task AdapterCannotAccessJudgments_AndContextCopiesProtectAnotherAdapter()
    {
        var options = Options() with { Methods = ["mutating", "control"] };
        Assert.IsNull(typeof(EvaluationQuery).GetProperty("Judgments"));
        var controlReceivedOriginalRecords = false;
        await EvaluationRunner.RunAsync(options, registry =>
        {
            registry.Register("mutating", context => new RecordingMethod(context, (self, query) =>
            {
                var copy = self.Context.Records.First();
                copy.Content = "changed by another adapter";
                copy.Metadata.Clear();
                self.Context.Options.Methods[0] = "changed";
                return [Hit(self.Context, "b")];
            }));
            registry.Register("control", context => new RecordingMethod(context, (self, query) =>
            {
                controlReceivedOriginalRecords = self.Context.Records.First().Content == "alpha cancellation token"
                    && self.Context.Options.Methods[0] == "mutating";
                return [Hit(self.Context, "a")];
            }));
        });
        Assert.IsTrue(controlReceivedOriginalRecords, "One adapter must not change another adapter's corpus or options.");
        using var report = Report(options);
        var methods = report.RootElement.GetProperty("methods").EnumerateArray().ToDictionary(method => method.GetProperty("id").GetString()!);
        Assert.AreEqual(0d, methods["mutating"].GetProperty("quality").GetProperty("recallAtK").GetDouble(), "Adapter changes must not rewrite the evaluation's ground truth.");
        Assert.AreEqual(1d, methods["control"].GetProperty("quality").GetProperty("recallAtK").GetDouble());
    }

    [TestMethod]
    public async Task LongCorpus_UsesBoundedChunksAndRecordsActualSegmentation()
    {
        var text = string.Join(" ", Enumerable.Range(0, 60).Select(i => $"word{i:D2}"));
        var dataset = ValidDataset() with { Documents = [new() { Id = "a", Text = text }] };
        var options = Options(dataset) with { ChunkSize = 20, ChunkOverlap = 0 };
        await EvaluationRunner.RunAsync(options);
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.Output, "corpus.json")));
        var chunks = corpus.RootElement.GetProperty("records").EnumerateArray()
            .Select(r => r.GetProperty("content").GetString()!).ToArray();
        Assert.IsTrue(chunks.Length > 1 && chunks.All(c => c.Length <= 20));
        Assert.AreEqual(text, string.Join(" ", chunks));
        using var report = Report(options);
        Assert.AreEqual(chunks.Max(c => c.Length), report.RootElement.GetProperty("maxChunkLength").GetInt32());
        Assert.AreEqual(64, report.RootElement.GetProperty("chunkFingerprint").GetString()!.Length);

        var changed = options with { ChunkSize = 28, Output = NewOutput() };
        await EvaluationRunner.RunAsync(changed);
        using var changedReport = Report(changed);
        Assert.AreNotEqual(report.RootElement.GetProperty("chunkFingerprint").GetString(),
            changedReport.RootElement.GetProperty("chunkFingerprint").GetString());
    }

    private EvaluationOptions Options(EvaluationDataset? dataset = null)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(dataset ?? ValidDataset(), Json));
        return new() { Root = root, Dataset = path, Output = NewOutput(), EmbeddingCache = Path.Combine(root, "cache"), Methods = ["bm25"], Warmup = 0 };
    }

    private string NewOutput() => Path.Combine(root, "run-" + Guid.NewGuid().ToString("N"));
    private static JsonDocument Report(EvaluationOptions options) => JsonDocument.Parse(File.ReadAllText(Path.Combine(options.Output, "results.json")));
    private static EvaluationDataset ValidDataset() => new()
    {
        Id = "runner-fixture", Version = "1",
        Documents =
        [
            new() { Id = "a", Text = "alpha cancellation token", Metadata = new() { ["tenant"] = "A" } },
            new() { Id = "b", Text = "beta image generation", Metadata = new() { ["tenant"] = "B" } }
        ],
        Cases = [new() { Id = "q", Query = "alpha", Judgments = new() { ["a"] = 1 } }]
    };
    private static VectorSearchResult Hit(EvaluationContext context, string document)
        => new(context.Records.First(record => record.Metadata[EvaluationRunner.DocumentMetadataKey] == document), 1);

    private sealed class RecordingMethod(EvaluationContext context, Func<RecordingMethod, EvaluationQuery, IReadOnlyList<VectorSearchResult>> search) : IEvaluationMethod
    {
        public EvaluationContext Context { get; } = context;
        public string Identity => "deterministic-test-adapter/v1";
        public int InitializeCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool ThrowOnDispose { get; init; }
        public Action? InitializeAction { get; init; }
        public Task InitializeAsync(CancellationToken cancellationToken) { InitializeCalls++; InitializeAction?.Invoke(); return Task.CompletedTask; }
        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(EvaluationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchCalls++;
            return Task.FromResult(search(this, query));
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            if (ThrowOnDispose) throw new IOException("Deliberate disposal failure.");
            return ValueTask.CompletedTask;
        }
    }
}
