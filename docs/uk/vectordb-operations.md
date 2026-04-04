# Операції з векторним сховищем

## Вставка/оновлення (Upsert)

Вставка або оновлення одного запису. Якщо запис із таким `Id` вже існує, він замінюється.

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

## Пакетна вставка

Вставка кількох записів за один виклик. Ефективніше, ніж виклик `UpsertAsync` в циклі — бекенди використовують пакетні API там, де це можливо.

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

## Пошук

Повертає top-K найбільш схожих записів за вектором запиту. Опціонально можна фільтрувати за метаданими перед оцінкою.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("What is the refund policy?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  Source: {r.Record.Metadata["source"]}");
}
```

### Пошук з фільтрацією

Поєднання векторної схожості з фільтрацією за метаданими:

```csharp
var filter = new VectorFilter()
    .Where("language", "en")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

Детальніше про API фільтрації — у розділі [VectorFilter](vector-filter.md).

## Гібридний пошук

Поєднує щільну векторну схожість із ключовим (BM25) пошуком. Краща повнота для запитів з конкретними термінами, іменами або кодами.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("order #12345 status");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "order #12345 status",   // Необроблений текст для BM25
    topK: 5
);
```

Реалізація гібридного пошуку за бекендами:

| Бекенд | Механізм |
|--------|----------|
| **InMemory** | RRF поєднує косинусну схожість + BM25 Lucene |
| **Qdrant** | Серверна сторона: щільні + розріджені вектори, злиття через RRF або DBSF |
| **Pinecone** | Розріджені + щільні вектори зливаються на сервері |
| **Postgres** | Векторна схожість + `tsvector`/`trigram`, об'єднання в SQL |

## Отримання за ID

Отримання конкретного запису за ідентифікатором:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("Not found");
```

Додайте фільтр для обмеження області пошуку (наприклад, при мультитенантних просторах імен):

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## Пакетне отримання

Отримання кількох записів за ID за один виклик:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## Видалення за ID

Видалення одного запису:

```csharp
await store.DeleteAsync("record-id-123");
```

## Видалення за фільтром

Видалення всіх записів, що відповідають фільтру. Використовуйте обережно — це масове видалення.

```csharp
// Видалити всі записи з конкретного документа
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## Заміна за фільтром

Атомарне видалення всіх записів за фільтром та вставка нового набору. Зручно для переіндексації документа без залишення застарілих чанків.

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

> У Postgres це виконується всередині транзакції, забезпечуючи повну атомарність.

## Підрахунок записів

Підрахунок збережених записів з опціональною фільтрацією:

```csharp
long total   = await store.CountAsync();
long english = await store.CountAsync(new VectorFilter().Where("language", "en"));

Console.WriteLine($"Total: {total}, English: {english}");
```

## Перевірка підключення

Перевірка доступності бекенда. Корисно для health-перевірок або валідації при запуску:

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

## Використання з RAG

Передайте `IVectorStore` до `RagBuilder` для використання будь-якого бекенда як сховища витягування RAG:

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

var answer = await ragService.GetCompletionAsync("Яка політика повернення?");
```

Або створіть `RagStore` окремо та використовуйте його з кількома AI-сервісами:

```csharp
RagStore ragStore = await RagBuilder.Create()
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey, http)
    .AddDocument("knowledge-base.pdf")
    .BuildAsync();

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
