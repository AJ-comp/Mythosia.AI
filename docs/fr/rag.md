# RAG (Retrieval-Augmented Generation)

Pour un résultat final et un bouton Arrêter, passez `cancellationToken` à `GetCompletionAsync`. Utilisez Run pour les événements de progression ou les instructions supplémentaires prises en charge. Voir [l’annulation](completions.md#completion-cancellation).

Pour une réponse enrichie par recherche, passez aussi `cancellationToken` à `RagEnabledService.GetCompletionAsync`. Il atteint la recherche, `LlmQueryRewriter`, `LlmReranker` et la complétion interne ; une annulation pendant la recherche empêche l’appel suivant au modèle. `RagPipeline.QueryAndGenerateAsync` transmet également son jeton. Chaque composant doit coopérer ; les recherches ou actions d’outils terminées ne sont pas annulées rétroactivement.

Le RAG permet au modèle de répondre à des questions à partir de vos propres documents, en récupérant les passages pertinents au moment de la requête.

Pour permettre à l’utilisateur de suivre ou d’arrêter la rédaction d’une réponse fondée sur ses documents, combinez la recherche RAG avec un run. Le [guide Run](execution-api-transition.md) explique le déroulement et les limites des instructions supplémentaires.

## Installation

```bash
dotnet add package Mythosia.AI.Rag
```

## Démarrage rapide

Utilisez `.WithRag()` sur n'importe quel `IAIService` pour activer le RAG avec une API fluente :

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manuel.txt")
        .AddDocument("politique.txt")
    );

var response = await service.GetCompletionAsync("Quelle est la politique de remboursement ?");
```

Les documents sont automatiquement découpés, transformés en embeddings et stockés. Au moment de la requête, les passages les plus pertinents sont récupérés et injectés dans le prompt.

## Ajouter des documents

Plusieurs types de sources sont pris en charge :

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // fichier local
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Du contenu inline peut aussi aller ici.")  // chaîne brute
)
```

## Fournisseur d'embeddings personnalisé

Par défaut, le RAG utilise le fournisseur local d'embeddings intégré. Pour utiliser un modèle d'embedding dédié :

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("base-de-connaissances.txt")
    );
```

## Stockage vectoriel personnalisé

Par défaut, un stockage en mémoire est utilisé. Pour la production, branchez un stockage vectoriel persistant :

```csharp
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("grand-corpus.txt")
    );
```

## Options de requête

Affinez le comportement de récupération par requête :

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,              // nombre de passages à récupérer
        MinScore = 0.7         // score de similarité minimum
    }
};

var response = await service.GetCompletionAsync("Votre question", options: options);
```

Si votre index est déjà hébergé par le fournisseur du modèle, comparez RAG avec [la recherche native de fichiers et les options communes de raisonnement](reasoning-and-search.md).

## Prochaines étapes

- [Recherche hybride](rag-hybrid-search.md) — recherche sémantique et par mots-clés simultanément
- [Réécriture de requêtes](rag-query-rewriting.md) — optimisation des requêtes avec le contexte conversationnel
- [Re-classement](rag-reranking.md) — améliorer la précision des résultats de recherche
- [Personnalisation du pipeline](rag-pipeline.md) — contrôle fin du processus RAG
- [RAG agentique](rag-agentic.md) — l'IA décide quand et quoi chercher
- [Stockages vectoriels](vectordb-overview.md) — configuration du stockage persistant
- [Découpeurs de texte](text-splitters.md) — personnaliser la segmentation des documents

Perplexity: [Utiliser des vecteurs dans votre index / Rechercher sans produire de réponse](perplexity.md).
