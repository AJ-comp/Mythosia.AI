using Mythosia.Documents.Hwp;

namespace Mythosia.Documents.Tests.Hwp;

[TestClass]
public class HwpDocumentLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_SimpleFile_ReturnsOneDocument()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var loader = new HwpDocumentLoader();
        var docs = await loader.LoadAsync(path);

        Assert.AreEqual(1, docs.Count);
    }

    [TestMethod]
    public async Task LoadAsync_SetsSourceMetadata()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var loader = new HwpDocumentLoader();
        var docs = await loader.LoadAsync(path);
        var doc = docs[0];

        Assert.AreEqual(path, doc.Source);
        Assert.AreEqual("hwp", doc.Metadata["type"]);
        Assert.AreEqual("simple.hwp", doc.Metadata["filename"]);
        Assert.AreEqual(".hwp", doc.Metadata["extension"]);
        Assert.AreEqual("HwpParser", doc.Metadata["parser"]);
    }

    [TestMethod]
    public async Task LoadAsync_EmptySource_ThrowsArgumentException()
    {
        var loader = new HwpDocumentLoader();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => loader.LoadAsync(""));
    }

    [TestMethod]
    public async Task LoadAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var loader = new HwpDocumentLoader();

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => loader.LoadAsync("nonexistent.hwp"));
    }

    [TestMethod]
    public async Task LoadAsync_NonHwpFile_ThrowsNotSupportedException()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var txtPath = Path.Combine(temp.Path, "test.docx");
        File.WriteAllText(txtPath, "dummy content");

        var loader = new HwpDocumentLoader();

        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            () => loader.LoadAsync(txtPath));
    }

    [TestMethod]
    public void Constructor_BothParserAndOptions_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new HwpDocumentLoader(new HwpParser(), new HwpParserOptions()));
    }

    [TestMethod]
    public async Task LoadAsync_WithOptions_PassesOptionsToParser()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var loader = new HwpDocumentLoader(options: new HwpParserOptions { IncludeMetadata = false });
        var docs = await loader.LoadAsync(path);
        var doc = docs[0];

        // Loader always adds these, but parser-level metadata should be absent
        Assert.IsTrue(doc.Metadata.ContainsKey("type"), "Loader metadata should be present");
        Assert.IsFalse(doc.Metadata.ContainsKey("section_count"),
            "Parser metadata should be absent when IncludeMetadata=false");
    }

    [TestMethod]
    public async Task LoadAsync_DocumentName_MatchesFilenameWithoutExtension()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path, "my_report.hwp");

        var loader = new HwpDocumentLoader();
        var docs = await loader.LoadAsync(path);

        Assert.AreEqual("my_report", docs[0].Name);
    }

    [TestMethod]
    public async Task LoadAsync_DoclingDocumentHasTextsAndBodyTree()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var loader = new HwpDocumentLoader();
        var docs = await loader.LoadAsync(path);
        var doc = docs[0];

        Assert.IsTrue(doc.Texts.Count >= 3, "Should extract paragraphs");
        Assert.IsNotNull(doc.Body, "Body root should exist");
        Assert.IsTrue(doc.Body.Children.Count > 0, "Body should have children");
    }

    [TestMethod]
    public async Task LoadAsync_WithTable_DoclingDocumentContainsTable()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateHwpWithTable(temp.Path);

        var loader = new HwpDocumentLoader();
        var docs = await loader.LoadAsync(path);
        var doc = docs[0];

        Assert.IsTrue(doc.Tables.Count >= 1, "Should extract table");
        Assert.IsTrue(doc.Texts.Count >= 1, "Should extract text paragraphs alongside table");
    }

    [TestMethod]
    public async Task LoadAsync_ToMarkdown_IntegrationCheck()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var loader = new HwpDocumentLoader();
        var docs = await loader.LoadAsync(path);
        var markdown = docs[0].ToMarkdown();

        Assert.IsFalse(string.IsNullOrWhiteSpace(markdown));
        Assert.IsTrue(markdown.Contains("첫 번째 문단"));
        Assert.IsTrue(markdown.Contains("두 번째 문단"));
        Assert.IsTrue(markdown.Contains("세 번째 문단"));
    }
}
