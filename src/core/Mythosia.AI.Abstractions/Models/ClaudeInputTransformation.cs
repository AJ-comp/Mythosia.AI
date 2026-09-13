namespace Mythosia.AI.Models
{
    /// <summary>A provider-reported change to input thinking blocks on a Claude request.</summary>
    /// <remarks>Path uses the provider's request-local indexing. Unknown Type and Reason values
    /// are retained for forward compatibility. This is diagnostic metadata, not generated answer text.</remarks>
    public sealed class ClaudeInputTransformation
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? ResponseId { get; set; }
        public string? Model { get; set; }

        public ClaudeInputTransformation Clone() => (ClaudeInputTransformation)MemberwiseClone();
    }
}
