# Hybrid Search

Los códigos se benefician de la búsqueda por palabras clave; las preguntas con otra redacción, de la semántica. El recuperador elegido prepara solo la representación necesaria, sin exigir un embedding antes de la búsqueda léxica.

## Modos integrados

```csharp
// Búsqueda semántica (predeterminada)
.UseVectorSearch()

// Búsqueda léxica sin embedding de consulta
.UseKeywordSearch()

// Búsqueda híbrida ponderada
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` omite embeddings de consulta. La ingesta sigue dividiendo y generando embeddings para el almacén vectorial existente; no es indexación solo de texto. La inicialización diferida aún puede llamar a embeddings de documentos durante la primera pregunta.

## Combinar resultados léxicos y semánticos

`VectorWeight` define el peso vectorial (0–1); `1 - VectorWeight`, el léxico. `CandidateMultiplier` controla los candidatos por rama y `RrfK` el suavizado de rangos de Reciprocal Rank Fusion ponderada. Son distintos del multiplicador del reranker RAG. Evalúelos con documentos y preguntas representativos.

Los modos vectorial y léxico puros conservan puntuaciones nativas. El híbrido configurable usa RRF ponderado normalizado incluso con una sola rama; el peso vectorial 0 omite embeddings de consulta. Las puntuaciones no son probabilidades. `WeightedBlend` mezcla puntuaciones sin calibrarlas; prefiera `RerankerOnly` para búsqueda léxica sin calibración previa.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## Compatibilidad de almacenes

InMemory, PostgreSQL y Qdrant admiten los nuevos modos léxico y RRF ponderado configurable. La puntuación textual difiere: BM25 en InMemory, texto completo o trigramas configurados en PostgreSQL, índice disperso en Qdrant. Las puntuaciones no son equivalentes entre motores.

Pinecone conserva el modo híbrido nativo mediante `UseHybridSearch()` con valores predeterminados en un índice `dotproduct` compatible. Este adaptador no admite modo léxico ni RRF ponderado configurable con ambas ramas. Otros almacenes deben implementar las interfaces opcionales correspondientes. Los modos u opciones incompatibles generan errores explícitos, sin cambiar silenciosamente a búsqueda vectorial ni ignorar pesos.

Los adaptadores existentes de InMemory, PostgreSQL y Qdrant no instalan modelos neuronales ni migran índices. La distinción `C#`/`C++` depende de sus analizadores. La opción PIXIE siguiente también exige evaluar los identificadores exactos.

Consulte [modos y almacenes compatibles](rag.md#retrieval-modes) y [recuperadores personalizados](rag-pipeline.md#custom-retriever).

<a id="pixie-search"></a>

## Comparar búsqueda neuronal local con PIXIE

Si la pregunta y el documento usan expresiones diferentes, la búsqueda dispersa aprendida puede añadir vocabulario relacionado. El paquete opcional `Mythosia.AI.Rag.Search.Pixie` codifica documentos y preguntas localmente con PIXIE y combina sus resultados con los embeddings densos existentes. PIXIE no requiere servidor Python ni clave API; los proveedores de embeddings densos o respuestas elegidos pueden seguir usando una API remota.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

Con este almacén, `UseKeywordSearch()` selecciona búsqueda neuronal dispersa: omite el proveedor de embeddings densos de consulta, pero ejecuta PIXIE para la pregunta. La ingesta RAG sigue creando embeddings densos de documentos. `UseHybridSearch(...)` fusiona las posiciones del producto escalar disperso y del coseno denso mediante el RRF ponderado configurado.

Esta versión preliminar ofrece `PixieInMemoryStore`, un índice en memoria. No conecta PIXIE con PostgreSQL, Qdrant ni Pinecone. Reconstruya el índice tras reiniciar o cambiar modelo/configuración. Mantenga vivo el codificador durante todas las operaciones y libérelo al terminar. La búsqueda existente sigue siendo la predeterminada: compare los mismos documentos y preguntas con relevancia evaluada antes de cambiar. PIXIE no garantiza coincidencias exactas `C#`/`C++` ni condiciones de exclusión.

[Guía de PIXIE y comparación (inglés)](../rag-pixie-search.md).
