using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        /// <summary>
        /// When set, automatically summarizes old messages when conversation exceeds
        /// the configured threshold. The summary is injected as a system message prefix.
        /// Set to null to disable (default).
        /// </summary>
        public SummaryConversationPolicy? ConversationPolicy { get; set; }

        /// <summary>
        /// Last known input token count from the API.
        /// Used by SummaryConversationPolicy for accurate trigger decisions
        /// instead of local estimation.
        /// </summary>
        internal int LastKnownInputTokens { get; set; }

        private bool _isSummarizing = false;

        /// <summary>
        /// Returns the effective system message, composed from (in order):
        /// the per-request <see cref="AIRequestContext.SystemMessagePrefix"/>,
        /// the conversation summary (if any), <see cref="Models.ChatBlock.SystemMessage"/>,
        /// and the per-request <see cref="AIRequestContext.SystemMessageSuffix"/>.
        /// Use this instead of ActivateChat.SystemMessage when building request bodies.
        /// </summary>
        internal string GetEffectiveSystemMessage()
        {
            var baseMsg = ActivateChat?.SystemMessage ?? "";
            var summary = ConversationPolicy?.CurrentSummary;
            var ctx = _currentRequestContext.Value;

            if (!string.IsNullOrEmpty(summary))
            {
                var summaryPrefix = $"[Previous conversation summary]\n{summary}";
                baseMsg = string.IsNullOrEmpty(baseMsg)
                    ? summaryPrefix
                    : $"{summaryPrefix}\n\n{baseMsg}";
            }

            if (!string.IsNullOrEmpty(ctx?.SystemMessagePrefix))
            {
                baseMsg = string.IsNullOrEmpty(baseMsg)
                    ? ctx!.SystemMessagePrefix!
                    : $"{ctx!.SystemMessagePrefix}\n\n{baseMsg}";
            }

            if (!string.IsNullOrEmpty(ctx?.SystemMessageSuffix))
            {
                baseMsg = string.IsNullOrEmpty(baseMsg)
                    ? ctx!.SystemMessageSuffix!
                    : $"{baseMsg}\n\n{ctx!.SystemMessageSuffix}";
            }

            return baseMsg;
        }

        /// <summary>
        /// Checks whether the conversation should be summarized based on the current
        /// ConversationPolicy, and if so, performs the summarization using StatelessMode.
        /// Called automatically at the beginning of GetCompletionAsync(string).
        /// For streaming scenarios, call this explicitly before StreamAsync().
        /// </summary>
        public Task ApplySummaryPolicyIfNeededAsync() => CompactAsync(force: false);

        /// <summary>
        /// Compacts the conversation regardless of the token trigger, and reports whether the set
        /// of messages that will actually be sent got smaller.
        /// <para>
        /// Used to recover from <see cref="Exceptions.ContextLengthExceededException"/>. The trigger
        /// cannot be consulted there: it keys off the previous response's token usage, and a request
        /// rejected for being too large produced no usage at all.
        /// </para>
        /// <para>
        /// Returning "did this actually shrink?" is the point, and it is decided before anything is
        /// sent or deleted. Compacting without shrinking would bill a summary and destroy history
        /// only for the caller to retry the identical request into the same wall.
        /// </para>
        /// </summary>
        internal Task<SummaryCompactionResult> ForceCompactAsync() => CompactAsync(force: true);

        private async Task<SummaryCompactionResult> CompactAsync(bool force)
        {
            if (_isSummarizing) return SummaryCompactionResult.Skipped("reentrant");
            // No policy means no summary store — there is nothing to compact into.
            // Injecting a policy here would silently start deleting history the caller never opted into.
            if (ConversationPolicy == null) return SummaryCompactionResult.Skipped("no-policy");
            if (StatelessMode) return SummaryCompactionResult.Skipped("stateless");

            if (!force && !ConversationPolicy.ShouldSummarize(ActivateChat.Messages, LastKnownInputTokens))
                return SummaryCompactionResult.Skipped("trigger-not-met");

            var (messagesToSummarize, keepFromIndex) = ConversationPolicy.GetMessagesToSummarize(ActivateChat.Messages);

            // A function-call assistant message and its result message form one protocol turn.
            // The regular trigger path needs the same boundary protection as forced recovery;
            // otherwise KeepRecentCount/KeepRecentTokens can retain an orphan tool result.
            keepFromIndex = ClampKeepIndexForFunctionPair(ActivateChat.Messages, keepFromIndex);
            messagesToSummarize = Take(ActivateChat.Messages, keepFromIndex);

            int sentBefore = 0, sentAfter = 0;

            if (force)
            {
                keepFromIndex = ClampKeepIndexForForcedCompaction(ActivateChat.Messages, keepFromIndex);
                messagesToSummarize = Take(ActivateChat.Messages, keepFromIndex);

                // KeepRecent covers everything (or the clamp pulled it back to zero) — nothing can be
                // removed, so a retry would send the same payload that just got rejected.
                if (messagesToSummarize.Count == 0)
                    return SummaryCompactionResult.Skipped("nothing-to-cut");

                sentBefore = ActivateChat.Messages.Count;
                sentAfter = ActivateChat.Messages.Count - keepFromIndex;
            }

            // When the trigger fires with nothing beyond KeepRecent, summarize everything but delete
            // nothing — the summary still accumulates. The forced path never reaches this branch.
            var msgsForSummary = messagesToSummarize.Count > 0
                ? messagesToSummarize
                : (IList<Message>)ActivateChat.Messages;

            var prompt = BuildSummaryPrompt(msgsForSummary, ConversationPolicy.CurrentSummary);

            _isSummarizing = true;

            // The summary is the library's own request, not a continuation of the caller's turn.
            // Left attached, the caller's per-request context would rewrite it: RequestMessageOverride
            // replaces the last message of every outgoing request (see GetLatestMessages), and this
            // request is one message long — the summarization prompt. What came back would be an
            // ordinary answer to the caller's question, stored as the conversation summary while the
            // messages it was supposed to summarize get deleted.
            var restoreContext = SuppressRequestContext();
            try
            {
                var summaryResult = await GetCompletionAsync(prompt, RequestProfiles.Summarization);
                ConversationPolicy.CurrentSummary = summaryResult;

                // Only remove messages when there are messages beyond KeepRecent
                if (messagesToSummarize.Count > 0)
                {
                    for (int i = keepFromIndex - 1; i >= 0; i--)
                    {
                        ActivateChat.Messages.RemoveAt(i);
                    }
                }
            }
            finally
            {
                restoreContext();
                _isSummarizing = false;
            }

            if (!force) return SummaryCompactionResult.Applied(messagesToSummarize.Count, 0, 0);

            return SummaryCompactionResult.Applied(messagesToSummarize.Count, sentBefore, sentAfter);
        }

        /// <summary>
        /// Pulls the cut point back so forced compaction cannot produce a request the server will
        /// reject for a *different* reason. Two boundaries are load-bearing:
        /// <list type="bullet">
        /// <item>the current turn's user message — deleting it yields a request with no user turn,
        /// which some OpenAI-compatible servers reject outright ("No user query found in messages");</item>
        /// <item>a function-call / function-result pair — a bare tool result whose originating
        /// assistant turn was cut is malformed for the chat/completions wire format.</item>
        /// </list>
        /// </summary>
        private static int ClampKeepIndexForForcedCompaction(IList<Message> messages, int keepFromIndex)
        {
            if (keepFromIndex <= 0) return 0;
            if (keepFromIndex > messages.Count) keepFromIndex = messages.Count;

            // Never cut past the last user message.
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role != ActorRole.User) continue;
                if (keepFromIndex > i) keepFromIndex = i;
                break;
            }

            return ClampKeepIndexForFunctionPair(messages, keepFromIndex);
        }

        private static int ClampKeepIndexForFunctionPair(
            IList<Message> messages,
            int keepFromIndex)
        {
            if (keepFromIndex <= 0) return 0;
            if (keepFromIndex > messages.Count) keepFromIndex = messages.Count;

            // Walk back off any function result so its originating assistant turn stays with it.
            while (keepFromIndex > 0 && keepFromIndex < messages.Count &&
                   messages[keepFromIndex].Role == ActorRole.Function)
            {
                keepFromIndex--;
            }

            return keepFromIndex;
        }

        private static IList<Message> Take(IList<Message> messages, int count)
        {
            var result = new List<Message>(count);
            for (int i = 0; i < count && i < messages.Count; i++)
                result.Add(messages[i]);
            return result;
        }

        private static string BuildSummaryPrompt(IList<Message> messages, string? existingSummary)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Please summarize the following conversation concisely while preserving key information, decisions, and context.");
            sb.AppendLine("Output ONLY the summary, no explanation or preamble.");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(existingSummary))
            {
                sb.AppendLine("[Existing summary]");
                sb.AppendLine(existingSummary);
                sb.AppendLine();
                sb.AppendLine("[New messages to incorporate]");
            }
            else
            {
                sb.AppendLine("[Conversation to summarize]");
            }

            foreach (var msg in messages)
            {
                sb.AppendLine($"{msg.Role}: {msg.GetDisplayText()}");
            }

            return sb.ToString();
        }
    }
}
