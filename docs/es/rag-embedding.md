# Embedding

> 📍 **Pipeline de Pregunta y Respuesta:** [Reescritura de Consulta](rag-query-rewriting.md) → [Filtrado](rag-filtering.md) → **`Embedding (si hace falta)`** → [Recuperación](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construcción de Contexto](rag-context-build.md)

La etapa de consulta `Embedding` depende del recuperador; la búsqueda léxica no la notifica. Un recuperador personalizado puede notificar etapas mediante `request.ProgressAsync`.

<a id="retrieval-aware-embeddings"></a>

## Conservar el contexto del documento y la intención de búsqueda

Un fragmento puede depender de pasajes vecinos, y una pregunta de búsqueda cumple un papel distinto al documento indexado. RAG 8.2.0 incorpora embeddings contextuales de Voyage y Gemini Embedding 2 para texto extraído de TXT, Markdown y PDF.

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

`IRetrievalEmbeddingProvider : IEmbeddingProvider` es opcional; los proveedores existentes siguen funcionando. La indexación entrega un `EmbeddingDocument(documentId, chunks, title)` inmutable con todos los fragmentos ordenados, independientemente de `EmbeddingBatchSize`. El título procede de `RagDocument.Metadata["title"]`. La búsqueda vectorial y el diagnóstico llaman a `GetQueryEmbeddingAsync`; los proveedores anteriores mantienen lotes `GetEmbeddingsAsync` y consultas `GetEmbeddingAsync`. La búsqueda solo por palabras clave no genera embeddings de consulta.

Elija una configuración de embeddings por almacén:

```csharp
rag.UseVoyageEmbedding(voyageApiKey, httpClient,
    model: "voyage-context-4", dimensions: 1024,
    timeout: TimeSpan.FromSeconds(60));

rag.UseGeminiEmbedding(geminiApiKey, httpClient,
    model: "gemini-embedding-2", dimensions: 1536,
    timeout: TimeSpan.FromSeconds(60), maxConcurrency: 4);
```

### Voyage

