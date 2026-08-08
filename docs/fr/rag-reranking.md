# Re-ranking & réglage de la récupération

> 📍 **Pipeline questions-réponses :** [Réécriture de requête](rag-query-rewriting.md) → Embedding → Filtrage → [Recherche](rag-hybrid-search.md) → **`Re-ranking`** → Construction du contexte

## Pourquoi le re-ranking ?

La recherche vectorielle retourne des candidats triés par similarité d'embedding, mais cette similarité est une **approximation**. Un passage avec un score de 0,82 peut en réalité être plus pertinent qu'un passage à 0,85 — l'embedding ne pouvait tout simplement pas les distinguer.

Un **re-classeur** prend la liste de candidats initiale et attribue à chaque passage un score par rapport à la requête originale avec un modèle plus puissant, produisant un classement de pertinence bien plus précis. C'est particulièrement utile quand :

- Votre corpus contient de nombreux passages qui se ressemblent (ex. entrées de FAQ)
- Les meilleurs résultats de la recherche vectorielle semblent « proches mais pas tout à fait »
- Vous avez besoin de réponses très précises pour des cas d'usage critiques

## Options de re-classeur

### LLM Reranker

Utilise votre service IA pour noter les résultats. Efficace mais ajoute de la latence :

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

### Cohere Reranker

Appelle l'API Cohere Rerank — rapide et précis :

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM Reranker

Utilise un endpoint de re-ranking vLLM hébergé localement :

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## Paramètres de récupération

Contrôlez le nombre de candidats récupérés et la façon dont ils sont filtrés avant la sélection finale :

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // Nombre final de passages retournés
    .WithRetrievalMultiplier(3)    // Récupérer topK × 3 candidats (pour le re-ranking)
    .WithScoreThreshold(0.6)       // Score de similarité minimum
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — combien de passages arrivent dans le contexte du LLM
- **`RetrievalMultiplier`** — élargir le filet pour que le re-classeur ait plus de choix. Un multiplicateur de 3 signifie : récupérer 15 candidats, puis ne garder que les 5 meilleurs après re-ranking.
- **`WithScoreThreshold`** — écarter tout ce qui est en dessous de ce seuil de similarité, même s'il reste moins de `TopK` passages

## Mode de sélection finale

Quand un re-classeur est utilisé, choisissez comment le score final est calculé :

```csharp
using Mythosia.AI.Rag;

// Par défaut : ne se fier qu'aux scores du re-classeur
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// Fusionner le score de récupération et le score du re-classeur
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)  // 65% récupération, 35% re-classeur
```

**`RerankerOnly`** est la valeur sûre par défaut — le jugement du re-classeur remplace complètement le score de récupération initial.

**`WeightedBlend`** préserve le signal de récupération original tout en intégrant le jugement du re-classeur. Utile quand vos embeddings vectoriels sont déjà de haute qualité et que vous voulez que le re-classeur joue le rôle d'arbitre plutôt que de tout écraser.
