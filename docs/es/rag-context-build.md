# Construcción de Contexto

> 📍 **Pipeline de Pregunta y Respuesta:** [Reescritura de Consulta](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → [Filtrado](rag-filtering.md) → [Recuperación](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → **`Construcción de Contexto`**

## ¿Qué es la Construcción de Contexto?

La Construcción de Contexto es la etapa final del pipeline RAG. Tras recuperar y clasificar los chunks más relevantes, esta etapa **los ensambla en un prompt** que el LLM puede entender y usar para generar una respuesta.

## Context Builder Predeterminado

Cuando no se establece ninguna configuración personalizada, el pipeline usa `DefaultContextBuilder`:

```
Responde la pregunta basándote en el siguiente contexto:

[1] (Fuente: manual.txt)
Los reembolsos están disponibles dentro de 30 días tras la compra...

[2] (Fuente: politica.txt)
Los productos digitales no tienen reembolso...

Pregunta: ¿Cuál es la política de devolución?
```

El builder predeterminado tiene propiedades configurables:

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "Responde la pregunta basándote en el siguiente contexto:",
    QueryPrefix = "Pregunta:",
    IncludeScores = false,
    IncludeSource = true
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

## Plantillas de Prompt

Para más control sobre el prompt final, usa una **plantilla de prompt** con marcadores `{context}` y `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Eres un asistente de soporte al cliente. Usa ÚNICAMENTE los siguientes documentos
        para responder la pregunta. Si la respuesta no está en los documentos, di
        "No tengo esa información."

        Documentos:
        {context}

        Pregunta del Cliente: {question}
        """)
    .AddDocument("soporte-kb.txt")
)
```

### Cuándo Usar Plantillas

Las plantillas son especialmente útiles cuando necesitas:

- **Restringir el comportamiento** — "Si la respuesta no está en el contexto, di 'No lo sé'"
- **Establecer el tono** — "Responde de forma profesional y concisa"
- **Agregar contexto de rol** — "Eres un asistente médico"
- **Controlar el idioma** — "Responde siempre en español"

## Context Builder Personalizado

Para control completo, implementa `IContextBuilder`:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Información Relevante ###");

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "desconocido";
            sb.AppendLine($"📄 De: {source} (relevancia: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"\nBasándote en la información anterior, responde: {query}");
        return sb.ToString();
    }
}
```
