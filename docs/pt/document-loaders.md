# Carregadores de Documentos

Os carregadores de documentos analisam arquivos em objetos `DoclingDocument` estruturados, que podem então ser passados para o pipeline RAG.

<a id="file-source-identity"></a>

## Manter uma identidade estável para cada arquivo

Registrar o mesmo arquivo por caminhos relativos e absolutos deve atualizar um único documento; arquivos de mesmo nome em pastas diferentes devem permanecer separados. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` e `PdfDocumentLoader` agora definem `DoclingDocument.Source` como o caminho absoluto normalizado, assim como os loaders TXT integrados. O RAG deriva daí os IDs automáticos; IDs explícitos continuam sob controle do chamador. As citações padrão podem exibir caminhos absolutos.

IDs relativos já armazenados não são migrados nem excluídos automaticamente. Identifique o ID anterior, exclua explicitamente apenas esse documento no armazenamento correspondente e reindexe-o. Outra opção é indexar todas as fontes em uma coleção nova e vazia, validá-la e mudar a aplicação para ela. Reindexar apenas o novo ID absoluto na coleção existente deixa os registros antigos. Não exclua documentos sem relação com a alteração. Consulte [identidade e migração](rag.md#document-identity).

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

Use `MarkdownTextSplitter` para preservar títulos, blocos de código e linhas de tabelas. Não há argumento de overlap. Títulos repetidos ficam fora do orçamento de conteúdo; um bloco inteiro ou cabeçalho com uma linha pode excedê-lo. Valide limites estritos com o tokenizer do modelo nos chunks finais. Consulte [Text Splitters](text-splitters.md).

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000))
        .AddDocuments(new ExcelDocumentLoader(), "dados.xlsx", new MarkdownTextSplitter(1000))
    );
```

Consulte [Text Splitters](text-splitters.md) para detalhes.
