# Міграція векторного сховища

Mythosia.AI включає інструменти міграції для оновлення схем векторних сховищ між версіями. Передусім це використовується при оновленні зі старої схеми колекції (лише щільні вектори) до поточної гібридної схеми (щільні + розріджені вектори).

## Коли потрібна міграція

Якщо ви створили колекцію Qdrant з більш ранньою версією бібліотеки (до впровадження гібридного пошуку), колекція матиме схему **лише щільних векторів**. Гібридний пошук по такій колекції завершуватиметься помилкою або повертатиме некоректні результати.

Міграція оновлює колекцію до поточної **гібридної схеми** (версія схеми 2), в якій зберігаються як щільні, так і розріджені вектори для кожного запису.

## CLI-інструмент

Встановіть CLI-інструмент міграції:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### Команди

**`migrate`** — оновлення колекції на місці:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- Без `--replace`: створюється нова колекція з іменем `my-collection_migrated`
- З `--replace`: вихідна колекція перезаписується при успіху (деструктивна операція)

**`copy`** — копіювання колекції з оновленням схеми:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

Створює цільову колекцію з поточною схемою і копіює всі записи з вихідної.

## Програмна міграція

Використовуйте `QdrantVectorStoreMigrator` безпосередньо в коді:

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

### Планування перед міграцією

Перевірте, що станеться при міграції, перш ніж запускати її:

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

### Міграція з відстеженням прогресу

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
        ReplaceOnSuccess = false   // true = перезаписати вихідну колекцію по завершенні
    },
    progress: progress
);

Console.WriteLine($"Migrated: {result.MigratedRecords} records");
Console.WriteLine($"Errors:   {result.ErrorCount}");
```

### Копіювання в нову колекцію

Копіювання колекції з оновленням схеми, без зміни вихідної:

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## Версіонування схеми

Mythosia.AI відстежує версію схеми внутрішньо через спеціальний маркерний запис у Qdrant (ID `__mythosia_schema__`). Керувати цим вручну не потрібно.

| Версія схеми | Тип | Опис |
|-------------|-----|------|
| 1 | `dense` | Лише щільні вектори (застаріла) |
| 2 | `hybrid` | Щільні + розріджені вектори (поточна) |

Якщо колекція не містить маркера схеми, вона вважається версією 1 (застарілою) і позначається для міграції.

## Підтримувані провайдери

| Провайдер | Міграція | Копіювання |
|-----------|----------|------------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
