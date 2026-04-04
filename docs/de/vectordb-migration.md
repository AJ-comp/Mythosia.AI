# Vektorspeicher-Migration

Mythosia.AI enthält Migrationswerkzeuge für das Upgrade von Vektorspeicher-Schemas zwischen Versionen. Das wird hauptsächlich beim Update von einem älteren Collection-Schema (nur Dense) auf das aktuelle Hybrid-Schema (Dense + Sparse Vektoren) verwendet.

## Wann Migration nötig ist

Wenn du eine Qdrant-Collection mit einer älteren Version der Bibliothek erstellt hast (vor Einführung der Hybridsuche), ist die Collection im **Nur-Dense-Schema**. Das Ausführen von Hybridsuche dagegen schlägt fehl oder liefert fehlerhafte Ergebnisse.

Die Migration aktualisiert deine Collection auf das aktuelle **Hybrid-Schema** (Schema-Version 2), das sowohl Dense als auch Sparse Vektoren pro Datensatz speichert.

## CLI-Tool

Das Migrations-CLI-Tool installieren:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Befehle

**`migrate`** — Collection in-place aktualisieren:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source meine-sammlung \
  [--api-key dein-key] \
  [--replace]
```

- Ohne `--replace`: erstellt eine neue Collection namens `meine-sammlung_migrated`
- Mit `--replace`: überschreibt die Quell-Collection bei Erfolg (destruktiv)

**`copy`** — Collection mit Schema-Upgrade kopieren:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source meine-sammlung \
  --target meine-sammlung-v2 \
  [--api-key dein-key]
```

Erstellt eine neue Ziel-Collection mit dem aktuellen Schema und kopiert alle Datensätze aus der Quelle.

## Programmatische Migration

`QdrantVectorStoreMigrator` direkt im Code verwenden:

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "meine-sammlung",
    Dimension      = 1536
});
```

### Vor der Migration planen

Prüfen, was die Migration tut, bevor sie ausgeführt wird:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "my-collection"
});

Console.WriteLine($"Aktuelles Schema: {plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"Ziel-Schema:      {plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"Migration erforderlich: {plan.MigrationRequired}");
```

### Migration mit Fortschrittsanzeige ausführen

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "my-collection",
        ReplaceOnSuccess = false   // true = Quelle bei Abschluss überschreiben
    },
    progress: progress
);

Console.WriteLine($"Migriert: {result.MigratedRecords} Datensätze");
```

### In neue Collection kopieren

Eine Collection kopieren und dabei ihr Schema upgraden, ohne die Quelle zu berühren:

```csharp
var result = await migrator.CopyAsync(
    source:   "meine-sammlung",
    target:   "meine-sammlung-v2",
    progress: progress,
    cancellationToken: default
);
```

## Schema-Versionierung

Mythosia.AI verfolgt die Schema-Version intern über einen speziellen Marker-Datensatz in Qdrant (ID `__mythosia_schema__`). Das musst du nicht manuell verwalten.

| Schema-Version | Art | Beschreibung |
|---------------|------|-------------|
| 1 | `dense` | Nur Dense-Vektoren (Legacy) |
| 2 | `hybrid` | Dense + Sparse Vektoren (aktuell) |

Wenn eine Collection ohne Schema-Marker gelesen wird, wird sie als Version 1 (Legacy) behandelt und zur Migration markiert.

## Unterstützte Anbieter

| Anbieter | Migrieren | Kopieren |
|----------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
