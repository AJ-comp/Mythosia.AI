using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
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

        private bool _isSummarizing = false;

        /// <summary>
        /// Returns the effective system message including the conversation summary prefix
        /// (if available) and the original system message.
        /// Use this instead of ActivateChat.SystemMessage when building request bodies.
        /// </summary>
        internal string GetEffectiveSystemMessage()
        {
            var baseMsg = ActivateChat?.SystemMessage ?? "";
            var summary = ConversationPolicy?.CurrentSummary;

            if (!string.IsNullOrEmpty(summary))
            {
                var summaryPrefix = $"[Previous conversation summary]\n{summary}";
                if (string.IsNullOrEmpty(baseMsg))
                    return summaryPrefix;
                return $"{summaryPrefix}\n\n{baseMsg}";
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
            if (!ConversationPolicy.ShouldSummarize(ActivateChat.Messages)) return;

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
                var backupStateless = StatelessMode;
                StatelessMode = true;
                try
                {
                    var summaryResult = await GetCompletionAsync(prompt);
                    ConversationPolicy.CurrentSummary = summaryResult;
                }
                finally
                {
                    StatelessMode = backupStateless;
                }

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
