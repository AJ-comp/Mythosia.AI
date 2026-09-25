# Reescritura de Consulta

> 📍 **Pipeline de Pregunta y Respuesta:** **`Reescritura de Consulta`** → Filtrado → Embedding (si hace falta) → [Recuperación](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → Construcción de Contexto

La etapa de consulta `Embedding` depende del recuperador; la búsqueda léxica no la notifica. Un recuperador personalizado puede notificar etapas mediante `request.ProgressAsync`. Los embeddings de documentos no cambian.

## ¿Por qué Reescribir Consultas?

En una conversación de múltiples turnos, los usuarios usan pronombres y referencias cortas de forma natural:

> Usuario: "Cuéntame sobre la política de devolución."
> Usuario: "¿Y las excepciones **a ella**?"

Si "¿Y las excepciones a ella?" se envía al vector store tal cual, el embedding no sabrá a qué se refiere "ella". La búsqueda devuelve resultados irrelevantes.

La **reescritura de consulta** resuelve estas referencias antes de la recuperación, expandiendo "ella" → "excepciones a la política de devolución". También implementa un **gate de búsqueda** — si la consulta no necesita recuperación (p. ej., "¡Gracias!"), omite la búsqueda vectorial por completo, ahorrando latencia y costo.

## Configuración

Un `LlmQueryRewriter` usa el propio servicio de IA para reescribir la consulta antes del embedding:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter(250)
    .AddDocument("docs.txt")
)
```

## RAG Multi-turno

Al consultar el `RagStore` directamente, pasa el historial de conversación para que el rewriter resuelva referencias:

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("¿Cuál es la política de devolución?", "Puedes devolver artículos en 30 días."),
    new ConversationTurn("¿Y los productos digitales?", "Los productos digitales no tienen reembolso.")
};

var result = await store.QueryAsync(
    query: "¿Hay alguna excepción a eso?",
    conversationHistory: history
);
```

<a id="runtime-query-rewriter"></a>

## Cambiar la reescritura durante las consultas

> Se necesita `Mythosia.AI.Rag` 8.1.1 o posterior para que los cambios en tiempo de ejecución lleguen a los contenedores `WithRag(store)` existentes. [Notas del parche](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v811).

Para añadir o sustituir un reescritor sin reconstruir el índice, use `store.SetQueryRewriter(rewriter)`; use `store.SetQueryRewriter(null)` para desactivar la reescritura y la extracción de términos de búsqueda. Tanto la sobrecarga de `RagStore.QueryAsync` que acepta `conversationHistory` como los contenedores ya conectados mediante `service.WithRag(store)` seleccionan el reescritor actual del almacén en cada solicitud. Una solicitud conserva la instancia seleccionada mientras espera una notificación de progreso o la reescritura. Añadir, sustituir o quitar el reescritor afecta a las solicitudes posteriores, incluidas la búsqueda, la generación de respuestas, el streaming y los runs mediante esos contenedores.

```csharp
var rag = service.WithRag(store);
store.SetQueryRewriter(rewriter);
var rewritten = await rag.RetrieveAsync("¿Qué excepciones tiene la política de reembolsos?");

store.SetQueryRewriter(null);
var original = await rag.RetrieveAsync("¿Qué excepciones tiene la política de reembolsos?");
```

El `LlmQueryRewriter` predeterminado activado por `WithQueryRewriter()` se crea una sola vez durante la inicialización diferida; al quitarlo, no se vuelve a crear en la siguiente solicitud. Las sobrecargas del almacén sin `conversationHistory` siguen omitiendo la reescritura, al igual que Agentic RAG, donde el agente formula la consulta de búsqueda.

## Cómo Funciona el Gate de Búsqueda

No cada mensaje del usuario necesita una búsqueda de documento. El rewriter clasifica la consulta y devuelve una reescritura vacía para mensajes como:

- "¡Gracias!"
- "Entendido, eso fue útil."
- "¿Puedes resumir lo que acabas de decir?"

Cuando el gate se activa, todo el pipeline de recuperación se omite — sin embedding, sin búsqueda vectorial, sin re-ranking — y el LLM responde directamente desde el contexto de conversación.
