using System.Text.RegularExpressions;

namespace Mythosia.AI.Tests;

/// <summary>Explicit, test-only configuration for existing synthetic API resources.</summary>
internal sealed class PerplexityResourceLiveSettings
{
    private const string Prefix = "MYTHOSIA_PERPLEXITY_";

    internal string Id { get; }
    internal string? Version { get; }
    internal string ExpectedText { get; }
    internal string? ConnectorServerLabel { get; }
    internal string? ConnectorTool { get; }
    internal string? ConnectorPrompt { get; }

    private PerplexityResourceLiveSettings(string id, string? version, string expectedText,
        string? connectorServerLabel = null, string? connectorTool = null, string? connectorPrompt = null)
    {
        Id = id;
        Version = version;
        ExpectedText = expectedText;
        ConnectorServerLabel = connectorServerLabel;
        ConnectorTool = connectorTool;
        ConnectorPrompt = connectorPrompt;
    }

    internal static PerplexityResourceLiveSettings Load(string resource)
        => Load(resource, Environment.GetEnvironmentVariable);

    internal static PerplexityResourceLiveSettings Load(string resource, Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        // Check consent before looking at resource settings. Callers must load these settings
        // before reading a secret or constructing any live HTTP probe.
        if (readEnvironment(Prefix + "RESOURCE_LIVE") != "1")
            throw new InvalidOperationException(Prefix + "RESOURCE_LIVE must be set to 1 before resource tests can run.");

        if (string.Equals(resource, "Profile", StringComparison.OrdinalIgnoreCase))
            return new PerplexityResourceLiveSettings(Required("PROFILE_ID"), Optional("PROFILE_VERSION"), Required("PROFILE_EXPECTED_TEXT"));
        if (string.Equals(resource, "CustomSkill", StringComparison.OrdinalIgnoreCase))
            return new PerplexityResourceLiveSettings(Required("CUSTOM_SKILL_ID"), Optional("CUSTOM_SKILL_VERSION"), Required("CUSTOM_SKILL_EXPECTED_TEXT"));
        if (!string.Equals(resource, "Connector", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose one resource scenario: Profile, CustomSkill or Connector.", nameof(resource));

        var id = Required("CONNECTOR_ID");
        var label = Required("CONNECTOR_SERVER_LABEL");
        if (!Regex.IsMatch(label, "\\A[a-zA-Z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException(Prefix + "CONNECTOR_SERVER_LABEL must contain 1 to 64 ASCII letters, digits, underscores or hyphens.");
        var tool = Required("CONNECTOR_TOOL");
        var prompt = Required("CONNECTOR_PROMPT");
        var expectedText = Required("CONNECTOR_EXPECTED_TEXT");
        if (prompt.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Prefix + "CONNECTOR_PROMPT must not contain the hidden marker from " + Prefix + "CONNECTOR_EXPECTED_TEXT.");
        return new PerplexityResourceLiveSettings(id, null, expectedText, label, tool, prompt);

        string Required(string name)
        {
            var value = readEnvironment(Prefix + name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(Prefix + name + " must be provided and cannot be empty or whitespace.");
            return value;
        }

        string? Optional(string name)
        {
            var value = readEnvironment(Prefix + name);
            if (value != null && string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(Prefix + name + " must be omitted or contain a nonblank value.");
            return value;
        }
    }
}
