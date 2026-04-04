# Opérations sur le stockage vectoriel

## Upsert

Insérez ou mettez à jour un enregistrement unique. Si un enregistrement avec le même `Id` existe déjà, il est remplacé.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("Les remboursements sont acceptés dans les 30 jours."),
    Content = "Les remboursements sont acceptés dans les 30 jours.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "fr",
        ["section"]  = "retours"
    }
};

await store.UpsertAsync(record);
```

## Batch Upsert

Insérez ou mettez à jour plusieurs enregistrements en un seul appel. Plus efficace qu'appeler `UpsertAsync` en boucle — les backends utilisent des API batch en interne lorsque c'est possible.

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manuel.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## Rechercher

Retourne les K enregistrements les plus similaires à un vecteur de requête. Filtrez optionnellement par métadonnées avant le scoring.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("Quelle est la politique de remboursement ?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Source : {r.Record.Metadata["source"]}");
}
```

### Recherche filtrée

Combinez la similarité vectorielle avec le filtrage par métadonnées :

```csharp
var filter = new VectorFilter()
    .Where("language", "fr")
    .Where("section", "retours")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

Consultez [VectorFilter](vector-filter.md) pour l'API de filtrage complète.

## Recherche hybride

Fusionne la similarité vectorielle dense avec la recherche par mots-clés (BM25). Meilleur recall pour les requêtes avec des termes spécifiques, des noms ou des codes.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("commande #12345 statut");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "commande #12345 statut",   // Texte brut pour BM25
    topK: 5
);
```

Fonctionnement de la recherche hybride par backend :

| Backend | Mécanisme |
|---------|-----------|
| **InMemory** | RRF fusionne similarité cosinus + scores Lucene BM25 |
| **Qdrant** | Côté serveur : vecteurs dense + sparse fusionnés avec RRF ou DBSF |
| **Pinecone** | Vecteurs sparse + dense fusionnés côté serveur |
| **Postgres** | Similarité vectorielle + scores `tsvector`/`trigram` fusionnés en SQL |

## Récupérer par ID

Récupérez un enregistrement spécifique par son ID :

```csharp
VectorRecord? record = await store.GetAsync("id-enregistrement-123");

if (record is null)
    Console.WriteLine("Non trouvé");
```

Appliquez un filtre pour limiter la recherche (ex. namespaces multi-tenants) :

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("id-enregistrement-123", filter: filter);
```

## Récupération par lot

Récupérez plusieurs enregistrements par ID en un seul appel :

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## Supprimer par ID

Supprimez un enregistrement unique :

```csharp
await store.DeleteAsync("id-enregistrement-123");
```

## Supprimer par filtre

Supprimez tous les enregistrements correspondant à un filtre. À utiliser avec précaution — c'est une suppression en masse.

```csharp
// Supprimer tous les enregistrements d'un document spécifique
var filter = new VectorFilter().Where("source", "ancien-manuel.pdf");
await store.DeleteByFilterAsync(filter);
```

## Remplacer par filtre

Supprimez atomiquement tous les enregistrements correspondant à un filtre et insérez un nouvel ensemble. Utile pour réindexer un document sans laisser de passages obsolètes.

```csharp
var filter = new VectorFilter().Where("source", "manuel-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manuel-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> Sur Postgres, cette opération s'exécute dans une transaction, la rendant entièrement atomique.

## Compter

Comptez les enregistrements stockés, optionnellement limités par filtre :

```csharp
long total   = await store.CountAsync();
long francais = await store.CountAsync(new VectorFilter().Where("language", "fr"));

Console.WriteLine($"Total : {total}, Français : {francais}");
```

## Vérifier la connexion

Vérifiez que le backend est accessible. Utile dans les health checks ou la validation au démarrage :

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Connexion au stockage vectoriel OK");
}
catch (Exception ex)
{
    Console.WriteLine($"Connexion échouée : {ex.Message}");
}
```

## Utiliser avec le RAG

Passez un `IVectorStore` à `RagBuilder` pour utiliser n'importe quel backend comme stockage de récupération RAG :

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "base-de-connaissances",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey, http)
        .AddDirectory("docs/", ".txt", ".md")
    );

var answer = await ragService.GetCompletionAsync("Quelle est la politique de retour ?");
```

Ou construisez un `RagStore` indépendamment et partagez-le entre plusieurs services IA :

```csharp
RagStore ragStore = await RagBuilder.Create()
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey, http)
    .AddDocument("base-de-connaissances.pdf")
    .BuildAsync();

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
