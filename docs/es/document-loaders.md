# Cargadores de Documentos

Los cargadores de documentos analizan archivos en objetos `DoclingDocument` estructurados, que luego pueden pasarse al pipeline RAG.

<a id="file-source-identity"></a>

## Mantener una identidad estable para cada archivo

Registrar el mismo archivo con rutas relativas o absolutas debe actualizar un único documento; los archivos homónimos de carpetas distintas deben mantenerse separados. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` y `PdfDocumentLoader` ahora asignan a `DoclingDocument.Source` la ruta absoluta normalizada, igual que los cargadores TXT integrados. RAG deriva de ella los ID automáticos; los ID explícitos siguen bajo control del llamador. Las citas predeterminadas pueden mostrar rutas absolutas.

Los ID relativos ya guardados no se migran ni eliminan automáticamente. Identifique el ID anterior, elimine explícitamente solo ese documento del almacén correspondiente y vuelva a indexarlo. También puede indexar todas las fuentes en una colección nueva y vacía, validarla y cambiar la aplicación a ella. Reindexar únicamente el nuevo ID absoluto en la colección existente deja los registros anteriores. No elimine documentos ajenos. Consulte [identidad y migración](rag.md#document-identity).

## Instalación

Los cargadores de Office y PDF están incluidos en `Mythosia.AI.Rag`. Para uso independiente:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## Formatos Soportados

| Cargador | Formato | Paquete |
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
    Password = "contraseña",
    IncludeMetadata = true,
    IncludePageNumbers = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("informe.pdf");
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

var docs = await loader.LoadAsync("hoja-calculo.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
{
    IncludeSlideNumbers = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentacion.pptx");
```

## Usar en RAG

Los cargadores se integran automáticamente al usar `.AddDocument()` en `RagBuilder`. Para cargar manualmente:

```csharp
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("informe.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("informe.pdf")
        .AddDocument("notas.docx")
    );
```

## Visión General del Pipeline de Procesamiento

Los documentos pasan por tres etapas antes de convertirse en chunks buscables:

```text
┌─────────────────────────────────────────────────────────────┐
│  1. Análisis (Documents.Office / Documents.Pdf)
│     .pdf, .docx, etc. → DoclingDocument (modelo estructurado)
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. Serialización (Documents.Abstractions)
│     DoclingDocument → string Markdown
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. Chunking (AI.Rag)
│     string Markdown → lista de chunks buscables
└─────────────────────────────────────────────────────────────┘
```

## Integración de Cargadores con Text Splitters

Use `MarkdownTextSplitter` para conservar títulos, código delimitado y filas de tablas. No recibe overlap. Los títulos repetidos no cuentan en el presupuesto de contenido; un bloque entero o la cabecera con una fila pueden excederlo. Compruebe los límites estrictos con el tokenizer del modelo sobre los chunks finales. Consulte [Text Splitters](text-splitters.md).

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000))
        .AddDocuments(new ExcelDocumentLoader(), "datos.xlsx", new MarkdownTextSplitter(1000))
    );
```

Consulta [Text Splitters](text-splitters.md) para más detalles.
