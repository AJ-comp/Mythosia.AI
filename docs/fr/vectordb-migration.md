# Migration du stockage vectoriel

Mythosia.AI inclut des outils de migration pour mettre à niveau les schémas de stockage vectoriel entre les versions. Cela s'utilise principalement lors d'une mise à jour depuis un ancien schéma de collection (dense uniquement) vers le schéma hybride actuel (vecteurs dense + sparse).

## Quand la migration est nécessaire

Si vous avez créé une collection Qdrant avec une version antérieure de la bibliothèque (avant l'introduction de la recherche hybride), la collection est dans un **schéma dense uniquement**. L'exécution d'une recherche hybride dessus échouera ou produira des résultats incorrects.

La migration met à niveau votre collection vers le **schéma hybride** actuel (version 2 du schéma), qui stocke à la fois des vecteurs dense et sparse par enregistrement.

## Outil CLI

Installez l'outil CLI de migration :

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Commandes

**`migrate`** — Mettre à niveau une collection sur place :

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source ma-collection \
  [--api-key votre-clé] \
  [--replace]
```

- Sans `--replace` : crée une nouvelle collection nommée `ma-collection_migrated`
- Avec `--replace` : écrase la collection source en cas de succès (destructif)

**`copy`** — Copier une collection avec mise à niveau du schéma :

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source ma-collection \
  --target ma-collection-v2 \
  [--api-key votre-clé]
```

Crée une nouvelle collection cible avec le schéma actuel et copie tous les enregistrements depuis la source.

## Migration programmatique

Utilisez `QdrantVectorStoreMigrator` directement dans le code :

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "ma-collection",
    Dimension      = 1536
});
```

### Planifier avant de migrer

Vérifiez ce que la migration va faire avant de l'exécuter :

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = "my-collection"
});

Console.WriteLine($"Schéma actuel : {plan.SchemaKind} v{plan.SchemaVersion}");
Console.WriteLine($"Schéma cible :  {plan.TargetSchemaKind} v{plan.TargetSchemaVersion}");
Console.WriteLine($"Migration requise : {plan.MigrationRequired}");
```

### Exécuter la migration avec suivi de progression

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = "my-collection",
        ReplaceOnSuccess = false   // true = écraser la source à la fin
    },
    progress: progress
);

Console.WriteLine($"Migrés : {result.MigratedRecords} enregistrements");
```

### Copier vers une nouvelle collection

Copiez une collection tout en mettant à niveau son schéma, sans toucher à la source :

```csharp
var result = await migrator.CopyAsync(
    source:   "ma-collection",
    target:   "ma-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## Gestion des versions de schéma

Mythosia.AI suit la version du schéma en interne via un enregistrement marqueur spécial dans Qdrant (ID `__mythosia_schema__`). Vous n'avez pas besoin de gérer cela manuellement.

| Version du schéma | Type | Description |
|---------------|------|-------------|
| 1 | `dense` | Vecteurs dense uniquement (hérité) |
| 2 | `hybrid` | Vecteurs dense + sparse (actuel) |

Si vous lisez une collection sans marqueur de schéma, elle est traitée comme version 1 (hérité) et signalée pour migration.

## Fournisseurs pris en charge

| Fournisseur | Migrer | Copier |
|----------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
