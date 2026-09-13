using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models.Perplexity
{
    /// <summary>A provider preset, selected independently from a model.</summary>
    public enum PerplexityPreset { Fast, Low, Medium, High, XHigh, WideResearch }

    /// <summary>Persistent Agent API configuration. Each logical request captures a deep copy.</summary>
    public sealed partial class PerplexityAgentOptions
    {
        public PerplexityPreset? Preset { get; set; }
        /// <summary>Explicit model override, including when a preset is selected.</summary>
        public string? ModelOverride { get; set; }
        /// <summary>Zero uses the provider default. Positive values bound the hosted agent loop.</summary>
        public int MaxSteps { get; set; }
        /// <summary>Auto omits the setting. Other levels require support from the selected model.</summary>
        public ReasoningLevel ReasoningEffort { get; set; } = ReasoningLevel.Auto;
        /// <summary>Disable the adapter's default web search tool. Explicit Tools still apply.</summary>
        public bool DisableWebSearch { get; set; }
        /// <summary>Explicit hosted tools. A web_search entry replaces the adapter's default entry.</summary>
        public List<PerplexityHostedTool> Tools { get; set; } = new List<PerplexityHostedTool>();

        public PerplexityAgentOptions Clone()
        {
            var copy = new PerplexityAgentOptions
            {
                Preset = Preset, ModelOverride = ModelOverride, MaxSteps = MaxSteps,
                ReasoningEffort = ReasoningEffort, DisableWebSearch = DisableWebSearch,
                Tools = (Tools ?? throw new ArgumentException("Tools cannot be null.")).Select(tool =>
                    (tool ?? throw new ArgumentException("Tools cannot contain null.")).Clone()).ToList()
            };
            CopyAdvancedTo(copy);
            return copy;
        }

        partial void CopyAdvancedTo(PerplexityAgentOptions copy);
    }

    /// <summary>A hosted tool and its documented provider parameters. Custom client functions use Functions.</summary>
    public sealed class PerplexityHostedTool
    {
        public string Type { get; set; } = "web_search";
        /// <summary>Additional JSON-compatible parameters, for example filters, search_context_size or server_url.</summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

        public PerplexityHostedTool Clone() => new PerplexityHostedTool
        {
            Type = Type,
            Parameters = ObjectGraphSnapshot.CloneDictionary(Parameters ?? throw new ArgumentException("Tool parameters cannot be null."))
        };
    }
}
