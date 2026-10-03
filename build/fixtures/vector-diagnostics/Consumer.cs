using LegacyDiagnosticsFixture;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Diagnostics;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

// This intentional compatibility probe is the only use of the obsolete name in this consumer.
#pragma warning disable CS0618
foreach (Probe probe in new Probe[] { new ExplicitStore(), new ImplicitStore() })
{
    var legacy = (IRagDiagnosticsStore)probe;
    var current = (IVectorStoreDiagnostics)probe;
    using var cancellation = new CancellationTokenSource();
    var vector = new[] { 1f, 0f };
    Require((await current.ListAllRecordsAsync(cancellation.Token)).Single().Id == "legacy", "New list bridge");
    Require(probe.LastToken == cancellation.Token, "List cancellation forwarding");
    Require((await current.ScoredListAsync(vector, cancellation.Token)).Single().Score == 0.75, "New score bridge");
    Require(probe.LastToken == cancellation.Token && ReferenceEquals(probe.LastVector, vector), "Score argument forwarding");
    Require((await OldCaller.Read(legacy, cancellation.Token)).Single().Id == "legacy", "Precompiled old list call");
    Require((await OldCaller.Score(legacy, vector, cancellation.Token)).Single().Score == 0.75, "Precompiled old score call");
    cancellation.Cancel();
    await ExpectCancellation(() => current.ListAllRecordsAsync(cancellation.Token), cancellation.Token);
    await ExpectCancellation(() => current.ScoredListAsync(vector, cancellation.Token), cancellation.Token);
}

using var memory = new InMemoryVectorStore();
Require(memory is IVectorStoreDiagnostics, "InMemory uses the new contract");
Require((object)memory is not IRagDiagnosticsStore, "InMemory intentionally no longer implements the legacy contract");
Require(!typeof(InMemoryVectorStore).Assembly.GetReferencedAssemblies().Any(a =>
    a.Name!.StartsWith("Mythosia.AI.Rag", StringComparison.Ordinal)), "InMemory assembly must not reference RAG");
#pragma warning restore CS0618
var mixed = new MixedStore();
var rag = await RagStore.BuildAsync(builder => builder.UseStore(mixed).UseLocalEmbedding(32));
var analysis = new RagDiagnostics(rag);
Require((await analysis.FindChunksContainingAsync("parking")).Single().Record.Id == "legacy", "Precompiled mixed-provider listing");
Require((await analysis.DiagnoseQueryAsync("parking", "parking")).TargetChunkInfo!.Score == 0.75, "Precompiled mixed-provider scoring");
Require((await rag.Diagnose().HealthCheckAsync()).TotalChunks == 1, "Precompiled mixed-provider health");
Require((await rag.Diagnose().WhyMissingAsync("parking", "parking")).QueryDetail!.TotalChunks == 1, "Precompiled mixed-provider missing analysis");
using var canceled = new CancellationTokenSource();
canceled.Cancel();
await ExpectCancellation(() => analysis.FindChunksContainingAsync("parking", canceled.Token), canceled.Token);
await ExpectCancellation(() => rag.Diagnose().HealthCheckAsync(canceled.Token), canceled.Token);
Console.WriteLine("Precompiled 6.4.0 implicit/explicit/mixed diagnostic providers and old callers passed with the new packages.");

static void Require(bool condition, string operation)
{
    if (!condition) throw new InvalidOperationException(operation);
}

static async Task ExpectCancellation(Func<Task> action, CancellationToken token)
{
    try { await action(); }
    catch (OperationCanceledException ex) when (ex.CancellationToken == token) { return; }
    throw new InvalidOperationException("Cancellation was not preserved by the legacy bridge.");
}
