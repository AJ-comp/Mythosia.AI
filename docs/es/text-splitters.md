# Text Splitters

Un resultado de búsqueda necesita contexto suficiente para responder sin representar todo el documento como una única unidad. El chunking equilibra tamaño y contexto. Estos splitters usan reglas locales, sin un modelo de IA. Elija según la estructura y evalúe la búsqueda con sus propias preguntas.

## Splitters Disponibles

### CharacterTextSplitter

Para texto plano cuando basta un límite de tamaño sencillo. Prefiere el separador configurado, pero puede cortar una oración. `RagBuilder` usa `CharacterTextSplitter(300, 30)` por defecto; la extensión `.md` no selecciona automáticamente el splitter Markdown.

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (predeterminado recomendado)

Para prosa cuyos párrafos y palabras convenga mantener juntos. El orden predeterminado es línea vacía → salto de línea → `. ` → espacio → caracteres. Son reglas de texto, no un modelo que evalúa el significado; punto y espacio solo aproximan un límite de oración.

Las entradas repetidas de `Separators` se aplican una sola vez, en el orden de su primera aparición. Duplicar un separador no añade otra pasada de división. Las listas largas se procesan sin anidar llamadas recursivas.

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Solo para contar aproximadamente palabras separadas por espacios en blanco. Pese al nombre, `MaxTokensPerChunk` y `TokenOverlap` cuentan unidades de `TokenSeparators` (espacios, tabulaciones y saltos de línea por defecto), no tokens del modelo. La salida normaliza los separadores a espacios. Un texto sin espacios puede quedar como una unidad larga; no garantiza el límite de tokens del modelo.

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Para documentación Markdown o la salida Markdown de cargadores Office/HWP cuando importan títulos, filas de tabla y bloques de código. Reconoce títulos ATX (`#`–`######`), bloques delimitados y tablas. Es un splitter por reglas, no un analizador completo del árbol sintáctico Markdown. El constructor solo recibe `chunkSize`; no hay argumento ni opción de overlap para Markdown.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### Calidad de División de Tablas

Las tablas GFM reconocidas se dividen entre filas y repiten la cabecera y la línea delimitadora en cada chunk de tabla. Las barras exteriores son opcionales (`Name | Value` también funciona). Esto conserva los nombres de columnas; la calidad de búsqueda depende de los documentos, embeddings y preguntas.

Una celda en negrita pertenece a su fila: por ejemplo, `**Sin reembolso**` en la fila de la empresa A no debe convertirse en una condición para B. Las celdas y los bloques de código nunca se convierten en etiquetas de texto repetidas. Una línea independiente `**etiqueta**` solo se reconoce al inicio de un párrafo o bloque de texto, tras una línea en blanco o un límite estructural. Una línea en negrita dentro de un párrafo continuado no crea un párrafo nuevo ni una etiqueta repetida. Una etiqueta reconocida puede repetirse entre los fragmentos de ese bloque; una tabla, una cerca de código, un título o la siguiente etiqueta reconocida termina su alcance.

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

#### Protección de bloques de código

Los bloques delimitados con acentos graves o tildes se mantienen enteros. El cierre debe usar el mismo carácter y ser al menos tan largo como la apertura; un delimitador más corto dentro del bloque no lo cierra. Conservar el bloque entero puede superar `ChunkSize`.

La sangría de la cerca de apertura se conserva junto con el código, para que la división no cambie la sangría del código renderizado. La información de apertura se analiza una vez por bloque, evitando recorrer una cerca larga de nuevo en cada línea del contenido.

#### Ruta de títulos

`IncludeHeadingBreadcrumb` es `true` por defecto: cada chunk repite su ruta de títulos para mantener el contexto del pasaje encontrado. Con `false` se elimina la repetición, pero se conservan los títulos originales. También se guardan las secciones que solo tienen título.

