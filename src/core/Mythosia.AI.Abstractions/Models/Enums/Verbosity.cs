namespace Mythosia.AI.Models
{
    /// <summary>
    /// Text verbosity level for supported GPT-5 family models.
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
