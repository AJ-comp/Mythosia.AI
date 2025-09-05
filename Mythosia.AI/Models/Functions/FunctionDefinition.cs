using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mythosia.AI.Models.Enums;

namespace Mythosia.AI.Models.Functions
{
    /// <summary>
    /// Represents a function that can be called by AI
    /// </summary>
    public class FunctionDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public FunctionParameters Parameters { get; set; }
        public Func<Dictionary<string, object>, Task<string>> Handler { get; set; }

        public FunctionDefinition()
        {
            Parameters = new FunctionParameters();
        }
    }

    /// <summary>
    /// Represents function parameters schema
    /// </summary>
    public class FunctionParameters
    {
        public string Type { get; set; } = "object";
        public Dictionary<string, ParameterProperty> Properties { get; set; }
        public List<string> Required { get; set; }

        public FunctionParameters()
        {
            Properties = new Dictionary<string, ParameterProperty>();
            Required = new List<string>();
        }
    }

    /// <summary>
    /// Represents a single parameter property
    /// </summary>
    public class ParameterProperty
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public List<string> Enum { get; set; }
        public object Default { get; set; }
    }

    /// <summary>
    /// Represents a function call from AI
    /// </summary>
    public class FunctionCall
    {
        public string Name { get; set; }
        public Dictionary<string, object> Arguments { get; set; }

        /// <summary>
        /// Unified ID for internal use (always generated)
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Provider-specific ID (e.g., call_id for OpenAI, tool_use_id for Claude)
        /// </summary>
        public string ProviderSpecificId { get; set; }

        /// <summary>
        /// The provider that generated this function call
        /// </summary>
        public AIProvider? Provider { get; set; }

        public FunctionCall()
        {
            Id = Guid.NewGuid().ToString();
            Arguments = new Dictionary<string, object>();
        }

        // Legacy property for backward compatibility
        [Obsolete("Use Id or ProviderSpecificId instead")]
        public string CallId
        {
            get => ProviderSpecificId;
            set => ProviderSpecificId = value;
        }
    }

    /// <summary>
    /// Function call mode
    /// </summary>
    public enum FunctionCallMode
    {
        None,   // Don't call functions
        Auto,   // AI decides when to call
    }

    /// <summary>
    /// Message metadata keys for standardization
    /// </summary>
    public static class MessageMetadataKeys
    {
        public const string FunctionCallId = "function_call_id";  // Unified ID
        public const string FunctionName = "function_name";
        public const string FunctionArguments = "function_arguments";
        public const string MessageType = "message_type";  // "function_call" or "function_result"

        // Provider-specific IDs
        public const string OpenAICallId = "openai_call_id";
        public const string ClaudeToolUseId = "claude_tool_use_id";
    }
}