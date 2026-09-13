namespace Mythosia.AI.Models.Streaming
{
    /// <summary>A normalized provider response termination reason. Errors still fail the run.</summary>
    public enum AIFinishReason
    {
        /// <summary>The provider did not report a termination reason.</summary>
        Unknown,
        /// <summary>The provider completed its response normally.</summary>
        Stop,
        /// <summary>The provider reached its output token limit.</summary>
        MaxTokens,
        /// <summary>The provider requested tool execution.</summary>
        ToolCalls,
        /// <summary>The provider reported filtering or refusal.</summary>
        ContentFilter,
        /// <summary>The provider reported a reason without a common equivalent.</summary>
        Other
    }
}
