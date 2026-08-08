namespace Mythosia.AI.Models.Enums
{
    /// <summary>
    /// Configurable Gemini harm-blocking thresholds. ProviderDefault omits the category
    /// from the request so that the selected model's current Google default applies.
    /// </summary>
    public enum GeminiSafetyThreshold
    {
        ProviderDefault,
        Off,
        BlockNone,
        BlockOnlyHigh,
        BlockMediumAndAbove,
        BlockLowAndAbove
    }
}
