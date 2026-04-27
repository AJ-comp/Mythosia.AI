using Mythosia.Documents.Elements;
using Mythosia.Documents.Office.PowerPoint.Parsers;

namespace Mythosia.Documents.Tests.Office.PowerPoint;

[TestClass]
public class OpenXmlPowerPointParserTests
{
    [TestMethod]
    public async Task ShapeOrder_Text_Table_Text_PreservesDocumentOrder()
    {
        using var temp = PowerPointTestHelper.CreateTempDir();
        var path = PowerPointTestHelper.CreateSlideWith(temp.Path, "ordered.pptx", new PowerPointTestHelper.ShapeSpec[]
        {
            new PowerPointTestHelper.TextBoxSpec("Before table"),
            new PowerPointTestHelper.TableSpec(new[]
            {
                new[] { "h1", "h2" },
                new[] { "a",  "b"  },
            }),
            new PowerPointTestHelper.TextBoxSpec("After table"),
        });

        var doc = await new OpenXmlPowerPointParser().ParseAsync(path);
        var md = doc.ToMarkdown();

        var idxBefore = md.IndexOf("Before table");
        var idxTable = md.IndexOf("| h1");
        var idxAfter = md.IndexOf("After table");

        Assert.IsTrue(idxBefore >= 0, $"Missing 'Before table':\n{md}");
        Assert.IsTrue(idxTable >= 0, $"Missing table header:\n{md}");
        Assert.IsTrue(idxAfter >= 0, $"Missing 'After table':\n{md}");

        Assert.IsTrue(idxBefore < idxTable, $"Table appeared before its preceding text:\n{md}");
        Assert.IsTrue(idxTable < idxAfter, $"Table appeared after its following text:\n{md}");
    }

    [TestMethod]
    public async Task TitlePlaceholder_RendersAsHeading()
    {
        using var temp = PowerPointTestHelper.CreateTempDir();
        var path = PowerPointTestHelper.CreateSlideWith(temp.Path, "title.pptx", new PowerPointTestHelper.ShapeSpec[]
        {
            new PowerPointTestHelper.TextBoxSpec("Slide Title", isTitle: true),
            new PowerPointTestHelper.TextBoxSpec("Body text"),
        });

        var doc = await new OpenXmlPowerPointParser().ParseAsync(path);

        var heading = doc.Texts.OfType<SectionHeaderItem>().Single();
        Assert.AreEqual("Slide Title", heading.Text);
        Assert.AreEqual(2, heading.Level);

        var body = doc.Texts.First(t => t.Label == DocItemLabel.Paragraph);
        Assert.AreEqual("Body text", body.Text);
    }

    [TestMethod]
    public async Task SingleTable_GetsParsed()
    {
        using var temp = PowerPointTestHelper.CreateTempDir();
        var path = PowerPointTestHelper.CreateSlideWith(temp.Path, "table_only.pptx", new PowerPointTestHelper.ShapeSpec[]
        {
            new PowerPointTestHelper.TableSpec(new[]
            {
                new[] { "h1", "h2", "h3" },
                new[] { "a",  "b",  "c"  },
            }),
        });

        var doc = await new OpenXmlPowerPointParser().ParseAsync(path);

        Assert.AreEqual(1, doc.Tables.Count);
        var data = doc.Tables[0].Data;
        Assert.AreEqual(2, data.NumRows);
        Assert.AreEqual(3, data.NumCols);
    }
}
