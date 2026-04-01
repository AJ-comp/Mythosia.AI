# Vector Store Migration

Mythosia.AI includes migration tooling for upgrading vector store schemas between versions. This is primarily used when updating from an older collection schema (dense-only) to the current hybrid schema (dense + sparse vectors).

## When You Need Migration

If you created a Qdrant collection with an earlier version of the library (before hybrid search was introduced), the collection will be in a **dense-only** schema. Running hybrid search against it will fail or produce incorrect results.

Migration upgrades your collection to the current **hybrid schema** (schema version 2), which stores both dense and sparse vectors per record.

## CLI Tool

Install the migration CLI tool:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Commands

**`migrate`** — Upgrade a collection in-place:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- Without `--replace`: creates a new collection named `my-collection_migrated`
- With `--replace`: overwrites the source collection on success (destructive)

**`copy`** — Copy a collection with schema upgrade:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

Creates a new target collection with the current schema and copies all records from source.

## Programmatic Migration

Use `QdrantVectorStoreMigrator` directly in code:

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "my-collection",
    Dimension      = 1536
});
```

### Plan Before Migrating

Check what migration will do before running it:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" },
    Target = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" }
});

Console.WriteLine($"Current schema: {plan.CurrentSchema}");
Console.WriteLine($"Target schema:  {plan.TargetSchema}");
Console.WriteLine($"Records to migrate: {plan.RecordCount}");
```

### Run Migration with Progress

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source          = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" },
        Target          = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" },
        ReplaceOnSuccess = false   // true = overwrite source on completion
    },
    progress: progress
);

Console.WriteLine($"Migrated: {result.MigratedRecords} records");
Console.WriteLine($"Errors:   {result.ErrorCount}");
```

### Copy to a New Collection

Copy a collection while upgrading its schema, without touching the source:

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## Schema Versioning

Mythosia.AI tracks schema version internally using a special marker record in Qdrant (ID `__mythosia_schema__`). You do not need to manage this manually.

| Schema Version | Kind | Description |
|---------------|------|-------------|
| 1 | `dense` | Dense vectors only (legacy) |
| 2 | `hybrid` | Dense + sparse vectors (current) |

If you read a collection that has no schema marker, it is treated as version 1 (legacy) and flagged for migration.

## Supported Providers

| Provider | Migrate | Copy |
|----------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
