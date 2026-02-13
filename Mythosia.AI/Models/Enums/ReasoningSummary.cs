namespace Mythosia.AI.Models.Enums
{
    /// <summary>
    /// Reasoning summary mode for GPT-5 family models.
    /// Controls how the model summarizes its reasoning process.
    /// Set to null (use nullable) to disable reasoning summaries entirely.
    /// </summary>
    public enum ReasoningSummary
    {
        /// <summary>Model decides the summary format automatically (default)</summary>
        Auto,

        /// <summary>Brief, concise reasoning summary</summary>
        Concise,

        /// <summary>Detailed reasoning summary</summary>
        Detailed
    }
}
