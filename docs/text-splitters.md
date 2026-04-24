# Text Splitters

Text splitters divide documents into chunks before embedding. Chunk size and overlap significantly affect retrieval quality.

## Available Splitters

### CharacterTextSplitter

Splits on character count. Simple and fast, but may cut mid-sentence:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (recommended default)

Tries to split on semantically meaningful boundaries in this order: paragraphs → sentences → words → characters. Produces more coherent chunks:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Splits by token count rather than character count. More accurate for LLM context window budgeting:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

Use this when the embedding model has strict token limits.

### MarkdownTextSplitter

A structure-aware splitter that understands Markdown heading hierarchy (H1–H6), code fences, and tables, splitting content into semantically meaningful units:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Best for documentation files, README files, and output from structured document loaders like Office and HWP.

> [!TIP]
> Document loaders for Word, Excel, PowerPoint, and HWP internally convert documents to Markdown. Using `MarkdownTextSplitter` with these documents ensures that table and code block structures are preserved throughout the chunking process.

#### Table Splitting Quality

`MarkdownTextSplitter` splits Markdown tables at **row boundaries**. It never cuts a row in half, and each resulting chunk automatically includes the **header row and separator line**:

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

Each chunk is a self-contained, valid table—ensuring embedding and retrieval quality.

#### Code Block Protection

Code fence blocks (`` ``` ``) are treated as **atomic units**. A code block is never split mid-way, even if it exceeds the chunk size, preserving code semantics.

#### Heading Breadcrumb

Each chunk is automatically prefixed with the heading path leading to its content, enriching context for vector search:

```
# Product Manual
## Installation Guide
### Windows

(actual content for this section)
```

This feature is controlled by the `IncludeHeadingBreadcrumb` property (default: `true`).

## Choosing Parameters

| Parameter | Effect |
|-----------|--------|
| `chunkSize` (larger) | More context per chunk, fewer chunks, cheaper embedding |
| `chunkSize` (smaller) | Higher precision retrieval, more chunks, more embeddings |
| `chunkOverlap` | Prevents information loss at chunk boundaries |

A common starting point: `chunkSize: 500, chunkOverlap: 50`.

## Chunk Size vs. Token Count (Multilingual)

`chunkSize` is measured in **characters**, but embedding model limits are in **tokens**. The same number of characters can produce vastly different token counts depending on the language:

| Language | 1,000 chars ≈ tokens | Recommended chunkSize |
|----------|----------------------|-----------------------|
| English | ~250 tokens | 500–2,000 |
| Korean / Japanese / Chinese | ~800–1,500 tokens | 300–1,000 |

> [!WARNING]
> CJK text (Korean, Japanese, Chinese) has a much higher token-per-character ratio than English. If chunks exceed the embedding model’s token limit (e.g., 2,048 tokens), an error will occur. Reduce `chunkSize` generously when working with CJK documents.

For example, with an embedding model that has a 2,048-token limit:

```csharp
// English documents: 2000 chars ≈ 500 tokens → well within limit
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// Korean documents: 1000 chars ≈ 1000 tokens → safe range
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

## Per-Document Splitter

Different splitters can be applied per document in `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // default for the rest
)
```

## Custom Splitter

If you want to build a custom splitting module and plug it in, implement `ITextSplitter`:

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Content = s,
            Index = i,
            DocumentId = document.Id
        }).ToList();
    }
}

// Register:
.WithTextSplitter(new SentenceSplitter())
```

---

## Want to dig deeper?

For RAG, the safest chunking is often done **before** markdown — by walking the `DoclingDocument` tree directly. This avoids cutting tables in half or separating headings from their body text.

- [Customizing the Output — chunking recipes](document-architecture-customization.md#recipe-4-chunking-for-rag--slice-the-tree-not-the-markdown) — slide-by-slide, sheet-by-sheet, and heading-context-preserving chunking patterns
- [DoclingDocument Data Model](document-architecture-data-model.md) — the tree structure you'd walk for tree-based chunking
