using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models.Runs
{
    /// <summary>An immutable snapshot of a successfully completed run, independent of output observation.</summary>
    /// <remarks>Cancellation and failures fault or cancel AIRun.Result instead of producing this result.
    /// Mutable usage and citation DTOs are defensively copied on input and access.</remarks>
    public sealed class AIRunResult
    {
        private readonly TokenUsage? _usage;
        private readonly AICitation[] _citations;

        public AIRunResult(string text, TokenUsage? usage = null, IEnumerable<AICitation>? citations = null,
            string provider = "", string? requestedModel = null, string? model = null, int roundCount = 0,
            AIFinishReason finishReason = AIFinishReason.Unknown, string? rawFinishReason = null)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            if (roundCount < 0) throw new ArgumentOutOfRangeException(nameof(roundCount));
            _usage = usage == null ? null : CopyUsage(usage);
            _citations = citations?.Select(citation => citation.Clone()).ToArray() ?? Array.Empty<AICitation>();
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            RequestedModel = requestedModel;
            Model = model;
            RoundCount = roundCount;
            FinishReason = finishReason;
            RawFinishReason = rawFinishReason;
        }

        /// <summary>All text emitted by the run, including intermediate turns and text before steering.</summary>
        public string Text { get; }

        /// <summary>Reported usage accumulated once per LLM round; null if unavailable.</summary>
        /// <remarks>Excludes separate auxiliary calls such as automatic conversation summaries.
        /// If some rounds omit usage, only the reported rounds are included. Returns a defensive copy.
        /// Core round aggregation uses checked integer arithmetic. A count beyond the
        /// Int32 range faults the run with OverflowException instead of returning wrapped usage.</remarks>
        public TokenUsage? Usage => _usage == null ? null : CopyUsage(_usage);

        /// <summary>Provider references retained across rounds. Returns defensive copies in a read-only collection.</summary>
        public IReadOnlyList<AICitation> Citations => Array.AsReadOnly(_citations.Select(citation => citation.Clone()).ToArray());

        /// <summary>The service provider captured at run startup.</summary>
        public string Provider { get; }

        /// <summary>The explicit model ID sent in the captured request, which can differ from the server's
        /// resolved model. Null when no single model ID is sent, for example preset or model-list routing.</summary>
        public string? RequestedModel { get; }

        /// <summary>The final round's actual model reported by the provider; null if unavailable.</summary>
        public string? Model { get; }

        /// <summary>Number of library LLM rounds, not tool calls or provider-internal steps.
        /// Zero means that a custom streaming implementation did not report round information.</summary>
        public int RoundCount { get; }

        /// <summary>The final response's normalized reason. Existing provider error validation still applies.</summary>
        public AIFinishReason FinishReason { get; }

        /// <summary>The provider's original final reason or status, or null if unavailable.</summary>
        public string? RawFinishReason { get; }

        private static TokenUsage CopyUsage(TokenUsage usage) => new TokenUsage
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            TotalTokens = usage.TotalTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheCreationTokens = usage.CacheCreationTokens,
            ReasoningTokens = usage.ReasoningTokens
        };
    }
}
