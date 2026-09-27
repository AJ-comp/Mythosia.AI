using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Loaders;
using Mythosia.Documents;
using Mythosia.Documents.Pdf;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Mythosia.AI.Tests;

internal sealed class RetrievalEmbeddingLiveFixture : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mythosia-retrieval-embedding-live", Guid.NewGuid().ToString("N"));

    public async Task<RagDocument> LoadAsync(string extension, string name, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name + extension);
        if (extension == ".pdf")
        {
            var pdf = new PdfDocumentBuilder();
            var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
            var page = pdf.AddPage(612, 792);
            var words = text.Split(' ');
            for (var index = 0; index < words.Length; index += 7)
                page.AddText(string.Join(" ", words.Skip(index).Take(7)), 12, new PdfPoint(40, 740 - index / 7 * 18), font);
            await File.WriteAllBytesAsync(path, pdf.Build(), cancellationToken);
        }
        else
            await File.WriteAllTextAsync(path, extension == ".md" ? $"# {name}\n\n{text}" : text, cancellationToken);

        IDocumentLoader loader = extension == ".pdf" ? new PdfDocumentLoader() : new PlainTextDocumentLoader();
        var loaded = await loader.LoadAsync(path, cancellationToken);
        Assert.HasCount(1, loaded);
        var document = DoclingDocumentConverter.ToRagDocument(loaded[0]);
        Assert.IsFalse(string.IsNullOrWhiteSpace(document.Content), "The production loader must extract the synthetic source.");
        document.Metadata["title"] = name;
        return document;
    }

    public void Dispose()
    {
        // The generated GUID directory is owned exclusively by this fixture.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
