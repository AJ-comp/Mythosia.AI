# Фильтрация

> 📍 **Пайплайн вопрос-ответ:** [Переписывание запросов](rag-query-rewriting.md) → [Эмбеддинг](rag-embedding.md) → **`Фильтрация`** → [Поиск](rag-hybrid-search.md) → [Переранжирование](rag-reranking.md) → [Построение контекста](rag-context-build.md)

## Что такое фильтрация?

Фильтрация сужает **какие чанки вообще рассматриваются** перед поиском по сходству. Вместо сканирования всего векторного хранилища поиск ограничивается подмножествами на основе метаданных или порогов по скору.

Применяются два вида фильтрации:

1. **Фильтрация по метаданным** — включение или исключение чанков по их метаданным (категория, тенант, дата)
2. **Фильтрация по скору** — минимальный порог сходства

## Фильтрация по метаданным

### Фильтр на запрос

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("Как получить возврат?", filter: filter);
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

| Метод | SQL-аналог | Описание |
| --- | --- | --- |
| `Where` | `=` | Точное совпадение |
| `WhereNot` | `!=` | Не равно |
| `WhereIn` | `IN (...)` | Значение в множестве |
| `WhereNotIn` | `NOT IN (...)` | Значение не в множестве |
| `WhereGreaterThan` | `>` | Больше |
| `WhereGreaterThanOrEqual` | `>=` | Больше или равно |
| `WhereLessThan` | `<` | Меньше |
| `WhereLessThanOrEqual` | `<=` | Меньше или равно |
| `WhereLike` | `LIKE` | Поиск по шаблону |
| `WhereExists` | `IS NOT NULL` | Ключ существует |
| `WhereNotExists` | `IS NULL` | Ключ не существует |

### Логические группы

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
```

## StoreFilter на уровне пайплайна

Для условий, которые **всегда должны действовать** (например, изоляция тенантов):

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};
```

`StoreFilter` и фильтр запроса объединяются через AND — ни один не игнорируется.

## Фильтрация по скору

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

При наличии [переранжирования](rag-reranking.md) порог на этапе извлечения автоматически смягчается, а строгий `MinScore` применяется после переранжирования.

## Типичные сценарии

### Мультитенантная изоляция

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### Поиск по категории

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("ошибка 404", filter: filter);
```

### Фильтрация по времени

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## Следующие шаги

- [Гибридный поиск](rag-hybrid-search.md) — совместить векторный и ключевой поиск
- [Справочник VectorFilter](vector-filter.md) — полная документация API фильтров
- [Переранжирование](rag-reranking.md) — уточнить результаты после извлечения
