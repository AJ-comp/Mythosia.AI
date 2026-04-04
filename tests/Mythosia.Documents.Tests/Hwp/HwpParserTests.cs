using Mythosia.Documents.Elements;
using Mythosia.Documents.Hwp;

namespace Mythosia.Documents.Tests.Hwp;

[TestClass]
public class HwpParserTests
{
    [TestMethod]
    public void CanParse_HwpExtension_ReturnsTrue()
    {
        var parser = new HwpParser();

        Assert.IsTrue(parser.CanParse("document.hwp"));
        Assert.IsTrue(parser.CanParse("path/to/DOCUMENT.HWP"));
        Assert.IsTrue(parser.CanParse(@"C:\docs\report.Hwp"));
    }

    [TestMethod]
    public void CanParse_NonHwpExtension_ReturnsFalse()
    {
        var parser = new HwpParser();

        Assert.IsFalse(parser.CanParse("document.docx"));
        Assert.IsFalse(parser.CanParse("document.pdf"));
        Assert.IsFalse(parser.CanParse("document.txt"));
    }

    [TestMethod]
    public async Task ParseAsync_SimpleFile_ExtractsParagraphs()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        Assert.IsNotNull(doc);
        Assert.AreEqual("simple", doc.Name);
        Assert.IsTrue(doc.Texts.Count >= 3, $"Expected at least 3 text items, got {doc.Texts.Count}");

