# VectorFilter

`VectorFilter` ist eine Fluent-API zum Filtern von Vektorspeicher-Abfragen nach Metadaten. Gilt für `IVectorStore.SearchAsync`, `HybridSearchAsync` und RAG-Abfragen.

## Einfache Gleichheit

```csharp
var filter = new VectorFilter()
    .Where("source", "handbuch.pdf")
    .Where("language", "de");
```

## Vergleichsoperatoren

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archiviert");
```

| Methode | SQL-Äquivalent |
|--------|---------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## Mengenzugehörigkeit

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "richtlinie")
    .WhereNotIn("type", "entwurf", "archiviert");
```

## Key-Existenz

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // Key muss vorhanden sein
    .WhereNotExists("deprecated");   // Key muss fehlen
```

## Logische Gruppierung (AND / OR)

Bedingungen auf derselben Ebene werden standardmäßig mit AND verbunden. Verwende `.Or()`, um OR-Gruppen zu erstellen:

```csharp
var filter = new VectorFilter()
    .Where("source", "handbuch.pdf")
    .Or(f => f
        .Where("type", "dringend")
        .Where("priority", "hoch")
    );
// source = "handbuch.pdf" AND (type = "dringend" OR priority = "hoch")
```

Verschachteltes AND:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "de").Where("region", "de"))
        .And(a => a.Where("lang", "fr").Where("region", "fr"))
    );
// (lang = "de" AND region = "de") OR (lang = "fr" AND region = "fr")
```

## Score-Schwellenwert

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Mit Vektorspeicher verwenden

```csharp
var filter = new VectorFilter()
    .Where("document_type", "vertrag")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## Mit RAG verwenden

Als `StoreFilter` in `RagQueryOptions` übergeben:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "produkt-handbuch.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("Wie setze ich das Gerät zurück?", options);
```

## Filter zusammenführen

Verwenden Sie `AppendConditionsFrom`, um zwei Filter zu kombinieren (z. B. Pipeline-Filter mit Query-Filter zusammenführen):

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "de");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter enthält nun beide Bedingungen
```
