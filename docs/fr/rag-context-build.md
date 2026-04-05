# Construction du contexte

> 📍 **Pipeline questions-réponses :** [Réécriture de requête](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → [Filtrage](rag-filtering.md) → [Recherche](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → **`Construction du contexte`**

## Qu'est-ce que la construction du contexte ?

La construction du contexte est la **dernière étape** du pipeline RAG. Après avoir récupéré et classé les chunks les plus pertinents, cette étape les **assemble en un prompt** que le LLM peut exploiter pour générer une réponse.

La qualité de cette étape impacte directement la qualité de la réponse. Un prompt bien structuré réduit les hallucinations et aide le modèle à s'appuyer sur le contexte fourni.

## Context Builder par défaut

Sans configuration personnalisée, le pipeline utilise `DefaultContextBuilder` :

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

## Templates de prompt

Utilisez `{context}` et `{question}` comme placeholders :

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Vous êtes un assistant de support client.
        Utilisez UNIQUEMENT les documents suivants pour répondre.
        Si la réponse n'est pas dans les documents, dites
        « Je n'ai pas cette information. »

        Documents :
        {context}

        Question du client : {question}
        """)
    .AddDocument("support-kb.txt")
)
```

### Quand utiliser un template

- **Restreindre le comportement** — « Si la réponse n'est pas dans le contexte, dites "Je ne sais pas" »
- **Définir le ton** — « Répondez de manière professionnelle et concise »
- **Ajouter un rôle** — « Vous êtes un conseiller juridique »
- **Contrôler la langue** — « Répondez toujours en français »

## Context Builder personnalisé

Pour un contrôle total, implémentez `IContextBuilder` :

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Informations pertinentes ###");
        sb.AppendLine();

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "inconnu";
            sb.AppendLine($"📄 Source : {source} (pertinence : {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine($"Répondez en vous basant sur les informations ci-dessus : {query}");
        return sb.ToString();
    }
}
```

## Fonctionnement interne

```
Résultats de recherche + Requête → ContextBuilder.BuildContext() → Prompt → LLM
```

Ordre de résolution :

1. **`IContextBuilder` personnalisé** — via `.WithContextBuilder()`
2. **`TemplateContextBuilder`** — via `.WithPromptTemplate()`
3. **`DefaultContextBuilder`** — par défaut

## Étapes suivantes

- [Personnalisation du pipeline](rag-pipeline.md) — ajuster finement le comportement RAG
- [Re-ranking](rag-reranking.md) — améliorer la qualité des chunks avant la construction
- [Bases du RAG](rag.md) — revoir le flux complet
