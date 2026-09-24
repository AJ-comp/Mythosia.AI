using System.Text.Json;
using Mythosia.AI.Rag.Evaluation;

namespace Mythosia.AI.Rag.Evaluation.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class DatasetLoaderTests
{
    private string _root = "";

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "Mythosia.Rag.Evaluation.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Mythosia.Rag.Evaluation.Tests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(expectedRoot, StringComparison.Ordinal) && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void VersionedDataset_LoadsGradedLabelsFiltersAndBothDocumentSources()
    {
        File.WriteAllText(Path.Combine(_root, "manual.md"), "한글 C++ manual");
        var dataset = Valid() with
        {
            Documents =
            [
                new() { Id = "a", Path = "manual.md", Metadata = new() { ["tenant"] = "A" } },
                new() { Id = "b", Text = "inline" }
            ],
            Cases = [new() { Id = "case", Query = "C++", Language = "ko", Filter = new() { ["tenant"] = "A" }, Judgments = new() { ["a"] = 3, ["b"] = 0 } }]
        };
        var loaded = Load(dataset, root: _root + Path.DirectorySeparatorChar);
        Assert.AreEqual("한글 C++ manual", loaded.Documents[0].Text);
        Assert.AreEqual("inline", loaded.Documents[1].Text);
        Assert.AreEqual("A", loaded.Documents[0].Metadata["tenant"]);
        Assert.AreEqual(3, loaded.Dataset.Cases[0].Judgments["a"]);
        Assert.AreEqual("1.0", loaded.Dataset.Version);
        Assert.AreEqual(64, loaded.Fingerprint.Length);
    }

    [TestMethod]
    public void LegacyDataset_NormalizesPagesToDocumentsWithBinaryJudgments()
    {
        File.WriteAllText(Path.Combine(_root, "a.md"), "content");
        var path = Write("""
            {"description":"legacy","sources":["a.md"],"cases":[{"id":"q","category":"identifier","query":"question","relevantSources":["a.md"]}]}
            """);
        var loaded = DatasetLoader.Load(path, _root);
        Assert.AreEqual("legacy-1", loaded.Dataset.Version);
        Assert.AreEqual("a.md", loaded.Documents[0].Id);
        Assert.AreEqual(1, loaded.Dataset.Cases[0].Judgments["a.md"]);
    }

    [TestMethod]
    public void EmptyJudgments_AreExplicitlyAllowed_AndMissingJudgmentsAreRejected()
    {
        var dataset = Valid() with { Cases = [new() { Id = "q", Query = "no answer", Judgments = [] }] };
        Assert.AreEqual(0, Load(dataset).Dataset.Cases[0].Judgments.Count);
        var missing = """{"schemaVersion":1,"id":"test","version":"1","documents":[{"id":"a","text":"x"}],"cases":[{"id":"q","query":"x"}]}""";
        Assert.ThrowsExactly<InvalidDataException>(() => DatasetLoader.Load(Write(missing), _root));
    }

    [TestMethod]
    public void Fingerprint_ChangesWhenFileContentsLabelsOrVersionChange()
    {
        var docPath = Path.Combine(_root, "manual.md");
        File.WriteAllText(docPath, "before");
        var dataset = Valid() with { Documents = [new() { Id = "a", Path = "manual.md" }] };
        var before = Load(dataset).Fingerprint;
        File.WriteAllText(docPath, "after");
        var after = Load(dataset).Fingerprint;
        Assert.AreNotEqual(before, after);
        Assert.AreNotEqual(after, Load(dataset with { Version = "2" }).Fingerprint);
        Assert.AreNotEqual(after, Load(dataset with { Cases = [dataset.Cases[0] with { Judgments = new() { ["a"] = 2 } }] }).Fingerprint);
    }

