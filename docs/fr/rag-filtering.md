# Filtrage

> 📍 **Pipeline questions-réponses :** [Réécriture de requête](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → **`Filtrage`** → [Recherche](rag-hybrid-search.md) → [Re-ranking](rag-reranking.md) → [Construction du contexte](rag-context-build.md)

## Qu'est-ce que le filtrage ?

Le filtrage restreint **quels chunks sont pris en compte** avant la recherche de similarité. Au lieu de parcourir tout le store vectoriel, vous limitez la recherche à des sous-ensembles basés sur les métadonnées ou des seuils de score.

Le pipeline applique deux types de filtrage :

1. **Filtrage par métadonnées** — inclure ou exclure des chunks selon leurs métadonnées (catégorie, tenant, date)
2. **Filtrage par score** — définir un seuil minimum de similarité

## Filtrage par métadonnées

### Filtre par requête

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("Comment obtenir un remboursement ?", filter: filter);
```

### API fluide

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")
    .WhereNot("status", "archived")
    .WhereIn("region", "us-east", "eu-west")
    .WhereGreaterThan("year", "2023")
    .WhereLike("title", "%kubernetes%");
```

| Méthode | Équivalent SQL | Description |
| --- | --- | --- |
| `Where` | `=` | Correspondance exacte |
| `WhereNot` | `!=` | Différent de |
| `WhereIn` | `IN (...)` | Valeur dans un ensemble |
| `WhereNotIn` | `NOT IN (...)` | Valeur hors d'un ensemble |
| `WhereGreaterThan` | `>` | Supérieur à |
| `WhereGreaterThanOrEqual` | `>=` | Supérieur ou égal |
| `WhereLessThan` | `<` | Inférieur à |
| `WhereLessThanOrEqual` | `<=` | Inférieur ou égal |
| `WhereLike` | `LIKE` | Correspondance de motif |
| `WhereExists` | `IS NOT NULL` | La clé existe |
| `WhereNotExists` | `IS NULL` | La clé n'existe pas |

### Groupes logiques

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
```

## StoreFilter au niveau du pipeline

Pour des conditions qui s'appliquent **systématiquement** (comme l'isolation par tenant) :

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};
```

Le `StoreFilter` et le filtre par requête sont combinés en AND — aucun n'est ignoré.

## Filtrage par score

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};
```

Lorsqu'un [re-ranker](rag-reranking.md) est configuré, le seuil de récupération est automatiquement assoupli pour donner plus de candidats au re-ranker, puis le `MinScore` strict est appliqué après le re-ranking.

## Cas d'usage courants

### Isolation multi-tenant

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### Recherche par catégorie

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("erreur 404", filter: filter);
```

### Filtrage temporel

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## Étapes suivantes

- [Recherche hybride](rag-hybrid-search.md) — combiner recherche vectorielle et par mots-clés
- [Référence VectorFilter](vector-filter.md) — documentation complète de l'API de filtrage
- [Re-ranking](rag-reranking.md) — affiner les résultats après la récupération
