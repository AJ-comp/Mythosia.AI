using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.VectorDb;

namespace Mythosia.AI.Rag.Evaluation;

public sealed record EvaluationHit(string ChunkId, string DocumentId, double Score);
public sealed record EvaluationMeasurement(string Method, string CaseId, string Category, string? Language,
    string Query, int Iteration, double Milliseconds, CaseMetrics Metrics, string[] Documents, EvaluationHit[] Hits);
public sealed record EvaluationMethodSummary(string Id, string Identity, MetricSummary Quality,
    IReadOnlyDictionary<string, MetricSummary> Categories, double MedianMilliseconds, double P95Milliseconds, bool RankingsStableAcrossRepeats);
public sealed record EvaluationRunResult(string OutputDirectory, bool Passed, string CompatibilityFingerprint);

public static class EvaluationRunner
{
    public const string DocumentMetadataKey = "__evaluation_document_id";
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        // Fingerprints must not depend on the operating system's JSON formatting.
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<EvaluationRunResult> RunAsync(EvaluationOptions options,
        Action<EvaluationMethodRegistry>? configureMethods = null, CancellationToken cancellationToken = default)
    {
        options.Validate();
        var registry = EvaluationMethodRegistry.CreateDefault();
        configureMethods?.Invoke(registry);
        var needsDense = options.Methods.Select(registry.RequiresDense).Any(value => value);
        if (needsDense && options.Dense == "none") throw new ArgumentException("Selected methods require --dense local-hash or openai.");
        var loaded = DatasetLoader.Load(options.Dataset, options.DataRoot ?? options.Root);
        if (loaded.Documents.Any(d => d.Metadata.ContainsKey(DocumentMetadataKey) || d.Metadata.ContainsKey("document_id")))
            throw new InvalidDataException($"Metadata keys {DocumentMetadataKey} and document_id are reserved for evaluation.");
        MetricBaseline? baseline = null;
        if (options.Baseline != null)
        {
            using var baselineJson = JsonDocument.Parse(await File.ReadAllTextAsync(options.Baseline, cancellationToken));
            if (!baselineJson.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 2
                || !baselineJson.RootElement.TryGetProperty("metricsBaseline", out var metrics))
                throw new InvalidDataException("Baseline must be a schemaVersion 2 evaluation results.json. Legacy PIXIE reports are historical results, not compatible baselines.");
            baseline = metrics.Deserialize<MetricBaseline>(Json) ?? throw new InvalidDataException("Empty baseline metrics.");
        }
        Directory.CreateDirectory(options.Output);
        if (Directory.EnumerateFileSystemEntries(options.Output).Any()) throw new IOException("Output directory is not empty; use a new run directory to preserve history.");
        // CreateNew also rejects concurrent writers to the same evaluation run.
        await using var marker = new FileStream(Path.Combine(options.Output, "run.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var records = new List<VectorRecord>();
        var splitter = new RecursiveTextSplitter(options.ChunkSize, options.ChunkOverlap);
        foreach (var document in loaded.Documents)
        {
            var chunkNumber = 0;
            foreach (var chunk in splitter.Split(new RagDocument(document.Id, document.Text, document.Id)))
            {
                var metadata = new Dictionary<string, string>(document.Metadata) { [DocumentMetadataKey] = document.Id, ["document_id"] = document.Id };
                records.Add(new VectorRecord(DenseEmbeddingCache.Hash(document.Id) + ":" + chunkNumber++, [], chunk.Content) { Metadata = metadata });
            }
        }
        if (records.Count == 0) throw new InvalidDataException("Dataset produces no searchable chunks.");
        var denseIdentity = !needsDense ? "disabled" : options.Dense == "local-hash"
            ? "LocalEmbeddingProvider/feature-hashing/1024" : $"OpenAI/{options.EmbeddingModel}/{options.Dimensions}";
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var embeddingWatch = Stopwatch.StartNew();
        var cache = new DenseEmbeddingCache(options.EmbeddingCache);
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        if (needsDense)
        {
            IEmbeddingProvider provider = options.Dense == "local-hash" ? new LocalEmbeddingProvider(1024)
                : new OpenAIEmbeddingProvider(Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException("OPENAI_API_KEY is required for OpenAI mode."), client, options.EmbeddingModel, options.Dimensions);
            vectors = await cache.GetAsync(records.Select(r => r.Content).Concat(loaded.Dataset.Cases.Select(c => c.Query)), denseIdentity, provider, cancellationToken);
            foreach (var record in records) record.Vector = vectors[record.Content].ToArray();
        }
        embeddingWatch.Stop();
        // Include the actual segmentation even for sparse-only runs, where no dense
        // vector texts are available to distinguish a changed splitter implementation.
        var chunkFingerprint = DenseEmbeddingCache.Hash(JsonSerializer.Serialize(
            records.Select(r => new { r.Id, r.Content }), Json));
        var fingerprint = DenseEmbeddingCache.Hash(JsonSerializer.Serialize(new
        {
            metricContract = "document-graded-v1/first-measured-quality", dataset = loaded.Fingerprint,
            chunkFingerprint,
            options.ChunkSize, options.ChunkOverlap, options.CandidateLimit, options.RecallK, options.RankK,
            options.VectorWeight, options.CandidateMultiplier, options.RrfK, denseIdentity,
            vectors = vectors.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { textHash = DenseEmbeddingCache.Hash(p.Key), values = p.Value })
        }, Json));
        // Corpus snapshots include exact texts/vectors for local reproducibility. Credentials are never included.
        await WriteJsonAsync(options.Output, "corpus.json", new { loaded.Dataset, loaded.Fingerprint, records, denseIdentity, queryVectors = loaded.Dataset.Cases.Select(c => new { c.Id, vector = vectors.GetValueOrDefault(c.Query, []) }) }, cancellationToken);
        using var context = new EvaluationContext(options, records, loaded.Dataset.Cases.ToDictionary(c => c.Id, c => vectors.GetValueOrDefault(c.Query, []), StringComparer.Ordinal));
        var methods = new List<(string Id, IEvaluationMethod Method)>();
        var measurements = new List<EvaluationMeasurement>();
        Exception? executionFailure = null;
        try
        {
            foreach (var id in options.Methods)
            {
                var method = registry.Create(id, context);
                methods.Add((id, method));
                Console.WriteLine($"Preparing {id} for {records.Count} chunks...");
                await method.InitializeAsync(cancellationToken);
                for (var warmup = 0; warmup < options.Warmup; warmup++)
                    await method.SearchAsync(EvaluationQuery.From(loaded.Dataset.Cases[0]), cancellationToken);
            }
            var validDocuments = loaded.Documents.ToDictionary(d => d.Id, StringComparer.Ordinal);
            var metricOptions = new EvaluationMetricOptions(options.RecallK, options.RankK);
            for (var iteration = 0; iteration < options.Repeat; iteration++)
            {
                for (var caseIndex = 0; caseIndex < loaded.Dataset.Cases.Count; caseIndex++)
                {
                    var query = loaded.Dataset.Cases[caseIndex];
                    // Rotate method order to reduce a fixed first/last-method timing advantage.
                    for (var offset = 0; offset < methods.Count; offset++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var method = methods[(offset + caseIndex + iteration) % methods.Count];
                        var watch = Stopwatch.StartNew();
                        var hits = await method.Method.SearchAsync(EvaluationQuery.From(query), cancellationToken);
                        watch.Stop();
                        if (hits.Count > options.CandidateLimit) throw new InvalidDataException($"{method.Id} exceeded the candidate limit.");
                        var normalized = hits.Select(hit =>
                        {
                            if (!double.IsFinite(hit.Score) || !hit.Record.Metadata.TryGetValue(DocumentMetadataKey, out var id) || !validDocuments.TryGetValue(id, out var doc))
                                throw new InvalidDataException($"{method.Id} returned an invalid score or unknown document.");
                            if (query.Filter != null && query.Filter.Any(p => !doc.Metadata.TryGetValue(p.Key, out var value) || value != p.Value))
                                throw new InvalidDataException($"{method.Id} returned a document outside the query filter: {id}");
                            return new EvaluationHit(hit.Record.Id, id, hit.Score);
                        }).ToArray();
                        var ranked = normalized.Select(h => new RankedDocument(h.DocumentId, h.Score)).ToArray();
                        var scores = RetrievalMetrics.Evaluate(query, ranked, metricOptions);
                        measurements.Add(new(method.Id, query.Id, query.Category, query.Language, query.Query, iteration,
                            watch.Elapsed.TotalMilliseconds, scores, ranked.Select(h => h.DocumentId).Distinct(StringComparer.Ordinal).ToArray(), normalized));
                    }
                }
            }
            var summaries = methods.Select(method =>
            {
                var samples = measurements.Where(m => m.Method == method.Id).ToArray();
                var first = samples.Where(m => m.Iteration == 0).Select(m => new CaseEvaluation(m.CaseId, m.Category, m.Language, m.Metrics)).ToArray();
                return new EvaluationMethodSummary(method.Id, method.Method.Identity, RetrievalMetrics.Aggregate(first),
                    RetrievalMetrics.AggregateByCategory(first), Percentile(samples.Select(s => s.Milliseconds), .5), Percentile(samples.Select(s => s.Milliseconds), .95),
                    samples.GroupBy(s => s.CaseId).All(g => g.All(s => s.Documents.SequenceEqual(g.First().Documents))));
            }).ToArray();
            // A completed report must also mean adapter cleanup succeeded.
            var cleanupFailures = new List<Exception>();
            foreach (var method in methods.AsEnumerable().Reverse())
                try { await method.Method.DisposeAsync(); }
                catch (Exception error) { cleanupFailures.Add(error); }
            methods.Clear();
            if (cleanupFailures.Count > 0) throw new AggregateException("Evaluation adapter cleanup failed.", cleanupFailures);
            var current = new MetricBaseline { CompatibilityFingerprint = fingerprint, Methods = summaries.ToDictionary(s => s.Id, s => s.Quality, StringComparer.Ordinal) };
            var regression = baseline == null ? null : RegressionComparer.Compare(baseline, current, options.MaxRegression);
            var report = new
            {
                schemaVersion = 2, timestampUtc = DateTimeOffset.UtcNow, datasetId = loaded.Dataset.Id, datasetVersion = loaded.Dataset.Version,
                description = loaded.Dataset.Description, datasetFingerprint = loaded.Fingerprint, compatibilityFingerprint = fingerprint,
                sourceRevision = GitRevision(options.Root), implementationHashes = ImplementationHashes(),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, options,
                documents = loaded.Documents.Count, chunks = records.Count, queries = loaded.Dataset.Cases.Count,
                chunkFingerprint, maxChunkLength = records.Max(r => r.Content.Length),
                denseIdentity, densePreparationMilliseconds = embeddingWatch.Elapsed.TotalMilliseconds, cache.CacheHits, cache.GeneratedVectors,
                pixieModelManifest = options.Methods.Any(id => id is "pixie" or "hybrid-pixie")
                    ? JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(options.ModelDirectory, "manifest.json"), cancellationToken)) : (JsonElement?)null,
                preparationMilliseconds = context.PreparationMilliseconds,
                timingNote = "Warmups excluded. Includes sparse query encoding. Excludes cached dense generation/network. Repeats measure latency; quality uses first measured iteration only.",
                metricNote = "Document-level graded relevance; deduplicate chunks preserving rank. Unanswerable cases excluded from recall/MRR/nDCG and reported separately. Labels may be incomplete.",
                processWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
                memoryNote = "Whole process, all indexes/vectors/model/report objects; not isolated per method.",
                methods = summaries, measurements, metricsBaseline = current, regression,
                passed = regression?.Passed ?? true
            };
            await WriteJsonAsync(options.Output, "results.json", report, cancellationToken);
            var markdown = FormatSummary(loaded, options, denseIdentity, summaries, regression);
            await File.WriteAllTextAsync(Path.Combine(options.Output, "summary.md"), markdown, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(options.Output, "measurements.csv"), FormatCsv(measurements), cancellationToken);
            Console.WriteLine(markdown);
            return new(options.Output, regression?.Passed ?? true, fingerprint);
        }
        catch (Exception error) { executionFailure = error; throw; }
        finally
        {
            var cleanupErrors = new List<Exception>();
            foreach (var method in methods.AsEnumerable().Reverse())
                try { await method.Method.DisposeAsync(); }
                catch (Exception error) { cleanupErrors.Add(error); }
            if (cleanupErrors.Count > 0)
            {
                if (executionFailure != null) cleanupErrors.Insert(0, executionFailure);
                throw new AggregateException("Evaluation adapter cleanup failed.", cleanupErrors);
            }
        }
    }

    private static Task WriteJsonAsync(string directory, string filename, object value, CancellationToken ct)
        => File.WriteAllTextAsync(Path.Combine(directory, filename), JsonSerializer.Serialize(value, Json), ct);
    private static Dictionary<string, string> ImplementationHashes()
    {
        return new[] { typeof(EvaluationRunner), typeof(RecursiveTextSplitter), typeof(Mythosia.AI.Rag.Search.Pixie.PixieSparseEncoder), typeof(Mythosia.VectorDb.InMemory.InMemoryVectorStore), typeof(VectorRecord) }
            .Select(t => t.Assembly).Distinct().ToDictionary(a => a.GetName().Name!, a =>
            {
                using var file = File.OpenRead(a.Location);
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).ToLowerInvariant();
            });
    }
    private static double Percentile(IEnumerable<double> samples, double p)
    {
        var sorted = samples.Order().ToArray();
        return sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
    }
    private static string? GitRevision(string root)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git") { WorkingDirectory = root, ArgumentList = { "rev-parse", "HEAD" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process == null) return null;
            var result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? result : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
    private static string FormatSummary(LoadedDataset dataset, EvaluationOptions options, string dense, EvaluationMethodSummary[] summaries, RegressionComparison? regression)
    {
        var text = new StringBuilder("# Retrieval evaluation\n\n")
            .AppendLine($"Dataset: {Escape(dataset.Dataset.Id)} v{Escape(dataset.Dataset.Version)}. Documents: {dataset.Documents.Count}; queries: {dataset.Dataset.Cases.Count}.")
            .AppendLine().AppendLine(Escape(dataset.Dataset.Description)).AppendLine()
            .AppendLine($"Dense: {Escape(dense)}. Quality uses the first measured iteration; latency uses {options.Repeat} iteration(s).")
            .AppendLine("Local-hash is a deterministic wiring check, not neural semantic retrieval. BM25 is InMemory Lucene BM25, not PostgreSQL search.")
            .AppendLine("Search timings exclude dense embedding API/network time. No-answer rate measures returned results, not answer generation.")
            .AppendLine().AppendLine($"| Method | Recall@{options.RecallK} | MRR@{options.RankK} | nDCG@{options.RankK} | No-answer FP | Median ms | P95 ms |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var s in summaries) text.AppendLine($"| {Escape(s.Id)} | {F(s.Quality.RecallAtK)} | {F(s.Quality.MrrAtK)} | {F(s.Quality.NdcgAtK)} | {F(s.Quality.NoAnswerFalsePositiveRate)} | {F(s.MedianMilliseconds)} | {F(s.P95Milliseconds)} |");
        text.AppendLine().AppendLine("## Categories").AppendLine().AppendLine("| Method | Category | Cases | Recall | nDCG | No-answer FP |").AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var s in summaries) foreach (var category in s.Categories)
            text.AppendLine($"| {Escape(s.Id)} | {Escape(category.Key)} | {category.Value.CaseCount} | {F(category.Value.RecallAtK)} | {F(category.Value.NdcgAtK)} | {F(category.Value.NoAnswerFalsePositiveRate)} |");
        if (regression != null)
        {
            text.AppendLine().AppendLine($"Baseline comparison: **{(regression.Passed ? "PASS" : "FAIL")}**. Absolute metric tolerance: {F(options.MaxRegression)}.");
            if (regression.IncompatibilityReason != null) text.AppendLine(Escape(regression.IncompatibilityReason));
            foreach (var difference in regression.Differences.Where(d => !d.Passed)) text.AppendLine($"- {Escape(difference.Method)} / {Escape(difference.Metric)}: {F(difference.Baseline)} → {F(difference.Current)}");
        }
        if (summaries.Any(s => !s.RankingsStableAcrossRepeats)) text.AppendLine().AppendLine("Some rankings changed between repeats; inspect measurements before interpreting latency or quality.");
        return text.AppendLine().AppendLine("See results.json for all rankings/configuration/category metrics, measurements.csv for tabular analysis, and corpus.json for the exact corpus and shared vectors. These artifacts can contain the evaluated documents.").ToString();
    }
    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string F(double? value) => value?.ToString("F4", CultureInfo.InvariantCulture) ?? "n/a";
    private static string FormatCsv(IEnumerable<EvaluationMeasurement> measurements)
    {
        // Quote cells and neutralize spreadsheet formulas in dataset-controlled strings.
        string Cell(string value) => "\"" + ((value.Length > 0 && "=+-@\t\r".Contains(value[0])) ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
        var text = new StringBuilder("method,case_id,category,iteration,recall,mrr,ndcg,milliseconds\n");
        foreach (var m in measurements) text.AppendLine(string.Join(",", Cell(m.Method), Cell(m.CaseId), Cell(m.Category), m.Iteration.ToString(CultureInfo.InvariantCulture), F(m.Metrics.RecallAtK), F(m.Metrics.ReciprocalRank), F(m.Metrics.NdcgAtK), F(m.Milliseconds)));
        return text.ToString();
    }
}

public static class EvaluationCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Retrieval evaluation: --dataset path --methods bm25,pixie,dense,hybrid-bm25,hybrid-pixie --dense none|local-hash|openai --output NEW_DIRECTORY [--baseline results.json --max-regression 0.02]. See tests/Mythosia.AI.Rag.Evaluation/README.md for datasets, cache, metrics and adapters.");
            return 0;
        }
        using var cancelled = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancelled.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var result = await EvaluationRunner.RunAsync(EvaluationOptions.Parse(args), cancellationToken: cancelled.Token);
            Console.WriteLine($"Report: {Path.Combine(result.OutputDirectory, "summary.md")}");
            return result.Passed ? 0 : 2;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Evaluation cancelled."); return 130; }
        catch (Exception error) { Console.Error.WriteLine($"Evaluation failed: {error.Message}"); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }
}
