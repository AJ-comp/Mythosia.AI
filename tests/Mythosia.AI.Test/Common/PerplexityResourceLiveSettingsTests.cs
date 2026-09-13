namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class PerplexityResourceLiveSettingsTests
{
    private const string Prefix = "MYTHOSIA_PERPLEXITY_";
    private const string Marker = "fixture_marker_3a4df696";

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("0")]
    [DataRow("true")]
    [DataRow("TRUE")]
    [DataRow(" 1 ")]
    public void ExplicitOptIn_IsRequiredBeforeAnyResourceSettingIsRead(string? flag)
    {
        var reads = new List<string>();
        var error = Assert.Throws<InvalidOperationException>(() => PerplexityResourceLiveSettings.Load("Profile", name =>
        {
            reads.Add(name);
            if (name != Prefix + "RESOURCE_LIVE") Assert.Fail("Resource values were read before explicit opt-in.");
            return flag;
        }));
        CollectionAssert.AreEqual(new[] { Prefix + "RESOURCE_LIVE" }, reads);
        StringAssert.Contains(error.Message, Prefix + "RESOURCE_LIVE");
    }

    [TestMethod]
    [DataRow("pRoFiLe", "PROFILE_ID")]
    [DataRow("cUsToMsKiLl", "CUSTOM_SKILL_ID")]
    [DataRow("cOnNeCtOr", "CONNECTOR_ID")]
    public void ResourceNames_AreCaseInsensitiveAndReturnExactConfiguredValues(string resource, string idName)
    {
        var values = ValidValues();
        var settings = Load(resource, values);
        Assert.AreEqual(values[Prefix + idName], settings.Id);
        Assert.AreEqual(Marker, settings.ExpectedText);
        if (idName == "CONNECTOR_ID")
        {
            Assert.IsNull(settings.Version);
            Assert.AreEqual("fixture_reader-1", settings.ConnectorServerLabel);
            Assert.AreEqual("read_fixture", settings.ConnectorTool);
            Assert.AreEqual("Read the synthetic fixture document and return its stored marker.", settings.ConnectorPrompt);
        }
        else
        {
            Assert.IsNull(settings.Version);
            Assert.IsNull(settings.ConnectorServerLabel);
            Assert.IsNull(settings.ConnectorTool);
            Assert.IsNull(settings.ConnectorPrompt);
        }
    }

    public static IEnumerable<object?[]> MissingRequiredFields
    {
        get
        {
            var fields = new[]
            {
                ("Profile", "PROFILE_ID"), ("Profile", "PROFILE_EXPECTED_TEXT"),
                ("CustomSkill", "CUSTOM_SKILL_ID"), ("CustomSkill", "CUSTOM_SKILL_EXPECTED_TEXT"),
                ("Connector", "CONNECTOR_ID"), ("Connector", "CONNECTOR_SERVER_LABEL"),
                ("Connector", "CONNECTOR_TOOL"), ("Connector", "CONNECTOR_PROMPT"), ("Connector", "CONNECTOR_EXPECTED_TEXT")
            };
            foreach (var (resource, fieldName) in fields)
                foreach (var value in new string?[] { null, "", " \t\r\n" })
                    yield return [resource, fieldName, value];
        }
    }

    [TestMethod]
    [DynamicData(nameof(MissingRequiredFields))]
    public void SelectedResource_RequiresEveryMandatoryNonblankField(string resource, string field, string? value)
    {
        var values = ValidValues();
        values[Prefix + field] = value;
        var error = Assert.Throws<InvalidOperationException>(() => Load(resource, values));
        StringAssert.Contains(error.Message, Prefix + field);
        Assert.IsFalse(error.Message.Contains(Marker, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("unknown-private-value")]
    [DataRow("Profile,Connector")]
    public void UnknownScenario_IsRejectedWithoutEchoingItsValue(string? resource)
    {
        var reads = new List<string>();
        var error = Assert.Throws<ArgumentException>(() => PerplexityResourceLiveSettings.Load(resource!, name =>
        {
            reads.Add(name);
            return "1";
        }));
        CollectionAssert.AreEqual(new[] { Prefix + "RESOURCE_LIVE" }, reads);
        Assert.IsFalse(error.Message.Contains("unknown-private-value", StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains("Profile,Connector", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("Profile", "PROFILE_")]
    [DataRow("CustomSkill", "CUSTOM_SKILL_")]
    [DataRow("Connector", "CONNECTOR_")]
    public void UnselectedResources_AreNeitherRequiredNorRead(string resource, string selectedPrefix)
    {
        var values = ValidValues();
        var settings = PerplexityResourceLiveSettings.Load(resource, name =>
        {
            Assert.IsTrue(name == Prefix + "RESOURCE_LIVE" || name.StartsWith(Prefix + selectedPrefix, StringComparison.Ordinal),
                "Only the selected resource's environment variables may be read.");
            return values.GetValueOrDefault(name);
        });
        Assert.AreEqual(Marker, settings.ExpectedText);
    }

    [TestMethod]
    [DataRow("Profile", "PROFILE_VERSION", null)]
    [DataRow("Profile", "PROFILE_VERSION", "version-7")]
    [DataRow("CustomSkill", "CUSTOM_SKILL_VERSION", null)]
    [DataRow("CustomSkill", "CUSTOM_SKILL_VERSION", "version-7")]
    public void Version_IsOptionalAndPreserved(string resource, string field, string? version)
    {
        var values = ValidValues();
        values[Prefix + field] = version;
        Assert.AreEqual(version, Load(resource, values).Version);
    }

    [TestMethod]
    [DataRow("Profile", "PROFILE_VERSION", "")]
    [DataRow("Profile", "PROFILE_VERSION", " \t")]
    [DataRow("CustomSkill", "CUSTOM_SKILL_VERSION", "")]
    [DataRow("CustomSkill", "CUSTOM_SKILL_VERSION", " \t")]
    public void ExplicitBlankVersion_IsRejected(string resource, string field, string version)
    {
        var values = ValidValues();
        values[Prefix + field] = version;
        var error = Assert.Throws<InvalidOperationException>(() => Load(resource, values));
        StringAssert.Contains(error.Message, Prefix + field);
    }

    [TestMethod]
    [DataRow("before fixture_marker_3a4df696 after")]
    [DataRow("before FIXTURE_MARKER_3A4DF696 after")]
    [DataRow("fixture_marker_3a4df696")]
    public void ConnectorPrompt_CannotRevealExpectedMarkerEvenWithDifferentCasing(string prompt)
    {
        var values = ValidValues();
        values[Prefix + "CONNECTOR_PROMPT"] = prompt;
        var error = Assert.Throws<InvalidOperationException>(() => Load("Connector", values));
        StringAssert.Contains(error.Message, Prefix + "CONNECTOR_PROMPT");
        StringAssert.Contains(error.Message, Prefix + "CONNECTOR_EXPECTED_TEXT");
        Assert.IsFalse(error.Message.Contains(Marker, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(error.Message.Contains(prompt, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("private invalid label")]
    [DataRow("private.invalid.label")]
    [DataRow("한글")]
    [DataRow("label\n")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ConnectorLabel_EnforcesSdkCharacterAndLengthContractWithoutEchoingValue(string label)
    {
        var values = ValidValues();
        values[Prefix + "CONNECTOR_SERVER_LABEL"] = label;
        var error = Assert.Throws<InvalidOperationException>(() => Load("Connector", values));
        StringAssert.Contains(error.Message, Prefix + "CONNECTOR_SERVER_LABEL");
        Assert.IsFalse(error.Message.Contains(label, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("a")]
    [DataRow("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-")]
    public void ConnectorLabel_AcceptsSdkBoundaries(string label)
    {
        var values = ValidValues();
        values[Prefix + "CONNECTOR_SERVER_LABEL"] = label;
        Assert.AreEqual(label, Load("Connector", values).ConnectorServerLabel);
    }

    private static PerplexityResourceLiveSettings Load(string resource, IReadOnlyDictionary<string, string?> values)
        => PerplexityResourceLiveSettings.Load(resource, name => values.GetValueOrDefault(name));

    private static Dictionary<string, string?> ValidValues() => new()
    {
        [Prefix + "RESOURCE_LIVE"] = "1",
        [Prefix + "PROFILE_ID"] = "profile_synthetic",
        [Prefix + "PROFILE_EXPECTED_TEXT"] = Marker,
        [Prefix + "CUSTOM_SKILL_ID"] = "skill_synthetic",
        [Prefix + "CUSTOM_SKILL_EXPECTED_TEXT"] = Marker,
        [Prefix + "CONNECTOR_ID"] = "connector_synthetic",
        [Prefix + "CONNECTOR_SERVER_LABEL"] = "fixture_reader-1",
        [Prefix + "CONNECTOR_TOOL"] = "read_fixture",
        [Prefix + "CONNECTOR_PROMPT"] = "Read the synthetic fixture document and return its stored marker.",
        [Prefix + "CONNECTOR_EXPECTED_TEXT"] = Marker
    };
}
