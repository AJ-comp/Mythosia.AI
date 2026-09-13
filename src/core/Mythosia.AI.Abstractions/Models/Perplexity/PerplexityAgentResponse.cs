using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Models.Perplexity
{
    /// <summary>A server response snapshot, including unsuccessful and in-progress states.</summary>
    public sealed class PerplexityAgentResponse
    {
        public string Id { get; internal set; } = string.Empty;
        public string Status { get; internal set; } = string.Empty;
        public string? Model { get; internal set; }
        public string Text { get; internal set; } = string.Empty;
        public TokenUsage? Usage { get; internal set; }
        public string? Error { get; internal set; }
        public IReadOnlyList<AICitation> Citations { get; internal set; } = Array.Empty<AICitation>();
        /// <summary>The complete provider output array. Includes hosted-tool traces and generated-file references.</summary>
        public string OutputJson { get; internal set; } = "[]";
        public bool IsTerminal => Status == "completed" || Status == "failed" || Status == "cancelled" || Status == "incomplete";
    }

    /// <summary>One durable Agent event. Save SequenceNumber to resume after a connection loss.</summary>
    public sealed class PerplexityAgentEvent
    {
        public string Type { get; internal set; } = string.Empty;
        public long? SequenceNumber { get; internal set; }
        public string? TextDelta { get; internal set; }
        public PerplexityAgentResponse? Response { get; internal set; }
        /// <summary>Exact event JSON, including hosted-tool outputs not represented by text fields.</summary>
        public string Json { get; internal set; } = string.Empty;
    }

    /// <summary>A file produced by the server sandbox. Content is downloaded explicitly.</summary>
    public sealed class PerplexityResponseFile
    {
        public string Id { get; internal set; } = string.Empty;
        public string FileName { get; internal set; } = string.Empty;
        public long Bytes { get; internal set; }
        public long? CreatedAt { get; internal set; }
    }
}
