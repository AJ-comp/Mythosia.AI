# Kontextaufbau

> 📍 **Fragen & Antworten Pipeline:** [Query-Umschreibung](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → [Filtering](rag-filtering.md) → [Retrieval](rag-hybrid-search.md) → [Re-Ranking](rag-reranking.md) → **`Kontextaufbau`**

## Was ist Kontextaufbau?

Kontextaufbau ist die **letzte Stufe** der RAG-Pipeline. Nachdem die relevantesten Chunks abgerufen und gerankt wurden, werden sie hier zu einem **Prompt zusammengesetzt**, den das LLM für die Antwort nutzen kann.

Ein gut strukturierter Prompt reduziert Halluzinationen und hilft dem Modell, sich auf den bereitgestellten Kontext zu stützen.

## Standard Context Builder

Ohne besondere Konfiguration verwendet die Pipeline `DefaultContextBuilder`:

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "Answer the question based on the following context:",
    QueryPrefix = "Question:",
    IncludeScores = false,
    IncludeSource = true
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

## Prompt-Templates

Verwenden Sie `{context}` und `{question}` als Platzhalter:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Sie sind ein Kundensupport-Assistent.
        Verwenden Sie NUR die folgenden Dokumente zur Beantwortung.
        Falls die Antwort nicht in den Dokumenten steht,
        sagen Sie „Diese Information liegt mir nicht vor."

        Dokumente:
        {context}

        Kundenfrage: {question}
        """)
    .AddDocument("support-kb.txt")
)
```

### Wann Templates verwenden

- **Verhalten einschränken** — „Antworten Sie nur basierend auf dem Kontext"
- **Ton festlegen** — „Antworten Sie professionell und prägnant"
- **Rolle zuweisen** — „Sie sind ein medizinischer Berater"
- **Sprache kontrollieren** — „Antworten Sie immer auf Deutsch"

## Eigener Context Builder

Für volle Kontrolle implementieren Sie `IContextBuilder`:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Relevante Informationen ###");

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "unbekannt";
            sb.AppendLine($"📄 Quelle: {source} (Relevanz: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"Beantworten Sie basierend auf obigen Informationen: {query}");
        return sb.ToString();
    }
}
```

## Interner Ablauf

```
Suchergebnisse + Anfrage → ContextBuilder.BuildContext() → Prompt → LLM
```

Auflösungsreihenfolge:

1. **Benutzerdefinierter `IContextBuilder`** — über `.WithContextBuilder()`
2. **`TemplateContextBuilder`** — über `.WithPromptTemplate()`
3. **`DefaultContextBuilder`** — Standard-Fallback

## Nächste Schritte

- [Pipeline-Anpassung](rag-pipeline.md) — RAG-Verhalten fein abstimmen
- [Re-Ranking](rag-reranking.md) — Chunk-Qualität vor dem Kontextaufbau verbessern
- [RAG-Grundlagen](rag.md) — den gesamten Ablauf überblicken
