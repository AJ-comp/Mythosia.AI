using Mythosia.AI.Models.Messages;
using System.Collections.Generic;

namespace Mythosia.AI.Protocols
{
    /// <summary>
    /// Parameters passed from a service to a protocol for building request bodies.
    /// </summary>
    public class ProtocolRequestParams
    {
        public string Model { get; set; } = string.Empty;
        public IEnumerable<Message> Messages { get; set; } = System.Array.Empty<Message>();
        public string? SystemMessage { get; set; }
        public float Temperature { get; set; }
        public float TopP { get; set; }
        public float FrequencyPenalty { get; set; }
        public float PresencePenalty { get; set; }
        public uint MaxTokens { get; set; }
        public bool Stream { get; set; }
        public string? StructuredOutputSchemaJson { get; set; }

        /// <summary>
        /// Additional provider-specific parameters merged into the request body.
        /// </summary>
        public Dictionary<string, object>? ExtraParameters { get; set; }

        /// <summary>
        /// Parameter keys to exclude from the default request body.
        /// Used when a provider rejects certain standard parameters (e.g., reasoning models).
        /// </summary>
        public HashSet<string>? ExcludeParameters { get; set; }
    }
}
