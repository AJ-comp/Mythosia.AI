# Carregadores de Documentos

Os carregadores de documentos analisam arquivos em objetos `DoclingDocument` estruturados, que podem então ser passados para o pipeline RAG.

## Instalação

Os carregadores de Office e PDF estão incluídos no `Mythosia.AI.Rag`. Para uso independente:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Formatos Suportados

| Carregador | Formato | Pacote |
|--------|--------|---------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`, `.md`, etc. | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions
{
    Password = "senha",
    IncludeMetadata = true,
    IncludePageNumbers = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("relatorio.pdf");
```

## Word (.docx)

```csharp
var loader = new WordDocumentLoader(options: new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("documento.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(options: new OfficeParserOptions
{
    IncludeSheetNames = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("planilha.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
{
    IncludeSlideNumbers = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("apresentacao.pptx");
```

## Usando no RAG

Os carregadores são integrados automaticamente ao usar `.AddDocument()` no `RagBuilder`. Para carregar manualmente:

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("relatorio.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("relatorio.pdf")
        .AddDocument("notas.docx")
    );
```

## Visão Geral do Pipeline de Processamento

Os documentos passam por três estágios antes de se tornarem chunks pesquisáveis:

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Análise (Documents.Office / Documents.Pdf)
│     .pdf, .docx, etc. → DoclingDocument (modelo estruturado)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Serialização (Documents.Abstractions)
│     DoclingDocument → string Markdown
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Chunking (AI.Rag)
│     string Markdown → lista de chunks pesquisáveis
└─────────────────────────────────────────────────────────────┘
```

## Integração de Carregadores com Text Splitters

`MarkdownTextSplitter` é a escolha mais eficaz para documentos Office:

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "dados.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

Consulte [Text Splitters](text-splitters.md) para detalhes.
