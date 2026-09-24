using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models
{
    /// <summary>Requested reasoning effort. Providers validate the levels supported by the selected model.</summary>
    public enum ReasoningLevel { Auto, None, Minimal, Low, Medium, High, XHigh, Max }

    /// <summary>Whether a reasoning change must preserve the eligible conversation cache prefix.</summary>
    public enum CachePreservation
    {
        /// <summary>No cache-preserving change mechanism is required.</summary>
        None,
        /// <summary>Require the provider's cache-preserving change mechanism. This does not guarantee a cache hit.</summary>
        Required
    }

    /// <summary>Reasoning settings for the next logical request, including its tool rounds and format repairs.</summary>
    public sealed class ReasoningOptions
    {
        public ReasoningLevel Level { get; set; } = ReasoningLevel.Auto;
        public CachePreservation Cache { get; set; }
        public ReasoningOptions Clone() => new ReasoningOptions { Level = Level, Cache = Cache };
    }

    /// <summary>Options for the provider's hosted web search tool.</summary>
    public sealed class WebSearchOptions
    {
        /// <summary>Optional provider-supported domain allowlist. Unsupported restrictions are rejected.</summary>
        public IReadOnlyList<string>? AllowedDomains { get; set; }
        public WebSearchOptions Clone() => new WebSearchOptions { AllowedDomains = AllowedDomains?.ToArray() };
    }

    /// <summary>An existing search store owned by one provider/account. Store creation and uploads are managed separately.</summary>
    public sealed class FileSearchStore
    {
        public string Provider { get; }
        public string Id { get; }

        public FileSearchStore(string provider, string id)
        {
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("A provider is required.", nameof(provider));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A store ID is required.", nameof(id));
            Provider = provider;
            Id = id;
        }
    }

    /// <summary>Existing provider-hosted document stores searched during a request.</summary>
    public sealed class FileSearchOptions
    {
        public IReadOnlyList<FileSearchStore> Stores { get; set; } = Array.Empty<FileSearchStore>();
        public FileSearchOptions Clone() => new FileSearchOptions { Stores = Stores?.ToArray() ?? throw new ArgumentException("Stores cannot be null.") };
    }

    /// <summary>Provider-neutral request options. A service copies these options before using them.</summary>
    public sealed class AIRequestFeatures
    {
        /// <summary>Optional provider processing-mode override, captured for one logical request.</summary>
        public InferenceSpeed? Speed { get; set; }
        public ReasoningOptions? Reasoning { get; set; }
        public WebSearchOptions? WebSearch { get; set; }
        public FileSearchOptions? FileSearch { get; set; }
        public bool IsEmpty => Reasoning == null && WebSearch == null && FileSearch == null &&
            (!Speed.HasValue || Speed == InferenceSpeed.ProviderDefault);
        public AIRequestFeatures Clone() => new AIRequestFeatures
        {
            Reasoning = Reasoning?.Clone(), WebSearch = WebSearch?.Clone(), FileSearch = FileSearch?.Clone(), Speed = Speed
        };
    }
}
