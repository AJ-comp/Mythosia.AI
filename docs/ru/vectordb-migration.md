# Миграция векторного хранилища

Mythosia.AI включает инструменты миграции для обновления схем векторных хранилищ между версиями. В первую очередь это используется при обновлении с устаревшей схемы коллекции (только плотные векторы) до текущей гибридной схемы (плотные + разреженные векторы).

## Когда нужна миграция

Если вы создали коллекцию Qdrant с более ранней версией библиотеки (до введения гибридного поиска), коллекция будет иметь схему **только плотных векторов**. Гибридный поиск по такой коллекции будет завершаться ошибкой или возвращать некорректные результаты.

Миграция обновляет коллекцию до текущей **гибридной схемы** (версия схемы 2), в которой хранятся как плотные, так и разреженные векторы для каждой записи.

## CLI-инструмент

Установите CLI-инструмент миграции:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Команды

**`migrate`** — обновление коллекции на месте:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- Без `--replace`: создаётся новая коллекция с именем `my-collection_migrated`
- С `--replace`: исходная коллекция перезаписывается при успехе (деструктивная операция)

**`copy`** — копирование коллекции с обновлением схемы:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

Создаёт целевую коллекцию с текущей схемой и копирует все записи из исходной.

## Программная миграция

Используйте `QdrantVectorStoreMigrator` напрямую в коде:

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

### Планирование перед миграцией

Проверьте, что произойдёт при миграции, прежде чем запускать её:

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

### Миграция с отслеживанием прогресса

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
        ReplaceOnSuccess = false   // true = перезаписать исходную коллекцию по завершении
    },
    progress: progress
);

Console.WriteLine($"Migrated: {result.MigratedRecords} records");
Console.WriteLine($"Errors:   {result.ErrorCount}");
```

### Копирование в новую коллекцию

Копирование коллекции с обновлением схемы, без изменения исходной:

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## Версионирование схемы

Mythosia.AI отслеживает версию схемы внутренне через специальную маркерную запись в Qdrant (ID `__mythosia_schema__`). Управлять этим вручную не нужно.

| Версия схемы | Тип | Описание |
|-------------|-----|----------|
| 1 | `dense` | Только плотные векторы (устаревшая) |
| 2 | `hybrid` | Плотные + разреженные векторы (текущая) |

Если коллекция не содержит маркера схемы, она считается версией 1 (устаревшей) и помечается для миграции.

## Поддерживаемые провайдеры

| Провайдер | Миграция | Копирование |
|-----------|----------|-------------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
