# Vektorspeicher-Operationen

## Upsert

Einen einzelnen Datensatz einfügen oder aktualisieren. Existiert bereits ein Datensatz mit derselben `Id`, wird er ersetzt.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("Rückgaben werden innerhalb von 30 Tagen akzeptiert."),
    Content = "Rückgaben werden innerhalb von 30 Tagen akzeptiert.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "de",
        ["section"]  = "rueckgaben"
    }
};

await store.UpsertAsync(record);
```

## Batch-Upsert

Mehrere Datensätze in einem einzigen Aufruf einfügen oder aktualisieren. Effizienter als `UpsertAsync` in einer Schleife aufzurufen — Backends nutzen Batch-APIs intern, wo verfügbar.

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "handbuch.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## Suchen

Gibt die Top-K ähnlichsten Datensätze zu einem Abfragevektor zurück. Optional nach Metadaten filtern, bevor bewertet wird.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("Was ist die Rückgaberichtlinie?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Quelle: {r.Record.Metadata["source"]}");
}
```

### Gefilterte Suche

Vektorähnlichkeit mit Metadaten-Filterung kombinieren:

```csharp
var filter = new VectorFilter()
    .Where("language", "de")
    .Where("section", "rueckgaben")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

Weitere Informationen zur Filter-API unter [VectorFilter](vector-filter.md).

## Hybridsuche

Kombiniert Dense-Vektorähnlichkeit mit Schlüsselwortsuche (BM25). Besserer Recall für Abfragen mit spezifischen Begriffen, Namen oder Codes.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("Bestellung #12345 Status");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "Bestellung #12345 Status",   // Rohtext für BM25
    topK: 5
);
```

Wie Hybridsuche je Backend funktioniert:

| Backend | Mechanismus |
|---------|-----------|
| **InMemory** | RRF kombiniert Kosinus-Ähnlichkeit + Lucene BM25-Bewertungen |
| **Qdrant** | Serverseitig: Dense + Sparse Vektoren mit RRF oder DBSF fusioniert |
| **Pinecone** | Sparse + Dense Vektoren serverseitig zusammengeführt |
| **Postgres** | Vektorähnlichkeit + `tsvector`/`trigram`-Bewertungen in SQL kombiniert |

### Textsuche und konfigurierbare Hybridsuche (Unreleased)

Die obige Überladung verwendet das bisherige Hybridverhalten des jeweiligen Backends. Im aktuellen Quellstand implementieren InMemory, PostgreSQL und Qdrant außerdem `ITextSearchStore` und `IConfigurableHybridSearchStore`. Diese optionalen APIs sind **noch nicht veröffentlicht (Unreleased)**; Pinecone implementiert sie nicht.

```csharp
using Mythosia.VectorDb;

var filter = new VectorFilter().Where("tenant", "acme");
var textResults = await ((ITextSearchStore)store).TextSearchAsync(
    "Bestellung #12345 Status", topK: 5, filter: filter);

var hybridResults = await ((IConfigurableHybridSearchStore)store).HybridSearchAsync(
    queryVector, "Bestellung #12345 Status",
    new HybridSearchOptions { VectorWeight = 0.7f, CandidateMultiplier = 4, RrfK = 60 },
    topK: 5, filter: filter);
```

`TextSearchAsync` benötigt keinen Dense-Abfragevektor. Die konfigurierbare Überladung verwendet gewichtete RRF-Scores, normalisiert auf `[0, 1]`; Gewicht `0` überspringt die Vektorsuche, Gewicht `1` die Textsuche. Metadatenfilter greifen vor der Top-K-Auswahl, `MinScore` nach der Fusion. Native Text- und Vektorscores haben unterschiedliche Skalen; Schwellenwerte müssen zum Modus passen. Qdrants `HybridFusionStrategy` steuert nur die bisherige Überladung oben.

## Per ID abrufen

Einen bestimmten Datensatz über seine ID abrufen:

```csharp
VectorRecord? record = await store.GetAsync("datensatz-id-123");

if (record is null)
    Console.WriteLine("Nicht gefunden");
```

Filter anwenden, um die Suche einzuschränken (z. B. bei Mehrmandanten-Namespaces):

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("datensatz-id-123", filter: filter);
```

## Batch-Abruf

Mehrere Datensätze per ID in einem einzigen Aufruf abrufen:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## Per ID löschen

Einen einzelnen Datensatz entfernen:

```csharp
await store.DeleteAsync("datensatz-id-123");
```

## Per Filter löschen

Alle Datensätze entfernen, die einem Filter entsprechen. Mit Bedacht verwenden — das ist ein Massenlöschvorgang.

```csharp
// Alle Datensätze eines bestimmten Dokuments löschen
var filter = new VectorFilter().Where("source", "altes-handbuch.pdf");
await store.DeleteByFilterAsync(filter);
```

## Per Filter ersetzen

Alle einem Filter entsprechenden Datensätze löschen und eine neue Menge einfügen. Nützlich für die Neuindizierung eines Dokuments ohne veraltete Abschnitte zu hinterlassen. Die Atomarität des gesamten Austauschs hängt vom Backend ab.

```csharp
var filter = new VectorFilter().Where("source", "handbuch-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "handbuch-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> Postgres verwendet eine Datenbanktransaktion. InMemory führt Löschen und Batch-Einfügen nacheinander aus, sodass eine andere Abfrage die Lücke sehen kann. Bei Fehlern oder Abbruch kann ein teilweise ersetzter Zustand bleiben; abgeschlossene Schreibvorgänge werden nicht zurückgesetzt. Die Synchronisierung einzelner Operationen hält Datensätze und BM25-Index konsistent, macht aber den gesamten Austausch nicht transaktional.

## Zählen

Gespeicherte Datensätze zählen, optional nach Filter eingeschränkt:

```csharp
long gesamt   = await store.CountAsync();
long deutsch  = await store.CountAsync(new VectorFilter().Where("language", "de"));

Console.WriteLine($"Gesamt: {gesamt}, Deutsch: {deutsch}");
```

## Verbindung prüfen

Prüfen, ob das Backend erreichbar ist. Nützlich bei Health Checks oder der Startvalidierung:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Vektorspeicher-Verbindung OK");
}
catch (Exception ex)
{
    Console.WriteLine($"Verbindung fehlgeschlagen: {ex.Message}");
}
```

## Mit RAG verwenden

Einen `IVectorStore` an `RagBuilder` übergeben, um ein beliebiges Backend als RAG-Retrieval-Speicher zu nutzen:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "wissensdatenbank",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey)
        .AddDocuments("docs/")
    );

var answer = await ragService.GetCompletionAsync("Was ist die Rückgaberichtlinie?");
```

Oder einen `RagStore` unabhängig aufbauen und über mehrere KI-Services teilen:

```csharp
RagStore ragStore = await RagStore.BuildAsync(rag => rag
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey)
    .AddDocument("wissensdatenbank.pdf"));

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
