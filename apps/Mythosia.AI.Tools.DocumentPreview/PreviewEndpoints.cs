using System.Collections.Generic;
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Splitters;
using Mythosia.Documents;
using Mythosia.Documents.Elements;
using Mythosia.Documents.Hwp;
using Mythosia.Documents.Office.Excel;
using Mythosia.Documents.Office.PowerPoint;
using Mythosia.Documents.Office.Word;
using Mythosia.Documents.Pdf;

namespace Mythosia.AI.Tools.DocumentPreview;

internal static class PreviewEndpoints
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".hwp", ".docx", ".xlsx", ".pptx", ".pdf" };

    private static object BuildDocTree(DoclingDocument doc, NodeItem node)
    {
        string type = node.GetType().Name;
        var props = new Dictionary<string, object?>();

        switch (node)
        {
            case SectionHeaderItem h:
                props["text"] = Truncate(h.Text);
                props["level"] = (object)h.Level;
                break;
            case DocListItem li:
                props["text"] = Truncate(li.Text);
                props["enumerated"] = (object)li.Enumerated;
                if (li.Enumerated) props["marker"] = li.Marker;
                break;
            case CodeItem code:
                props["text"] = Truncate(code.Text);
                if (!string.IsNullOrEmpty(code.CodeLanguage)) props["language"] = code.CodeLanguage;
                break;
            case FormulaItem f:
                props["text"] = Truncate(f.Text);
                break;
            case TitleItem t:
                props["text"] = Truncate(t.Text);
                break;
            case TextItem t:
                props["text"] = Truncate(t.Text);
                props["label"] = t.Label.ToString();
                break;
            case TableItem table:
                props["rows"] = (object)table.Data.NumRows;
                props["cols"] = (object)table.Data.NumCols;
                props["cells"] = (object)table.Data.TableCells.Count;
                break;
            case GroupItem g:
                props["name"] = g.Name;
                if (g.Label != GroupLabel.Unspecified) props["groupLabel"] = g.Label.ToString();
                break;
        }

        var children = node.Children
            .Select(r => r.Resolve(doc))
            .Where(c => c != null)
            .Select(c => BuildDocTree(doc, c!))
            .ToList();

        return new { type, selfRef = node.SelfRef, props, children };
    }

    private static string Truncate(string text, int max = 80)
        => text.Length <= max ? text : text[..max] + "…";

    public static void MapPreviewEndpoints(this WebApplication app)
    {
        app.MapPost("/api/preview", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data." });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file == null || file.Length == 0)
                return Results.BadRequest(new { error = "No file uploaded." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
                return Results.BadRequest(new { error = $"Unsupported file type: {ext}. Supported: {string.Join(", ", SupportedExtensions)}" });

            var chunkSize = int.TryParse(form["chunkSize"], out var cs) ? Math.Max(100, cs) : 1000;

            var tempPath = Path.Combine(Path.GetTempPath(), $"docpreview_{Guid.NewGuid()}{ext}");
            try
            {
                await using (var stream = File.Create(tempPath))
                    await file.CopyToAsync(stream, ct);

                IDocumentLoader loader = ext switch
                {
                    ".hwp"  => new HwpDocumentLoader(),
                    ".docx" => new WordDocumentLoader(),
                    ".xlsx" => new ExcelDocumentLoader(),
                    ".pptx" => new PowerPointDocumentLoader(),
                    ".pdf"  => new PdfDocumentLoader(),
                    _       => throw new NotSupportedException()
                };

                var docs = await loader.LoadAsync(tempPath, ct);
                if (docs.Count == 0)
                    return Results.BadRequest(new { error = "Document loaded but produced no content." });

                var markdown = new MarkdownSerializer().Serialize(docs[0]);

                var ragDoc = new RagDocument
                {
                    Id      = "preview",
                    Source  = file.FileName,
                    Content = markdown
                };

                var chunks = new MarkdownTextSplitter(chunkSize).Split(ragDoc);

                return Results.Ok(new
                {
                    fileName      = file.FileName,
                    markdownLength = markdown.Length,
                    markdown,
                    chunkSize,
                    chunks = chunks.Select(c => new
                    {
                        index   = c.Index,
                        length  = c.Content.Length,
                        content = c.Content
                    }),
                    tree     = BuildDocTree(docs[0], docs[0].Body),
                    metadata = docs[0].Metadata
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, title: "Preview failed");
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        });
    }
}
