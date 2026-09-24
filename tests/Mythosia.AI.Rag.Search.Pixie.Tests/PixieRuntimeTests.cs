using System.Text.Json;

namespace Mythosia.AI.Rag.Search.Pixie.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class PixieRuntimeTests
{
    [TestMethod]
    public void Tokenizer_MatchesOfficialHuggingFaceTokenizerOnUnicodeAndCodeCases()
    {
        var tokenizer = new PixieTokenizer(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokenizer.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokenizer-parity.json")));
        foreach (var item in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var text = item.GetProperty("text").GetString()!;
            var expected = item.GetProperty("ids").EnumerateArray().Select(x => x.GetInt64()).ToArray();
            CollectionAssert.AreEqual(expected, tokenizer.Encode(text, 5632, default), $"Tokenizer mismatch for {JsonSerializer.Serialize(text)}");
        }
    }

    [TestMethod]
    public void Tokenizer_RejectsLongInputRatherThanDroppingTheTail()
    {
        var tokenizer = new PixieTokenizer(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokenizer.json"));
        var text = "hello hello hello hello";
        var full = tokenizer.Encode(text, 5632, default);
        CollectionAssert.AreEqual(full, tokenizer.Encode(text, full.Length, default));
        Assert.ThrowsExactly<ArgumentException>(() => tokenizer.Encode(text, full.Length - 1, default));
    }

    [TestMethod]
    public void Tokenizer_ObservesCancellation()
    {
        var tokenizer = new PixieTokenizer(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tokenizer.json"));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => tokenizer.Encode("문서 검색", 512, source.Token));
    }

    [TestMethod]
    public void SparseVector_DefensiveCopiesPreventIndexAndWeightMutation()
    {
        var ids = new[] { 38, 42 };
        var values = new[] { 1f, 2f };
        var vector = new PixieSparseVector(ids, values);
        ids[0] = 9;
        values[0] = 7;
        Assert.AreEqual(38, vector.Indices[0]);
        Assert.AreEqual(1f, vector.Values[0]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<int>)vector.Indices)[0] = 9);
    }

    [TestMethod]
    public void SparseVector_RejectsInvalidVocabularyAndWeights()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PixieSparseVector(new[] { 1, 1 }, new[] { 1f, 1f }));
        Assert.ThrowsExactly<ArgumentException>(() => new PixieSparseVector(new[] { 2, 1 }, new[] { 1f, 1f }));
        Assert.ThrowsExactly<ArgumentException>(() => new PixieSparseVector(new[] { 50000 }, new[] { 1f }));
        Assert.ThrowsExactly<ArgumentException>(() => new PixieSparseVector(new[] { -1 }, new[] { 1f }));
        Assert.ThrowsExactly<ArgumentException>(() => new PixieSparseVector(new[] { 1 }, Array.Empty<float>()));
        foreach (var bad in new[] { -1f, 0f, float.NaN, float.PositiveInfinity })
            Assert.ThrowsExactly<ArgumentException>(() => new PixieSparseVector(new[] { 1 }, new[] { bad }));
    }

    [TestMethod]
    public void Pool_UsesMaximumPositiveActivationAndRemovesSpecialTokens()
    {
        var logits = new float[100000];
        logits[0] = 10;
        logits[38] = -5;
        logits[50038] = 3;
        logits[39] = 1;
        logits[50039] = 2;
        var vector = PixieSparseEncoder.Pool(logits, 2, id => id == 0, 0, default);
        CollectionAssert.AreEqual(new[] { 38, 39 }, vector.Indices.ToArray());
        Assert.AreEqual((float)Math.Log(4), vector.Values[0], 0.000001f);
        Assert.AreEqual((float)Math.Log(3), vector.Values[1], 0.000001f);
        Assert.AreEqual(0, PixieSparseEncoder.Pool(logits, 2, id => id == 0, 2f, default).Indices.Count);
    }

    [TestMethod]
    public void Pool_RejectsNonFiniteModelOutput()
    {
        var logits = new float[50000];
        logits[42] = float.NaN;
        Assert.ThrowsExactly<InvalidDataException>(() => PixieSparseEncoder.Pool(logits, 1, _ => false, 0, default));
    }

    [TestMethod]
    public void Pool_PreservesTinyPositiveWeightsAtZeroThreshold()
    {
        var logits = new float[50000];
        logits[42] = 1e-20f;
        var result = PixieSparseEncoder.Pool(logits, 1, _ => false, 0, default);
        Assert.AreEqual(1e-20f, result.Values.Single());
    }

    [TestMethod]
    public void Constructor_RejectsOptionsBeforeAccessingAssets()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PixieSparseEncoder(new PixieOptions { MaxSequenceLength = 5633 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PixieSparseEncoder(new PixieOptions { MaxSequenceLength = 1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PixieSparseEncoder(new PixieOptions { IntraOpThreads = -1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PixieSparseEncoder(new PixieOptions { MinimumWeight = float.NaN }));
        Assert.ThrowsExactly<ArgumentException>(() => new PixieSparseEncoder(new PixieOptions { ModelDirectory = " " }));
    }

    [TestMethod]
    public void Constructor_RejectsTamperedModelInsteadOfTrustingLocalManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mythosia-pixie-tamper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "model.int8.onnx"), "tampered");
            Assert.ThrowsExactly<InvalidDataException>(() => new PixieSparseEncoder(new PixieOptions { ModelDirectory = directory }));
        }
        finally { Directory.Delete(directory, true); }
    }
}

