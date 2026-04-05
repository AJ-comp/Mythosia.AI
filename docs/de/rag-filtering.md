# Filtering

> 📍 **Fragen & Antworten Pipeline:** [Query-Umschreibung](rag-query-rewriting.md) → [Embedding](rag-embedding.md) → **`Filtering`** → [Retrieval](rag-hybrid-search.md) → [Re-Ranking](rag-reranking.md) → [Kontextaufbau](rag-context-build.md)

## Was ist Filtering?

Filtering grenzt ein, **welche Chunks überhaupt berücksichtigt werden**, bevor die Ähnlichkeitssuche läuft. Statt den gesamten Vector Store zu durchsuchen, wird die Suche auf bestimmte Teilmengen anhand von Metadaten oder Score-Schwellenwerten eingeschränkt.

Es werden zwei Arten von Filtering angewendet:

1. **Metadaten-Filtering** — Chunks anhand ihrer Metadaten (Kategorie, Tenant, Datum) ein- oder ausschließen
2. **Score-Filtering** — einen Mindest-Ähnlichkeitsscore festlegen

## Metadaten-Filtering

### Filter pro Abfrage

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("Wie bekomme ich eine Rückerstattung?", filter: filter);
```

### Fluent API

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")
    .WhereNot("status", "archived")
    .WhereIn("region", "us-east", "eu-west")
    .WhereGreaterThan("year", "2023")
    .WhereLike("title", "%kubernetes%");
```

| Methode | SQL-Äquivalent | Beschreibung |
| --- | --- | --- |
| `Where` | `=` | Exakte Übereinstimmung |
| `WhereNot` | `!=` | Ungleich |
| `WhereIn` | `IN (...)` | Wert in einer Menge |
| `WhereNotIn` | `NOT IN (...)` | Wert nicht in einer Menge |
| `WhereGreaterThan` | `>` | Größer als |
| `WhereGreaterThanOrEqual` | `>=` | Größer oder gleich |
| `WhereLessThan` | `<` | Kleiner als |
| `WhereLessThanOrEqual` | `<=` | Kleiner oder gleich |
| `WhereLike` | `LIKE` | Mustervergleich |
| `WhereExists` | `IS NOT NULL` | Schlüssel existiert |
| `WhereNotExists` | `IS NULL` | Schlüssel existiert nicht |

### Logische Gruppierung

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
```

## Pipeline-weiter StoreFilter

Für Bedingungen, die **immer gelten** sollen (z.B. Tenant-Isolation):

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};
```

`StoreFilter` und Query-Filter werden per AND kombiniert — keiner wird ignoriert.

## Score-Filtering

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

Bei konfiguriertem [Re-Ranker](rag-reranking.md) wird der Retrieval-Schwellenwert automatisch gelockert, um dem Re-Ranker mehr Kandidaten zu geben. Der strenge `MinScore` wird nach dem Re-Ranking angewendet.

## Häufige Anwendungsfälle

### Multi-Tenant-Isolation

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### Kategoriebasierte Suche

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("Fehler 404", filter: filter);
```

### Zeitbasiertes Filtering

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## Nächste Schritte

- [Hybridsuche](rag-hybrid-search.md) — Vektor- und Stichwortsuche kombinieren
- [VectorFilter-Referenz](vector-filter.md) — vollständige Filter-API-Dokumentation
- [Re-Ranking](rag-reranking.md) — Ergebnisse nach dem Retrieval verfeinern
