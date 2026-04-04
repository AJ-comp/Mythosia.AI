# RAG (Retrieval-Augmented Generation)

Le RAG permet au modèle de répondre à des questions à partir de vos propres documents, en récupérant les passages pertinents au moment de la requête.

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
    .AddDocument("https://example.com/doc.txt")   // URL
    .AddText("Du contenu inline peut aussi aller ici.")  // chaîne brute
)
```

## Fournisseur d'embeddings personnalisé

Par défaut, le RAG utilise le fournisseur du service pour les embeddings. Pour utiliser un modèle d'embedding dédié :

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbeddingProvider(embedder)
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

var store = new PostgresStore(connectionString, embedDimension: 1536);

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseVectorStore(store)
        .AddDocument("grand-corpus.txt")
    );
```

## Options de requête

Affinez le comportement de récupération par requête :

```csharp
var options = new RagQueryOptions
{
    TopK = 5,                  // nombre de passages à récupérer
    ScoreThreshold = 0.7f      // score de similarité minimum
};

var response = await service.GetCompletionAsync("Votre question", ragOptions: options);
```

## Prochaines étapes

- [Stockages vectoriels](../api/Mythosia.VectorDb.yml) — référence API Postgres, Qdrant, Pinecone
- [Embeddings](../api/Mythosia.AI.Rag.Embeddings.yml) — fournisseurs d'embeddings disponibles
- [Découpeurs de texte](../api/Mythosia.AI.Rag.Splitters.yml) — personnaliser la segmentation des documents
