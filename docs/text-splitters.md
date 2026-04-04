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

Preserves Markdown structure — splits on headers, lists, and code blocks before falling back to character splitting:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Best for documentation files, README files, and any structured Markdown content.

## Choosing Parameters

| Parameter | Effect |
|-----------|--------|
| `chunkSize` (larger) | More context per chunk, fewer chunks, cheaper embedding |
| `chunkSize` (smaller) | Higher precision retrieval, more chunks, more embeddings |
| `chunkOverlap` | Prevents information loss at chunk boundaries |

A common starting point: `chunkSize: 500, chunkOverlap: 50`.

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

Implement `ITextSplitter` for fully custom splitting logic:

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
