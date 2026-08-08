# Personalización del Pipeline RAG

## ¿Por qué Personalizar el Pipeline?

El pipeline RAG predeterminado funciona bien de inmediato, pero los proyectos reales a menudo necesitan más control:

- **Depuración** — ¿qué etapa es lenta? ¿El rewriter está cambiando la consulta de formas inesperadas?
- **Ingeniería de prompt** — la plantilla de prompt predeterminada puede no adaptarse al tono o las restricciones de tu dominio
- **Arquitectura** — múltiples servicios compartiendo un índice ahorra memoria y mantiene los embeddings consistentes
- **Inspección** — a veces necesitas ver qué devuelve la recuperación *antes* de enviarlo al LLM

## Seguimiento de Progreso

Rastrea qué etapa RAG se está ejecutando mediante un callback asíncrono por consulta:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Etapas: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
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
