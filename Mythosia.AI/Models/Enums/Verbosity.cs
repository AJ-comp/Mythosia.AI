namespace Mythosia.AI.Models.Enums
{
    /// <summary>
    /// Text verbosity level for GPT-5.1 and GPT-5.2 models.
    /// Controls how verbose the model's text output is.
    /// </summary>
    public enum Verbosity
    {
        /// <summary>Concise, shorter responses</summary>
        Low,

        /// <summary>Balanced verbosity (default)</summary>
        Medium,

        /// <summary>More detailed, longer responses</summary>
        High
    }
}