[TestClass]
[TestCategory("LocalModel")]
public sealed class PixieLocalModelTests
{
    private static PixieSparseEncoder? encoder;

    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        var directory = Environment.GetEnvironmentVariable("MYTHOSIA_PIXIE_MODEL_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            Assert.Inconclusive("Set MYTHOSIA_PIXIE_MODEL_DIR to run the pinned local-model inference tests.");
        encoder = new PixieSparseEncoder(new PixieOptions { ModelDirectory = directory!, IntraOpThreads = 4 });
    }

    [ClassCleanup]
    public static void Cleanup() => encoder?.Dispose();

    [TestMethod]
    public async Task LocalInference_MatchesPythonOnnxRuntimeSparseVector()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inference-parity.json")));
        var text = doc.RootElement.GetProperty("text").GetString()!;
        var expectedIds = doc.RootElement.GetProperty("indices").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var expectedValues = doc.RootElement.GetProperty("values").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        var actual = await encoder!.EncodeQueryAsync(text);
        CollectionAssert.AreEqual(expectedIds, actual.Indices.ToArray());
        for (var i = 0; i < expectedValues.Length; i++)
            Assert.AreEqual(expectedValues[i], actual.Values[i], 0.0001f, $"Weight mismatch for token {expectedIds[i]}");
    }

    [TestMethod]
    public async Task QueryAndDocumentEncoding_AreIdenticalAndConcurrentCallsAreSafe()
    {
        var results = await Task.WhenAll(encoder!.EncodeQueryAsync("한국어 검색"), encoder.EncodeDocumentAsync("한국어 검색"), encoder.EncodeQueryAsync("한국어 검색"));
        Assert.IsGreaterThan(0, results[0].Indices.Count);
        foreach (var result in results.Skip(1))
        {
            CollectionAssert.AreEqual(results[0].Indices.ToArray(), result.Indices.ToArray());
            CollectionAssert.AreEqual(results[0].Values.ToArray(), result.Values.ToArray());
        }
    }

    [TestMethod]
    public async Task EmptyInputIsEmptyAndLongInputIsRejected()
    {
        Assert.AreEqual(0, (await encoder!.EncodeQueryAsync(" \n ")).Indices.Count);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => encoder.EncodeDocumentAsync(string.Join(" ", Enumerable.Repeat("hello", 600))));
    }

    [TestMethod]
    public async Task CancelledRequestDoesNotPoisonLaterInference()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => encoder!.EncodeQueryAsync("취소 요청", source.Token));
        Assert.IsGreaterThan(0, (await encoder!.EncodeQueryAsync("정상 요청")).Indices.Count);
    }

    [TestMethod]
    public async Task NativeInferenceCancellationIsIsolatedAndDisposedEncoderRejectsCalls()
    {
        var directory = Environment.GetEnvironmentVariable("MYTHOSIA_PIXIE_MODEL_DIR")!;
        var dedicated = new PixieSparseEncoder(new PixieOptions { ModelDirectory = directory, IntraOpThreads = 1 });
        try
        {
            using var cancellation = new CancellationTokenSource();
            var running = dedicated.EncodeQueryAsync(string.Join(" ", Enumerable.Repeat("a", 500)), cancellation.Token);
            await Task.Delay(25);
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => running);
            Assert.IsGreaterThan(0, (await dedicated.EncodeQueryAsync("위성 데이터")).Indices.Count);
        }
        finally { dedicated.Dispose(); }
        dedicated.Dispose();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => dedicated.EncodeQueryAsync("after disposal"));
    }
}
