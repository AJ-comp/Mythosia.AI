# Mythosia.AI.Loaders.Abstractions

## Package Summary

Defines the `IDocumentLoader` and `IDocumentParser` interfaces plus `RagDocument`/`ParsedDocument` models for loading documents into the RAG pipeline.  
Reference this package only if you're building a **custom document loader**.

## Documentation

- [Loaders Guide (EN)](docs/en/loaders.md)
- [Loaders Guide (KO)](docs/ko/loaders.md)
- [Loaders Guide (JA)](docs/ja/loaders.md)
- [Loaders Guide (ZH)](docs/zh/loaders.md)

## Related Packages

- **Mythosia.AI.Loaders.Office** — Word/Excel/PowerPoint (.docx/.xlsx/.pptx)
- **Mythosia.AI.Loaders.Pdf** — PDF loader with PdfPig parser

## Interfaces

### IDocumentLoader

```csharp
public interface IDocumentLoader
{
    Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken ct = default);
}
```

### IDocumentParser

```csharp
public interface IDocumentParser
{
    bool CanParse(string source);
    Task<ParsedDocument> ParseAsync(string source, CancellationToken ct = default);
}
```

### RagDocument

```csharp
public class RagDocument
{
    public string Id { get; set; }
    public string Content { get; set; }
    public string Source { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}
```

## Built-in Loaders (in Mythosia.AI.Rag)

- **`PlainTextDocumentLoader`** — Loads a single `.txt` file
- **`DirectoryDocumentLoader`** — Recursively loads all text files from a directory

## Custom Loader Example

```csharp
public class PdfDocumentLoader : IDocumentLoader
{
    public async Task<IReadOnlyList<RagDocument>> LoadAsync(
        string source, CancellationToken ct = default)
    {
        var text = await ParsePdfAsync(source);
        return new[]
        {
            new RagDocument
            {
                Id = Path.GetFileName(source),
                Content = text,
                Source = source,
                Metadata = { ["type"] = "pdf" }
            }
        };
    }
}
```

Register via the builder:

```csharp
.WithRag(rag => rag.AddDocuments(new PdfDocumentLoader(), "docs/manual.pdf"))
```