    [TestMethod]
    public void Fingerprint_IgnoresDictionaryKeyOrderAndCheckoutLocation()
    {
        var first = Valid() with { Documents = [new() { Id = "a", Text = "same", Metadata = new() { ["z"] = "1", ["a"] = "2" } }] };
        var second = first with { Documents = [first.Documents[0] with { Metadata = new() { ["a"] = "2", ["z"] = "1" } }] };
        Assert.AreEqual(Load(first).Fingerprint, Load(second).Fingerprint);
        File.WriteAllText(Path.Combine(_root, "same.md"), "same");
        var fileDocument = second with { Documents = [second.Documents[0] with { Path = "same.md", Text = null }] };
        Assert.AreEqual(Load(second).Fingerprint, Load(fileDocument).Fingerprint);
    }

    [TestMethod]
    [DataRow("../outside.md")]
    [DataRow("nested/../../outside.md")]
    [DataRow("..\\outside.md")]
    [DataRow("C:\\outside.md")]
    [DataRow("/outside.md")]
    [DataRow("\\\\server\\share\\outside.md")]
    [DataRow("file.txt:stream")]
    public void AbsoluteAndEscapingPaths_AreRejected(string path)
    {
        var dataset = Valid() with { Documents = [new() { Id = "a", Path = path }] };
        Assert.ThrowsExactly<InvalidDataException>(() => Load(dataset));
    }

    [TestMethod]
    public void DuplicateDocumentAndCaseIds_AreRejected()
    {
        var dataset = Valid();
        Assert.ThrowsExactly<InvalidDataException>(() => Load(dataset with { Documents = [dataset.Documents[0], dataset.Documents[0]] }));
        Assert.ThrowsExactly<InvalidDataException>(() => Load(dataset with { Cases = [dataset.Cases[0], dataset.Cases[0]] }));
    }

    [TestMethod]
    public void UnknownDocumentsAndInvalidGrades_AreRejected()
    {
        foreach (var judgments in new[] { new Dictionary<string, int> { ["missing"] = 1 }, new() { ["a"] = -1 }, new() { ["a"] = 31 } })
            Assert.ThrowsExactly<InvalidDataException>(() => Load(Valid() with { Cases = [new() { Id = "q", Query = "q", Judgments = judgments }] }));
    }

    [TestMethod]
    public void RelevantDocumentOutsideMetadataFilter_IsRejected()
    {
        var dataset = Valid() with { Cases = [new() { Id = "q", Query = "q", Filter = new() { ["tenant"] = "other" }, Judgments = new() { ["a"] = 1 } }] };
        Assert.ThrowsExactly<InvalidDataException>(() => Load(dataset));
    }

    [TestMethod]
    public void DuplicateJsonKeys_AreRejectedInsteadOfOverwritingLabels()
    {
        var path = Write("""{"schemaVersion":1,"id":"x","version":"1","documents":[{"id":"a","text":"a"}],"cases":[{"id":"q","query":"q","judgments":{"a":1,"a":0}}]}""");
        Assert.ThrowsExactly<InvalidDataException>(() => DatasetLoader.Load(path, _root));
    }

    [TestMethod]
    public void UnknownSchemaAndMisspelledProperties_AreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Load(Valid() with { SchemaVersion = 999 }));
        var typo = """{"schemaVersion":1,"id":"x","version":"1","documents":[{"id":"a","text":"a"}],"cases":[{"id":"q","query":"q","judgments":{"a":1},"filtre":{"tenant":"A"}}]}""";
        Assert.ThrowsExactly<JsonException>(() => DatasetLoader.Load(Write(typo), _root));
    }

    [TestMethod]
    public void BothPathAndText_OrNeitherSource_AreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Load(Valid() with { Documents = [new() { Id = "a", Path = "a", Text = "a" }] }));
        Assert.ThrowsExactly<InvalidDataException>(() => Load(Valid() with { Documents = [new() { Id = "a" }] }));
    }

    private static EvaluationDataset Valid() => new()
    {
        Id = "test", Version = "1.0", Documents = [new() { Id = "a", Text = "document" }],
        Cases = [new() { Id = "case", Query = "query", Judgments = new() { ["a"] = 1 } }]
    };

    private LoadedDataset Load(EvaluationDataset dataset, string? root = null) =>
        DatasetLoader.Load(Write(JsonSerializer.Serialize(dataset, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })), root ?? _root);
    private string Write(string json)
    {
        var path = Path.Combine(_root, "dataset.json");
        File.WriteAllText(path, json);
        return path;
    }
}
