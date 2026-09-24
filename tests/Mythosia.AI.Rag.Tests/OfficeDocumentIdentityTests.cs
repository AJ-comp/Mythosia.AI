using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Mythosia.AI.Rag.Embeddings;
using Mythosia.AI.Rag.Splitters;
using Mythosia.Documents;
using Mythosia.Documents.Elements;
using Mythosia.Documents.Office.Excel;
using Mythosia.Documents.Office.PowerPoint;
using Mythosia.Documents.Office.Word;
using Mythosia.Documents.Pdf;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public sealed class OfficeDocumentIdentityTests
{
    [TestMethod]
    [DataRow(".docx", false)]
    [DataRow(".docx", true)]
    [DataRow(".xlsx", false)]
    [DataRow(".xlsx", true)]
    [DataRow(".pptx", false)]
    [DataRow(".pptx", true)]
    [DataRow(".pdf", false)]
    [DataRow(".pdf", true)]
    public async Task AddDocument_EquivalentFilePaths_ReplaceOldChunksAndPreserveOtherDocuments(
        string extension, bool absoluteFirst)
    {
        using var files = new TemporaryDocuments();
        var source = files.PathFor("company-a/policy" + extension);
        var otherSource = files.PathFor("company-b/policy" + extension);
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, source);
        var directory = Path.GetDirectoryName(source)!;
        Directory.CreateDirectory(Path.Combine(directory, "child"));
        WriteDocument(source, extension, string.Join(" ", Enumerable.Repeat("OLDPOLICY", 45)));
        WriteDocument(otherSource, extension, "OTHERCOMPANY");
        using var vectors = new InMemoryVectorStore();

        await IndexAsync(vectors, absoluteFirst ? source : relative);
        await IndexAsync(vectors, otherSource);
        var initial = await vectors.ListAllRecordsAsync();
        var initialCount = initial.Count(r => DocumentId(r) == source);
        Assert.IsGreaterThan(2, initialCount, "The original document must have trailing chunks to remove.");
        var otherBefore = Snapshot(initial.Where(r => DocumentId(r) == otherSource));
        Assert.IsNotEmpty(otherBefore, "The other company must have searchable records.");

        var replacementPaths = new[]
        {
            absoluteFirst ? relative : source,
            Path.Combine(directory, ".", "policy" + extension),
            Path.Combine(directory, "child", "..", "policy" + extension)
        };
        for (int i = 0; i < replacementPaths.Length; i++)
        {
            var marker = $"NEWPOLICY{i}";
            WriteDocument(source, extension, marker);
            await IndexAsync(vectors, replacementPaths[i]);
            var current = await vectors.ListAllRecordsAsync();
            CollectionAssert.AreEquivalent(new[] { source, otherSource },
                current.Select(DocumentId).Distinct(StringComparer.Ordinal).ToArray(),
                "Equivalent source paths must not create another document identity.");
            CollectionAssert.AreEqual(otherBefore,
                Snapshot(current.Where(r => DocumentId(r) == otherSource)),
                "Replacing one file must leave the other company's same-named file unchanged.");
            var updated = current.Where(r => DocumentId(r) == source).ToArray();
            Assert.IsTrue(updated.Length > 0 && updated.Length < initialCount);
            Assert.IsTrue(string.Join("", updated.Select(r => r.Content)).Contains(marker, StringComparison.Ordinal));
            Assert.IsFalse(current.Any(r => r.Content.Contains("OLDPOLICY", StringComparison.Ordinal)),
                "No old trailing chunk may survive a shorter replacement.");
            Assert.IsTrue(updated.All(r => r.Metadata["source"] == source));
            Assert.IsTrue(updated.All(r => r.Metadata["filename"] == "policy" + extension));
            Assert.IsTrue(updated.All(r => r.Metadata["extension"] == extension));
        }

        var oldMatches = await vectors.TextSearchAsync("OLDPOLICY", 100);
        Assert.IsEmpty(oldMatches, "The old content must also be removed from the keyword index.");
        var retainedMatches = await vectors.TextSearchAsync("OTHERCOMPANY", 100);
        Assert.IsNotEmpty(retainedMatches);
        Assert.IsTrue(retainedMatches.All(r => DocumentId(r.Record) == otherSource));
    }

    [TestMethod]
    [DataRow(".docx")]
    [DataRow(".xlsx")]
    [DataRow(".pptx")]
    [DataRow(".pdf")]
    public async Task FileLoader_NormalizesSourceWithoutChangingCustomParserArgumentsOrStructure(string extension)
    {
        using var files = new TemporaryDocuments();
        var source = files.PathFor("parser-input" + extension);
        await File.WriteAllTextAsync(source, "The supplied parser owns the format.");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, source);
        var parser = new RecordingParser();
        var expectedMarkdown = parser.Document.ToMarkdown();
        using var cancellation = new CancellationTokenSource();
        var result = (await CreateLoader(extension, parser).LoadAsync(relative, cancellation.Token)).Single();

        Assert.AreEqual(relative, parser.CanParseSource);
        Assert.AreEqual(relative, parser.ParseSource);
        Assert.AreEqual(cancellation.Token, parser.Token);
        Assert.AreSame(parser.Document, result);
        Assert.AreEqual(expectedMarkdown, result.ToMarkdown(), "The loader must not reinterpret the parser's body.");
        Assert.AreEqual(source, result.Source);
        Assert.AreEqual("kept", result.Metadata["custom"]);
        Assert.AreEqual(Path.GetFileName(source), result.Metadata["filename"]);
        Assert.AreEqual(extension, result.Metadata["extension"]);
    }

    [TestMethod]
    [DataRow("urn:company-a:policy")]
    [DataRow("https://example.test/policy")]
    [DataRow("caller-owned-relative-identifier")]
    [DataRow("company A policy")]
    public void Converter_CustomSourceIdentifiers_RemainUnchanged(string source)
    {
        var doc = new DoclingDocument { Source = source, Name = "policy", RawContent = "Policy" };
        var result = DoclingDocumentConverter.ToRagDocument(doc);
        Assert.AreEqual(source, result.Source);
        Assert.AreEqual(source, result.Id);
    }

    private static Task<RagStore> IndexAsync(InMemoryVectorStore vectors, string source) =>
        RagStore.BuildAsync(b => b.AddDocument(source).UseStore(vectors)
            .UseEmbedding(new LocalEmbeddingProvider(32))
            .WithTextSplitter(new CharacterTextSplitter(48, 0, null)));

    private static string DocumentId(VectorRecord record) => record.Metadata["document_id"];

    private static string[] Snapshot(IEnumerable<VectorRecord> records) => records
        .OrderBy(r => r.Id, StringComparer.Ordinal)
        .Select(r => $"{r.Id}|{r.Content}|{string.Join(",", r.Vector)}|{string.Join(",", r.Metadata.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))}")
        .ToArray();

    private static IDocumentLoader CreateLoader(string extension, IDocumentParser parser) => extension switch
    {
        ".docx" => new WordDocumentLoader(parser),
        ".xlsx" => new ExcelDocumentLoader(parser),
        ".pptx" => new PowerPointDocumentLoader(parser),
        ".pdf" => new PdfDocumentLoader(parser),
        _ => throw new ArgumentOutOfRangeException(nameof(extension))
    };

    private static void WriteDocument(string path, string extension, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        switch (extension)
        {
            case ".docx":
                using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
                {
                    doc.AddMainDocumentPart().Document = new W.Document(new W.Body(
                        new W.Paragraph(new W.Run(new W.Text(text)))));
                }
                break;
            case ".xlsx":
                using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
                {
                    var workbook = doc.AddWorkbookPart();
                    workbook.Workbook = new S.Workbook();
                    var worksheet = workbook.AddNewPart<WorksheetPart>();
                    worksheet.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(
                        new S.Cell(new S.InlineString(new S.Text(text)))
                        { CellReference = "A1", DataType = S.CellValues.InlineString }) { RowIndex = 1U }));
                    workbook.Workbook.Append(new S.Sheets(new S.Sheet
                    { Id = workbook.GetIdOfPart(worksheet), SheetId = 1U, Name = "Policy" }));
                }
                break;
            case ".pptx":
                using (var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
                {
                    var presentation = doc.AddPresentationPart();
                    presentation.Presentation = new P.Presentation();
                    var slide = presentation.AddNewPart<SlidePart>();
                    slide.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                        new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                            new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                        new P.GroupShapeProperties(new A.TransformGroup()),
                        new P.Shape(new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = 2U, Name = "Policy" },
                                new P.NonVisualShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                            new P.ShapeProperties(), new P.TextBody(new A.BodyProperties(), new A.ListStyle(),
                                new A.Paragraph(new A.Run(new A.Text(text))))))));
                    presentation.Presentation.SlideIdList = new P.SlideIdList(
                        new P.SlideId { Id = 256U, RelationshipId = presentation.GetIdOfPart(slide) });
                }
                break;
            case ".pdf":
                var pdf = new PdfDocumentBuilder();
                var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
                var page = pdf.AddPage(612, 792);
                // Multiple lines ensure the long original remains inside the page bounds.
                var words = text.Split(' ');
                for (int i = 0; i < words.Length; i += 6)
                    page.AddText(string.Join(" ", words.Skip(i).Take(6)), 12, new PdfPoint(40, 740 - i / 6 * 18), font);
                File.WriteAllBytes(path, pdf.Build());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(extension));
        }
    }

    private sealed class RecordingParser : IDocumentParser
    {
        public DoclingDocument Document { get; } = new() { Name = "Structured parser output" };
        public string? CanParseSource { get; private set; }
        public string? ParseSource { get; private set; }
        public CancellationToken Token { get; private set; }

        public RecordingParser()
        {
            Document.AddHeading("Policy", 2);
            Document.AddParagraph("Parser-owned body.");
            Document.Metadata["custom"] = "kept";
        }
        public bool CanParse(string source) { CanParseSource = source; return true; }
        public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
        { ParseSource = source; Token = ct; return Task.FromResult(Document); }
    }

    private sealed class TemporaryDocuments : IDisposable
    {
        private readonly string _parent = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        private readonly string _name = "mythosia-office-identity-" + Guid.NewGuid().ToString("N");
        private readonly string _root;

        public TemporaryDocuments() { _root = Path.Combine(_parent, _name); Directory.CreateDirectory(_root); }
        public string PathFor(string relativePath) => Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        public void Dispose()
        {
            var target = Path.GetFullPath(_root);
            if (!target.StartsWith(_parent, StringComparison.Ordinal) || Path.GetFileName(target) != _name)
                throw new InvalidOperationException("Document identity cleanup escaped its temporary directory.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
