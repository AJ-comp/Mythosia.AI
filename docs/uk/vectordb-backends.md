# Налаштування бекендів

## In-Memory

Найпростіший бекенд — без зовнішніх залежностей. Дані зберігаються в оперативній пам'яті та втрачаються при завершенні процесу. Підходить для розробки, тестів і демонстрацій.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**Вбудований гібридний пошук**: RRF (Reciprocal Rank Fusion) об'єднує оцінки косинусної схожості та BM25.

### Діагностика

```csharp
// Список усіх записів
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"Total: {store.GetTotalRecordCount()}");

// Перегляд необроблених оцінок схожості
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

Векторна база даних продакшен-класу з нативним гібридним пошуком. Розгортається як самостійний сервіс через Docker або Qdrant Cloud.

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
    Dimension        = 1536,           // Має збігатися з вашою моделлю ембеддингу
    AutoCreateCollection = true        // Створення колекції при першій вставці
});
```

### Усі параметри

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,             // Обов'язковий для Qdrant Cloud

    CollectionName         = "my-collection",  // Обов'язковий
    Dimension              = 1536,             // Обов'язковий

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // Додаткові індекси корисного навантаження для прискорення серверної фільтрації
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### Стратегії відстані

| Значення | Опис |
|----------|------|
| `Cosine` | Косинусна схожість — найкращий вибір для нормалізованих ембеддингів (за замовчуванням) |
| `Euclidean` | Відстань L2 — менша відстань = вища схожість |
| `DotProduct` | Скалярний добуток — використовуйте з нормалізованими за одиницю векторами |

### Стратегії гібридного злиття

| Значення | Опис |
|----------|------|
| `Rrf` | Reciprocal Rank Fusion — надійне злиття на основі рангів (за замовчуванням) |
| `Dbsf` | Distribution-Based Score Fusion — злиття на основі розподілу оцінок |

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

### Використання зовнішнього QdrantClient

Якщо у вас вже є налаштований `QdrantClient` (наприклад, з DI-контейнера), передайте його безпосередньо:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

Стор **не** звільнить зовнішньо наданого клієнта.

> Усі векторні стори реалізують `IDisposable`. При створенні стору через стандартний конструктор викликайте `Dispose()` (або `using`) для звільнення внутрішніх ресурсів.

---

## Pinecone

Повністю керована серверлес-векторна база даних. Жодної інфраструктури для управління.

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

### Автоматичне створення індексу

Якщо індексу ще немає, SDK створить його автоматично:

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp" або "azure"
    Region          = "us-east-1"
}
```

> При увімкненому `AutoCreateIndex` індекс створюється з метрикою `dotproduct` — вона обов'язкова для гібридного (sparse + dense) пошуку.

### Усі параметри

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",   // Обов'язковий (або AutoCreateIndex)
    ApiKey                 = "...",           // Обов'язковий
    Namespace              = "production",    // Необов'язковий: застосовується до всіх операцій

    UpsertBatchSize        = 100,             // Записів на батч-запит
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

### Використання зовнішнього HttpClient

Якщо у вас вже є налаштований `HttpClient` (наприклад, з `IHttpClientFactory`):

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

Стор **не** звільнить зовнішньо наданого клієнта.

---

## PostgreSQL (pgvector)

Використовує розширення [`pgvector`](https://github.com/pgvector/pgvector) для додавання векторного пошуку за схожістю до стандартної бази даних PostgreSQL.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### Попередні вимоги

```sql
-- Виконайте один раз на сервері PostgreSQL
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Лише при використанні триграмного текстового пошуку
```

Або довірте SDK автоматичне налаштування з `EnsureSchema = true`.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // Автоматичне створення розширення, таблиці та індексів
});
```

### Типи індексів

| Тип | Клас | Коли використовувати |
|-----|------|---------------------|
| HNSW | `HnswIndexOptions` | За замовчуванням. Швидкий наближений пошук. Підходить для більшості завдань. |
| IVFFlat | `IvfFlatIndexOptions` | Менше пам'яті. Добрий для великих статичних датасетів. |
| None | `NoIndexOptions` | Послідовне сканування. Лише для дуже маленьких датасетів. |

```csharp
// HNSW (за замовчуванням)
new PostgresOptions
{
    // ...
    Index = new HnswIndexOptions
    {
        M              = 16,   // Макс. сусідніх зв'язків на вузол
        EfConstruction = 64,   // Область пошуку при побудові індексу (вище = краща якість)
        EfSearch       = 40    // Область пошуку при запиті (вище = краща повнота, повільніше)
    }
}

// IVFFlat
new PostgresOptions
{
    // ...
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // Кількість інвертованих списків
        Probes = 10    // Скільки списків перевіряти при запиті
    }
}

// Без індексу (послідовне сканування)
new PostgresOptions { Index = new NoIndexOptions() }
```

### Режими текстового пошуку

Використовуються для ключової складової гібридного пошуку:

| Режим | Призначення |
|-------|-------------|
| `TsVector` | Стандартний повнотекстовий пошук — англійська, більшість західних мов |
| `Trigram` | CJK-мови (корейська, китайська, японська), нечіткий пошук |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"     // Конфігурація текстового пошуку PostgreSQL
}
```

### Стратегії відстані

| Значення | Оператор Postgres | Опис |
|----------|------------------|------|
| `Cosine` | `<=>` | 1 − косинусна подібність (за замовчуванням) |
| `Euclidean` | `<->` | L2-відстань |
| `InnerProduct` | `<#>` | Від'ємний скалярний добуток — використовуйте з нормалізованими векторами |

### Профіль пошуку під час виконання

Тонке налаштування балансу повноти та затримки при запиті:

```csharp
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // Пряме перевизначення HNSW ef_search
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

### Усі параметри

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
