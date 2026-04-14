# Embedding

> 📍 **Pipeline de Pregunta y Respuesta:** [Reescritura de Consulta](rag-query-rewriting.md) → **`Embedding`** → [Filtrado](rag-filtering.md) → [Recuperación](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construcción de Contexto](rag-context-build.md)

## ¿Qué es el Embedding?

El embedding es el proceso de convertir texto en vectores numéricos que capturan significado. Estos vectores viven en un espacio de alta dimensionalidad donde **los textos con significados similares quedan cercanos entre sí**.

En el pipeline RAG, el embedding ocurre en dos puntos:

1. **Indexación de documentos** — cada chunk se incrusta y almacena en el vector store
2. **Tiempo de consulta** — la pregunta del usuario se incrusta para compararla con los chunks almacenados

## Proveedores de Embedding Integrados

### OpenAI Embedding

La opción más popular basada en la nube:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

O con el builder fluente:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (Local)

Ejecuta embeddings localmente sin enviar datos a la nube:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

### vLLM (Auto-hospedado)

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (Sin API)

Un proveedor ligero basado en hashing de features. **No recomendado para producción.**

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

## Dimensiones

La propiedad `Dimensions` controla el tamaño de cada vector de embedding. El vector store debe tener la misma dimensión configurada.

| Proveedor | Modelo | Dimensiones Predeterminadas |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 |
| Local | (hashing de features) | 1024 |

## Proveedor de Embedding Personalizado

Implementa `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Llama a tu API de embedding aquí
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Llamada de embedding en lote
    }
}
```

Regístralo con el builder:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```
