# Backend-Einrichtung

## In-Memory

Das einfachste Backend — keine externen Abhängigkeiten. Daten liegen im RAM und gehen verloren, wenn der Prozess beendet wird. Gut für Entwicklung, Tests und Demos.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Eingebaute Hybridsuche**: RRF (Reciprocal Rank Fusion) kombiniert Kosinus-Ähnlichkeit und BM25-Schlüsselwortbewertungen.

### Diagnose

```csharp
// Alle gespeicherten Datensätze auflisten
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Gesamt: {store.GetTotalRecordCount()}");

// Rohe Ähnlichkeitsbewertungen inspizieren
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Produktionsreife Vektordatenbank mit nativer Hybridsuche. Läuft als eigenständiger Dienst über Docker oder Qdrant Cloud.

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# Qdrant lokal starten
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // gRPC-Port
    CollectionName   = "meine-docs",
    Dimension        = 1536,           // Muss zum Embedding-Modell passen
    AutoCreateCollection = true        // Erstellt Collection beim ersten Einfügen
});
```

### Alle Optionen

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Erforderlich für Qdrant Cloud

    CollectionName         = "meine-sammlung", // Pflichtfeld
    Dimension              = 1536,             // Pflichtfeld

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Zusätzliche Payload-Indizes für schnellere serverseitige Filterung
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Distanzstrategien

| Wert | Beschreibung |
|-------|-------------|
| `Cosine` | Kosinus-Ähnlichkeit — am besten für normalisierte Embeddings (Standard) |
| `Euclidean` | L2-Distanz — geringere Distanz = ähnlicher |
| `DotProduct` | Skalarprodukt — für einheitsnormalisierte Vektoren |

### Hybrid-Fusionsstrategien

| Wert | Beschreibung |
|-------|-------------|
| `Rrf` | Reciprocal Rank Fusion — robuste rangbasierte Zusammenführung (Standard) |
| `Dbsf` | Distribution-Based Score Fusion — Zusammenführung nach Score-Verteilung |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "dein-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "dein-qdrant-cloud-key",
    CollectionName = "produktion",
    Dimension      = 1536
}
```

### Externen QdrantClient verwenden

Wenn Sie bereits einen konfigurierten `QdrantClient` haben (z. B. aus einem DI-Container), übergeben Sie ihn direkt:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

Der Store gibt den extern bereitgestellten Client **nicht** frei.

> Alle Vector Stores implementieren `IDisposable`. Wenn Sie einen Store mit dem Standard-Konstruktor erstellen, rufen Sie `Dispose()` auf (oder verwenden Sie `using`), um interne Ressourcen freizugeben.

---

## Pinecone

Vollständig verwaltete serverlose Vektordatenbank. Keine Infrastruktur zu verwalten.

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://mein-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "dein-api-key"
});
```

### Index automatisch erstellen

Falls noch kein Index vorhanden ist, lasse das SDK ihn erstellen:

```csharp
new PineconeOptions
{
    ApiKey          = "dein-api-key",
    AutoCreateIndex = true,
    IndexName       = "mein-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp" oder "azure"
    Region          = "us-east-1"
}
```

> Wenn `AutoCreateIndex` aktiviert ist, wird der Index mit dem Metrik `dotproduct` erstellt — erforderlich für Hybridsuche (sparse + dense).

### Alle Optionen

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // Pflichtfeld (oder AutoCreateIndex nutzen)
    ApiKey                 = "...",           // Pflichtfeld
    Namespace              = "produktion",    // Optional: für alle Operationen angewendet

    UpsertBatchSize        = 100,             // Datensätze pro Batch-Upsert-Anfrage
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### Externen HttpClient verwenden

Wenn Sie bereits einen konfigurierten `HttpClient` haben (z. B. aus einer `IHttpClientFactory`):

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

Der Store gibt den extern bereitgestellten Client **nicht** frei.

---

## PostgreSQL (pgvector)

Nutzt die [`pgvector`](https://github.com/pgvector/pgvector)-Erweiterung, um Vektorähnlichkeitssuche zu einer Standard-PostgreSQL-Datenbank hinzuzufügen.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Voraussetzungen

```sql
-- Einmal auf deinem PostgreSQL-Server ausführen
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Nur bei Trigram-Textsuche
```

Oder lass das SDK das mit `EnsureSchema = true` automatisch erledigen.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=meindb;Username=benutzer;Password=passwort;",
    Dimension        = 1536,
    EnsureSchema     = true    // Erstellt Extension, Tabelle und Indizes automatisch
});
```

### Indextypen

| Typ | Klasse | Wann verwenden |
|------|-------|-------------|
| HNSW | `HnswIndexOptions` | Standard. Schnelle Näherungssuche. Für die meisten Fälle am besten. |
| IVFFlat | `IvfFlatIndexOptions` | Weniger Speicher. Gut für große statische Datensätze. |
| Keiner | `NoIndexOptions` | Sequenzieller Scan. Nur für sehr kleine Datensätze. |

```csharp
// HNSW (Standard)
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // Maximale Nachbarverbindungen pro Knoten
        EfConstruction = 64,   // Suchbereich beim Index-Aufbau (höher = bessere Qualität)
        EfSearch       = 40    // Laufzeit-Suchbereich (höher = besserer Recall, langsamer)
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // Anzahl der invertierten Listen
        Probes = 10    // Wie viele Listen bei der Abfrage durchsucht werden
    }
}

// Kein Index (sequenzieller Scan)
new PostgresOptions { Index = new NoIndexOptions() }
```

### Textsuche-Modi

Für die Schlüsselwortseite der Hybridsuche:

| Modus | Am besten für |
|------|----------|
| `TsVector` | Standard-Volltextsuche — Englisch, die meisten westeuropäischen Sprachen |
| `Trigram` | CJK-Sprachen (Koreanisch, Chinesisch, Japanisch), unscharfe Suche |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // PostgreSQL-Textsuchkonfiguration
}
```

### Distanzstrategien

| Wert | Postgres-Operator | Hinweise |
|-------|------------------|-------|
| `Cosine` | `<=>` | 1 − Kosinus-Ähnlichkeit (Standard) |
| `Euclidean` | `<->` | L2-Distanz |
| `InnerProduct` | `<#>` | Negatives Skalarprodukt — für einheitsnormalisierte Vektoren |

### Laufzeit-Suchprofil

Recall vs. Latenz zur Abfragezeit feinjustieren:

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // HNSW ef_search direkt überschreiben
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### Alle Optionen

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
