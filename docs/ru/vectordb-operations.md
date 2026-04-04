# Операции с векторным хранилищем

## Вставка/обновление (Upsert)

Вставка или обновление одной записи. Если запись с таким `Id` уже существует, она заменяется.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("Refunds are accepted within 30 days."),
    Content = "Refunds are accepted within 30 days.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "en",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## Пакетная вставка

Вставка нескольких записей за один вызов. Эффективнее, чем вызов `UpsertAsync` в цикле — бэкенды используют пакетные API там, где это возможно.

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## Поиск

Возвращает top-K наиболее похожих записей по вектору запроса. Опционально можно фильтровать по метаданным перед оценкой.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("What is the refund policy?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Source: {r.Record.Metadata["source"]}");
}
```

### Поиск с фильтрацией

Сочетание векторного сходства с фильтрацией по метаданным:

```csharp
var filter = new VectorFilter()
    .Where("language", "en")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

Подробнее об API фильтрации — в разделе [VectorFilter](vector-filter.md).

## Гибридный поиск

Объединяет плотное векторное сходство с ключевым (BM25) поиском. Лучше полнота для запросов с конкретными терминами, именами или кодами.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("order #12345 status");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "order #12345 status",   // Необработанный текст для BM25
    topK: 5
);
```

Реализация гибридного поиска по бэкендам:

| Бэкенд | Механизм |
|--------|----------|
| **InMemory** | RRF объединяет косинусное сходство + BM25 Lucene |
| **Qdrant** | Серверная сторона: плотные + разреженные векторы, слияние через RRF или DBSF |
| **Pinecone** | Разреженные + плотные векторы сливаются на сервере |
| **Postgres** | Векторное сходство + `tsvector`/`trigram`, объединение в SQL |

## Получение по ID

Получение конкретной записи по идентификатору:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("Not found");
```

Добавьте фильтр для ограничения области поиска (например, при мультитенантных пространствах имён):

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## Пакетное получение

Получение нескольких записей по ID за один вызов:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## Удаление по ID

Удаление одной записи:

```csharp
await store.DeleteAsync("record-id-123");
```

## Удаление по фильтру

Удаление всех записей, соответствующих фильтру. Используйте с осторожностью — это массовое удаление.

```csharp
// Удалить все записи из конкретного документа
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## Замена по фильтру

Атомарное удаление всех записей по фильтру и вставка нового набора. Удобно для переиндексации документа без оставления устаревших чанков.

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> В Postgres это выполняется внутри транзакции, обеспечивая полную атомарность.

## Подсчёт записей

Подсчёт хранимых записей с опциональной фильтрацией:

```csharp
long total   = await store.CountAsync();
long english = await store.CountAsync(new VectorFilter().Where("language", "en"));

Console.WriteLine($"Total: {total}, English: {english}");
```

## Проверка подключения

Проверка доступности бэкенда. Полезно для health-чеков или валидации при запуске:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("Vector store connection OK");
}
catch (Exception ex)
{
    Console.WriteLine($"Connection failed: {ex.Message}");
}
```

## Использование с RAG

Передайте `IVectorStore` в `RagBuilder` для использования любого бэкенда в качестве хранилища извлечения RAG:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "knowledge-base",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey, http)
        .AddDirectory("docs/", ".txt", ".md")
    );

var answer = await ragService.GetCompletionAsync("Какая политика возврата?");
```

Или создайте `RagStore` отдельно и используйте его с несколькими AI-сервисами:

```csharp
RagStore ragStore = await RagBuilder.Create()
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey, http)
    .AddDocument("knowledge-base.pdf")
    .BuildAsync();

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
