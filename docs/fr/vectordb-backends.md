# Configuration du backend

## In-Memory

Le backend le plus simple — aucune dépendance externe. Les données sont en RAM et perdues à l'arrêt du processus. Idéal pour le développement, les tests et les démos.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Recherche hybride intégrée** : RRF (Reciprocal Rank Fusion) fusionne les scores de similarité cosinus et BM25.

### Accès simultanés et modification des enregistrements

Lorsqu’un stockage partagé est mis à jour pendant une requête, le texte et l’index de mots-clés doivent correspondre à la même révision. `InMemoryVectorStore` synchronise les écritures, suppressions et lectures pour que chaque recherche vectorielle, textuelle ou hybride consulte un état cohérent. Les deux branches d’une recherche hybride utilisent ce même état.

Le stockage copie les enregistrements entrants, y compris les tableaux de vecteurs et les métadonnées. Les consultations, recherches et diagnostics renvoient aussi des copies indépendantes. Modifier un objet d’entrée ou un enregistrement retourné ne modifie pas les données stockées ; appelez de nouveau `UpsertAsync` pour enregistrer le changement. Ne modifiez pas les enregistrements, vecteurs ou métadonnées d’entrée pendant que l’appel les copie ou les lit.

Un `CancellationToken` fourni peut annuler un appel pendant qu’il attend qu’une autre opération libère le verrou du stockage. L’annulation de cette attente n’interrompt pas à elle seule l’opération qui utilise déjà le stockage. Une fois la mise à jour d’un enregistrement commencée, l’annulation ne l’interrompt pas entre le texte et l’index.

L’annulation d’un lot peut conserver les enregistrements déjà écrits. `ReplaceByFilterAsync` effectue toujours la suppression puis l’insertion par lot sans transaction : une autre requête peut observer l’intervalle vide, et un échec ou une annulation n’annule pas les écritures terminées.

### Diagnostics

```csharp
// Lister tous les enregistrements stockés
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Total : {store.GetTotalRecordCount()}");

// Inspecter les scores bruts de similarité
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Base de données vectorielle de niveau production avec recherche hybride native. S'exécute en service autonome via Docker ou Qdrant Cloud.

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# Démarrer Qdrant en local
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // port gRPC
    CollectionName   = "mes-docs",
    Dimension        = 1536,           // Doit correspondre à votre modèle d'embedding
    AutoCreateCollection = true        // Crée la collection au premier upsert
});
```

### Toutes les options

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Requis pour Qdrant Cloud

    CollectionName         = "ma-collection",  // Obligatoire
    Dimension              = 1536,             // Obligatoire

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Index de payload supplémentaires pour un filtrage côté serveur plus rapide
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Stratégies de distance

| Valeur | Description |
|-------|-------------|
| `Cosine` | Similarité cosinus — idéale pour les embeddings normalisés (par défaut) |
| `Euclidean` | Distance L2 — distance plus faible = plus similaire |
| `DotProduct` | Produit scalaire — à utiliser avec des vecteurs unitaires |

### Stratégies de fusion hybride

| Valeur | Description |
|-------|-------------|
| `Rrf` | Reciprocal Rank Fusion — fusion robuste basée sur le rang (par défaut) |
| `Dbsf` | Distribution-Based Score Fusion — fusion par distribution de scores |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "votre-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "votre-clé-qdrant-cloud",
    CollectionName = "production",
    Dimension      = 1536
}
```

### Utiliser un QdrantClient externe

Si vous disposez déjà d'un `QdrantClient` configuré (par ex. depuis un conteneur DI), passez-le directement :

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

Le store ne libère **pas** le client fourni en externe.

> Tous les vector stores implémentent `IDisposable`. Lorsque vous créez un store avec le constructeur standard, appelez `Dispose()` (ou utilisez `using`) pour libérer les ressources internes.

---

## Pinecone

Base de données vectorielle serverless entièrement managée. Aucune infrastructure à gérer.

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://mon-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "votre-clé-api"
});
```

### Créer l'index automatiquement

Si vous n'avez pas encore d'index, laissez le SDK le créer :

```csharp
new PineconeOptions
{
    ApiKey          = "votre-clé-api",
    AutoCreateIndex = true,
    IndexName       = "mon-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp" ou "azure"
    Region          = "us-east-1"
}
```

