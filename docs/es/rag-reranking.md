# Re-ranking y Ajuste de Recuperación

> 📍 **Pipeline de Pregunta y Respuesta:** [Reescritura de Consulta](rag-query-rewriting.md) → Filtrado → Embedding (si hace falta) → [Recuperación](rag-hybrid-search.md) → **`Re-ranking`** → Construcción de Contexto

La etapa de consulta `Embedding` depende del recuperador; la búsqueda léxica no la notifica. Un recuperador personalizado puede notificar etapas mediante `request.ProgressAsync`. Los embeddings de documentos no cambian.

## ¿Por qué Re-ranking?

La búsqueda vectorial devuelve candidatos ordenados por similitud de embedding, pero la similitud de embedding es una **aproximación**. Un chunk con puntuación 0.82 puede ser más relevante que uno con 0.85.

Un **re-ranker** toma la lista inicial de candidatos y puntúa cada chunk frente a la consulta original con un modelo más potente, produciendo un ordenamiento de relevancia mucho más preciso.

## Opciones de Re-ranker

### LLM Reranker

Usa tu servicio de IA para puntuar resultados. Efectivo pero añade latencia:

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

Para evitar que la pregunta y los documentos de cada evaluación se mezclen con evaluaciones anteriores o con la conversación del servicio, `LlmReranker` usa una solicitud sin estado en cada evaluación. No lee ni añade contenido al historial de conversación. Los valores predeterminados del servicio y tu código de llamada no cambian. Las evaluaciones de los re-rankers que comparten el mismo servicio de IA se procesan de forma secuencial.

### Cohere Reranker

Llama a la API Cohere Rerank — rápido y preciso:

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM Reranker

Usa un endpoint de reranking vLLM hospedado localmente:

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## Parámetros de Recuperación

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // Número final de chunks devueltos
    .WithRetrievalMultiplier(3)    // Recupera topK × 3 candidatos (para re-ranking)
    .WithScoreThreshold(0.6)       // Puntuación mínima de similitud
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — cuántos chunks llegan al contexto del LLM
- **`RetrievalMultiplier`** — lanza una red más amplia para que el re-ranker tenga más con qué trabajar. Un multiplicador de 3 significa que se recuperan 15 candidatos y los 5 mejores sobreviven al re-ranking.
- **`WithScoreThreshold`** — descarta todo por debajo de este umbral de similitud

## Modo de Selección Final

```csharp
// Predeterminado: confiar solo en las puntuaciones del re-ranker
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// Mezclar puntuación de recuperación y puntuación del re-ranker
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)
```

Los modos vectorial y léxico puros conservan puntuaciones nativas. El híbrido configurable usa RRF ponderado normalizado incluso con una sola rama; el peso vectorial 0 omite embeddings de consulta. Las puntuaciones no son probabilidades. `WeightedBlend` mezcla puntuaciones sin calibrarlas; prefiera `RerankerOnly` para búsqueda léxica sin calibración previa.