`MinSplitHeadingLevel` acepta 1–6 para elegir qué niveles de título inician una sección; el valor predeterminado es 1. Cuando cambia un título superior, termina la subsección anterior para que su antigua ruta de títulos no se aplique al nuevo contenido.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## Elegir Parámetros

`CharacterTextSplitter`, `RecursiveTextSplitter` y `MarkdownTextSplitter` miden unidades de código UTF-16 (`string.Length`), no tokens del modelo ni glifos visibles. Nunca parten un par sustituto, como un emoji. Con tamaño 1, un par de 2 unidades puede superar el límite. No se garantiza conservar juntos caracteres combinados o grafemas completos.

Los tamaños deben ser positivos y los overlaps no negativos. Los valores inválidos producen `ArgumentOutOfRangeException` antes del procesamiento; los ajustes mutables se vuelven a comprobar al dividir. Un overlap mayor o igual al tamaño lo desactiva por compatibilidad. En Character/Recursive es un objetivo ajustado a separadores, límites Unicode y espacio disponible en el siguiente chunk; `0` significa sin overlap. No se emite un chunk final que contenga solo el último overlap.

En Markdown, `ChunkSize` es el presupuesto de contenido **sin la ruta de títulos repetida**. Un bloque de código entero o la cabecera de tabla con una fila completa pueden excederlo. El texto normal respeta el tamaño, salvo la excepción de pares sustitutos.

Los títulos y encabezados de tabla repetidos no deben convertir un documento pequeño en una entrada de embedding sin límite. Markdown impone por ello un presupuesto total por documento de `max(65536, 32 × document.Content.Length)` unidades UTF-16, sumando todos los fragmentos finales, incluidas rutas de títulos, encabezados y etiquetas repetidos. Lo comprueba antes de crear una repetición excesiva y lanza `InvalidOperationException` si se excedería; no recorta contenido ni devuelve resultados parciales. `ChunkSize` y sus excepciones para bloques indivisibles siguen sujetos a este límite global. En la indexación RAG predeterminada, el fallo ocurre antes del embedding o de sustituir registros, conservando el índice existente del documento. Es un límite de texto de salida, no de tokens del modelo ni de memoria del proceso. El presupuesto se aplica en cada llamada a `Split` y crece con la longitud de entrada; no es un tamaño máximo fijo del documento.

Puede empezar con `RecursiveTextSplitter(500, 50)` para prosa o `MarkdownTextSplitter(500)` para Markdown y evaluar preguntas representativas. Los chunks grandes conservan más contexto; el overlap repite contenido y aumenta el trabajo de embedding. Ninguno garantiza mejorar la búsqueda.

Para límites estrictos, cuente cada chunk final, incluidos títulos y cabeceras repetidos, con el tokenizer del modelo. Los recuentos de caracteres/palabras y los ratios por idioma no son presupuestos seguros. Implemente `ITextSplitter` con ese tokenizer si necesita un límite estricto.

Estas correcciones cambian los límites de los chunks afectados. Reindexe los mismos ID de documento para reemplazar los chunks antiguos y actualice las cachés de embeddings y referencias de evaluación pertinentes. Los chunks almacenados no se reescriben automáticamente.

## Splitter por Documento

Se pueden aplicar splitters diferentes por documento en `RagBuilder`:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "datos.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // predeterminado para el resto
)
```

## Splitter Personalizado

Si quieres crear un módulo de división personalizado e integrarlo, implementa `ITextSplitter`:

La indexación no debe indicar éxito mientras un fragmento sobrescribe otro. Asigna a cada fragmento un ID no vacío y único en la colección, y copia los metadatos del documento para conservar los filtros de empresa o acceso. Este ejemplo combina el ID del documento y el índice del fragmento. La canalización rechaza los ID ausentes y los duplicados dentro de un documento; no genera ID de reemplazo. Consulta la [validación de indexación](rag-pipeline.md#indexing-validation).

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

// Registrar:
.WithTextSplitter(new SentenceSplitter())
```
