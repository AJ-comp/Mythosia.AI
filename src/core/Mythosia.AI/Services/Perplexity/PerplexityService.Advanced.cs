using Mythosia.AI.Models.Perplexity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        partial void ApplyAdvancedAgentOptions(Dictionary<string, object> body, PerplexityAgentOptions options)
        {
            if (options.Profile != null)
            {
                if (options.Preset != null) throw new ArgumentException("Preset and Profile cannot be combined.");
                if (string.IsNullOrWhiteSpace(options.Profile.Id) || options.Profile.Id.Length > 128)
                    throw new ArgumentException("A profile ID of 1 to 128 characters is required.");
                var profile = new Dictionary<string, object> { ["type"] = "custom", ["id"] = options.Profile.Id };
                if (options.Profile.Version != null)
                {
                    if (string.IsNullOrWhiteSpace(options.Profile.Version)) throw new ArgumentException("Profile version cannot be empty.");
                    profile["version"] = options.Profile.Version;
                }
                body["profile"] = profile;
                if (options.ModelOverride == null) body.Remove("model");
            }
            if (options.Models != null)
            {
                if (options.Models.Count < 1 || options.Models.Count > 5 || options.Models.Any(string.IsNullOrWhiteSpace))
                    throw new ArgumentException("Models must contain one to five non-empty Agent model IDs.");
                if (options.Models.Any(model => !model.Contains("/")))
                    throw new ArgumentException("Agent models require provider/model IDs. Legacy Sonar IDs are removed.");
                body["models"] = options.Models.ToArray();
                body.Remove("model");
            }
            if (options.ServiceTier.HasValue)
            {
                if (!Enum.IsDefined(typeof(PerplexityServiceTier), options.ServiceTier.Value)) throw new ArgumentOutOfRangeException(nameof(options.ServiceTier));
                body["service_tier"] = options.ServiceTier.Value.ToString().ToLowerInvariant();
            }
            if (options.Store.HasValue) body["store"] = options.Store.Value;
            if (options.PreviousResponseId != null)
            {
                if (string.IsNullOrWhiteSpace(options.PreviousResponseId)) throw new ArgumentException("PreviousResponseId cannot be empty.");
                if (!RequestStatelessMode) throw new InvalidOperationException("Use StatelessMode with PreviousResponseId to avoid resending conversation history.");
                body["previous_response_id"] = options.PreviousResponseId;
            }
            if (options.LanguagePreference != null)
            {
                if (!Regex.IsMatch(options.LanguagePreference, "^[a-z]{2}$")) throw new ArgumentException("LanguagePreference must be a lowercase ISO 639-1 code.");
                body["language_preference"] = options.LanguagePreference;
            }
            if (options.PromptCacheKey != null)
            {
                if (string.IsNullOrWhiteSpace(options.PromptCacheKey)) throw new ArgumentException("PromptCacheKey cannot be empty.");
                body["prompt_cache_key"] = options.PromptCacheKey;
            }
            if (options.Skills != null)
            {
                if (options.Skills.Count > 16) throw new ArgumentException("At most 16 skills can be used in one request.");
                body["skills"] = options.Skills.Select(SerializeSkill).ToArray();
            }
        }

        private static Dictionary<string, object> SerializeSkill(PerplexitySkill skill)
        {
            if (skill == null || !Enum.IsDefined(typeof(PerplexitySkillType), skill.Type)) throw new ArgumentException("A valid skill type is required.");
            var result = new Dictionary<string, object> { ["type"] = skill.Type.ToString().ToLowerInvariant() };
            if (skill.Type == PerplexitySkillType.Custom)
            {
                if (string.IsNullOrWhiteSpace(skill.Id)) throw new ArgumentException("A custom skill ID is required.");
                result["id"] = skill.Id!;
                if (skill.Version != null)
                {
                    if (string.IsNullOrWhiteSpace(skill.Version)) throw new ArgumentException("Skill version cannot be empty.");
                    result["version"] = skill.Version;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(skill.Name)) throw new ArgumentException("A skill name is required.");
                result["name"] = skill.Name!;
                if (skill.Type == PerplexitySkillType.Inline)
                {
                    if (skill.Name!.Length > 64 || !Regex.IsMatch(skill.Name, "^[a-z0-9]+(-[a-z0-9]+)*$"))
                        throw new ArgumentException("Inline skill names require 1 to 64 lowercase letters, digits, and single hyphens.");
                    if (string.IsNullOrWhiteSpace(skill.Description) || Encoding.UTF8.GetByteCount(skill.Description!) > 1024)
                        throw new ArgumentException("Inline skill descriptions require 1 to 1024 UTF-8 bytes.");
                    if (string.IsNullOrWhiteSpace(skill.Instructions) || Encoding.UTF8.GetByteCount(skill.Instructions!) > 65536)
                        throw new ArgumentException("Inline skill instructions require 1 to 65536 UTF-8 bytes.");
                    result["description"] = skill.Description!;
                    result["instructions"] = skill.Instructions!;
                }
            }
            return result;
        }
    }
}
