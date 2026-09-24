namespace Mythosia.AI.Models
{
    /// <summary>
    /// Reasoning effort level for GPT-5 base models.
    /// Auto: Uses model default (Medium).
    /// </summary>
    public enum Gpt5Reasoning
    {
        Auto,
        Minimal,
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Reasoning effort level for GPT-5.1 models.
    /// Auto: Uses model default (None).
    /// </summary>
    public enum Gpt5_1Reasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Reasoning effort level for GPT-5.2 models.
    /// Auto: Uses model default (None for GPT-5.2, Medium for GPT-5.2 Pro).
    /// </summary>
    public enum Gpt5_2Reasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High,
        XHigh
    }

    /// <summary>
    /// Reasoning effort level for GPT-5.3 models.
    /// Auto: Uses model default (Medium for Codex).
    /// GPT-5.3 Codex supports: low, medium (default), high, xhigh.
    /// GPT-5.3 Codex Spark and Instant use simplified configs.
    /// </summary>
    public enum Gpt5_3Reasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High,
        XHigh
    }

    /// <summary>
    /// Reasoning effort level for GPT-5.4 models.
    /// Auto: Uses model default (None for GPT-5.4, Medium for GPT-5.4 Pro).
    /// GPT-5.4 supports: none (default), low, medium, high, xhigh.
    /// GPT-5.4 Pro supports: medium, high, xhigh.
    /// </summary>
    public enum Gpt5_4Reasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High,
        XHigh
    }

    /// <summary>
    /// Reasoning effort level for GPT-5.5 models.
    /// Auto: Uses model default (None for GPT-5.5, Medium for GPT-5.5 Pro).
    /// GPT-5.5 supports: none (default), low, medium, high, xhigh.
    /// GPT-5.5 Pro supports: medium, high, xhigh.
    /// </summary>
    public enum Gpt5_5Reasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High,
        XHigh
    }

    /// <summary>
    /// Reasoning effort level for GPT-5.6 models.
    /// Auto uses the model default (Medium).
    /// GPT-5.6 supports: none, low, medium (default), high, xhigh, max.
    /// </summary>
    public enum Gpt5_6Reasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High,
        XHigh,
        Max
    }

    /// <summary>
    /// Reasoning execution mode for GPT-5.6 models.
    /// Standard omits the API mode parameter; Pro sends reasoning.mode as pro.
    /// </summary>
    public enum Gpt5_6ReasoningMode
    {
        Standard,
        Pro
    }

    /// <summary>
    /// Reasoning effort level for GPT-6 models.
    /// Auto uses the library default (Medium).
    /// GPT-6 supports low, medium, high, xhigh, and max. Sol and Luna also support None;
    /// Astra always reasons and does not support None.
    /// </summary>
    public enum Gpt6Reasoning
    {
        Auto,
        Low,
        Medium,
        High,
        XHigh,
        Max,
        /// <summary>Disables reasoning on GPT-6 Sol and Luna. Not supported by Astra.</summary>
        None
    }

    /// <summary>
    /// Reasoning execution mode for GPT-6 models.
    /// Standard omits the API mode parameter; Pro sends reasoning.mode as pro.
    /// </summary>
    public enum Gpt6ReasoningMode
    {
        Standard,
        Pro
    }

    /// <summary>
    /// Adaptive-thinking effort for current Claude models.
    /// Auto preserves the legacy <c>ThinkingBudget</c>-to-effort mapping.
    /// With explicit adaptive thinking on Opus 5.5, Auto uses its medium default.
    /// </summary>
    public enum ClaudeReasoningEffort
    {
        Auto,
        Low,
        Medium,
        High,
        XHigh,
        Max
    }

    /// <summary>
    /// Controls readable output from Claude adaptive thinking.
    /// Omitted hides readable reasoning; Summarized requests summaries.
    /// Updates requests supported user-facing progress updates without reasoning summaries.
    /// </summary>
    public enum ClaudeThinkingDisplay
    {
        Omitted,
        Summarized,
        Updates
    }

    /// <summary>How supported Claude requests handle thinking blocks bound to a changed conversation prefix.</summary>
    public enum ClaudeThinkingPrefixMismatchBehavior
    {
        /// <summary>Reject an invalid prefix instead of discarding prior thinking.</summary>
        Error,
        /// <summary>Let the provider discard invalid thinking and report the dropped blocks.</summary>
        DropBlock
    }

    /// <summary>
    /// Reasoning effort for xAI Grok models.
    /// Auto omits the provider parameter. Grok 4.3 supports None through High;
    /// Grok 4.5 supports Low through High; Grok 4.6 and 4.7 also support XHigh.
    /// Grok 4.5 and 4.6 cannot disable reasoning.
    /// </summary>
    public enum GrokReasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High,
        XHigh
    }
}
