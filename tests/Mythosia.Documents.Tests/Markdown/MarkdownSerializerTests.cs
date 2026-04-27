using Mythosia.Documents;
using Mythosia.Documents.Elements;

namespace Mythosia.Documents.Tests.Markdown;

[TestClass]
public class MarkdownSerializerTests
{
    [TestMethod]
    public void HeadingLevel_ClampsToMaxSixHashes()
    {
        var doc = new DoclingDocument();
        // Word Heading6 → SectionHeaderItem.Level = 6 → would naively render as 7 #
        doc.AddHeading("Deep heading", level: 6);
        doc.AddHeading("Even deeper", level: 9);

        var md = doc.ToMarkdown();

        Assert.IsFalse(md.Contains("####### "), $"Should not produce 7+ hashes:\n{md}");
        StringAssert.Contains(md, "###### Deep heading");
        StringAssert.Contains(md, "###### Even deeper");
    }

    [TestMethod]
    public void Title_RendersAsH1()
    {
        var doc = new DoclingDocument();
        doc.AddTitle("My Title");

        var md = doc.ToMarkdown();

        StringAssert.StartsWith(md, "# My Title");
    }

    [TestMethod]
    public void Heading_LevelOneRendersAsH2()
    {
        var doc = new DoclingDocument();
        doc.AddHeading("Section", level: 1);

        var md = doc.ToMarkdown();

        StringAssert.Contains(md, "## Section");
    }

    [TestMethod]
    public void EscapeText_EscapesMarkdownSpecialChars()
    {
        var doc = new DoclingDocument();
        doc.AddParagraph("This *is* not _italic_ and `not code`");
        doc.AddParagraph("Pipe | and brackets [link]");

        var md = doc.ToMarkdown();

        StringAssert.Contains(md, @"This \*is\* not \_italic\_ and \`not code\`");
        StringAssert.Contains(md, @"Pipe \| and brackets \[link\]");
    }

    [TestMethod]
    public void EscapeText_CanBeDisabled()
    {
        var doc = new DoclingDocument();
        doc.AddParagraph("Keep *as is*");

        var serializer = new MarkdownSerializer { EscapeText = false };
        var md = serializer.Serialize(doc);

        StringAssert.Contains(md, "Keep *as is*");
        Assert.IsFalse(md.Contains(@"\*"), "Should not contain escaped asterisks when disabled");
    }

    [TestMethod]
    public void EscapeText_AppliesToHeadingsAndListsAndTitle()
    {
        var doc = new DoclingDocument();
        doc.AddTitle("Title with *star*");
        doc.AddHeading("Heading [bracket]", level: 1);
        doc.AddListItem("item with `tick`");

        var md = doc.ToMarkdown();

        StringAssert.Contains(md, @"# Title with \*star\*");
        StringAssert.Contains(md, @"## Heading \[bracket\]");
        StringAssert.Contains(md, @"- item with \`tick\`");
    }

    [TestMethod]
    public void List_HasBlankLineBeforeFollowingParagraph()
    {
        var doc = new DoclingDocument();
        doc.AddListItem("item 1");
        doc.AddListItem("item 2");
        doc.AddParagraph("Following paragraph");

        var md = NormalizeNewlines(doc.ToMarkdown());

        StringAssert.Contains(md, "- item 2\n\nFollowing paragraph");
    }

    [TestMethod]
    public void List_HasBlankLineBeforeFollowingHeading()
    {
        var doc = new DoclingDocument();
        doc.AddListItem("item 1");
        doc.AddHeading("After list", level: 1);

        var md = NormalizeNewlines(doc.ToMarkdown());

        StringAssert.Contains(md, "- item 1\n\n## After list");
    }

    [TestMethod]
    public void List_NoBlankLineBetweenConsecutiveItems()
    {
        var doc = new DoclingDocument();
        doc.AddListItem("item 1");
        doc.AddListItem("item 2");
        doc.AddListItem("item 3");

        var md = NormalizeNewlines(doc.ToMarkdown());

        StringAssert.Contains(md, "- item 1\n- item 2\n- item 3");
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n");

    [TestMethod]
    public void RawContent_BypassesSerialization()
    {
        var doc = new DoclingDocument
        {
            RawContent = "literal *raw* content with no escaping",
        };
        doc.AddParagraph("This should be ignored");

        var md = doc.ToMarkdown();

        Assert.AreEqual("literal *raw* content with no escaping", md);
    }
}
