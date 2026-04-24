# Customizing the Output

If you've read the [concept page](document-architecture-concept.md) and the [data model page](document-architecture-data-model.md), you're ready to bend the behavior to your needs. Each section below is a self-contained "recipe" — pick what you need.

---

## Recipe 1: Change how tables are rendered

The same table can be rendered in two ways.

```csharp
var doc = (await new ExcelDocumentLoader().LoadAsync("data.xlsx"))[0];

// (A) Default: standard markdown pipe table
string md1 = doc.ToMarkdown();
// | header1 | header2 | header3 |
// |---|---|---|
// | a | b | c |

// (B) Semantic header-value form
doc.TableSerializer = new SemanticTableSerializer();
string md2 = doc.ToMarkdown();
// header1: a, header2: b, header3: c
```

**When to use which?**

- **GridTableSerializer (default)**: human-friendly, renders properly in GitHub and standard markdown viewers.
- **SemanticTableSerializer**: better for RAG — the header-value pairing makes the table searchable, and LLMs often understand it more reliably.

---

## Recipe 2: Disable markdown escaping

By default, markdown special characters in body text (`*`, `_`, `[`, `|`, etc.) are escaped automatically. If your source already uses markdown syntax intentionally, turn escaping off.

```csharp
var serializer = new MarkdownSerializer { EscapeText = false };
string md = serializer.Serialize(doc);
```

**When to disable?** When your input contains code snippets, programming docs, or text that deliberately uses markdown syntax.

**When to keep it on (default)?** User-authored documents — prevents accidental `*` in Word/HWP body text from being misread as emphasis.

---

## Recipe 3: Bypass the structured pipeline (RawContent)

For files that are already markdown (`.md`) or plain text (`.txt`), you may want to pass the content through unchanged.

```csharp
var doc = new DoclingDocument
{
    RawContent = File.ReadAllText("README.md"),
};

string md = doc.ToMarkdown();  // returns RawContent verbatim
```

When `RawContent` is set, `ToMarkdown()` returns it directly, skipping tree serialization.

**When to use?** Content-preserving loaders like `PlainTextDocumentLoader`. Office loaders never set this.

---

## Recipe 4: Chunking for RAG — slice the tree, not the markdown

To pass long documents to an LLM, you need to split into chunks. **Do not slice the markdown string directly** — tables and section context will break. Slice the `DoclingDocument` tree instead.

### Slide-level chunking (PowerPoint)

```csharp
var doc = (await new PowerPointDocumentLoader().LoadAsync("deck.pptx"))[0];

var serializer = new MarkdownSerializer();
foreach (var slide in doc.Groups.Where(g => g.Label == GroupLabel.Slide))
{
    // Build a one-slide doc and serialize it
    var slideDoc = new DoclingDocument();
    slideDoc.Body.Children.Add(slide.GetRef());
    // (in practice, factor out a helper method)

    var chunkMarkdown = serializer.Serialize(slideDoc);
    // store chunk in your RAG index
}
```

Filter for `GroupLabel.Slide` and you get per-slide chunks. The same pattern applies to Excel with `GroupLabel.Sheet`.

### Heading-context-preserving chunking (Word, HWP)

When splitting long body text, prefix each chunk with its enclosing heading(s) so context isn't lost.

```csharp
foreach (var heading in doc.Texts.OfType<SectionHeaderItem>())
{
    var children = heading.Children.Select(r => r.Resolve(doc));
    var bodyText = string.Join("\n", children.OfType<TextItem>().Select(t => t.Text));

    var chunk = $"## {heading.Text}\n\n{bodyText}";
    // store in index
}
```

This works because the [Word parser's heading stack](document-architecture-data-model.md#whats-a-refitem-read-only-if-needed) builds an accurate H1/H2/H3 hierarchy in the tree.

### Tables: always whole

Tables are unlike body text — **they must never be split across chunks**. Separating headers from data destroys the meaning.

```csharp
foreach (var table in doc.Tables)
{
    var sb = new StringBuilder();
    new GridTableSerializer().Render(table, sb);
    var tableMarkdown = sb.ToString();
    // one table = one chunk
}
```

---

## Recipe 5: Write a custom table serializer

Implement `ITableSerializer`.

```csharp
public class CsvTableSerializer : ITableSerializer
{
    public void Render(TableItem table, StringBuilder sb)
    {
        var data = table.Data;
        var grid = data.BuildGrid();
        for (int r = 0; r < data.NumRows; r++)
        {
            for (int c = 0; c < data.NumCols; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(grid[r, c]?.Text ?? "");
            }
            sb.AppendLine();
        }
        sb.AppendLine();
    }
}

// Usage
doc.TableSerializer = new CsvTableSerializer();
```

`TableData.BuildGrid()` returns a 2D array with merged cells expanded, so positional access is straightforward.

---

## Recipe 6: Add support for a new file format

Implement `IDocumentParser`.

```csharp
public class OdtDocumentParser : IDocumentParser
{
    public bool CanParse(string source) =>
        Path.GetExtension(source).Equals(".odt", StringComparison.OrdinalIgnoreCase);

    public async Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
    {
        var doc = new DoclingDocument { Name = Path.GetFileNameWithoutExtension(source) };

        // (1) Open the ODT (e.g., SharpZipLib + XML parsing)
        // (2) Walk paragraphs/tables/headings, calling doc.AddParagraph(), doc.AddHeading(), doc.AddTable(), etc.
        // (3) Return doc

        return doc;
    }
}

// Usage
var loader = new WordDocumentLoader(new OdtDocumentParser());
// (or define your own ILoader subclass)
```

**Parser-writing guidelines** (see also CLAUDE.md "문제 원인 분석 규칙"):

- A parser **records the source format's structure as-is**. Don't pre-decide how it should look in markdown.
- Example: in HWP, "which cell is a header" is determined by the author's explicit `TitleCell` flag — applying a "first row is always header" heuristic at parse time would corrupt tables that use **left-column headers**.
- Presentation decisions belong to the serializer (or its fallback logic), not the parser.

---

## What to read next

- **[Data model page](document-architecture-data-model.md)** — detailed reference for `TableData.BuildGrid()`, `GroupLabel`, `RefItem`, etc.
- **[Concept page](document-architecture-concept.md)** — the big picture of the two-stage pipeline.
- **[Document Loaders](document-loaders.md)** — per-loader options and usage examples.
