// Mythosia.AI/Models/Streaming/StreamingContent.cs
using System.Collections.Generic;
using System.Text;

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
        /// For internal use - accumulating function call data
        /// </summary>
        internal FunctionCallData? FunctionCallData { get; set; }
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

    /// <summary>
    /// Internal class for accumulating function call data
    /// </summary>
    internal class FunctionCallData
    {
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new StringBuilder();
        public bool IsComplete { get; set; }
        /// <summary>
        /// Gemini 3 thought signature attached to this function call part.
        /// Must be circulated back in the next request for strict validation.
        /// </summary>
        public string? ThoughtSignature { get; set; }
    }
}
