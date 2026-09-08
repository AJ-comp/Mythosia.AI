namespace Mythosia.AI.Models
{
    /// <summary>A provider-supplied source reference. Fields unavailable from the provider remain null.</summary>
    /// <remarks>Offsets refer to the provider's response content part, not the concatenated run result.
    /// Keep ResponseId, OutputIndex and ContentIndex when rendering references across rounds.</remarks>
    public sealed class AICitation
    {
        public string Provider { get; set; } = string.Empty;
        public string? Url { get; set; }
        public string? FileId { get; set; }
        public string? Title { get; set; }
        public string? Text { get; set; }
        public string? ResponseId { get; set; }
        public int? OutputIndex { get; set; }
        public int? ContentIndex { get; set; }
        public int? StartIndex { get; set; }
        public int? EndIndex { get; set; }
        public AICitation Clone() => (AICitation)MemberwiseClone();
    }
}
