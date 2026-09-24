# Personalización del Pipeline RAG

<a id="indexing-validation"></a>

## Proteger los documentos existentes si falla la indexación

Un divisor personalizado o una respuesta de embeddings incorrectos no deben sustituir silenciosamente un documento consultable por contenido incompleto o mal asociado. La canalización valida cada documento antes de iniciar la persistencia, también al usar `onDocumentEmbedded`.

Antes de los embeddings, el almacenamiento o el callback de persistencia, un `RagDocument.Id` nulo, vacío o compuesto solo por espacios provoca `ArgumentException`. Una salida del divisor no válida provoca `InvalidOperationException`: lista o fragmento nulo, `Content` o `Metadata` nulos, ID de fragmento vacío o compuesto solo por espacios, o ID repetidos dentro del mismo documento. Se usa `StringComparer.Ordinal`, que distingue mayúsculas y minúsculas. Los valores de los fragmentos y sus metadatos se copian antes de la primera llamada de embeddings.

Los ID personalizados válidos se conservan exactamente como se proporcionan. No se generan, recortan ni reparan automáticamente, y no se detectan globalmente las colisiones de ID de fragmentos entre documentos distintos. Usa ID únicos en la colección de destino, como en el [ejemplo de divisor personalizado](text-splitters.md). La clave reservada `document_id` solo se normaliza en la copia destinada al almacenamiento; los metadatos originales no cambian.

