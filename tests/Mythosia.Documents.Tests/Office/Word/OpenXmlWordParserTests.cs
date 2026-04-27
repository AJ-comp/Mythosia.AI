using Mythosia.Documents.Elements;
using Mythosia.Documents.Office.Word.Parsers;

namespace Mythosia.Documents.Tests.Office.Word;

[TestClass]
public class OpenXmlWordParserTests
{
    [TestMethod]
    public async Task HeadingHierarchy_SecondH1_IsSiblingOfFirstH1_NotChild()
    {
        using var temp = WordTestHelper.CreateTempDir();
        var path = WordTestHelper.CreateDocxWithParagraphs(temp.Path, "h1_h1.docx", new[]
        {
            ("Section A",  (string?)"Heading1"),
            ("Para under A", null),
            ("Section B",  (string?)"Heading1"),
            ("Para under B", null),
        });

        var doc = await new OpenXmlWordParser().ParseAsync(path);

        var headings = doc.Texts.OfType<SectionHeaderItem>().ToList();
        Assert.AreEqual(2, headings.Count);

        // Both H1s must share the same parent (the body root), not nest.
        Assert.AreEqual(doc.Body.SelfRef, headings[0].Parent?.Ref);
        Assert.AreEqual(doc.Body.SelfRef, headings[1].Parent?.Ref,
            "Second H1 must not nest inside the first H1");
    }

    [TestMethod]
    public async Task HeadingHierarchy_LowerLevelHeading_NestsUnderHigher()
    {
        using var temp = WordTestHelper.CreateTempDir();
        var path = WordTestHelper.CreateDocxWithParagraphs(temp.Path, "h1_h2.docx", new[]
        {
            ("Outer",  (string?)"Heading1"),
            ("Inner",  (string?)"Heading2"),
        });

        var doc = await new OpenXmlWordParser().ParseAsync(path);

        var h1 = doc.Texts.OfType<SectionHeaderItem>().First(h => h.Level == 1);
        var h2 = doc.Texts.OfType<SectionHeaderItem>().First(h => h.Level == 2);

        Assert.AreEqual(h1.SelfRef, h2.Parent?.Ref,
            "H2 should be a child of the immediately preceding H1");
    }

    [TestMethod]
    public async Task HeadingHierarchy_PoppingOnEqualLevel_KeepsTreeBalanced()
    {
        using var temp = WordTestHelper.CreateTempDir();
        var path = WordTestHelper.CreateDocxWithParagraphs(temp.Path, "h1_h2_h2.docx", new[]
        {
            ("Top",    (string?)"Heading1"),
            ("Sub A",  (string?)"Heading2"),
            ("Sub B",  (string?)"Heading2"),
        });

        var doc = await new OpenXmlWordParser().ParseAsync(path);

        var headings = doc.Texts.OfType<SectionHeaderItem>().ToList();
        var top = headings.Single(h => h.Text == "Top");
        var subA = headings.Single(h => h.Text == "Sub A");
        var subB = headings.Single(h => h.Text == "Sub B");

        Assert.AreEqual(top.SelfRef, subA.Parent?.Ref);
        Assert.AreEqual(top.SelfRef, subB.Parent?.Ref,
            "Sub B (H2) should sit alongside Sub A under Top, not nest inside Sub A");
    }

    [TestMethod]
    public async Task HeadingHierarchy_HigherLevel_PopsBackToRoot()
    {
        using var temp = WordTestHelper.CreateTempDir();
        var path = WordTestHelper.CreateDocxWithParagraphs(temp.Path, "h1_h2_h1.docx", new[]
        {
            ("First",  (string?)"Heading1"),
            ("Sub",    (string?)"Heading2"),
            ("Second", (string?)"Heading1"),
        });

        var doc = await new OpenXmlWordParser().ParseAsync(path);

        var second = doc.Texts.OfType<SectionHeaderItem>().Single(h => h.Text == "Second");
        Assert.AreEqual(doc.Body.SelfRef, second.Parent?.Ref);
    }

    [TestMethod]
    public async Task Paragraph_AfterHeading_NestsUnderHeading()
    {
        using var temp = WordTestHelper.CreateTempDir();
        var path = WordTestHelper.CreateDocxWithParagraphs(temp.Path, "heading_para.docx", new[]
        {
            ("Section",  (string?)"Heading1"),
            ("Body text", null),
        });

        var doc = await new OpenXmlWordParser().ParseAsync(path);

        var heading = doc.Texts.OfType<SectionHeaderItem>().Single();
        var paragraph = doc.Texts
            .First(t => t.Label == DocItemLabel.Paragraph && t.Text == "Body text");

        Assert.AreEqual(heading.SelfRef, paragraph.Parent?.Ref);
    }

    [TestMethod]
    public async Task Markdown_HeadingSequence_IsOrdered()
    {
        using var temp = WordTestHelper.CreateTempDir();
        var path = WordTestHelper.CreateDocxWithParagraphs(temp.Path, "h_seq.docx", new[]
        {
            ("First",   (string?)"Heading1"),
            ("Body 1",  null),
            ("Second",  (string?)"Heading1"),
            ("Body 2",  null),
        });

        var doc = await new OpenXmlWordParser().ParseAsync(path);
        var md = doc.ToMarkdown();

        var idxFirst = md.IndexOf("## First");
        var idxBody1 = md.IndexOf("Body 1");
        var idxSecond = md.IndexOf("## Second");
        var idxBody2 = md.IndexOf("Body 2");

        Assert.IsTrue(idxFirst >= 0 && idxBody1 > idxFirst && idxSecond > idxBody1 && idxBody2 > idxSecond,
            $"Markdown order is wrong:\n{md}");
    }

    [TestMethod]
    public async Task Title_ResetsHeadingStack()
    {
        using var temp = WordTestHelper.CreateTempDir();
        var path = WordTestHelper.CreateDocxWithParagraphs(temp.Path, "title.docx", new[]
        {
            ("Sub",    (string?)"Heading2"),
            ("Doc Title", (string?)"Title"),
            ("Body after title", null),
        });

        var doc = await new OpenXmlWordParser().ParseAsync(path);

        var title = doc.Texts.OfType<TitleItem>().Single();
        var bodyPara = doc.Texts
            .First(t => t.Label == DocItemLabel.Paragraph && t.Text == "Body after title");

        // Title sits under Body, body paragraph sits under title (level 0 container).
        Assert.AreEqual(doc.Body.SelfRef, title.Parent?.Ref);
        Assert.AreEqual(title.SelfRef, bodyPara.Parent?.Ref);
    }
}