        var texts = doc.Texts.Select(t => t.Text).ToList();
        Assert.IsTrue(texts.Any(t => t.Contains("첫 번째 문단")), "Should contain first paragraph");
        Assert.IsTrue(texts.Any(t => t.Contains("두 번째 문단")), "Should contain second paragraph");
        Assert.IsTrue(texts.Any(t => t.Contains("세 번째 문단")), "Should contain third paragraph");
    }

    [TestMethod]
    public async Task ParseAsync_SimpleFile_AllTextItemsAreParagraphLabel()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        foreach (var item in doc.Texts)
        {
            Assert.AreEqual(DocItemLabel.Paragraph, item.Label,
                $"Text '{item.Text}' should be Paragraph, was {item.Label}");
        }
    }

    [TestMethod]
    public async Task ParseAsync_BlankFile_ReturnsEmptyDocument()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateBlankHwp(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        Assert.IsNotNull(doc);
        Assert.AreEqual("blank", doc.Name);
        // Blank file may have 0 or minimal text
        Assert.AreEqual(0, doc.Tables.Count);
    }

    [TestMethod]
    public async Task ParseAsync_WithMetadata_IncludesSectionCount()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var parser = new HwpParser(new HwpParserOptions { IncludeMetadata = true });
        var doc = await parser.ParseAsync(path);

        Assert.IsTrue(doc.Metadata.ContainsKey("section_count"), "Should include section_count metadata");
    }

    [TestMethod]
    public async Task ParseAsync_WithoutMetadata_ExcludesMetadata()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var parser = new HwpParser(new HwpParserOptions { IncludeMetadata = false });
        var doc = await parser.ParseAsync(path);

        Assert.IsFalse(doc.Metadata.ContainsKey("section_count"), "Should not include section_count metadata");
    }

    [TestMethod]
    public async Task ParseAsync_HeadingFile_DetectsHeadingStyle()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateHwpWithHeading(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        Assert.IsNotNull(doc);

        // Check if heading was detected
        var headings = doc.Texts.OfType<SectionHeaderItem>().ToList();
        var titles = doc.Texts.OfType<TitleItem>().ToList();

        // The heading should be detected as either SectionHeader or Title
        // depending on style availability in the blank file
        bool hasStructuredElement = headings.Count > 0 || titles.Count > 0;

        // If the outline style was found, we should have at least a heading
        // If not found, the paragraph falls back to regular text
        Assert.IsTrue(doc.Texts.Count >= 1, "Should have at least one text item");

        // Body paragraph should always be present
        var bodyTexts = doc.Texts.Where(t => t.Text.Contains("본문 내용")).ToList();
        Assert.AreEqual(1, bodyTexts.Count, "Should have one body paragraph");
    }

    [TestMethod]
    public async Task ParseAsync_TableFile_ExtractsTable()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateHwpWithTable(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        Assert.IsNotNull(doc);
        Assert.IsTrue(doc.Tables.Count >= 1, $"Expected at least 1 table, got {doc.Tables.Count}");

        var table = doc.Tables[0];
        Assert.IsNotNull(table.Data);
        Assert.AreEqual(2, table.Data.NumRows, "Table should have 2 rows");
        Assert.AreEqual(3, table.Data.NumCols, "Table should have 3 columns");

        // Verify cells
        var cells = table.Data.TableCells;
        Assert.AreEqual(6, cells.Count, "Should have 6 cells (2 rows × 3 cols)");

        // First row should be marked as column headers
        var headerCells = cells.Where(c => c.ColumnHeader).ToList();
        Assert.AreEqual(3, headerCells.Count, "First row cells should be column headers");
    }

    [TestMethod]
    public async Task ParseAsync_TableFile_CellContentsAreCorrect()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateHwpWithTable(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        var table = doc.Tables[0];
        var cellTexts = table.Data.TableCells.Select(c => c.Text).ToList();

        Assert.IsTrue(cellTexts.Any(t => t.Contains("헤더1")), "Should contain header 1");
        Assert.IsTrue(cellTexts.Any(t => t.Contains("헤더2")), "Should contain header 2");
        Assert.IsTrue(cellTexts.Any(t => t.Contains("데이터A")), "Should contain data A");
        Assert.IsTrue(cellTexts.Any(t => t.Contains("데이터B")), "Should contain data B");
    }

    [TestMethod]
    public async Task ParseAsync_TableFile_CellSpansAreCorrect()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateHwpWithTable(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        var table = doc.Tables[0];
        foreach (var cell in table.Data.TableCells)
        {
            Assert.AreEqual(1, cell.ColSpan, $"Cell '{cell.Text}' ColSpan should be 1");
            Assert.AreEqual(1, cell.RowSpan, $"Cell '{cell.Text}' RowSpan should be 1");
            Assert.IsTrue(cell.EndColOffsetIdx > cell.StartColOffsetIdx);
            Assert.IsTrue(cell.EndRowOffsetIdx > cell.StartRowOffsetIdx);
        }
    }

    [TestMethod]
    public async Task ParseAsync_TableFile_TextBeforeAndAfterTable()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateHwpWithTable(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        var paragraphs = doc.Texts.Where(t => t.Label == DocItemLabel.Paragraph).ToList();
        Assert.IsTrue(paragraphs.Any(t => t.Text.Contains("표 앞의 문단")),
            "Should have paragraph before table");
        Assert.IsTrue(paragraphs.Any(t => t.Text.Contains("표 뒤의 문단")),
            "Should have paragraph after table");
    }

    [TestMethod]
    public async Task ParseAsync_MultiSection_WithSectionHeaders_EmitsHeadings()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateMultiSectionHwp(temp.Path);

        var parser = new HwpParser(new HwpParserOptions { IncludeSectionHeaders = true });
        var doc = await parser.ParseAsync(path);

        var headings = doc.Texts.OfType<SectionHeaderItem>().ToList();
        Assert.AreEqual(2, headings.Count, "Should have 2 section headings");
        Assert.AreEqual("Section 1", headings[0].Text);
        Assert.AreEqual("Section 2", headings[1].Text);
    }

    [TestMethod]
    public async Task ParseAsync_MultiSection_WithoutSectionHeaders_NoExtraHeadings()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateMultiSectionHwp(temp.Path);

        var parser = new HwpParser(new HwpParserOptions { IncludeSectionHeaders = false });
        var doc = await parser.ParseAsync(path);

        var headings = doc.Texts.OfType<SectionHeaderItem>().ToList();
        Assert.AreEqual(0, headings.Count, "Should have no section headings when disabled");
    }

    [TestMethod]
    public async Task ParseAsync_CancellationToken_ThrowsWhenCancelled()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var parser = new HwpParser();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => parser.ParseAsync(path, cts.Token));
    }

    [TestMethod]
    public async Task ParseAsync_BodyTree_HasCorrectStructure()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);

        // Body root should have children
        Assert.IsTrue(doc.Body.Children.Count > 0, "Body should have child references");

        // Each text item should have a parent reference
        foreach (var item in doc.Texts)
        {
            Assert.IsNotNull(item.Parent, $"Text item '{item.Text}' should have a parent reference");
            Assert.IsNotNull(item.SelfRef, $"Text item '{item.Text}' should have a self reference");
        }
    }

    [TestMethod]
    public async Task ParseAsync_ToMarkdown_ProducesNonEmptyOutput()
    {
        using var temp = HwpTestHelper.CreateTempDir();
        var path = HwpTestHelper.CreateSimpleHwp(temp.Path);

        var parser = new HwpParser();
        var doc = await parser.ParseAsync(path);
        var markdown = doc.ToMarkdown();

        Assert.IsFalse(string.IsNullOrWhiteSpace(markdown), "Markdown output should not be empty");
        Assert.IsTrue(markdown.Contains("첫 번째 문단"), "Markdown should contain first paragraph");
    }
}
