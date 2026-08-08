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
    /// Adaptive-thinking effort for current Claude models.
    /// Auto preserves the legacy <c>ThinkingBudget</c>-to-effort mapping.
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
    /// Controls whether Claude returns a readable summary of its adaptive thinking.
    /// Omitted keeps the provider default and Summarized requests summarized reasoning blocks.
    /// </summary>
    public enum ClaudeThinkingDisplay
    {
        Omitted,
        Summarized
    }

    /// <summary>
    /// Reasoning effort for xAI Grok models.
    /// Auto omits the provider parameter. Grok 4.3 supports None through High;
    /// Grok 4.5 supports Low through High and cannot disable reasoning.
    /// </summary>
    public enum GrokReasoning
    {
        Auto,
        None,
        Low,
        Medium,
        High
    }
}
