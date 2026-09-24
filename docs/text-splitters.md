# Text Splitters

A search result needs enough context to answer the question without embedding an entire document as one unit. Chunking controls that tradeoff. These splitters use local rules and need no AI model; choose according to the document's structure, then evaluate retrieval on your own questions.

## Available Splitters

### CharacterTextSplitter

Use this for plain text when a simple size limit is enough. It prefers the configured separator where available, but may split a sentence. `RagBuilder` uses `CharacterTextSplitter(300, 30)` by default; a `.md` extension does not automatically select the Markdown splitter.

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (recommended default)

Use this for prose when paragraphs and words should stay together where possible. The default separator order is blank line → newline → `. ` → space → individual characters. These are text rules, not a model judging meaning; a period followed by a space is only an approximate sentence boundary.

Repeated entries in `Separators` are applied once, in the order of their first occurrence. Duplicating a separator does not add another splitting pass. Long separator lists are processed without nesting recursive calls.

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Use this only when you want a rough count of whitespace-separated words. Despite the name, `MaxTokensPerChunk` and `TokenOverlap` count units split by `TokenSeparators` (by default spaces, tabs and line breaks), not the embedding model's tokens. Output normalizes those separators to spaces. Unspaced text can remain a single long unit; this splitter cannot enforce a model token limit.

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Use this for Markdown documentation or Markdown emitted by Office/HWP loaders when heading context, table rows and fenced code should stay together. It recognizes ATX headings (`#`–`######`), code fences and tables. It is a rule-based splitter, not a complete Markdown syntax-tree parser. The constructor accepts only `chunkSize`; there is no Markdown overlap parameter.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### Table Splitting Quality

Recognized GFM tables are split between rows, with the header and delimiter row repeated in each resulting table chunk. Outer pipes are optional (`Name | Value` is supported). This preserves the column labels; retrieval quality still depends on the documents, embeddings and queries.

A bold table cell belongs to its row: for example, `**No refund**` in company A's row must not become a condition for company B. Table cells and fenced code are never promoted to repeated prose labels. A standalone `**label**` line is recognized only at the start of a paragraph or text block, after a blank line or a structural boundary. A soft-wrapped bold line inside an existing paragraph does not create a new paragraph or a repeated label. A recognized label can repeat across that text block's split pieces; a table, fence, heading or next recognized label ends its scope.

```
Original table:
| Name   | Dept   | Salary  |
|--------|--------|---------|
| Alice  | Dev    | $90,000 |
| Bob    | PM     | $85,000 |
| Carol  | Design | $80,000 |

→ Chunk 1:
| Name   | Dept   | Salary  |
|--------|--------|---------|
| Alice  | Dev    | $90,000 |
| Bob    | PM     | $85,000 |

→ Chunk 2:
| Name   | Dept   | Salary  |
|--------|--------|---------|
| Carol  | Design | $80,000 |
```

#### Code Block Protection

Backtick and tilde fenced blocks stay intact. The closing fence must use the same character and be at least as long as the opening fence; a shorter fence inside a block does not close it. Preserving a whole fenced block can exceed `ChunkSize`.

The opening fence's indentation is preserved along with the code, so splitting does not change the code's rendered indentation. Opening-fence metadata is read once per block, avoiding repeated scans of a long opening fence on every body line.

#### Heading Breadcrumb

`IncludeHeadingBreadcrumb` defaults to `true`: each chunk repeats the section's heading path so a retrieved passage retains its context. Setting it to `false` stops that repetition while preserving the original headings. Heading-only sections are retained too.

`MinSplitHeadingLevel` accepts 1–6 and selects which heading levels start sections; the default is 1. When a parent heading changes, the preceding child section ends so its old heading path is not applied to the new content.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## Choosing Parameters

`CharacterTextSplitter`, `RecursiveTextSplitter` and `MarkdownTextSplitter` measure size in UTF-16 code units (`string.Length`), not model tokens or visible glyphs. A surrogate pair such as an emoji is never split in half. If the configured size is 1, one pair needs 2 units and may exceed it; combining characters and whole grapheme clusters are not guaranteed to stay together.

Sizes must be positive and overlaps nonnegative; invalid settings fail with `ArgumentOutOfRangeException` before processing. Mutable settings are checked again on splitting. An overlap greater than or equal to the size disables overlap for compatibility. For Character/Recursive, overlap is a target adjusted to separator/Unicode boundaries and the space available in the next chunk; `0` means no overlap. No extra chunk containing only the final overlap is emitted.

For Markdown, `ChunkSize` is the content budget **excluding the repeated heading breadcrumb**. A complete fenced code block, or a table header plus one complete row, may exceed that budget. Ordinary text respects it, subject to the surrogate-pair exception above.

Repeated headings and table headers must not turn a small document into an unbounded amount of text to embed. Markdown therefore has a separate per-document output budget of `max(65536, 32 × document.Content.Length)` UTF-16 code units, summed across all final chunks, including repeated breadcrumbs, table headers and labels. It checks the budget before constructing excessive repeated output and throws `InvalidOperationException` if it would be exceeded; it neither truncates content nor returns a partial result. `ChunkSize` and its atomic-block exceptions still apply within this overall limit. In the default RAG indexing flow, this splitting failure occurs before embedding or storage replacement, so the document's existing index is left unchanged. This is a text-output limit, not a model-token or process-memory limit. The budget applies to each `Split` call and grows with input length; it is not a fixed maximum document size.

Start, for example, with `RecursiveTextSplitter(500, 50)` for prose or `MarkdownTextSplitter(500)` for Markdown, and measure on representative questions. Larger chunks retain more surrounding text; overlap repeats content and increases embedding work. Neither choice guarantees better retrieval.

For strict embedding or LLM limits, count each final chunk with the target model's tokenizer, including repeated headings and table headers. Character/word counts and language-based ratios are not safe token budgets. Implement `ITextSplitter` with that tokenizer if a hard token cap is required.

The correctness fixes change chunk boundaries for affected documents. Reindex the same document IDs to replace obsolete chunks, then refresh relevant embedding caches and evaluation baselines. Persisted chunks are not rewritten automatically.

## Per-Document Splitter

Different splitters can be applied per document in `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // default for the rest
)
```

## Custom Splitter

If you want to build a custom splitting module and plug it in, implement `ITextSplitter`:

Indexing should not report success while one chunk overwrites another. Give each chunk a nonblank ID that is unique in the collection, and copy the document metadata so company or access filters remain available. This example combines the document ID and chunk index. The pipeline rejects missing IDs and duplicates within one document; it does not generate replacement IDs. See [indexing validation](rag-pipeline.md#indexing-validation).

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Id = $"{document.Id}_chunk_{i}",
            Content = s,
            Index = i,
            DocumentId = document.Id,
            Metadata = new Dictionary<string, string>(document.Metadata)
        }).ToList();
    }
}

// Register:
.WithTextSplitter(new SentenceSplitter())
```

---

## Want to dig deeper?

If slide, sheet or nested document boundaries matter, you can also implement chunking **before** Markdown conversion by walking the `DoclingDocument` tree. This gives your custom splitter access to structure that a text representation may not retain.

- [Customizing the Output — chunking recipes](document-architecture-customization.md#recipe-4-chunking-for-rag--slice-the-tree-not-the-markdown) — slide-by-slide, sheet-by-sheet, and heading-context-preserving chunking patterns
- [DoclingDocument Data Model](document-architecture-data-model.md) — the tree structure you'd walk for tree-based chunking