> Quand `AutoCreateIndex` est activé, l'index est créé avec la métrique `dotproduct` — requise pour la recherche hybride (sparse + dense).

### Toutes les options

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // Obligatoire (ou utiliser AutoCreateIndex)
    ApiKey                 = "...",           // Obligatoire
    Namespace              = "production",    // Optionnel : appliqué à toutes les opérations

    UpsertBatchSize        = 100,             // Enregistrements par requête batch upsert
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### Utiliser un HttpClient externe

Si vous disposez déjà d'un `HttpClient` configuré (par ex. depuis un `IHttpClientFactory`) :

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

Le store ne libère **pas** le client fourni en externe.

---

## PostgreSQL (pgvector)

Utilise l'extension [`pgvector`](https://github.com/pgvector/pgvector) pour ajouter la recherche par similarité vectorielle à une base PostgreSQL standard.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Prérequis

```sql
-- À exécuter une fois sur votre serveur PostgreSQL
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Uniquement si vous utilisez la recherche Trigram
```

Ou laissez le SDK s'en charger automatiquement avec `EnsureSchema = true`.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mabd;Username=utilisateur;Password=motdepasse;",
    Dimension        = 1536,
    EnsureSchema     = true    // Crée automatiquement l'extension, la table et les index
});
```

### Types d'index

| Type | Classe | Quand l'utiliser |
|------|-------|-------------|
| HNSW | `HnswIndexOptions` | Par défaut. Recherche approchée rapide. Meilleur pour la plupart des cas. |
| IVFFlat | `IvfFlatIndexOptions` | Moins de mémoire. Bon pour les grands datasets statiques. |
| Aucun | `NoIndexOptions` | Scan séquentiel. À utiliser uniquement pour les petits datasets. |

```csharp
// HNSW (par défaut)
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // Connexions voisines max par nœud
        EfConstruction = 64,   // Portée de recherche à la construction (plus élevé = meilleure qualité)
        EfSearch       = 40    // Portée de recherche au runtime (plus élevé = meilleur recall, plus lent)
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // Nombre de listes inversées
        Probes = 10    // Combien de listes sonder à la requête
    }
}

// Pas d'index (scan séquentiel)
new PostgresOptions { Index = new NoIndexOptions() }
```

### Modes de recherche textuelle

Utilisés pour le côté mots-clés de la recherche hybride :

| Mode | Idéal pour |
|------|----------|
| `TsVector` | Recherche plein texte standard — anglais, la plupart des langues occidentales |
| `Trigram` | Langues CJK (coréen, chinois, japonais), correspondance floue |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // Configuration de recherche textuelle PostgreSQL
}
```

### Stratégies de distance

| Valeur | Opérateur Postgres | Notes |
|-------|------------------|-------|
| `Cosine` | `<=>` | 1 − similarité cosinus (par défaut) |
| `Euclidean` | `<->` | Distance L2 |
| `InnerProduct` | `<#>` | Produit scalaire négatif — pour les vecteurs unitaires |

### Profil de recherche au runtime

Affinez recall vs. latence à la requête :

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // Remplacer directement ef_search HNSW
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

> `Mythosia.VectorDb.Postgres` 10.8.1 ou ultérieur est nécessaire pour appliquer les paramètres vectoriels configurés lorsque les deux voies de recherche hybride sont actives. [Notes du correctif](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081).

Pour régler l'exploration des candidats vectoriels dans la recherche hybride, configurez `HnswIndexOptions.EfSearch` ou `IvfFlatIndexOptions.Probes` dans `PostgresOptions.Index`. Ces valeurs par défaut s'appliquent à la recherche vectorielle classique et à la branche vectorielle de la recherche hybride, dans la même transaction que la requête. Les surcharges d'exécution par requête présentées ci-dessus concernent uniquement `SearchAsync`. La recherche approchée et les filtres peuvent toujours renvoyer moins de `topK` résultats.

### Toutes les options

```csharp
new PostgresOptions
{
    ConnectionString  = "...",
    Dimension         = 1536,

    SchemaName        = "public",
    TableName         = "vectors",

    EnsureSchema      = false,
    DistanceStrategy  = DistanceStrategy.Cosine,
    Index             = new HnswIndexOptions(),

    TextSearchConfig  = "simple",
    TextSearchMode    = TextSearchMode.TsVector,

    FailFastOnIndexCreationFailure = true
}
```
