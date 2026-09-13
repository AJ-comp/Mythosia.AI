namespace Mythosia.AI.Models.Enums
{
    /// <summary>
    /// Controls the thinking level for Gemini 3 models.
    /// Auto uses the selected model's default. Gemini 3.8/3.6/3.5 Flash default to Medium,
    /// Flash-Lite defaults to Minimal, while Gemini 3 Flash Preview and Pro Preview default to High.
    /// </summary>
    public enum GeminiThinkingLevel
    {
        /// <summary>Uses the selected model's provider default.</summary>
        Auto,

        /// <summary>Minimal thinking where supported; unavailable on Gemini 3 Pro and Gemini 3.7/3.8 Flash.</summary>
        Minimal,

        /// <summary>Low thinking level</summary>
        Low,

        /// <summary>Medium thinking level.</summary>
        Medium,

        /// <summary>High thinking level.</summary>
        High
    }
}
