namespace Mythosia.AI.Rag.Search.Pixie;

/// <summary>Local execution options for the pinned PIXIE SPLADE model.</summary>
public sealed class PixieOptions
{
    /// <summary>Directory containing model.int8.onnx, tokenizer.json and manifest.json.
    /// Defaults to models/pixie in the application output directory. No network download is performed.</summary>
    public string ModelDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "models", "pixie");

    /// <summary>Maximum tokens including the two boundary tokens; 512 by default, up to 5632.
    /// Longer input is rejected rather than silently truncated. Split long documents before ingestion.</summary>
    public int MaxSequenceLength { get; set; } = 512;

    /// <summary>Number of CPU threads used within an inference call. Zero uses the ONNX Runtime default.</summary>
    public int IntraOpThreads { get; set; } = 0;

    /// <summary>Exclude weights at or below this threshold. Zero preserves all positive non-special weights.</summary>
    public float MinimumWeight { get; set; } = 0;
}
