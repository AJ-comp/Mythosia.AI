# Vektordatenbank-Überblick

Mythosia.AI stellt eine einheitliche `IVectorStore`-Abstraktion bereit, die über mehrere Vektordatenbank-Backends hinweg funktioniert. Du schreibst deine Anwendung einmal gegen das Interface und kannst Backends tauschen, ohne jegliche Retrieval-Logik zu ändern.

## Kern-Interface: `IVectorStore`

```csharp
// Einfügen/Aktualisieren
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Suchen
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Per ID abrufen
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Löschen
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Hilfsfunktionen
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Datenmodelle

### VectorRecord

Jeder gespeicherte Eintrag ist ein `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Eindeutiger Bezeichner
    public float[] Vector { get; set; }                      // Embedding-Vektor
    public string Content { get; set; }                      // Ursprünglicher Textinhalt
    public Dictionary<string, string> Metadata { get; set; } // Benutzerdefinierte Metadaten
}
```

Verwende das `Metadata`-Dictionary für beliebige benutzerdefinierte Felder — Quelldatei, Sprache, Datum, Kategorie usw.:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("Irgendein Text"),
    Content = "Irgendein Text",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "handbuch.pdf",
        ["language"] = "de",
        ["date"] = "2024-01-15",
        ["category"] = "richtlinie"
    }
};
```

### VectorSearchResult

Suchergebnisse verbinden einen Datensatz mit seiner Ähnlichkeitsbewertung:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0,0–1,0 (höher = ähnlicher)
}
```

## Verfügbare Backends

| Backend | Paket | Anwendungsfall |
|---------|---------|----------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Entwicklung, Tests, Demos |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Produktiv, native Hybridsuche |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Serverloser verwalteter Dienst |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Bestehende Postgres-Deployments, ACID |

Alle Backends implementieren dasselbe `IVectorStore`-Interface. Backend-spezifische Konfiguration findest du unter [Backend-Einrichtung](vectordb-backends.md).

## Dependency Injection

Beliebiges Backend als `IVectorStore` registrieren:

```csharp
// In-Memory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "meine-sammlung",
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

## Filter-Ausführung nach Backend

`VectorFilter`-Bedingungen werden nach Möglichkeit ans Backend weitergeleitet:

| Operator | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Client | **Server** | **Server** | **SQL** |
| In / NotIn | Client | **Server** | **Server** | **SQL** |
| Gt / Gte / Lt / Lte | Client | Client | **Server** | **SQL** |
| Like | Client | Client | Client | **SQL** |
| Exists / NotExists | Client | Client | Client | **SQL** |

Postgres hat volles SQL-Pushdown für alle Operatoren. Qdrant und Pinecone pushen Gleichheits-, Mengenzugehörigkeits- und Vergleichsprüfungen nativ herunter.

> **Hinweis:** Qdrant ignoriert nicht unterstützte Filteroperatoren (`Like`, `Exists`, `NotExists`) stillschweigend — sie werden auch clientseitig nicht angewendet. Wenn Sie diese Operatoren mit Qdrant benötigen, wenden Sie zusätzliche Filterung auf die zurückgegebenen Ergebnisse an.
