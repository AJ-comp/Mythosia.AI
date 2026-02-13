namespace Mythosia.AI.Models.Enums
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
}
