# Embedding

> 📍 **Pipeline de Pregunta y Respuesta:** [Reescritura de Consulta](rag-query-rewriting.md) → [Filtrado](rag-filtering.md) → **`Embedding (si hace falta)`** → [Recuperación](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construcción de Contexto](rag-context-build.md)

La etapa de consulta `Embedding` depende del recuperador; la búsqueda léxica no la notifica. Un recuperador personalizado puede notificar etapas mediante `request.ProgressAsync`. Los embeddings de documentos no cambian.

## ¿Qué es el Embedding?

El embedding es el proceso de convertir texto en vectores numéricos que capturan significado. Estos vectores viven en un espacio de alta dimensionalidad donde **los textos con significados similares quedan cercanos entre sí**.

En el pipeline RAG, el embedding ocurre en dos puntos:

1. **Indexación de documentos** — cada chunk se incrusta y almacena en el vector store
2. **Tiempo de consulta** — la pregunta del usuario se incrusta para compararla con los chunks almacenados

## Proveedores de Embedding Integrados

Elija el proveedor de embeddings según el idioma de los documentos, el entorno de alojamiento y las necesidades de recuperación.

### Perplexity

Los embeddings estándar tratan cada pasaje por separado e implementan `IEmbeddingProvider`, por lo que encajan en el constructor RAG. Los contextuales conservan el orden de fragmentos y sus grupos documentales. Su API separada evita aplanar documentos sin relación.

[Perplexity Agent API, búsqueda y embeddings](perplexity.md).

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

<a id="openai-dimensions"></a>

`text-embedding-ada-002` tiene un tamaño fijo de **1536 dimensiones**. El proveedor omite el campo `dimensions`, que este modelo no admite, en solicitudes individuales y por lotes; configurar otro tamaño provoca una `ArgumentOutOfRangeException` antes de llamar a la API. Las solicitudes de `text-embedding-3-small` y `text-embedding-3-large` siguen incluyendo el valor configurado de `dimensions`.

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

<a id="ollama-dimensions"></a>

Los vectores de documentos y consultas deben usar el mismo modelo y las mismas dimensiones. `OllamaEmbeddingProvider` envía las `dimensions` configuradas a `/api/embed` y comprueba la longitud de cada vector recibido. El proveedor mantiene `qwen3-embedding:4b` con **1024 dimensiones solicitadas** como valor predeterminado; la salida nativa del modelo tiene 2560 dimensiones. El servidor Ollama y el modelo seleccionado deben admitir el tamaño solicitado. Una solicitud no admitida o una respuesta que la ignore falla, sin cambiar `Dimensions` silenciosamente ni redimensionar los vectores localmente.

Si cambia el modelo o las dimensiones, regenere los embeddings de los documentos con la misma configuración que las consultas y adapte el almacén vectorial. Los vectores existentes no se convierten automáticamente.

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

## Procesamiento por lotes

Al indexar documentos, el pipeline agrupa fragmentos para no enviarlos todos en una sola llamada. Ajuste el tamaño del lote según los límites del proveedor y la memoria disponible.

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100;
pipeline.Options = options;
```

`EmbeddingBatchSize` debe ser positivo. El pipeline valida y captura el valor al comenzar cada llamada de indexación de un documento, antes del embedding o de sustituir registros. Así evita bucles de lotes vacíos y que un cambio durante una espera asíncrona omita fragmentos. Las llamadas posteriores pueden usar el nuevo valor.

<a id="embedding-validation"></a>

## Mantener cada vector asociado a su fragmento

Una respuesta HTTP exitosa puede contener vectores ausentes o en orden incorrecto, asociando el texto con el significado de otro fragmento. Un `IEmbeddingProvider` personalizado debe devolver exactamente un `float[]` no nulo por entrada, en el orden de entrada, y proporcionar un valor positivo de `Dimensions`. Cada vector debe tener esa longitud y todos sus valores deben ser finitos, sin `NaN` ni infinito.

Durante la indexación, la canalización rechaza dimensiones, cantidades de respuestas o vectores no válidos con `InvalidOperationException` antes del almacenamiento o de `onDocumentEmbedded`. Copia cada vector aceptado antes de solicitar el siguiente lote para que reutilizar un búfer del proveedor en un lote posterior no cambie los fragmentos anteriores. Los datos devueltos deben permanecer estables mientras se leen; no se admite su modificación concurrente durante la validación o copia. Si falla la validación, se conservan los registros existentes de ese documento.

`OpenAIEmbeddingProvider` exige un `index` válido y único en cada elemento de respuesta y restablece el orden de entrada. `VllmEmbeddingProvider` aplica la misma regla cuando hay índices; por compatibilidad, también admite respuestas en las que todos los elementos omiten `index`, usando el orden de respuesta. Se rechazan los índices parcialmente ausentes, duplicados o fuera de rango. Un proveedor personalizado o sin índices sigue siendo responsable del orden; las comprobaciones de estructura no verifican el significado del vector.

<a id="query-embedding-validation"></a>

## Proteger el vector de la pregunta antes de buscar

Reutilizar un búfer del proveedor no debe cambiar una pregunta mientras se espera una notificación o búsqueda. La recuperación densa integrada, incluido el adaptador `IRetrievalStrategy`, exige `Dimensions` positivas, un vector no nulo de esa longitud exacta y valores finitos. Los resultados inválidos producen `InvalidOperationException` antes de buscar. El vector aceptado se copia inmediatamente al retornar, antes de posteriores notificaciones o búsquedas. El proveedor debe mantener los datos estables durante su lectura; un `IRagRetriever` propio gestiona su preparación y validación.

`OllamaEmbeddingProvider` también valida estructura, cantidad exacta de vectores, dimensiones y valores finitos en llamadas directas individuales o por lotes. JSON o vectores incorrectos producen `InvalidOperationException` en lugar de resultados incompletos. El `HttpClient` proporcionado sigue siendo propiedad del llamador; liberar las solicitudes y respuestas HTTP no libera ese cliente.

## Dimensiones

La propiedad `Dimensions` controla el tamaño de cada vector de embedding. El vector store debe tener la misma dimensión configurada.

| Proveedor | Modelo | Dimensiones Predeterminadas |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 solicitadas (nativas: 2560) |
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
