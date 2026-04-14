# Text Splitters

Los text splitters dividen documentos en chunks antes del embedding. El tamaño y la superposición de los chunks afectan significativamente la calidad de la recuperación.

## Splitters Disponibles

### CharacterTextSplitter

Divide por conteo de caracteres. Simple y rápido, pero puede cortar a mitad de una oración:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (predeterminado recomendado)

Intenta dividir en límites semánticamente significativos en este orden: párrafos → oraciones → palabras → caracteres. Produce chunks más coherentes:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Divide por conteo de tokens en lugar de conteo de caracteres. Más preciso para presupuestar la ventana de contexto del LLM:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Un splitter con conciencia de estructura que entiende la jerarquía de títulos Markdown (H1–H6), bloques de código y tablas:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Mejor para archivos de documentación, README y salida de cargadores de documentos estructurados como Office.

> [!TIP]
> Los cargadores de documentos para Word, Excel, PowerPoint y HWP convierten internamente los documentos a Markdown. Usar `MarkdownTextSplitter` con estos documentos garantiza que las estructuras de tabla y bloque de código se preserven durante el chunking.

#### Calidad de División de Tablas

`MarkdownTextSplitter` divide tablas Markdown en **límites de fila**. Nunca corta una fila a la mitad, y cada chunk resultante incluye automáticamente el **encabezado y la línea separadora**:

```
Tabla original:
| Nombre | Depto  | Salario  |
|--------|--------|----------|
| Alice  | Dev    | $90.000  |
| Bob    | PM     | $85.000  |
| Carol  | Diseño | $80.000  |

→ Chunk 1:
| Nombre | Depto  | Salario  |
|--------|--------|----------|
| Alice  | Dev    | $90.000  |
| Bob    | PM     | $85.000  |

→ Chunk 2:
| Nombre | Depto  | Salario  |
|--------|--------|----------|
| Carol  | Diseño | $80.000  |
```

## Elegir Parámetros

| Parámetro | Efecto |
|-----------|--------|
| `chunkSize` (mayor) | Más contexto por chunk, menos chunks, embedding más barato |
| `chunkSize` (menor) | Recuperación de mayor precisión, más chunks, más embeddings |
| `chunkOverlap` | Evita pérdida de información en los límites de los chunks |

Un punto de partida común: `chunkSize: 500, chunkOverlap: 50`.

## Splitter por Documento

Se pueden aplicar splitters diferentes por documento en `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "datos.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // predeterminado para el resto
)
```

## Splitter Personalizado

Implementa `ITextSplitter` para lógica de división completamente personalizada:

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

// Registrar:
.WithTextSplitter(new SentenceSplitter())
```
