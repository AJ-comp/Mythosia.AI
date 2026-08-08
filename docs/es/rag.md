# RAG (Retrieval-Augmented Generation)

El RAG permite que el modelo responda preguntas basándose en tus propios documentos, recuperando chunks relevantes en el momento de la consulta.

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

## Agregar Documentos

Se soportan varios tipos de fuentes:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // archivo local
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("El contenido en línea también puede ir aquí.")   // string en bruto
)
```

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

## Próximos Pasos

- [Hybrid Search](rag-hybrid-search.md) — combina búsqueda semántica y por palabras clave
- [Reescritura de Consulta](rag-query-rewriting.md) — optimiza consultas con contexto de conversación
- [Re-ranking](rag-reranking.md) — refina aún más la precisión de los resultados
- [Personalización de Pipeline](rag-pipeline.md) — control fino sobre el proceso RAG
- [Agentic RAG](rag-agentic.md) — la IA decide cuándo y qué buscar
- [Vector Stores](vectordb-overview.md) — configuración de almacenamiento persistente
- [Text Splitters](text-splitters.md) — personaliza cómo se dividen los documentos
