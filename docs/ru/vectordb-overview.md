# Обзор векторных баз данных

Mythosia.AI предоставляет единую абстракцию `IVectorStore`, работающую с различными бэкендами векторных баз данных. Вы пишете приложение против интерфейса один раз и меняете бэкенд без изменения логики извлечения.

## Основной интерфейс: `IVectorStore`

```csharp
// Вставка/обновление
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// Поиск
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// Получение по ID
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// Удаление
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// Утилиты
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## Модели данных

### VectorRecord

Каждая запись хранится как `VectorRecord`:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                           // Уникальный идентификатор
    public float[] Vector { get; set; }                      // Вектор эмбеддинга
    public string Content { get; set; }                      // Исходный текстовый контент
    public Dictionary<string, string> Metadata { get; set; } // Пользовательские метаданные
}
```

В словаре `Metadata` храните любые дополнительные поля — исходный файл, язык, дату, категорию и т.д.:

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

Результаты поиска объединяют запись с оценкой сходства:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (выше = более похоже)
}
```

## Доступные бэкенды

| Бэкенд | Пакет | Назначение |
|--------|-------|------------|
| **In-Memory** | `Mythosia.VectorDb.InMemory` | Разработка, тесты, демо |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | Продакшен, нативный гибридный поиск |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | Серверлесс-управляемый сервис |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | Существующие развёртывания Postgres, ACID |

Все бэкенды реализуют единый интерфейс `IVectorStore`. Подробнее о настройке каждого — в разделе [Настройка бэкендов](vectordb-backends.md).

## Внедрение зависимостей

Регистрация любого бэкенда как `IVectorStore`:

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

## Выполнение фильтров по бэкендам

Условия `VectorFilter` при возможности делегируются на сторону бэкенда:

| Оператор | InMemory | Qdrant | Pinecone | Postgres |
|----------|----------|--------|----------|----------|
| Eq / Ne | Клиент | **Сервер** | **Сервер** | **SQL** |
| In / NotIn | Клиент | **Сервер** | **Сервер** | **SQL** |
| Gt / Gte / Lt / Lte | Клиент | Клиент | Клиент | **SQL** |
| Like | Клиент | Клиент | Клиент | **SQL** |
| Exists / NotExists | Клиент | Клиент | Клиент | **SQL** |

Postgres обеспечивает полное SQL-делегирование для всех операторов. Qdrant и Pinecone нативно поддерживают равенство и проверку принадлежности множеству.
