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
        Completion      // Stream completed
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