Los ID no válidos, los fallos de división y los lotes de embeddings no válidos conservan los registros anteriores de ese documento y no invocan el callback de persistencia. Todos sus lotes deben superar la [validación de embeddings](rag-embedding.md#embedding-validation) antes del almacenamiento. No se revierten documentos ya completados anteriormente en la operación; una vez iniciado el almacenamiento, la reversión depende del almacén o del callback.

Estas comprobaciones y correcciones del orden de respuesta no recuperan automáticamente el contenido ya sobrescrito ni los vectores almacenados asociados a fragmentos incorrectos; vuelve a indexar los documentos afectados desde sus fuentes originales.

<a id="custom-persistence"></a>

## Reemplazar el documento completo en el callback de persistencia

Si un documento se acorta, hacer upsert solo de los nuevos fragmentos deja su antigua parte final disponible en las búsquedas. `onDocumentEmbedded` sustituye por completo la persistencia predeterminada: use el `document_id` normalizado de los registros para reemplazar todo el documento. El callback recibe un documento validado y no vacío por vez:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

Una división correcta con cero fragmentos no invoca este callback ni accede al almacén predeterminado. Elimine explícitamente ese ID conocido de su propio almacén; use `DeleteDocumentAsync` solo para el almacén de la canalización. La atomicidad y la reversión dependen del almacén o callback.

<a id="url-documents"></a>

## Leer documentos URL de forma segura

Un servidor puede comprimir un documento de texto para transferirlo. `AddUrl` descomprime `gzip`, `deflate` y Brotli (`br`) antes de leer el texto y comprueba que el flujo comprimido esté completo. Una transferencia HTTP correcta no basta: los datos comprimidos truncados, los errores de descompresión o los fallos de las sumas de comprobación incluidas en el formato interrumpen la carga antes del embedding o la persistencia y conservan los registros anteriores del documento. Los valores `Content-Encoding` no admitidos o con varias capas también se rechazan antes del embedding o la persistencia.

Para dejar de esperar un documento URL lento, pase `cancellationToken` a `RagStore.BuildAsync`. El token llega a la solicitud HTTP, la lectura del cuerpo y la descompresión. La cancelación es cooperativa y no revierte documentos ya guardados.

<a id="custom-retriever"></a>

## Conectar un recuperador sin embeddings obligatorios

Los códigos se benefician de la búsqueda por palabras clave; las preguntas con otra redacción, de la semántica. El recuperador elegido prepara solo la representación necesaria, sin exigir un embedding antes de la búsqueda léxica.

- Antes: cada estrategia recibía un embedding de consulta.
- Después: el recuperador prepara solo la representación necesaria.

Implemente `IRagRetriever` para índices externos u otras representaciones. `RagRetrievalRequest` lleva `Query` (consulta semántica completa), `TextQuery` nullable (sustitución léxica), `TopK`, `Filter` y `ProgressAsync`. Los recuperadores integrados usan `Query` cuando `TextQuery` es null; una cadena vacía omite la rama textual. El recuperador personalizado debe preparar la consulta y aplicar filtro, límite y cancelación.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

Regístrelo con `UseRetriever(...)` o `RagPipeline.SetRetriever(...)`. `IRetrievalStrategy` y `SetRetrievalStrategy(...)` siguen disponibles mediante un adaptador que genera embeddings de consulta. Los resultados deben incluir contenido y metadatos para reclasificación y contexto.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` omite embeddings de consulta. La ingesta sigue dividiendo y generando embeddings para el almacén vectorial existente; no es indexación solo de texto. La inicialización diferida aún puede llamar a embeddings de documentos durante la primera pregunta.

La etapa de consulta `Embedding` depende del recuperador; la búsqueda léxica no la notifica. Un recuperador personalizado puede notificar etapas mediante `request.ProgressAsync`. Los embeddings de documentos no cambian.

## ¿Por qué Personalizar el Pipeline?

El pipeline RAG predeterminado funciona bien de inmediato, pero los proyectos reales a menudo necesitan más control:

- **Depuración** — ¿qué etapa es lenta? ¿El rewriter está cambiando la consulta de formas inesperadas?
- **Ingeniería de prompt** — la plantilla de prompt predeterminada puede no adaptarse al tono o las restricciones de tu dominio
- **Arquitectura** — múltiples servicios compartiendo un índice ahorra memoria y mantiene los embeddings consistentes
- **Inspección** — a veces necesitas ver qué devuelve la recuperación *antes* de enviarlo al LLM

El pipeline personalizado se puede combinar con un run para mostrar el progreso y permitir la cancelación durante la respuesta. La [guía de Run](execution-api-transition.md) explica cuándo se realiza la búsqueda y qué modifican las instrucciones adicionales.

## Seguimiento de Progreso

Rastrea qué etapa RAG se está ejecutando mediante un callback asíncrono por consulta:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Etapas: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Tu pregunta", options);
```

## Plantilla de Prompt Personalizada

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Usa únicamente la siguiente información para responder la pregunta.
        Si la respuesta no está en el contexto, di "No lo sé."

        Contexto:
        {context}

        Pregunta: {question}
        """)
    .AddDocument("faq.txt")
)
```

## Compartir un RagStore

Construye el índice una vez y reutilízalo en múltiples instancias de servicio:

```csharp
// Construir una vez
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// Reutilizar en varios servicios
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

## Consulta Directa al RagStore

Consulta el store independientemente de cualquier servicio de IA para inspeccionar qué se recuperaría:

```csharp
RagProcessedQuery result = await store.QueryAsync("¿Cuál es la política de devolución?");

Console.WriteLine($"Consulta reescrita: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` contiene el prompt completamente ensamblado que se enviaría al LLM. Extremadamente útil para depurar la calidad de la recuperación sin gastar tokens de LLM.

## Cómo Funciona Internamente

Cuando llamas a `.WithRag()`, se crea un wrapper `RagEnabledService` alrededor de tu AIService. El mecanismo clave es [AIRequestContext](request-contexts.md):

- El historial de conversación mantiene la pregunta original
- El modelo recibe el prompt ensamblado (con documentos recuperados + pregunta)
- El estado del AIService nunca se muta — `AsyncLocal<T>` proporciona aislamiento por solicitud

`AIService` guarda el contexto en `AsyncLocal`. `GetLatestMessages()` aplica `RequestMessageOverride` a la entrada inicial de la solicitud lógica actual y conserva las llamadas a herramientas del asistente y sus resultados posteriores. Así, los documentos recuperados y las respuestas de las herramientas se envían juntos en las siguientes solicitudes al modelo. Al finalizar, se restaura el contexto anterior.
