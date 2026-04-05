# Фільтрація

> 📍 **Пайплайн запитання-відповіді:** [Переписування запитів](rag-query-rewriting.md) → [Ембеддинг](rag-embedding.md) → **`Фільтрація`** → [Пошук](rag-hybrid-search.md) → [Переранжування](rag-reranking.md) → [Побудова контексту](rag-context-build.md)

## Що таке фільтрація?

Фільтрація звужує **які чанки взагалі розглядаються** перед пошуком за схожістю. Замість сканування всього сховища пошук обмежується підмножинами на основі метаданих або порогів скору.

Застосовуються два види фільтрації:

1. **Фільтрація за метаданими** — включення або виключення чанків за їхніми метаданими (категорія, тенант, дата)
2. **Фільтрація за скором** — мінімальний поріг схожості

## Фільтрація за метаданими

### Фільтр на запит

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("Як отримати повернення?", filter: filter);
```

### Fluent API

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")
    .WhereNot("status", "archived")
    .WhereIn("region", "us-east", "eu-west")
    .WhereGreaterThan("year", "2023")
    .WhereLike("title", "%kubernetes%");
```

| Метод | SQL-аналог | Опис |
| --- | --- | --- |
| `Where` | `=` | Точний збіг |
| `WhereNot` | `!=` | Не дорівнює |
| `WhereIn` | `IN (...)` | Значення в множині |
| `WhereNotIn` | `NOT IN (...)` | Значення не в множині |
| `WhereGreaterThan` | `>` | Більше |
| `WhereGreaterThanOrEqual` | `>=` | Більше або дорівнює |
| `WhereLessThan` | `<` | Менше |
| `WhereLessThanOrEqual` | `<=` | Менше або дорівнює |
| `WhereLike` | `LIKE` | Пошук за шаблоном |
| `WhereExists` | `IS NOT NULL` | Ключ існує |
| `WhereNotExists` | `IS NULL` | Ключ не існує |

### Логічні групи

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
```

## StoreFilter на рівні пайплайну

Для умов, що **завжди мають діяти** (наприклад, ізоляція тенантів):

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};
```

`StoreFilter` і фільтр запиту об'єднуються через AND — жоден не ігнорується.

## Фільтрація за скором

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};
```

При наявності [переранжування](rag-reranking.md) поріг на етапі витягування автоматично послаблюється, а суворий `MinScore` застосовується після переранжування.

## Типові сценарії

### Мультитенантна ізоляція

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### Пошук за категорією

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("помилка 404", filter: filter);
```

### Фільтрація за часом

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## Наступні кроки

- [Гібридний пошук](rag-hybrid-search.md) — поєднати векторний і ключовий пошук
- [Довідник VectorFilter](vector-filter.md) — повна документація API фільтрів
- [Переранжування](rag-reranking.md) — уточнити результати після витягування
