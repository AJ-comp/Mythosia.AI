# Migración del Vector Store

Mythosia.AI incluye herramientas de migración para actualizar schemas de vector store entre versiones. El caso principal es actualizar colecciones con schema antiguo (solo vectores densos) al schema híbrido actual (vectores densos + dispersos).

## Cuándo Necesitas Migrar

Si creaste una colección Qdrant con una versión anterior de la biblioteca (antes de que se introdujera el hybrid search), la colección estará en schema **solo denso**. Ejecutar hybrid search contra ella fallará o producirá resultados incorrectos.

La migración actualiza tu colección al **schema híbrido** actual (schema versión 2), que almacena vectores densos y dispersos por registro.

## Herramienta CLI

Instala la herramienta CLI de migración:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Comandos

**`migrate`** — Actualiza una colección en su lugar:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source mi-coleccion \
  [--api-key tu-clave] \
  [--replace]
```

- Sin `--replace`: crea una nueva colección llamada `mi-coleccion_migrated`
- Con `--replace`: sobreescribe la colección de origen al completar (destructivo)

**`copy`** — Copia una colección con actualización de schema:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source mi-coleccion \
  --target mi-coleccion-v2 \
  [--api-key tu-clave]
```

Crea una nueva colección de destino con el schema actual y copia todos los registros del origen.

## Migración Programática

Usa `QdrantVectorStoreMigrator` directamente en el código:

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "mi-coleccion",
    Dimension      = 1536
});
```

### Planifica Antes de Migrar

Comprueba qué hará la migración antes de ejecutarla:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "mi-coleccion"
});

Console.WriteLine($"Schema actual: {plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"Schema destino: {plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"Migración requerida: {plan.MigrationRequired}");
```

### Ejecutar Migración con Progreso

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "mi-coleccion",
        ReplaceOnSuccess = false   // true = sobreescribe el origen al completar
    },
    progress: progress
);

Console.WriteLine($"Migrados: {result.MigratedRecords} registros");
```

### Copiar a una Nueva Colección

Copia una colección actualizando su schema, sin tocar el origen:

```csharp
var result = await migrator.CopyAsync(
    source:   "mi-coleccion",
    target:   "mi-coleccion-v2",
    progress: progress,
    cancellationToken: default
);
```

## Versionado de Schema

Mythosia.AI rastrea la versión del schema internamente usando un registro marcador especial en Qdrant (ID `__mythosia_schema__`). No necesitas gestionarlo manualmente.

| Versión del Schema | Tipo | Descripción |
|-------------------|------|-------------|
| 1 | `dense` | Solo vectores densos (legado) |
| 2 | `hybrid` | Vectores densos + dispersos (actual) |

Si lees una colección que no tiene marcador de schema, se trata como versión 1 (legado) y se marca para migración.

## Proveedores Soportados

| Proveedor | Migrate | Copy |
|-----------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
