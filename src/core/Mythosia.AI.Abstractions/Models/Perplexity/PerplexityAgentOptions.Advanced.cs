using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models.Perplexity
{
    /// <summary>Server processing preference; the selected model may use its default tier instead.</summary>
    public enum PerplexityServiceTier { Auto, Default, Flex, Priority }

    /// <summary>A saved Perplexity configuration. Pin Version for reproducible server settings.</summary>
    public sealed class PerplexityProfile
    {
        public string Id { get; set; } = string.Empty;
        public string? Version { get; set; }
        public PerplexityProfile Clone() => new PerplexityProfile { Id = Id, Version = Version };
    }

    public enum PerplexitySkillType { Builtin, Inline, Custom }

    /// <summary>On-demand server instructions. Custom skills are uploaded in the Perplexity portal.</summary>
    public sealed class PerplexitySkill
    {
        public PerplexitySkillType Type { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Id { get; set; }
        public string? Version { get; set; }
        public PerplexitySkill Clone() => (PerplexitySkill)MemberwiseClone();
    }

    public sealed partial class PerplexityAgentOptions
    {
        /// <summary>Fallback models in priority order, at most five. Overrides the single model.</summary>
        public IReadOnlyList<string>? Models { get; set; }
        /// <summary>A saved configuration. Cannot be combined with Preset.</summary>
        public PerplexityProfile? Profile { get; set; }
        public PerplexityServiceTier? ServiceTier { get; set; }
        /// <summary>Controls retrieval visibility, not server persistence. False does not disable retention.</summary>
        public bool? Store { get; set; }
        /// <summary>Completed response to continue. Use StatelessMode and send only the new turn.</summary>
        public string? PreviousResponseId { get; set; }
        public string? LanguagePreference { get; set; }
        public string? PromptCacheKey { get; set; }
        public IReadOnlyList<PerplexitySkill>? Skills { get; set; }

        partial void CopyAdvancedTo(PerplexityAgentOptions copy)
        {
            copy.Models = Models?.ToArray();
            copy.Profile = Profile?.Clone();
            copy.ServiceTier = ServiceTier;
            copy.Store = Store;
            copy.PreviousResponseId = PreviousResponseId;
            copy.LanguagePreference = LanguagePreference;
            copy.PromptCacheKey = PromptCacheKey;
            copy.Skills = Skills?.Select(skill => skill?.Clone() ?? throw new ArgumentException("Skills cannot contain null.")).ToArray();
        }
    }
}
