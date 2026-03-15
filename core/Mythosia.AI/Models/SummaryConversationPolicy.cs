using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models
{
    /// <summary>
    /// Policy that automatically summarizes old conversation messages when the conversation
    /// exceeds a configured threshold (token count, message count, or both).
    /// The summary is stored as a string and injected into the system message on each request.
    /// </summary>
    public class SummaryConversationPolicy
    {
        /// <summary>
        /// When total estimated tokens exceed this value, summarization is triggered.
        /// Null means token-based trigger is disabled.
        /// </summary>
        public uint? TriggerTokens { get; set; }

        /// <summary>
        /// When the message count exceeds this value, summarization is triggered.
        /// Null means count-based trigger is disabled.
        /// </summary>
        public uint? TriggerCount { get; set; }

        /// <summary>
        /// Number of recent tokens to keep after summarization.
        /// Used when KeepRecentCount is null.
        /// </summary>
        public uint? KeepRecentTokens { get; set; }

        /// <summary>
        /// Number of recent messages to keep after summarization.
        /// Takes priority over KeepRecentTokens when set.
        /// </summary>
        public uint? KeepRecentCount { get; set; }

        /// <summary>
        /// The accumulated summary of previous conversations.
        /// Automatically updated after each summarization cycle.
        /// </summary>
        public string? CurrentSummary { get; set; }

        #region Factory Methods

        /// <summary>
        /// Creates a token-based summarization policy.
        /// Triggers when total estimated tokens exceed triggerTokens,
        /// keeps recent messages fitting within keepRecentTokens.
        /// </summary>
        public static SummaryConversationPolicy ByToken(uint triggerTokens, uint keepRecentTokens = 1000)
        {
            return new SummaryConversationPolicy
            {
                TriggerTokens = triggerTokens,
                KeepRecentTokens = keepRecentTokens
            };
        }

        /// <summary>
        /// Creates a message-count-based summarization policy.
        /// Triggers when message count exceeds triggerCount,
        /// keeps the most recent keepRecentCount messages.
        /// </summary>
        public static SummaryConversationPolicy ByMessage(uint triggerCount, uint? keepRecentCount = null)
        {
            var effectiveKeepRecentCount = keepRecentCount ?? 5u;
            if (!keepRecentCount.HasValue && effectiveKeepRecentCount >= triggerCount)
                effectiveKeepRecentCount = triggerCount > 0 ? triggerCount - 1 : 0;

            ValidateKeepRecentCount(triggerCount, effectiveKeepRecentCount);
            return new SummaryConversationPolicy
            {
                TriggerCount = triggerCount,
                KeepRecentCount = effectiveKeepRecentCount
            };
        }

        /// <summary>
        /// Creates a combined policy (OR condition).
        /// Triggers when either token limit or message count is exceeded.
        /// </summary>
        public static SummaryConversationPolicy ByBoth(
            uint triggerTokens,
            uint triggerCount,
            uint? keepRecentTokens = null,
            uint? keepRecentCount = null)
        {
            var effectiveKeepRecentTokens = keepRecentTokens ?? triggerTokens / 3;
            var effectiveKeepRecentCount = keepRecentCount ?? Math.Max(3u, triggerCount / 4);
            if (!keepRecentCount.HasValue && effectiveKeepRecentCount >= triggerCount)
                effectiveKeepRecentCount = triggerCount > 0 ? triggerCount - 1 : 0;
            ValidateKeepRecentCount(triggerCount, effectiveKeepRecentCount);
            return new SummaryConversationPolicy
            {
                TriggerTokens = triggerTokens,
                TriggerCount = triggerCount,
                KeepRecentTokens = effectiveKeepRecentTokens,
                KeepRecentCount = effectiveKeepRecentCount
            };
        }

        #endregion

        #region Core Methods

        /// <summary>
        /// Determines whether the current conversation should be summarized.
        /// Returns true if any configured trigger threshold is exceeded (OR condition).
        /// </summary>
        public bool ShouldSummarize(IList<Message> messages)
        {
            if (messages == null || messages.Count == 0) return false;

            if (TriggerTokens.HasValue)
            {
                var totalTokens = messages.Sum(m => (long)m.EstimateTokens());
                if (totalTokens > TriggerTokens.Value) return true;
            }

            if (TriggerCount.HasValue)
            {
                if (messages.Count > TriggerCount.Value) return true;
            }

            return false;
        }

        /// <summary>
        /// Determines which messages should be summarized and which should be kept.
        /// Returns the list of messages to summarize and the index from which to keep.
        /// </summary>
        public (IList<Message> toSummarize, int keepFromIndex) GetMessagesToSummarize(IList<Message> messages)
        {
            if (messages == null || messages.Count == 0)
                return (new List<Message>(), 0);

            int keepCount = CalculateKeepCount(messages);
            int keepFromIndex = Math.Max(0, messages.Count - keepCount);

            if (keepFromIndex <= 0)
                return (new List<Message>(), 0);

            var toSummarize = new List<Message>();
            for (int i = 0; i < keepFromIndex; i++)
            {
                toSummarize.Add(messages[i]);
            }

            return (toSummarize, keepFromIndex);
        }

        /// <summary>
        /// Loads a previously saved summary for session restoration.
        /// </summary>
        public void LoadSummary(string summary)
        {
            CurrentSummary = summary;
        }

        #endregion

        #region Private Helpers

        private int CalculateKeepCount(IList<Message> messages)
        {
            if (KeepRecentCount.HasValue)
                return (int)KeepRecentCount.Value;

            if (KeepRecentTokens.HasValue)
            {
                uint tokenBudget = KeepRecentTokens.Value;
                uint accumulated = 0;

                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    accumulated += messages[i].EstimateTokens();
                    if (accumulated > tokenBudget)
                        return messages.Count - i - 1;
                }

                // All messages fit within the token budget — nothing to summarize
                return messages.Count;
            }

            // Default: keep last 5 messages
            return 5;
        }

        private static void ValidateKeepRecentCount(uint triggerCount, uint keepRecentCount)
        {
            if (keepRecentCount >= triggerCount)
                throw new ArgumentException("keepRecentCount must be less than triggerCount.", nameof(keepRecentCount));
        }

        #endregion
    }
}
