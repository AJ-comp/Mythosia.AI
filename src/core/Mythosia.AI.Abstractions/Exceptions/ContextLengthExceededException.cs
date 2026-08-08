namespace Mythosia.AI.Exceptions
{
    /// <summary>
    /// Thrown when the provider rejects a request because the prompt exceeds the model's
    /// context window.
    /// <para>
    /// This is usually a <b>recoverable</b> condition: the core <c>AIService</c> catches
    /// it, compacts the conversation, and retries. When it does reach the caller,
    /// <see cref="RecoverySkipReason"/> says why recovery could not save it — no conversation policy,
    /// nothing left to compact, a tool had already run, the retry budget ran out, and so on. A null
    /// reason means recovery never got the chance — the caller invoked the raw
    /// <c>GetCompletionAsync(Message)</c> provider overload, which is not routed through it.
    /// </para>
    /// <para>
    /// <c>StreamAsync</c> never throws this: there the overflow arrives as an error chunk carrying
    /// <c>context_length_exceeded</c> metadata, and recovery happens inside the round loop. The
    /// legacy callback API <c>StreamCompletionAsync</c> has no round loop and does throw it.
    /// </para>
    /// <para>
    /// The context limit is owned and enforced by the server, never guessed by the client.
    /// That is the whole point of reacting to this exception rather than estimating token counts.
    /// </para>
    /// </summary>
    public class ContextLengthExceededException : AIServiceException
    {
        /// <summary>The model's context window, when the provider reported it. Null if unknown.</summary>
        public int? MaxContextTokens { get; }

        /// <summary>The size of the rejected prompt, when the provider reported it. Null if unknown.</summary>
        public int? RequestedTokens { get; }

        /// <summary>HTTP status the provider answered with (400, 413, …).</summary>
        public int StatusCode { get; }

        /// <summary>How many compaction attempts were made before giving up. 0 = never attempted.</summary>
        public int RecoveryAttempts { get; internal set; }

        /// <summary>Why recovery was skipped or failed, for diagnostics. Null if never attempted.</summary>
        public string? RecoverySkipReason { get; internal set; }

        public ContextLengthExceededException(
            string message,
            string errorDetails,
            int statusCode,
            int? maxContextTokens = null,
            int? requestedTokens = null)
            : base(message, errorDetails)
        {
            StatusCode = statusCode;
            MaxContextTokens = maxContextTokens;
            RequestedTokens = requestedTokens;
        }
    }
}
