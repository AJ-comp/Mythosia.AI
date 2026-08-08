namespace Mythosia.AI.Models.Enums
{
    /// <summary>
    /// Controls the thinking level for Gemini 3 models.
    /// Auto uses the selected model's default. Gemini 3.6/3.5 Flash default to Medium,
    /// Flash-Lite defaults to Minimal, while Gemini 3 Flash Preview and Pro Preview default to High.
    /// </summary>
    public enum GeminiThinkingLevel
    {
        /// <summary>Uses the selected model's provider default.</summary>
        Auto,

        /// <summary>Minimal thinking (supported by current Flash and Flash-Lite models).</summary>
        Minimal,

        /// <summary>Low thinking level</summary>
        Low,

        /// <summary>Medium thinking level.</summary>
        Medium,

        /// <summary>High thinking level.</summary>
        High
    }
}
