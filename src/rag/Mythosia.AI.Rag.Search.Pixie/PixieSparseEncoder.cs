using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Mythosia.AI.Rag.Search.Pixie;

/// <summary>Runs the pinned, quantized PIXIE SPLADE model locally on the CPU.
/// Concurrent calls are serialized to bound inference memory. Query and document encoding use the same model.</summary>
public sealed class PixieSparseEncoder : IDisposable
{
    /// <summary>The upstream model revision used by this package.</summary>
    public const string ModelRevision = "730b515a74727b5f57f031a4c8108743b99e689f";
    internal const string ModelHash = "DCF25F9FA452E61A48F5330A63A2AA68988DDAC5BFC574A59C338EF1CFF06E42";
    internal const string TokenizerHash = "36BDC1F1FE0135D10667322A493FD6A32EB93E8F4F68D09004EEB92F39FC8F25";
    private readonly PixieTokenizer tokenizer;
    private readonly InferenceSession session;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly int maxSequenceLength;
    private readonly float minimumWeight;
    private int disposed;

    /// <summary>Loads and validates the bundled model, or the same pinned assets in an explicit directory.
    /// No model is downloaded at runtime. Options are copied at construction.</summary>
    public PixieSparseEncoder(PixieOptions? options = null)
    {
        options ??= new PixieOptions();
        var modelDirectory = options.ModelDirectory;
        var sequenceLimit = options.MaxSequenceLength;
        var intraOpThreads = options.IntraOpThreads;
        var threshold = options.MinimumWeight;
        if (string.IsNullOrWhiteSpace(modelDirectory)) throw new ArgumentException("ModelDirectory is required.", nameof(options));
        if (sequenceLimit < 2 || sequenceLimit > 5632)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSequenceLength must be between 2 and 5632.");
        if (intraOpThreads < 0) throw new ArgumentOutOfRangeException(nameof(options), "IntraOpThreads cannot be negative.");
        if (!float.IsFinite(threshold) || threshold < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumWeight must be finite and nonnegative.");
        maxSequenceLength = sequenceLimit;
        minimumWeight = threshold;
        var modelPath = Path.Combine(modelDirectory, "model.int8.onnx");
        var tokenizerPath = Path.Combine(modelDirectory, "tokenizer.json");
        VerifyFile(modelPath, ModelHash);
        VerifyFile(tokenizerPath, TokenizerHash);
        tokenizer = new PixieTokenizer(tokenizerPath);
        using var settings = new SessionOptions
        {
            IntraOpNumThreads = intraOpThreads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        var loaded = new InferenceSession(modelPath, settings);
        try
        {
            if (loaded.InputMetadata.Count != 2
                || !loaded.InputMetadata.TryGetValue("input_ids", out var ids) || ids.ElementType != typeof(long) || ids.Dimensions.Length != 2
                || !loaded.InputMetadata.TryGetValue("attention_mask", out var mask) || mask.ElementType != typeof(long) || mask.Dimensions.Length != 2
                || !loaded.OutputMetadata.TryGetValue("logits", out var logits) || logits.ElementType != typeof(float)
                || logits.Dimensions.Length != 3 || logits.Dimensions[2] != 50000)
                throw new InvalidDataException("Unexpected PIXIE ONNX input/output contract.");
            session = loaded;
        }
        catch { loaded.Dispose(); throw; }
    }

    /// <summary>Encodes a query into weighted token IDs. Blank text produces an empty vector.</summary>
    public Task<PixieSparseVector> EncodeQueryAsync(string text, CancellationToken cancellationToken = default)
        => EncodeAsync(text, cancellationToken);

    /// <summary>Encodes a document chunk into weighted token IDs. Blank text produces an empty vector.</summary>
    public Task<PixieSparseVector> EncodeDocumentAsync(string text, CancellationToken cancellationToken = default)
        => EncodeAsync(text, cancellationToken);

    /// <summary>Returns the vocabulary spelling for a token ID, useful for inspecting retrieval evidence.</summary>
    public string GetToken(int tokenId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (tokenId < 0 || tokenId >= 50000) throw new ArgumentOutOfRangeException(nameof(tokenId));
        return tokenizer.GetToken(tokenId);
    }

    private async Task<PixieSparseVector> EncodeAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (string.IsNullOrWhiteSpace(text)) return PixieSparseVector.Empty;
            // CPU work does not run on the caller's UI/request synchronization context.
            return await Task.Run(() => Run(text, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private PixieSparseVector Run(string text, CancellationToken cancellationToken)
    {
        var ids = tokenizer.Encode(text, maxSequenceLength, cancellationToken);
        var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
        var shape = new[] { 1, ids.Length };
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, shape))
        };
        using var runOptions = new RunOptions();
        // The registration is disposed before runOptions: a concurrent cancellation callback
        // can never access a released native handle. Termination is local and cooperative.
        using var registration = cancellationToken.Register(() => runOptions.Terminate = true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var outputs = session.Run(inputs, new[] { "logits" }, runOptions);
            cancellationToken.ThrowIfCancellationRequested();
            var tensor = outputs[0].AsTensor<float>();
            if (tensor.Dimensions.Length != 3 || tensor.Dimensions[0] != 1
                || tensor.Dimensions[1] != ids.Length || tensor.Dimensions[2] != 50000)
                throw new InvalidDataException("Unexpected PIXIE inference output shape.");
            return tensor is DenseTensor<float> dense
                ? Pool(dense.Buffer.Span, ids.Length, tokenizer.IsSpecial, minimumWeight, cancellationToken)
                : Pool(tensor.ToArray(), ids.Length, tokenizer.IsSpecial, minimumWeight, cancellationToken);
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    internal static PixieSparseVector Pool(ReadOnlySpan<float> logits, int sequenceLength, Func<int, bool> isSpecial,
        float minimumWeight, CancellationToken cancellationToken)
    {
        var maxima = new float[50000];
        for (var position = 0; position < sequenceLength; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = position * 50000;
            for (var tokenId = 0; tokenId < 50000; tokenId++)
            {
                var value = logits[offset + tokenId];
                if (!float.IsFinite(value)) throw new InvalidDataException("PIXIE returned non-finite logits.");
                if (value > maxima[tokenId]) maxima[tokenId] = value;
            }
        }
        var indices = new List<int>();
        var values = new List<float>();
        for (var tokenId = 0; tokenId < maxima.Length; tokenId++)
        {
            // log1p(relu(.)) is monotonic, so max before activation is equivalent
            // to the upstream max pooling and avoids repeating log for every position.
            var x = (double)maxima[tokenId];
            var weight = (float)(x < 0.0001 ? x * (1 - x / 2 + x * x / 3) : Math.Log(1.0 + x));
            if (weight <= minimumWeight || isSpecial(tokenId)) continue;
            indices.Add(tokenId);
            values.Add(weight);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PixieSparseVector(indices, values);
    }

    private static void VerifyFile(string path, string expectedHash)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("PIXIE model assets are missing. Install the model-bundled package, or prepare the pinned assets with build/prepare-pixie-model.py for a source checkout.", path);
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException($"PIXIE asset hash mismatch: {Path.GetFileName(path)}. Use the exact model/tokenizer pair distributed with this package.");
    }

    /// <summary>Waits for the current inference to finish, then releases the native session.
    /// Queued and future calls fail with ObjectDisposedException.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        gate.Wait();
        try { session.Dispose(); }
        finally { gate.Release(); }
        // Keep the small semaphore alive: callers already waiting on it must wake and see disposed.
    }
}
