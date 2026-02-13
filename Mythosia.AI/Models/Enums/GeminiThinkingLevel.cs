namespace Mythosia.AI.Models.Enums
{
    /// <summary>
    /// Controls the thinking level for Gemini 3 models.
    /// Auto: Uses model default (High for Gemini 3).
    /// Gemini 3 Pro/Flash shared levels: Low, High (default)
    /// Gemini 3 Flash additional levels: Minimal, Medium
    /// </summary>
    public enum GeminiThinkingLevel
    {
        /// <summary>Uses model default (High for Gemini 3)</summary>
        Auto,

        /// <summary>Minimal thinking (Gemini 3 Flash only)</summary>
        Minimal,

        /// <summary>Low thinking level</summary>
        Low,

        /// <summary>Medium thinking level (Gemini 3 Flash only)</summary>
        Medium,

        /// <summary>High thinking level (default for Gemini 3)</summary>
        High
    }
}
