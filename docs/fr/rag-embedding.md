# Embedding

> 📍 **Pipeline questions-réponses :** [Réécriture de requête](rag-query-rewriting.md) → **`Embedding`** → [Filtrage](rag-filtering.md) → [Recherche](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construction du contexte](rag-context-build.md)

## Qu'est-ce que l'embedding ?

L'embedding convertit du texte en **vecteurs numériques** (tableaux de nombres) qui capturent le sens. Dans cet espace vectoriel, **les textes au sens similaire se retrouvent proches les uns des autres**.

Imaginez placer des villes sur une carte : les villes géographiquement proches apparaissent côte à côte. De la même façon, « Comment résilier mon abonnement ? » et « Je souhaite mettre fin à mon adhésion » produisent des vecteurs proches — même si les mots sont différents.

Dans le pipeline RAG, l'embedding intervient à deux moments :

1. **Indexation des documents** — chaque chunk est vectorisé et stocké
2. **Au moment de la requête** — la question de l'utilisateur est vectorisée pour la comparaison

Cette page se concentre sur l'embedding de la requête (étape 2).

## Fournisseurs intégrés

### OpenAI

```csharp
var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Raccourci via le builder :

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (local)

Exécutez les embeddings en local avec [Ollama](https://ollama.com/) :

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

### vLLM (auto-hébergé)

Pour les équipes exploitant leur propre serveur [vLLM](https://docs.vllm.ai/) :

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (sans API)

Fournisseur léger basé sur le hachage de caractéristiques, sans clé API ni service externe. Cependant, la qualité des embeddings est nettement inférieure aux modèles neuronaux, il **n'est donc pas recommandé en production**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **Conseil :** Utilisez plutôt `OpenAIEmbeddingProvider` avec le modèle `text-embedding-3-small`. Son coût est extrêmement faible — quasi gratuit — pour des résultats bien meilleurs.

## Traitement par lots

Lors de l'indexation, les chunks sont traités par lots pour éviter un seul appel API massif :

```csharp
var options = new RagPipelineOptions
{
    EmbeddingBatchSize = 100   // par défaut : 100 chunks par appel
};
```

## Dimensions

| Fournisseur | Modèle | Dimensions par défaut |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (hachage) | 1024 |

## Fournisseur personnalisé

Implémentez `IEmbeddingProvider` pour intégrer un autre service :

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Appelez votre API ici
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Appel batch
    }
}
```

## Fonctionnement interne

```
Question utilisateur (string) → EmbeddingProvider.GetEmbeddingAsync() → Vecteur de requête (float[])
```

Ce vecteur est transmis à l'étape suivante ([Filtrage](rag-filtering.md)), puis à la [Recherche](rag-hybrid-search.md).

## Étapes suivantes

- [Filtrage](rag-filtering.md) — restreindre les chunks recherchés
- [Recherche hybride](rag-hybrid-search.md) — combiner recherche vectorielle et par mots-clés
- [Personnalisation du pipeline](rag-pipeline.md) — partager les fournisseurs d'embedding entre services
