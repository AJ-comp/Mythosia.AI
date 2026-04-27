using HwpLib.Object;
using HwpLib.Object.BodyText;
using HwpLib.Object.BodyText.Control;
using HwpLib.Object.BodyText.Control.Table;
using HwpLib.Object.BodyText.Paragraph;
using HwpLib.Tool.BlankFileMaker;
using HwpLib.Writer;

namespace Mythosia.Documents.Tests.Hwp;

/// <summary>
/// Creates sample HWP files programmatically for testing.
/// </summary>
internal static class HwpTestHelper
{
    /// <summary>
    /// Creates a temporary directory for test HWP files.
    /// </summary>
    public static TempDir CreateTempDir() => new TempDir();

    /// <summary>
    /// Creates a simple HWP file with a few paragraphs of text.
    /// </summary>
    public static string CreateSimpleHwp(string directory, string filename = "simple.hwp")
    {
        var hwpFile = BlankFileMaker.Make();
        var section = hwpFile.BodyText.SectionList[0];

        // The blank file already has one empty paragraph. Set text on it.
        var firstPara = section.GetParagraph(0);
        SetParagraphText(firstPara, "첫 번째 문단입니다.");

        // Add a second paragraph
        var secondPara = AddParagraphWithText(section, hwpFile, "두 번째 문단입니다.");

        // Add a third paragraph
        AddParagraphWithText(section, hwpFile, "세 번째 문단입니다.");

        var path = Path.Combine(directory, filename);
        HWPWriter.ToFile(hwpFile, path);
        return path;
    }

    /// <summary>
    /// Creates an HWP file with a heading-styled paragraph.
    /// The heading is set via StyleId matching "개요 1" style.
    /// </summary>
    public static string CreateHwpWithHeading(string directory, string filename = "heading.hwp")
    {
        var hwpFile = BlankFileMaker.Make();
        var section = hwpFile.BodyText.SectionList[0];

        // Find the outline style index (개요 1)
        int outlineStyleId = FindStyleId(hwpFile, "개요 1", "Outline 1");

        // Set first paragraph as heading
        var headingPara = section.GetParagraph(0);
        SetParagraphText(headingPara, "제목 문단");
        if (outlineStyleId >= 0)
            headingPara.Header.StyleId = (short)outlineStyleId;

        // Add body paragraph under the heading
        AddParagraphWithText(section, hwpFile, "본문 내용입니다.");

        var path = Path.Combine(directory, filename);
        HWPWriter.ToFile(hwpFile, path);
        return path;
    }

    /// <summary>
    /// Creates an HWP file with a table control.
    /// </summary>
    public static string CreateHwpWithTable(string directory, string filename = "table.hwp")
    {
        var hwpFile = BlankFileMaker.Make();
        var section = hwpFile.BodyText.SectionList[0];

        // First paragraph: normal text
        var firstPara = section.GetParagraph(0);
        SetParagraphText(firstPara, "표 앞의 문단");

        // Second paragraph: will contain a table control
        var tablePara = section.AddNewParagraph();
        tablePara.CreateText();
        tablePara.CreateCharShape();
        tablePara.CharShape!.AddParaCharShape(0, 0);

        // Add table control
        var tableCtrl = (ControlTable?)tablePara.AddNewControl(ControlType.Table);

        // Create a 2x3 table. First row cells are explicitly marked as title (header)
        // cells — HWP relies on the TitleCell flag rather than positional heuristics
        // because both top-row and left-column header layouts are common.
        var row1 = tableCtrl!.AddNewRow();
        AddCell(row1, hwpFile, "헤더1", 0, 0, isTitle: true);
        AddCell(row1, hwpFile, "헤더2", 0, 1, isTitle: true);
        AddCell(row1, hwpFile, "헤더3", 0, 2, isTitle: true);

        var row2 = tableCtrl.AddNewRow();
        AddCell(row2, hwpFile, "데이터A", 1, 0);
        AddCell(row2, hwpFile, "데이터B", 1, 1);
        AddCell(row2, hwpFile, "데이터C", 1, 2);

        // Third paragraph: after table
        AddParagraphWithText(section, hwpFile, "표 뒤의 문단");

        var path = Path.Combine(directory, filename);
        HWPWriter.ToFile(hwpFile, path);
        return path;
    }

    /// <summary>
    /// Creates an empty (blank) HWP file.
    /// </summary>
    public static string CreateBlankHwp(string directory, string filename = "blank.hwp")
    {
        var hwpFile = BlankFileMaker.Make();
        var path = Path.Combine(directory, filename);
        HWPWriter.ToFile(hwpFile, path);
        return path;
    }

    /// <summary>
    /// Creates an HWP file with multiple sections.
    /// </summary>
    public static string CreateMultiSectionHwp(string directory, string filename = "multi_section.hwp")
    {
        var hwpFile = BlankFileMaker.Make();

        // Section 1
        var section1 = hwpFile.BodyText.SectionList[0];
        SetParagraphText(section1.GetParagraph(0), "섹션1 내용");

        // Section 2
        var section2 = hwpFile.BodyText.AddNewSection();
        EmptyParagraphAdder.Add(section2);
        SetParagraphText(section2.GetParagraph(0), "섹션2 내용");

        var path = Path.Combine(directory, filename);
        HWPWriter.ToFile(hwpFile, path);
        return path;
    }

    // -----------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------

    private static Paragraph AddParagraphWithText(Section section, HWPFile hwpFile, string text)
    {
        var para = section.AddNewParagraph();
        para.CreateText();
        para.CreateCharShape();
        para.CharShape!.AddParaCharShape(0, 0);
        para.Text!.AddString(text);
        return para;
    }

    private static void SetParagraphText(Paragraph para, string text)
    {
        if (para.Text == null)
        {
            para.CreateText();
        }
        else
        {
            para.Text.Clear();
        }

        para.Text!.AddString(text);
    }

    private static int FindStyleId(HWPFile hwpFile, string koreanName, string englishName)
    {
        var styles = hwpFile.DocInfo?.StyleList;
        if (styles == null) return -1;

        for (int i = 0; i < styles.Count; i++)
        {
            var style = styles[i];
            if (string.Equals(style.HangulName, koreanName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(style.EnglishName, englishName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void AddCell(Row row, HWPFile hwpFile, string text, int rowIndex, int colIndex, bool isTitle = false)
    {
        var cell = row.AddNewCell();
        cell.ListHeader.RowIndex = rowIndex;
        cell.ListHeader.ColIndex = colIndex;
        cell.ListHeader.ColSpan = 1;
        cell.ListHeader.RowSpan = 1;
        if (isTitle)
            cell.ListHeader.Property.TitleCell = true;

        EmptyParagraphAdder.Add(cell.ParagraphList);
        var cellPara = cell.ParagraphList.GetParagraph(0);
        SetParagraphText(cellPara, text);
    }

    /// <summary>
    /// Disposable temp directory helper.
    /// </summary>
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mythosia_doc_tests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
