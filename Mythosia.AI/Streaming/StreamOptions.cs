using System;

namespace Mythosia.AI.Models.Streaming
{
    /// <summary>
    /// Options for controlling streaming behavior
    /// </summary>
    public class StreamOptions
    {
        /// <summary>
        /// Include metadata in streaming content (default: true when using StreamingContent)
        /// </summary>
        public bool IncludeMetadata { get; set; } = true;

        /// <summary>
        /// Include function calling support (default: true)
        /// </summary>
        public bool IncludeFunctionCalls { get; set; } = true;

        /// <summary>
        /// Include token count information when available (default: false)
        /// </summary>
        public bool IncludeTokenInfo { get; set; } = false;

        /// <summary>
        /// Include reasoning/thinking content from reasoning models like GPT-5, o3 (default: false)
        /// </summary>
        public bool IncludeReasoning { get; set; } = false;

        /// <summary>
        /// Filter out everything except text content (default: false)
        /// </summary>
        public bool TextOnly { get; set; } = false;

        /// <summary>
        /// Default options for simple text streaming
        /// </summary>
        public static StreamOptions TextOnlyOptions => new StreamOptions()
        {
            TextOnly = true,
            IncludeMetadata = false,
            IncludeFunctionCalls = false
        };

        /// <summary>
        /// Default options for full featured streaming
        /// </summary>
        public static StreamOptions FullOptions => new StreamOptions()
        {
            IncludeMetadata = true,
            IncludeFunctionCalls = true,
            IncludeTokenInfo = true,
            IncludeReasoning = true
        };

        /// <summary>
        /// Creates default options
        /// </summary>
        public static StreamOptions Default => new StreamOptions();

        /// <summary>
        /// Creates options for function calling scenarios
        /// </summary>
        public static StreamOptions WithFunctions => new StreamOptions()
        {
            IncludeMetadata = true,
            IncludeFunctionCalls = true,
            TextOnly = false
        };

        /// <summary>
        /// Creates options for minimal overhead streaming
        /// </summary>
        public static StreamOptions Minimal => new StreamOptions()
        {
            IncludeMetadata = false,
            IncludeFunctionCalls = false,
            IncludeTokenInfo = false,
            TextOnly = true
        };

        /// <summary>
        /// Creates a copy of these options
        /// </summary>
        public StreamOptions Clone()
        {
            return new StreamOptions()
            {
                IncludeMetadata = this.IncludeMetadata,
                IncludeFunctionCalls = this.IncludeFunctionCalls,
                IncludeTokenInfo = this.IncludeTokenInfo,
                IncludeReasoning = this.IncludeReasoning,
                TextOnly = this.TextOnly
            };
        }

        /// <summary>
        /// Builder pattern for fluent configuration
        /// </summary>
        public StreamOptions WithMetadata(bool include = true)
        {
            IncludeMetadata = include;
            return this;
        }

        /// <summary>
        /// Builder pattern for fluent configuration
        /// </summary>
        public StreamOptions WithFunctionCalls(bool include = true)
        {
            IncludeFunctionCalls = include;
            return this;
        }

        /// <summary>
        /// Builder pattern for fluent configuration
        /// </summary>
        public StreamOptions WithTokenInfo(bool include = true)
        {
            IncludeTokenInfo = include;
            return this;
        }

        /// <summary>
        /// Builder pattern for fluent configuration
        /// </summary>
        public StreamOptions WithReasoning(bool include = true)
        {
            IncludeReasoning = include;
            return this;
        }

        /// <summary>
        /// Builder pattern for fluent configuration
        /// </summary>
        public StreamOptions AsTextOnly(bool textOnly = true)
        {
            TextOnly = textOnly;
            if (textOnly)
            {
                // When text only, disable other features for efficiency
                IncludeMetadata = false;
                IncludeFunctionCalls = false;
                IncludeTokenInfo = false;
                IncludeReasoning = false;
            }
            return this;
        }
    }
}