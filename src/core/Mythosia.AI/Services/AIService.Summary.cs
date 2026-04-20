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
        public async Task ApplySummaryPolicyIfNeededAsync()
        {
            if (_isSummarizing) return;
            if (ConversationPolicy == null) return;
            if (StatelessMode) return;
            if (!ConversationPolicy.ShouldSummarize(ActivateChat.Messages, LastKnownInputTokens)) return;

            var (messagesToSummarize, keepFromIndex) = ConversationPolicy.GetMessagesToSummarize(ActivateChat.Messages);

            // When trigger fires, always generate summary.
            // If KeepRecent >= message count, summarize all messages but don't delete any.
            var msgsForSummary = messagesToSummarize.Count > 0
                ? messagesToSummarize
                : (IList<Message>)ActivateChat.Messages;

            var prompt = BuildSummaryPrompt(msgsForSummary, ConversationPolicy.CurrentSummary);

            _isSummarizing = true;
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
                _isSummarizing = false;
            }
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
