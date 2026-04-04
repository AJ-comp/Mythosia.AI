# VectorFilter

`VectorFilter` — fluent-API для фильтрации запросов к векторному хранилищу по метаданным. Применяется к `IVectorStore.SearchAsync`, `HybridSearchAsync` и RAG-запросам.

## Простое равенство

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "en");
```

## Операторы сравнения

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| Метод | SQL-эквивалент |
|-------|---------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## Принадлежность множеству

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## Проверка существования ключа

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // Ключ должен присутствовать
    .WhereNotExists("deprecated");   // Ключ должен отсутствовать
```

## Логическая группировка (AND / OR)

Условия на одном уровне объединяются через AND по умолчанию. Используйте `.Or()` для создания OR-групп:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

Вложенный AND:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "en").Where("region", "us"))
        .And(a => a.Where("lang", "ko").Where("region", "kr"))
    );
// (lang = "en" AND region = "us") OR (lang = "ko" AND region = "kr")
```

## Порог оценки

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Использование с векторным хранилищем

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contract")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## Использование с RAG

Передайте как `StoreFilter` в `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
```
