# VectorFilter

`VectorFilter` est une API fluente pour filtrer les requêtes de stockage vectoriel par métadonnées. S'applique à `IVectorStore.SearchAsync`, `HybridSearchAsync` et aux requêtes RAG.

## Égalité simple

```csharp
var filter = new VectorFilter()
    .Where("source", "manuel.pdf")
    .Where("language", "fr");
```

## Opérateurs de comparaison

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archivé");
```

| Méthode | Équivalent SQL |
|--------|---------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## Appartenance à un ensemble

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "juridique", "conformité", "politique")
    .WhereNotIn("type", "brouillon", "archivé");
```

## Existence de clé

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // La clé doit être présente
    .WhereNotExists("deprecated");   // La clé doit être absente
```

## Groupement logique (AND / OR)

Les conditions au même niveau sont combinées avec AND par défaut. Utilisez `.Or()` pour créer des groupes OR :

```csharp
var filter = new VectorFilter()
    .Where("source", "manuel.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "haute")
    );
// source = "manuel.pdf" AND (type = "urgent" OR priority = "haute")
```

AND imbriqué :

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "fr").Where("region", "fr"))
        .And(a => a.Where("lang", "de").Where("region", "de"))
    );
// (lang = "fr" AND region = "fr") OR (lang = "de" AND region = "de")
```

## Seuil de score

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Utiliser avec le stockage vectoriel

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contrat")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## Utiliser avec le RAG

Passez comme `StoreFilter` dans `RagQueryOptions` :

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "manuel-produit.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("Comment réinitialiser l'appareil ?", options);
```
