// Mythosia.AI/Models/Streaming/StreamingContent.cs
using System.Collections.Generic;
using Mythosia.AI.Models.Functions;

namespace Mythosia.AI.Models.Streaming
{
    public class StreamingContent
    {
        /// <summary>
        /// The actual text content being streamed
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Type of the streaming content
        /// </summary>
        public StreamingContentType Type { get; set; }

        /// <summary>
        /// Additional metadata about the stream
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Token usage information (always populated when the provider returns usage data)
        /// </summary>
        public TokenUsage? Usage { get; set; }

        /// <summary>
        /// One-based LLM round index for round-scoped events.
        /// </summary>
        public int? RoundIndex { get; set; }

        /// <summary>
        /// True when this event describes the final LLM round in the current stream.
        /// </summary>
        public bool IsFinalRound { get; set; }

        /// <summary>
        /// Typed function call associated with this event, when applicable.
        /// </summary>
        public FunctionCall? FunctionCall { get; set; }

        /// <summary>
        /// Typed function result associated with this event, when applicable.
        /// </summary>
        public FunctionCallResult? FunctionResult { get; set; }

        /// <summary>
        /// Identifies all calls and results that belong to the same assistant turn.
        /// </summary>
        public string? FunctionCallBatchId { get; set; }

    }

    public enum StreamingContentType
    {
        Text,           // Regular text content
        Reasoning,      // Reasoning/thinking content (GPT-5, o3, etc.)
        FunctionCall,   // Function is being called
        FunctionResult, // Function execution result
        Status,         // Status message
        Error,          // Error occurred
        Completion,     // Stream completed
        RoundUsage      // Token usage for one LLM round
    }

}
