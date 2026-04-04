# Настройка бэкендов

## In-Memory

Простейший бэкенд — без внешних зависимостей. Данные хранятся в оперативной памяти и теряются при завершении процесса. Подходит для разработки, тестов и демонстраций.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Встроенный гибридный поиск**: RRF (Reciprocal Rank Fusion) объединяет оценки косинусного сходства и BM25.

### Диагностика

```csharp
// Список всех записей
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Total: {store.GetTotalRecordCount()}");

// Просмотр необработанных оценок сходства
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Векторная база данных продакшен-класса с нативным гибридным поиском. Разворачивается как самостоятельный сервис через Docker или Qdrant Cloud.

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# Запуск Qdrant локально
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,           // gRPC-порт
    CollectionName   = "my-docs",
    Dimension        = 1536,           // Должен совпадать с вашей моделью эмбеддинга
    AutoCreateCollection = true        // Создание коллекции при первой вставке
});
```

### Все параметры

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Обязателен для Qdrant Cloud

    CollectionName         = "my-collection",  // Обязательный
    Dimension              = 1536,             // Обязательный

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Дополнительные индексы полезной нагрузки для ускорения серверной фильтрации
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Стратегии расстояния

| Значение | Описание |
|----------|----------|
| `Cosine` | Косинусное сходство — лучший выбор для нормализованных эмбеддингов (по умолчанию) |
| `Euclidean` | Расстояние L2 — меньшее расстояние = выше сходство |
| `DotProduct` | Скалярное произведение — используйте с нормализованными по единице векторами |

### Стратегии гибридного слияния

| Значение | Описание |
|----------|----------|
| `Rrf` | Reciprocal Rank Fusion — надёжное слияние на основе рангов (по умолчанию) |
| `Dbsf` | Distribution-Based Score Fusion — слияние на основе распределения оценок |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "your-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "your-qdrant-cloud-key",
    CollectionName = "production",
    Dimension      = 1536
}
```

### Использование внешнего QdrantClient

Если у вас уже есть настроенный `QdrantClient` (например, из DI-контейнера), передайте его напрямую:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

Стор **не** освободит внешне предоставленный клиент.

> Все векторные сторы реализуют `IDisposable`. При создании стора через стандартный конструктор вызывайте `Dispose()` (или используйте `using`) для освобождения внутренних ресурсов.

---

## Pinecone

Полностью управляемая серверлесс-векторная база данных. Никакой инфраструктуры для управления.

```bash
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "your-api-key"
});
```

### Автоматическое создание индекса

Если индекса ещё нет, SDK создаст его автоматически:

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp" или "azure"
    Region          = "us-east-1"
}
```

> При включённом `AutoCreateIndex` индекс создаётся с метрикой `dotproduct` — она обязательна для гибридного (sparse + dense) поиска.

### Все параметры

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // Обязательный (или AutoCreateIndex)
    ApiKey                 = "...",           // Обязательный
    Namespace              = "production",    // Необязательный: применяется ко всем операциям

    UpsertBatchSize        = 100,             // Записей на батч-запрос
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### Использование внешнего HttpClient

Если у вас уже есть настроенный `HttpClient` (например, из `IHttpClientFactory`):

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

Стор **не** освободит внешне предоставленный клиент.

---

## PostgreSQL (pgvector)

Использует расширение [`pgvector`](https://github.com/pgvector/pgvector) для добавления векторного поиска по сходству в стандартную базу данных PostgreSQL.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Предварительные требования

```sql
-- Выполните один раз на сервере PostgreSQL
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Только при использовании триграммного текстового поиска
```

Или доверьте SDK автоматическую настройку с `EnsureSchema = true`.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // Автоматическое создание расширения, таблицы и индексов
});
```

### Типы индексов

| Тип | Класс | Когда использовать |
|-----|-------|-------------------|
| HNSW | `HnswIndexOptions` | По умолчанию. Быстрый приближённый поиск. Подходит для большинства задач. |
| IVFFlat | `IvfFlatIndexOptions` | Меньше памяти. Хорош для больших статических датасетов. |
| None | `NoIndexOptions` | Последовательное сканирование. Только для очень маленьких датасетов. |

```csharp
// HNSW (по умолчанию)
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // Макс. соседних связей на узел
        EfConstruction = 64,   // Область поиска при построении индекса (выше = лучше качество)
        EfSearch       = 40    // Область поиска при запросе (выше = лучше полнота, медленнее)
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // Количество инвертированных списков
        Probes = 10    // Сколько списков проверять при запросе
    }
}

// Без индекса (последовательное сканирование)
new PostgresOptions { Index = new NoIndexOptions() }
```

### Режимы текстового поиска

Используются для ключевой составляющей гибридного поиска:

| Режим | Назначение |
|-------|------------|
| `TsVector` | Стандартный полнотекстовый поиск — английский, большинство западных языков |
| `Trigram` | CJK-языки (корейский, китайский, японский), нечёткий поиск |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // Конфигурация текстового поиска PostgreSQL
}
```

### Стратегии расстояния

| Значение | Оператор Postgres | Описание |
|----------|------------------|----------|
| `Cosine` | `<=>` | 1 − косинусное сходство (по умолчанию) |
| `Euclidean` | `<->` | L2-расстояние |
| `InnerProduct` | `<#>` | Отрицательное скалярное произведение — используйте с нормализованными векторами |

### Профиль поиска во время выполнения

Тонкая настройка баланса полноты и задержки при запросе:

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // Прямое переопределение HNSW ef_search
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### Все параметры

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
