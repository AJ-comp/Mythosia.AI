# Operaciones del Vector Store

## Upsert

Inserta o actualiza un único registro. Si ya existe un registro con el mismo `Id`, se reemplaza.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("Los reembolsos se aceptan dentro de 30 días."),
    Content = "Los reembolsos se aceptan dentro de 30 días.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "es",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## Upsert en Lote

Inserta o actualiza múltiples registros en una sola llamada. Más eficiente que llamar a `UpsertAsync` en un bucle — los backends usan APIs en lote internamente cuando están disponibles.

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## Búsqueda

Devuelve los K registros más similares a un vector de consulta. Opcionalmente filtra por metadatos antes de puntuar.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("¿Cuál es la política de reembolso?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Fuente: {r.Record.Metadata["source"]}");
}
```

### Búsqueda con Filtro

Combina similitud vectorial con filtrado por metadatos:

```csharp
var filter = new VectorFilter()
    .Where("language", "es")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

Consulta [VectorFilter](vector-filter.md) para la API completa de filtrado.

## Hybrid Search

Combina similitud vectorial densa con búsqueda por palabras clave (BM25). Mejor recall para consultas con términos específicos, nombres o códigos.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("pedido #12345 estado");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "pedido #12345 estado",   // Texto sin procesar usado para BM25
    topK: 5
);
```

Cómo funciona el hybrid search por backend:

| Backend | Mecanismo |
|---------|-----------|
| **InMemory** | RRF combina similitud coseno + puntuaciones BM25 Lucene |
| **Qdrant** | En servidor: vectores densos + dispersos fusionados con RRF o DBSF |
| **Pinecone** | Vectores sparse + dense fusionados en servidor |
| **Postgres** | Similitud vectorial + puntuaciones `tsvector`/`trigram` fusionadas en SQL |

### Búsqueda de texto e híbrida configurable (sin publicar)

La sobrecarga anterior usa el comportamiento híbrido existente de cada backend. En el código fuente actual, InMemory, PostgreSQL y Qdrant también implementan `ITextSearchStore` e `IConfigurableHybridSearchStore`. Estas API opcionales están **sin publicar (Unreleased)**; Pinecone no las implementa.

```csharp
using Mythosia.VectorDb;

var filter = new VectorFilter().Where("tenant", "acme");
var textResults = await ((ITextSearchStore)store).TextSearchAsync(
    "pedido #12345 estado", topK: 5, filter: filter);

var hybridResults = await ((IConfigurableHybridSearchStore)store).HybridSearchAsync(
    queryVector, "pedido #12345 estado",
    new HybridSearchOptions { VectorWeight = 0.7f, CandidateMultiplier = 4, RrfK = 60 },
    topK: 5, filter: filter);
```

`TextSearchAsync` no necesita un vector denso de consulta. La sobrecarga configurable usa puntuaciones RRF ponderadas normalizadas a `[0, 1]`; el peso `0` omite la búsqueda vectorial y `1` omite la de texto. Los filtros de metadatos se aplican antes de seleccionar top-K, y `MinScore` después de la fusión. Las puntuaciones nativas de texto y vector tienen escalas distintas; ajuste los umbrales por modo. `HybridFusionStrategy` de Qdrant solo controla la sobrecarga anterior.

## Obtener por ID

Recupera un registro específico por su ID:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("No encontrado");
```

Aplica un filtro para acotar la búsqueda (p.ej., en namespaces multi-tenant):

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## Obtención en Lote por ID

Recupera múltiples registros por ID en una sola llamada:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## Eliminar por ID

Elimina un único registro:

```csharp
await store.DeleteAsync("record-id-123");
```

## Eliminar por Filtro

Elimina todos los registros que coincidan con un filtro. Úsalo con cuidado — es una eliminación masiva.

```csharp
// Eliminar todos los registros de un documento específico
var filter = new VectorFilter().Where("source", "manual-viejo.pdf");
await store.DeleteByFilterAsync(filter);
```

## Reemplazar por Filtro

Elimina todos los registros que coinciden con un filtro e inserta un nuevo conjunto. Útil para re-indexar un documento sin dejar chunks desactualizados. La atomicidad del reemplazo completo depende del backend.

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> Postgres usa una transacción de base de datos. InMemory ejecuta la eliminación y la inserción por lotes en secuencia, por lo que otra consulta puede observar el intervalo vacío. Un fallo o una cancelación puede dejar un reemplazo parcial; las escrituras completadas no se revierten. Sincronizar cada operación mantiene la coherencia entre los registros y el índice BM25, pero no convierte el reemplazo completo en una transacción.

## Contar

Cuenta los registros almacenados, opcionalmente con alcance por filtro:

```csharp
long total   = await store.CountAsync();
long spanish = await store.CountAsync(new VectorFilter().Where("language", "es"));

Console.WriteLine($"Total: {total}, Español: {spanish}");
```

## Verificar Conexión

Comprueba que el backend es accesible. Útil en health checks o validación al arrancar:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Conexión con vector store OK");
}
catch (Exception ex)
{
    Console.WriteLine($"Conexión fallida: {ex.Message}");
}
```

## Usando con RAG

Pasa un `IVectorStore` a `RagBuilder` para usar cualquier backend como store de recuperación RAG:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "base-de-conocimiento",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey)
        .AddDocuments("docs/")
    );

var answer = await ragService.GetCompletionAsync("¿Cuál es la política de devolución?");
```

O construye un `RagStore` de forma independiente y compártelo entre múltiples servicios de IA:

```csharp
RagStore ragStore = await RagStore.BuildAsync(rag => rag
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey)
    .AddDocument("base-de-conocimiento.pdf"));

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
