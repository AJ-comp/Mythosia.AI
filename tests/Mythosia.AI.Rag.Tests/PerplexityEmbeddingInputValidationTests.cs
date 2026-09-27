using System.Collections;
using System.Text.Json.Nodes;
using Mythosia.AI.Rag.Embeddings;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class PerplexityEmbeddingInputValidationTests
{
    [TestMethod]
    [DataRow("standard-float")]
    [DataRow("standard-binary")]
    [DataRow("context-flat")]
    public async Task FlatInput_StopsAtFirstExcessTextWithoutReadingIt(string mode)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        var texts = new ObservedSequence<string>(call => call <= 20000, call =>
        {
            Assert.IsTrue(call <= 512, "The first excess text must not be read.");
            return "text " + call;
        });

        await Assert.ThrowsAsync<ArgumentException>(() => CallFlat(mode, http, texts));

        Assert.AreEqual(513, texts.MoveNextCalls);
        Assert.AreEqual(512, texts.CurrentReads);
        Assert.IsTrue(texts.WasDisposed);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GroupedInput_StopsAtFirstExcessDocumentWithoutReadingIt(bool binary)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        var documents = new ObservedSequence<IEnumerable<string>>(call => call <= 20000, call =>
        {
            Assert.IsTrue(call <= 512, "The first excess document must not be read.");
            return new[] { "document " + call };
        });

        await Assert.ThrowsAsync<ArgumentException>(() => CallGrouped(binary, http, documents));

        Assert.AreEqual(513, documents.MoveNextCalls);
        Assert.AreEqual(512, documents.CurrentReads);
        Assert.IsTrue(documents.WasDisposed);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 8000)]
    [DataRow(true, 8000)]
    [DataRow(false, 15999)]
    [DataRow(true, 15999)]
    [DataRow(false, 16000)]
    [DataRow(true, 16000)]
    public async Task GroupedInput_EnforcesRemainingChunkBudgetBeforeReadingExcessChunk(bool binary, int precedingChunks)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        var remaining = 16000 - precedingChunks;
        var chunks = new ObservedSequence<string>(call => call <= 20000, call =>
        {
            Assert.IsTrue(call <= remaining, "Chunks beyond the remaining request budget must not be read.");
            return "chunk " + call;
        });
        var groups = precedingChunks == 0
            ? new IEnumerable<string>[] { chunks }
            : new IEnumerable<string>[] { Enumerable.Repeat("preceding", precedingChunks), chunks };
        var documents = new ObservedSequence<IEnumerable<string>>(call => call <= groups.Length, call => groups[call - 1]);

        await Assert.ThrowsAsync<ArgumentException>(() => CallGrouped(binary, http, documents));

        Assert.IsTrue(chunks.MoveNextCalls <= remaining + 1);
        Assert.AreEqual(remaining, chunks.CurrentReads);
        Assert.IsTrue(chunks.GetEnumeratorCalls == 0 || chunks.WasDisposed);
        Assert.IsTrue(documents.WasDisposed);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.IsEmpty(handler.Bodies);
    }

    public static IEnumerable<object[]> FlatCancellationCases()
    {
        foreach (var mode in new[] { "standard-float", "standard-binary", "context-flat" })
        foreach (var stage in new[] { "GetEnumerator", "MoveNext", "Current", "Dispose" })
            yield return new object[] { mode, stage, false };
        foreach (var mode in new[] { "standard-float", "standard-binary", "context-flat" })
        foreach (var stage in new[] { "GetEnumerator", "MoveNext", "Dispose" })
            yield return new object[] { mode, stage, true };
    }

    [TestMethod]
    [DynamicData(nameof(FlatCancellationCases))]
    public async Task FlatInput_PropagatesEnumerationCancellationWithoutFurtherReadsOrHttp(string mode, string stage, bool empty)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var texts = CancelingSequence(cancellation, stage, empty, "text");

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => CallFlat(mode, http, texts, cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        AssertCancellationReads(texts, stage, empty);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.IsEmpty(handler.Bodies);
    }

    public static IEnumerable<object[]> NestedCancellationCases()
    {
        foreach (var binary in new[] { false, true })
        foreach (var outer in new[] { false, true })
        foreach (var stage in new[] { "GetEnumerator", "MoveNext", "Current", "Dispose" })
            yield return new object[] { binary, outer, stage, false };
        foreach (var binary in new[] { false, true })
        foreach (var outer in new[] { false, true })
        foreach (var stage in new[] { "GetEnumerator", "MoveNext", "Dispose" })
            yield return new object[] { binary, outer, stage, true };
    }

    [TestMethod]
    [DynamicData(nameof(NestedCancellationCases))]
    public async Task GroupedInput_PropagatesOuterAndInnerCancellationAndDisposesEnumerators(bool binary, bool cancelOuter, string stage, bool empty)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var chunks = cancelOuter
            ? new ObservedSequence<string>(call => call <= 1, _ => "text")
            : CancelingSequence(cancellation, stage, empty, "text");
        var documents = cancelOuter
            ? CancelingSequence<IEnumerable<string>>(cancellation, stage, empty, chunks)
            : new ObservedSequence<IEnumerable<string>>(call => call <= 1, _ => chunks);

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => CallGrouped(binary, http, documents, cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsTrue(documents.WasDisposed);
        Assert.IsTrue(chunks.GetEnumeratorCalls == 0 || chunks.WasDisposed);
        if (cancelOuter)
        {
            AssertCancellationReads(documents, stage, empty);
            if (stage != "Dispose") Assert.AreEqual(0, chunks.GetEnumeratorCalls);
        }
        else AssertCancellationReads(chunks, stage, empty);
        Assert.AreEqual(0, handler.RequestCount);
        Assert.IsEmpty(handler.Bodies);
    }

    [TestMethod]
    [DataRow("standard-float")]
    [DataRow("standard-binary")]
    [DataRow("context-flat")]
    public async Task FlatInput_ExactLimitPreservesOrderAndBypassesCollectionFastPaths(string mode)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        var texts = Enumerable.Range(0, 512).Select(index => "text " + index).ToArray();

        await Assert.ThrowsAsync<RequestCapturedException>(() => CallFlat(mode, http, new EnumerationOnlyCollection<string>(texts)));

        Assert.HasCount(1, handler.Bodies);
        var input = handler.Bodies[0]["input"]!.AsArray();
        Assert.HasCount(512, input);
        for (var index = 0; index < texts.Length; index++)
            Assert.AreEqual(texts[index], (mode == "context-flat" ? input[index]![0] : input[index])!.GetValue<string>());
        if (mode == "context-flat") Assert.IsTrue(input.All(group => group!.AsArray().Count == 1));
        Assert.AreEqual(mode == "standard-binary" ? "base64_binary" : "base64_int8", handler.Bodies[0]["encoding_format"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false, 512, 1)]
    [DataRow(true, 512, 1)]
    [DataRow(false, 2, 8000)]
    [DataRow(true, 2, 8000)]
    [DataRow(false, 1, 16000)]
    [DataRow(true, 1, 16000)]
    public async Task GroupedInput_ExactLimitsPreserveDocumentAndChunkOrder(bool binary, int documentCount, int chunksPerDocument)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        var documents = Enumerable.Range(0, documentCount)
            .Select(document => (IEnumerable<string>)new EnumerationOnlyCollection<string>(Enumerable.Range(0, chunksPerDocument)
                .Select(chunk => $"document {document} chunk {chunk}").ToArray())).ToArray();

        await Assert.ThrowsAsync<RequestCapturedException>(() => CallGrouped(binary, http, new EnumerationOnlyCollection<IEnumerable<string>>(documents)));

        Assert.HasCount(1, handler.Bodies);
        var input = handler.Bodies[0]["input"]!.AsArray();
        Assert.HasCount(documentCount, input);
        for (var document = 0; document < documentCount; document++)
        {
            Assert.HasCount(chunksPerDocument, input[document]!.AsArray());
            for (var chunk = 0; chunk < chunksPerDocument; chunk++)
                Assert.AreEqual($"document {document} chunk {chunk}", input[document]![chunk]!.GetValue<string>());
        }
        Assert.AreEqual(binary ? "base64_binary" : "base64_int8", handler.Bodies[0]["encoding_format"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(16000)]
    [DataRow(16001)]
    public async Task RetrievalDocumentAdapter_UsesTheWholeDocumentChunkLimit(int chunkCount)
    {
        using var handler = new CaptureTransport();
        using var http = new HttpClient(handler);
        IRetrievalEmbeddingProvider provider = new PerplexityContextualizedEmbeddingProvider("key", http, dimensions: 128);
        var document = new EmbeddingDocument("document-id", Enumerable.Range(0, chunkCount).Select(index => "chunk " + index));

        if (chunkCount > 16000)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => provider.GetDocumentEmbeddingsAsync(document));
            Assert.AreEqual(0, handler.RequestCount);
            Assert.IsEmpty(handler.Bodies);
        }
        else
        {
            await Assert.ThrowsAsync<RequestCapturedException>(() => provider.GetDocumentEmbeddingsAsync(document));
            var groups = handler.Bodies.Single()["input"]!.AsArray();
            Assert.HasCount(1, groups);
            Assert.HasCount(chunkCount, groups[0]!.AsArray());
            Assert.AreEqual("chunk 0", groups[0]![0]!.GetValue<string>());
            Assert.AreEqual("chunk 15999", groups[0]![15999]!.GetValue<string>());
        }
    }

    private static Task CallFlat(string mode, HttpClient http, IEnumerable<string> texts, CancellationToken cancellationToken = default)
        => mode switch
        {
            "standard-float" => new PerplexityEmbeddingProvider("key", http, dimensions: 128).GetEmbeddingsAsync(texts, cancellationToken),
            "standard-binary" => new PerplexityEmbeddingProvider("key", http, dimensions: 128).GetBinaryEmbeddingsAsync(texts, cancellationToken),
            "context-flat" => new PerplexityContextualizedEmbeddingProvider("key", http, dimensions: 128).GetEmbeddingsAsync(texts, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static Task CallGrouped(bool binary, HttpClient http, IEnumerable<IEnumerable<string>> documents, CancellationToken cancellationToken = default)
    {
        var provider = new PerplexityContextualizedEmbeddingProvider("key", http, dimensions: 128);
        return binary ? provider.GetBinaryDocumentEmbeddingsAsync(documents, cancellationToken)
            : provider.GetDocumentEmbeddingsAsync(documents, cancellationToken);
    }

    private static ObservedSequence<T> CancelingSequence<T>(CancellationTokenSource cancellation, string stage, bool empty, T value)
        => new(call => call <= (empty ? 0 : 1), _ => value)
        {
            OnGetEnumerator = stage == "GetEnumerator" ? cancellation.Cancel : null,
            OnMoveNext = stage == "MoveNext" ? cancellation.Cancel : null,
            OnCurrent = stage == "Current" ? cancellation.Cancel : null,
            OnDispose = stage == "Dispose" ? cancellation.Cancel : null
        };

    private static void AssertCancellationReads<T>(ObservedSequence<T> sequence, string stage, bool empty)
    {
        Assert.IsTrue(sequence.WasDisposed);
        Assert.AreEqual(stage == "GetEnumerator" ? 0 : stage == "Dispose" && !empty ? 2 : 1, sequence.MoveNextCalls);
        Assert.AreEqual(!empty && (stage == "Current" || stage == "Dispose") ? 1 : 0, sequence.CurrentReads);
    }

    private sealed class ObservedSequence<T>(Func<int, bool> moveNext, Func<int, T> current) : IEnumerable<T>, IEnumerator<T>
    {
        internal Action? OnGetEnumerator { get; init; }
        internal Action? OnMoveNext { get; init; }
        internal Action? OnCurrent { get; init; }
        internal Action? OnDispose { get; init; }
        internal int GetEnumeratorCalls { get; private set; }
        internal int MoveNextCalls { get; private set; }
        internal int CurrentReads { get; private set; }
        internal bool WasDisposed { get; private set; }
        public IEnumerator<T> GetEnumerator() { GetEnumeratorCalls++; OnGetEnumerator?.Invoke(); return this; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public bool MoveNext() { MoveNextCalls++; OnMoveNext?.Invoke(); return moveNext(MoveNextCalls); }
        public T Current { get { CurrentReads++; OnCurrent?.Invoke(); return current(CurrentReads); } }
        object? IEnumerator.Current => Current;
        public void Dispose() { WasDisposed = true; OnDispose?.Invoke(); }
        public void Reset() => throw new NotSupportedException();
    }

    private sealed class EnumerationOnlyCollection<T>(T[] values) : ICollection<T>
    {
        public int Count => throw new AssertFailedException("Validation must not trust a user collection's Count.");
        public void CopyTo(T[] array, int arrayIndex) => throw new AssertFailedException("Validation must enumerate with cancellation and size bounds.");
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public bool IsReadOnly => true;
        public void Add(T item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(T item) => throw new NotSupportedException();
        public bool Remove(T item) => throw new NotSupportedException();
    }

    private sealed class RequestCapturedException : Exception;

    private sealed class CaptureTransport : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal List<JsonObject> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            // Boundary tests verify accepted requests without building 16,000 response vectors.
            throw new RequestCapturedException();
        }
    }
}
