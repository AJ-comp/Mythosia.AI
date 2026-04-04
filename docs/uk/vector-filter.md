# VectorFilter

`VectorFilter` — fluent-API для фільтрації запитів до векторного сховища за метаданими. Застосовується до `IVectorStore.SearchAsync`, `HybridSearchAsync` та RAG-запитів.

## Проста рівність

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "en");
```

## Оператори порівняння

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| Метод | SQL-еквівалент |
|-------|---------------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## Належність множині

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## Перевірка існування ключа

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // Ключ має бути присутнім
    .WhereNotExists("deprecated");   // Ключ має бути відсутнім
```

## Логічне групування (AND / OR)

Умови на одному рівні об'єднуються через AND за замовчуванням. Використовуйте `.Or()` для створення OR-груп:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

Вкладений AND:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "en").Where("region", "us"))
        .And(a => a.Where("lang", "ko").Where("region", "kr"))
    );
// (lang = "en" AND region = "us") OR (lang = "ko" AND region = "kr")
```

## Поріг оцінки

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## Використання з векторним сховищем

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

## Використання з RAG

Передайте як `StoreFilter` у `RagQueryOptions`:

```csharp
var options = new RagQueryOptions
```
