# Embedding

> 📍 **Pipeline questions-réponses :** [Réécriture de requête](rag-query-rewriting.md) → [Filtrage](rag-filtering.md) → **`Embedding (si nécessaire)`** → [Recherche](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construction du contexte](rag-context-build.md)

L’étape de requête `Embedding` dépend désormais du moteur ; la recherche lexicale ne la signale pas. Un moteur personnalisé peut signaler ses étapes via `request.ProgressAsync`. Les embeddings des documents ne changent pas.

## Qu'est-ce que l'embedding ?

L'embedding convertit du texte en **vecteurs numériques** (tableaux de nombres) qui capturent le sens. Dans cet espace vectoriel, **les textes au sens similaire se retrouvent proches les uns des autres**.

Imaginez placer des villes sur une carte : les villes géographiquement proches apparaissent côte à côte. De la même façon, « Comment résilier mon abonnement ? » et « Je souhaite mettre fin à mon adhésion » produisent des vecteurs proches — même si les mots sont différents.

Dans le pipeline RAG, l'embedding intervient à deux moments :

1. **Indexation des documents** — chaque chunk est vectorisé et stocké
2. **Au moment de la requête** — la question de l'utilisateur est vectorisée pour la comparaison

Cette page se concentre sur l'embedding de la requête (étape 2).

## Fournisseurs intégrés

Choisissez un fournisseur d’embeddings selon la langue des documents, les contraintes d’hébergement et les besoins de recherche.

### Perplexity

Les embeddings standard traitent chaque passage indépendamment et implémentent `IEmbeddingProvider`, donc s'intègrent au constructeur RAG. Les embeddings contextuels préservent l'ordre des passages et les groupes documentaires. Leur API distincte évite d'aplatir des documents sans rapport.

[Perplexity Agent API, recherche et embeddings](perplexity.md).

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

<a id="openai-dimensions"></a>

`text-embedding-ada-002` a une taille fixe de **1536 dimensions**. Le fournisseur omet le champ `dimensions`, non pris en charge par ce modèle, dans les requêtes individuelles et par lots ; configurer une autre taille déclenche une `ArgumentOutOfRangeException` avant tout appel API. Les requêtes pour `text-embedding-3-small` et `text-embedding-3-large` continuent d'inclure la valeur configurée de `dimensions`.

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

<a id="ollama-dimensions"></a>

Les vecteurs des documents et des requêtes doivent utiliser le même modèle et les mêmes dimensions. `OllamaEmbeddingProvider` envoie les `dimensions` configurées à `/api/embed` et vérifie la longueur de chaque vecteur renvoyé. Le fournisseur utilise toujours par défaut `qwen3-embedding:4b` avec **1024 dimensions demandées** ; la sortie native du modèle en compte 2560. Le serveur Ollama et le modèle choisi doivent prendre en charge la taille demandée. Une demande non prise en charge ou une réponse qui l’ignore provoque un échec, sans modifier silencieusement `Dimensions` ni redimensionner les vecteurs localement.

Si vous changez de modèle ou de dimensions, recréez les embeddings des documents avec les mêmes réglages que les requêtes et adaptez le stockage vectoriel. Les vecteurs existants ne sont pas convertis automatiquement.

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
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // par défaut : 100 chunks par appel
pipeline.Options = options;
```

`EmbeddingBatchSize` doit être positif. Le pipeline valide et capture sa valeur au début de chaque appel d’indexation d’un document, avant l’embedding ou le remplacement des enregistrements. Cela évite les boucles de lots vides et les fragments sautés si le réglage change pendant une attente asynchrone. Les appels suivants peuvent utiliser la nouvelle valeur.

<a id="embedding-validation"></a>

## Associer chaque vecteur au bon fragment

Une réponse HTTP réussie peut malgré tout contenir des vecteurs manquants ou dans le mauvais ordre. Le texte serait alors associé au sens d’un autre fragment. Un `IEmbeddingProvider` personnalisé doit renvoyer exactement un `float[]` non null par entrée, dans l’ordre d’entrée, et fournir une valeur `Dimensions` positive. Chaque vecteur doit avoir cette longueur et ne contenir que des valeurs finies, sans `NaN` ni infini.

Pendant l’indexation, le pipeline refuse les dimensions, nombres de réponses ou vecteurs invalides avec `InvalidOperationException`, avant le stockage ou `onDocumentEmbedded`. Il copie chaque vecteur accepté avant de demander le lot suivant : la réutilisation ultérieure d’un tampon du fournisseur ne peut donc pas modifier les fragments précédents. Les données renvoyées doivent rester stables pendant leur lecture ; leur modification concurrente pendant la validation ou la copie n’est pas prise en charge. Un échec de validation préserve les enregistrements existants du document.

`OpenAIEmbeddingProvider` exige un `index` valide et unique pour chaque élément de réponse, puis rétablit l’ordre d’entrée. `VllmEmbeddingProvider` applique la même règle lorsque les indices sont présents ; pour compatibilité, il accepte aussi les réponses dont tous les éléments omettent `index`, en conservant l’ordre de réponse. Les indices partiellement absents, dupliqués ou hors limites sont refusés. Un fournisseur personnalisé ou sans indices reste responsable de l’ordre : les contrôles de structure ne vérifient pas le sens des vecteurs.

<a id="query-embedding-validation"></a>

## Protéger le vecteur de la question avant la recherche

La réutilisation d’un tampon ne doit pas modifier une question pendant l’attente d’une notification ou d’une recherche. La recherche dense intégrée, y compris l’adaptateur `IRetrievalStrategy`, exige des `Dimensions` positives, un vecteur non null de cette longueur exacte et des valeurs finies. Une sortie invalide déclenche `InvalidOperationException` avant la recherche. Le vecteur accepté est copié dès son retour, avant les notifications et la recherche suivantes. Le fournisseur doit stabiliser les données pendant leur lecture ; un `IRagRetriever` personnalisé gère sa propre préparation et validation.

`OllamaEmbeddingProvider` vérifie aussi la structure, le nombre exact de vecteurs, leurs dimensions et leurs valeurs finies lors des appels directs unitaires ou par lot. Un JSON ou des vecteurs mal formés déclenchent `InvalidOperationException` au lieu d’un résultat incomplet. Le `HttpClient` fourni reste la propriété de l’appelant ; libérer les requêtes/réponses ne libère pas ce client.

## Dimensions

| Fournisseur | Modèle | Dimensions par défaut |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 demandées (natif : 2560) |
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
