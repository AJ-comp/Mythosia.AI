namespace Mythosia.AI.Services.Base
{
    /// <summary>
    /// Outcome of a forced conversation compaction.
    /// <para>
    /// <see cref="IsApplied"/> answers the only question the caller actually has: is it worth
    /// re-sending? Summarizing is not the same as shrinking when the retained recent messages
    /// already cover the whole conversation.
    /// </para>
    /// <para>
    /// This is decided before the summary call, so a skipped compaction costs nothing and
    /// leaves the conversation untouched. Only <see cref="Applied"/> deletes messages.
    /// </para>
    /// </summary>
    internal readonly struct SummaryCompactionResult
    {
        /// <summary>True when the set of messages that will be sent actually got smaller.</summary>
        public bool IsApplied { get; }

        /// <summary>How many messages were folded into the summary and removed.</summary>
        public int RemovedMessageCount { get; }

        /// <summary>Messages that would have been sent before compaction.</summary>
        public int SentCountBefore { get; }

        /// <summary>Messages that will be sent after compaction.</summary>
        public int SentCountAfter { get; }

        /// <summary>Why compaction did not shrink anything. Null when <see cref="IsApplied"/> is true.</summary>
        public string? SkipReason { get; }

        private SummaryCompactionResult(
            bool isApplied, int removed, int sentBefore, int sentAfter, string? skipReason)
        {
            IsApplied = isApplied;
            RemovedMessageCount = removed;
            SentCountBefore = sentBefore;
            SentCountAfter = sentAfter;
            SkipReason = skipReason;
        }

        public static SummaryCompactionResult Applied(int removed, int sentBefore, int sentAfter)
            => new SummaryCompactionResult(true, removed, sentBefore, sentAfter, null);

        public static SummaryCompactionResult Skipped(string reason)
            => new SummaryCompactionResult(false, 0, 0, 0, reason);
    }
}
