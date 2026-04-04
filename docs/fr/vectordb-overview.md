# Vue d'ensemble de la base de données vectorielle

Mythosia.AI fournit une abstraction `IVectorStore` unifiée qui fonctionne sur plusieurs backends de bases de données vectorielles. Vous écrivez votre application une seule fois contre l'interface et pouvez changer de backend sans modifier aucune logique de récupération.

## Interface principale : `IVectorStore`

```csharp
// Insérer/Mettre à jour
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Rechercher
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Récupérer par ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Supprimer
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Utilitaires
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Modèles de données

### VectorRecord

Chaque entrée stockée est un `VectorRecord` :

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Identifiant unique
    public float[] Vector { get; set; }                      // Vecteur d'embedding
    public string Content { get; set; }                      // Contenu textuel original
    public Dictionary<string, string> Metadata { get; set; } // Métadonnées personnalisées
}
```

Utilisez le dictionnaire `Metadata` pour tout champ personnalisé — fichier source, langue, date, catégorie, etc. :

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("Un texte quelconque"),
    Content = "Un texte quelconque",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manuel.pdf",
        ["language"] = "fr",
        ["date"] = "2024-01-15",
        ["category"] = "politique"
    }
};
```

### VectorSearchResult

Les résultats de recherche associent un enregistrement à son score de similarité :

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0,0–1,0 (plus élevé = plus similaire)
}
```

## Backends disponibles

| Backend | Package | Cas d'usage |
|---------|---------|----------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Développement, tests, démos |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Production, recherche hybride native |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Service managé serverless |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Déploiements Postgres existants, ACID |

Tous les backends implémentent la même interface `IVectorStore`. Consultez [Configuration du backend](vectordb-backends.md) pour la configuration par backend.

## Injection de dépendances

Enregistrez n'importe quel backend comme `IVectorStore` :

```csharp
// In-Memory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "ma-collection",
    Dimension = 1536
}));

// PostgreSQL
services.AddSingleton<IVectorStore>(new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Database=vectors;",
    Dimension = 1536,
    EnsureSchema = true
}));
```

## Exécution des filtres par backend

Les conditions `VectorFilter` sont poussées vers le backend lorsque c'est possible :

| Opérateur | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Client | **Serveur** | **Serveur** | **SQL** |
| In / NotIn | Client | **Serveur** | **Serveur** | **SQL** |
| Gt / Gte / Lt / Lte | Client | Client | Client | **SQL** |
| Like | Client | Client | Client | **SQL** |
| Exists / NotExists | Client | Client | Client | **SQL** |

Postgres dispose d'un pushdown SQL complet pour tous les opérateurs. Qdrant et Pinecone poussent nativement les tests d'égalité et d'appartenance à un ensemble.
