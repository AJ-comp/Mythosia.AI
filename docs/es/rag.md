# RAG (Retrieval-Augmented Generation)

Para una respuesta final con botón Detener, pase `cancellationToken` a `GetCompletionAsync`. Use Run para eventos de progreso o instrucciones adicionales compatibles. Consulte [cancelación](completions.md#completion-cancellation).

Para respuestas enriquecidas con búsqueda, pase también `cancellationToken` a `RagEnabledService.GetCompletionAsync`. El mismo token llega a la búsqueda, `LlmQueryRewriter`, `LlmReranker` y la respuesta interna; cancelar durante la búsqueda evita la llamada posterior al modelo. `RagPipeline.QueryAndGenerateAsync` también transmite su token. Los componentes deben cooperar; no se revierten búsquedas ni acciones de herramientas completadas.

El RAG permite que el modelo responda preguntas basándose en tus propios documentos, recuperando chunks relevantes en el momento de la consulta.

Para que el usuario pueda seguir o detener la redacción de una respuesta basada en sus documentos, combina la búsqueda RAG con un run. La [guía de Run](execution-api-transition.md) explica el flujo y los límites de las instrucciones adicionales.


Con una referencia `IAIService`, use `GetLastProcessing()` de `Mythosia.AI.Extensions`. Lee la interfaz opcional `IAIProcessingInfoService` y devuelve una lista vacía si no hay diagnósticos. `IAIService` no añade miembros obligatorios. En RAG, `RagEnabledService.WithSpeed(...)` configura la próxima respuesta tras la búsqueda; `LastProcessing` describe esa respuesta. La reescritura interna queda separada y Run expone los mismos registros `Processing`. [WithSpeed](request-building.md#inference-speed)

## Instalación

```bash
dotnet add package Mythosia.AI.Rag
```

## Inicio Rápido

Usa `.WithRag()` en cualquier `IAIService` para habilitar RAG con una API fluente:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("politica.txt")
    );

var response = await service.GetCompletionAsync("¿Cuál es la política de devolución?");
```

Los documentos se dividen, se incrustan y se almacenan automáticamente. En el momento de la consulta, los chunks más relevantes se recuperan y se inyectan en el prompt.

La versión preliminar opcional `Mythosia.AI.Rag.Search.Pixie` permite comparar búsqueda neuronal dispersa local con la búsqueda existente. Mantiene el proveedor de embeddings densos y un índice PIXIE en memoria, sin migrar almacenes persistentes ni reemplazar la búsqueda predeterminada. [Guía de PIXIE y comparación (inglés)](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## Responder sobre un adjunto usando sus documentos

Para explicar una foto de producto a partir de su manual, pase un `Message` con la pregunta y la imagen a `RagEnabledService.GetCompletionAsync(Message)` o `StartRunAsync(Message)`. Ambos conservan los adjuntos no textuales en la solicitud al servicio de IA interno. La recuperación utiliza el texto del mensaje; los adjuntos no se indexan ni se convierten en embeddings automáticamente. El proveedor y el modelo elegidos deben admitir ese tipo de adjunto. El contexto recuperado solo se añade a la solicitud saliente: no sobrescribe el `Message` original ni sustituye el texto del usuario en el historial de conversación.

Si una respuesta necesita tanto un manual como el inventario actual, combine RAG con sus herramientas registradas. Durante las llamadas a herramientas de `GetCompletionAsync`, el contexto recuperado se conserva en la entrada inicial y cada resultado posterior se envía al modelo sin cambios. El historial conserva la entrada original del usuario.

<a id="retrieval-modes"></a>

## Elegir cómo buscar documentos

Los códigos se benefician de la búsqueda por palabras clave; las preguntas con otra redacción, de la semántica. El recuperador elegido prepara solo la representación necesaria, sin exigir un embedding antes de la búsqueda léxica.

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` omite embeddings de consulta. La ingesta sigue dividiendo y generando embeddings para el almacén vectorial existente; no es indexación solo de texto. La inicialización diferida aún puede llamar a embeddings de documentos durante la primera pregunta.

Consulte [modos y almacenes compatibles](rag-hybrid-search.md) y [recuperadores personalizados](rag-pipeline.md#custom-retriever).

## Agregar Documentos

Se soportan varios tipos de fuentes:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // archivo local
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("El contenido en línea también puede ir aquí.")   // string en bruto
)
```

`AddUrl` valida y descomprime los formatos HTTP admitidos antes de leer el texto y rechaza codificaciones incompletas, no admitidas o con varias capas. Consulte [descompresión de URL y cancelación](rag-pipeline.md#url-documents).

<a id="document-identity"></a>

### Distinguir archivos con el mismo nombre

Dos empresas pueden aportar cada una un `docs/faq.txt`. Ambos documentos deben conservarse en el índice y registrar de nuevo el mismo archivo debe reutilizar su identidad:

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

En el flujo de almacenamiento predeterminado de RAG, el ID del documento se crea antes de enviar los registros al almacén vectorial; después se reemplazan los registros con el mismo `document_id`. Nuestro almacén PostgreSQL (pgvector) utiliza ese ID y no examina por sí mismo la ruta original del archivo. Antes, el registro de directorios enviaba `faq.txt` tanto para `company-a/docs/faq.txt` como para `company-b/docs/faq.txt`, por lo que el segundo documento reemplazaba al primero. La corrección conserva la ruta completa al crear el ID; el esquema de PostgreSQL no cambia. Un filtro `full_path` en un ejemplo de almacenamiento utiliza metadatos proporcionados por quien llama; no genera automáticamente IDs únicos de documentos o registros.

Los cargadores integrados `PlainTextDocumentLoader` y `DirectoryDocumentLoader` usan la ruta absoluta del archivo, normalizada con `Path.GetFullPath`, como `Source` e ID automático del documento. Así, los archivos de directorios distintos tienen IDs diferentes. Las rutas relativas, absolutas y con `./` reutilizan el ID cuando se resuelven a la misma ruta absoluta, incluidas las mayúsculas y minúsculas. Mantenga estable el directorio de trabajo al usar rutas relativas. Mover archivos, acceder mediante enlaces simbólicos o físicos, o variar las mayúsculas no garantiza conservar el ID.

`AddText(..., id: ...)`, un `RagDocument.Id` asignado explícitamente y las reglas de `Source` de los cargadores personalizados no cambian. No es necesario modificar la API de llamada. Como `Source` de estos cargadores integrados ahora es absoluto, las citas predeterminadas también pueden mostrar una ruta absoluta. Para la presentación, use `filename` o los metadatos `relative_path` del cargador de directorios predeterminado. La sobrecarga de directorio con configuración no añade `relative_path` automáticamente.

**Índices existentes:** Los IDs anteriores basados en rutas relativas no se eliminan ni migran automáticamente. Es preferible reindexar todos los documentos en una colección nueva, verificarla y luego cambiar la aplicación. Si reutiliza una colección, elimine únicamente los IDs antiguos cuya pertenencia haya confirmado y reindexe sus archivos de origen. No elimine documentos de forma general por su nombre de archivo: otros directorios pueden contener documentos con el mismo nombre.

Para limitar las actualizaciones y eliminaciones al documento correcto, `document_id` es una clave reservada del pipeline. Antes de guardar, cada registro recibe el `RagDocument.Id` real, aunque los metadatos de entrada indiquen otro valor. No se modifican los diccionarios de metadatos del documento de entrada ni del splitter; los callbacks de persistencia personalizados también reciben registros normalizados. Use otra clave para un identificador propio de la aplicación.

Esto no repara registros ya guardados con un `document_id` incorrecto. Reconstruya una colección nueva desde fuentes fiables, o identifique y limpie solo los registros afectados antes de reindexar. Reindexar únicamente con el ID correcto no permite encontrar de forma fiable registros antiguos guardados bajo otro ID.

Registrar el mismo archivo con rutas relativas o absolutas debe actualizar un único documento; los archivos homónimos de carpetas distintas deben mantenerse separados. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` y `PdfDocumentLoader` ahora asignan a `DoclingDocument.Source` la ruta absoluta normalizada, igual que los cargadores TXT integrados. RAG deriva de ella los ID automáticos; los ID explícitos siguen bajo control del llamador. Las citas predeterminadas pueden mostrar rutas absolutas.

[Mantener una identidad estable para cada archivo](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### Vaciar un documento sin conservar resultados antiguos

Si vacía una política de reembolso retirada y vuelve a indexar el mismo documento, el texto anterior debe dejar de aparecer en las respuestas. Con el almacenamiento RAG predeterminado, una división correcta que produce cero fragmentos reemplaza los registros de ese `document_id` por un conjunto vacío. No se solicitan embeddings y los demás ID no se modifican. Esto incluye documentos vacíos o con solo espacios cuando su divisor devuelve cero fragmentos, y divisores personalizados que devuelven correctamente cero fragmentos.

En una `RagPipeline` ya configurada llamada `pipeline`, reutilice el ID del documento almacenado:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

Más adelante puede indexar contenido no vacío con el mismo ID. Que un cargador no devuelva documentos, o que un documento falte en una lista posterior de archivos, no ordena borrarlo: no se proporcionó un ID para reemplazar.

Las excepciones de carga, análisis o división, y la cancelación detectada antes de llamar al almacenamiento, conservan los registros de ese documento. Los cargadores y analizadores deben comunicar los fallos mediante excepciones; un resultado correcto de cero fragmentos no permite distinguir un fallo de un vaciado intencionado. Una vez iniciada la escritura, la reversión por fallo o cancelación depende del almacén; PostgreSQL usa una transacción para el reemplazo. Los lotes se procesan por documento y no revierten los documentos ya completados.

**Persistencia personalizada:** si se proporciona `onDocumentEmbedded`, la persistencia sigue siendo su responsabilidad. Con cero fragmentos no se invoca el callback ni se accede al almacén predeterminado. La aplicación debe borrar explícitamente el ID conocido en su propio almacén, o usar `DeleteDocumentAsync` para el almacén de la canalización.

## Proveedor de Embedding Personalizado

Por defecto, RAG usa el proveedor local de embeddings integrado. Para usar un modelo de embedding dedicado:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("base-conocimiento.txt")
    );
```

## Vector Store Personalizado

Por defecto se usa un store en memoria. Para producción, conecta un vector store persistente:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("corpus-grande.txt")
    );
```

## Opciones de Consulta

Ajusta el comportamiento de recuperación por consulta:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};

var response = await service.GetCompletionAsync("Tu pregunta", options: options);
```

Si el índice ya está alojado por el proveedor del modelo, compare RAG con la [búsqueda nativa de archivos y las opciones comunes de razonamiento](reasoning-and-search.md).

## Próximos Pasos

- [Hybrid Search](rag-hybrid-search.md) — combina búsqueda semántica y por palabras clave
- [Reescritura de Consulta](rag-query-rewriting.md) — optimiza consultas con contexto de conversación
- [Re-ranking](rag-reranking.md) — refina aún más la precisión de los resultados
- [Personalización de Pipeline](rag-pipeline.md) — control fino sobre el proceso RAG
- [Agentic RAG](rag-agentic.md) — la IA decide cuándo y qué buscar
- [Vector Stores](vectordb-overview.md) — configuración de almacenamiento persistente
- [Text Splitters](text-splitters.md) — personaliza cómo se dividen los documentos

Perplexity: [Usar vectores en su propio índice / Buscar sin generar una respuesta](perplexity.md).
