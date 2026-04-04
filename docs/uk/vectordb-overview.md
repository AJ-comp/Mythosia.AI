# Огляд векторних баз даних

Mythosia.AI надає єдину абстракцію `IVectorStore`, що працює з різними бекендами векторних баз даних. Ви пишете застосунок проти інтерфейсу один раз і змінюєте бекенд без зміни логіки витягування.

## Основний інтерфейс: `IVectorStore`

```csharp
// Вставка/оновлення
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Пошук
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Отримання за ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Видалення
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Утиліти
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Моделі даних

### VectorRecord

Кожен запис зберігається як `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Унікальний ідентифікатор
    public float[] Vector { get; set; }                      // Вектор ембеддингу
    public string Content { get; set; }                      // Вихідний текстовий контент
    public Dictionary<string, string> Metadata { get; set; } // Користувацькі метадані
}
```

У словнику `Metadata` зберігайте будь-які додаткові поля — вихідний файл, мову, дату, категорію тощо:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("Some text"),
    Content = "Some text",
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["language"] = "en",
        ["date"] = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

Результати пошуку поєднують запис з оцінкою схожості:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (вище = більш схоже)
}
```

## Доступні бекенди

| Бекенд | Пакет | Призначення |
|--------|-------|-------------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Розробка, тести, демо |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Продакшен, нативний гібридний пошук |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Серверлес-керований сервіс |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Наявні розгортання Postgres, ACID |

Усі бекенди реалізують єдиний інтерфейс `IVectorStore`. Детальніше про налаштування кожного — у розділі [Налаштування бекендів](vectordb-backends.md).

## Впровадження залежностей

Реєстрація будь-якого бекенда як `IVectorStore`:

```csharp
// In-Memory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "my-collection",
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

## Виконання фільтрів за бекендами

Умови `VectorFilter` за можливості делегуються на бік бекенда:

| Оператор | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Клієнт | **Сервер** | **Сервер** | **SQL** |
| In / NotIn | Клієнт | **Сервер** | **Сервер** | **SQL** |
| Gt / Gte / Lt / Lte | Клієнт | Клієнт | Клієнт | **SQL** |
| Like | Клієнт | Клієнт | Клієнт | **SQL** |
| Exists / NotExists | Клієнт | Клієнт | Клієнт | **SQL** |

Postgres забезпечує повне SQL-делегування для всіх операторів. Qdrant та Pinecone нативно підтримують рівність та перевірку належності множині.