`VoyageContextualizedEmbeddingProvider` usa por defecto `voyage-context-4` y 1024 dimensiones (256, 512, 1024 o 2048). Envía todo el documento en un grupo ordenado con `input_type=document`, y cada consulta en su propio grupo con `input_type=query`. La fragmentación automática está desactivada. Un documento admite hasta 16.000 fragmentos; el servicio aplica los límites de tokens. Los métodos genéricos omiten `input_type` y tratan hasta 1.000 textos como grupos independientes de un solo fragmento. No se envían el ID ni el título. [Voyage API](https://docs.voyageai.com/docs/contextualized-chunk-embeddings).

Los lotes genéricos comprueban la cancelación al leer las entradas. En cuanto superan los 1.000 textos, se detiene la lectura y se rechaza el lote sin enviar ninguna solicitud HTTP. Los grupos de documentos se mantienen completos.

### Gemini

`GeminiEmbeddingProvider` usa por defecto `gemini-embedding-2`, 1536 dimensiones (128–3072) y `maxConcurrency=4`. Cada fragmento obtiene un vector mediante su propia petición HTTP. La entrada de búsqueda usa `title: {title} | text: {text}` (sin título: `none`) o `task: search result | query: {query}`. Los prefijos solo afectan a la entrada HTTP; los métodos genéricos envían el texto original. `embedContentConfig.autoTruncate=false` rechaza entradas demasiado largas sin acortarlas. [Gemini API](https://ai.google.dev/gemini-api/docs/embeddings).

Ambos proveedores preservan el texto almacenado y validan cantidad, dimensiones y valores finitos de los vectores. El `HttpClient` sigue perteneciendo al llamador y conserva sus ajustes. Voyage restaura el orden mediante índices de respuesta validados. Los errores omiten claves y contenido remoto; se propaga la cancelación y los tiempos de espera lanzan `TimeoutException`. El `timeout` de Voyage se aplica por petición; el de Gemini cubre toda la operación, incluidas esperas de concurrencia. También rige el tiempo de espera del cliente. No hay división ni truncamiento silenciosos. Un fallo previo a la persistencia conserva el documento anterior; después, la atomicidad depende del almacén o callback. Si cambia el modelo, dimensiones o formato de búsqueda, reindexe los documentos y ajuste el almacén al mismo espacio vectorial.

<a id="playground-embeddings"></a>

### Probar embeddings en Playground

En Playground, abra Pipeline → Embedding y elija Voyage Context 4, Gemini Embedding 2 o embeddings contextuales de Perplexity. Introduzca la clave del proveedor y las dimensiones. El tiempo de espera de la aplicación es de 120 segundos por defecto (1–600); la concurrencia de Gemini es 4 (1–16). Estos ajustes se restauran en el navegador y se usan al reconectar una base de datos vectorial. Los cambios de tiempo de espera, concurrencia y clave API se aplican a las operaciones posteriores sin reindexar.

Abra Documents y ejecute Run Reference para indexar archivos, o Cancel para detener una solicitud activa. Revise los recuentos de fragmentos y vectores por documento y use View Code para exportar la configuración con marcadores de clave. Cambiar de proveedor, modelo o dimensiones requiere reindexar; la reconexión no convierte los vectores almacenados. Cancelar no deshace los documentos ya guardados.

Los modelos pueden usar espacios vectoriales distintos aunque tengan las mismas dimensiones. La aplicación rechaza cambiar de proveedor, modelo o dimensiones para la tabla, colección o espacio de nombres externo conectado actualmente. Elija un destino nuevo e indexe todos los documentos necesarios con la nueva configuración. Tras cancelar, compruebe el índice antes de reintentar.

### Verificar el servicio real

Las pruebas en vivo envían texto sintético TXT, Markdown y PDF y generan cargos de API. Configure `MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE=1` y las credenciales, y seleccione `All`, `Voyage` o `Gemini`. El ejecutor rechaza casos omitidos o no concluyentes; las pruebas sin conexión no demuestran la disponibilidad del servicio.

```powershell
$env:MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE = "1"
pwsh -NoProfile -File build/test-retrieval-embedding-live.ps1 -Provider All
```

[Verificar el servicio real](https://github.com/AJ-comp/Mythosia.AI/blob/main/build/RELEASE.md#retrieval-embedding-live-validation).

## ¿Qué es el Embedding?

El embedding es el proceso de convertir texto en vectores numéricos que capturan significado. Estos vectores viven en un espacio de alta dimensionalidad donde **los textos con significados similares quedan cercanos entre sí**.

En el pipeline RAG, el embedding ocurre en dos puntos:

1. **Indexación de documentos** — cada chunk se incrusta y almacena en el vector store
2. **Tiempo de consulta** — la pregunta del usuario se incrusta para compararla con los chunks almacenados

## Proveedores de Embedding Integrados

Elija el proveedor de embeddings según el idioma de los documentos, el entorno de alojamiento y las necesidades de recuperación.

### Perplexity

`PerplexityContextualizedEmbeddingProvider` ahora implementa `IRetrievalEmbeddingProvider` y se registra con `.UseEmbedding(contextual)`. La API pública agrupada `GetDocumentEmbeddingsAsync` y los métodos binarios se mantienen. El nuevo método para un documento implementa explícitamente la interfaz, preservando las llamadas existentes. RAG mantiene los límites documentales y usa el mismo modelo contextual y dimensiones para las consultas.

Los lotes de coma flotante y binarios de Perplexity admiten hasta 512 textos independientes, o 512 documentos contextuales con 16.000 fragmentos en total. La validación comprueba la cancelación durante la lectura y se detiene al superar un límite, rechazando el lote antes de enviar HTTP. Se conservan la agrupación y el orden. Consulte la [guía de Perplexity](perplexity.md).

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

`EmbeddingBatchSize` controla los lotes planos de implementaciones anteriores de `IEmbeddingProvider`. `IRetrievalEmbeddingProvider` recibe el documento completo y gestiona sus peticiones HTTP; reducir este valor no divide un documento Voyage en varios grupos de contexto.

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
| Voyage | voyage-context-4 | 1024 |
| Gemini | gemini-embedding-2 | 1536 |
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
