using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Mythosia.Documents.Tests.Office.Word;

/// <summary>
/// Builds .docx files programmatically using OpenXml SDK for parser testing.
/// </summary>
internal static class WordTestHelper
{
    public static TempDir CreateTempDir() => new TempDir();

    /// <summary>
    /// Builds a .docx with the supplied (text, styleId) sequence.
    /// styleId examples: "Heading1".."Heading9", "Title", "ListParagraph", or null/"" for body.
    /// </summary>
    public static string CreateDocxWithParagraphs(
        string directory,
        string filename,
        IEnumerable<(string Text, string? StyleId)> paragraphs)
    {
        var path = Path.Combine(directory, filename);
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            foreach (var (text, styleId) in paragraphs)
            {
                body.AppendChild(BuildParagraph(text, styleId));
            }
        }
        return path;
    }

    private static Paragraph BuildParagraph(string text, string? styleId)
    {
        var para = new Paragraph();
        if (!string.IsNullOrEmpty(styleId))
        {
            para.ParagraphProperties = new ParagraphProperties(
                new ParagraphStyleId { Val = styleId });
        }
        para.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return para;
    }

    internal sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mythosia_word_tests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
