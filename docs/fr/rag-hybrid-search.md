# Recherche hybride

Un code produit bénéficie de la recherche par mots-clés, une question formulée autrement de la recherche sémantique. Le moteur choisi prépare seulement la représentation nécessaire ; la recherche lexicale n’impose plus d’embedding préalable.

## Modes de recherche intégrés

```csharp
// Recherche sémantique (par défaut)
.UseVectorSearch()

// Recherche lexicale sans embedding de requête
.UseKeywordSearch()

// Recherche hybride pondérée
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` ignore les embeddings de requête. L’import découpe et vectorise toujours les documents pour le stockage existant ; ce n’est pas une indexation purement textuelle. L’initialisation différée peut encore appeler les embeddings de documents lors de la première question.

## Combiner les résultats lexicaux et sémantiques

`VectorWeight` définit le poids vectoriel (0–1), et `1 - VectorWeight` le poids lexical. `CandidateMultiplier` contrôle les candidats par branche, `RrfK` le lissage des rangs de Reciprocal Rank Fusion pondérée. Ils sont distincts du multiplicateur du reranker RAG. Évaluez-les avec vos documents et questions.

Les modes vectoriel et lexical purs gardent leurs scores natifs. Le mode hybride configurable utilise un RRF pondéré normalisé même avec une seule branche ; un poids vectoriel nul évite l’embedding de requête. Ces scores ne sont pas des probabilités. `WeightedBlend` mélange les scores sans calibration ; préférez `RerankerOnly` pour le lexical sans calibration préalable.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## Prise en charge et compatibilité

InMemory, PostgreSQL et Qdrant prennent en charge les nouveaux chemins lexical et RRF pondéré configurable. Les scores textuels diffèrent : BM25 pour InMemory, plein texte ou trigrammes configurés pour PostgreSQL, index creux pour Qdrant. Les scores ne sont pas équivalents entre moteurs.

Pinecone conserve son chemin hybride natif via `UseHybridSearch()` avec les valeurs par défaut sur un index `dotproduct` compatible. Cet adaptateur ne prend pas en charge le mode lexical ni le RRF pondéré configurable avec les deux branches. Les autres stockages doivent implémenter les interfaces optionnelles correspondantes. Les modes ou options non pris en charge échouent explicitement, sans bascule silencieuse vers le vectoriel ni poids ignorés.

Les adaptateurs InMemory, PostgreSQL et Qdrant existants n’installent pas de modèle neuronal et ne migrent pas les index. La distinction `C#`/`C++` dépend de leurs analyseurs. L’option PIXIE ci-dessous nécessite aussi une évaluation des identifiants exacts.

Voir [modes et stockages compatibles](rag.md#retrieval-modes) et [moteurs personnalisés](rag-pipeline.md#custom-retriever).

<a id="pixie-search"></a>

## Comparer une recherche neuronale locale avec PIXIE

Quand la question et le document emploient des termes différents, une recherche creuse apprise peut ajouter du vocabulaire associé. Le paquet optionnel `Mythosia.AI.Rag.Search.Pixie` encode localement les documents et les requêtes avec PIXIE et combine ces résultats avec vos embeddings denses existants. PIXIE n’exige ni serveur Python ni clé API ; vos fournisseurs d’embeddings denses ou de réponses peuvent encore utiliser une API distante.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

Avec ce stockage, `UseKeywordSearch()` sélectionne la recherche neuronale creuse : il ignore le fournisseur d’embedding dense de requête, mais exécute PIXIE sur la question. L’indexation RAG produit toujours les embeddings denses des documents. `UseHybridSearch(...)` fusionne les rangs du produit scalaire creux et du cosinus dense avec le RRF pondéré configuré.

Cette préversion fournit `PixieInMemoryStore`, un index en mémoire. Elle n’ajoute pas PIXIE à PostgreSQL, Qdrant ou Pinecone. Réindexez après un redémarrage ou un changement de modèle/paramètres. Gardez l’encodeur vivant pendant toutes les opérations, puis libérez-le. La recherche existante reste celle par défaut : comparez les mêmes documents et questions annotées avant de changer. PIXIE ne garantit ni la distinction exacte `C#`/`C++`, ni les contraintes d’exclusion.

[Guide PIXIE et comparaison (anglais)](../rag-pixie-search.md).
