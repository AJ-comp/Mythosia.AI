# RAG (генерація з витягуванням)

## Що таке RAG

RAG (Retrieval-Augmented Generation) — технологія, при якій AI-модель спочатку **знаходить релевантну інформацію у ваших документах**, а потім формує відповідь на її основі.

Уявіть, що ви пишете реферат у бібліотеці. Замість того щоб покладатися лише на пам'ять, ви спочатку знаходите потрібні книги і використовуєте їх як опору. Саме так працює RAG.

## Навіщо потрібен RAG

LLM відповідає на основі навчальних даних, а отже:

- **Не знає свіжу інформацію** — дані після навчання йому недоступні
- **Не знає внутрішніх документів** — корпоративні політики й мануали йому не видні
- **Галюцинації** — може впевнено вигадувати факти

RAG вирішує ці проблеми: при надходженні запитання спочатку виконується пошук по ваших документах, а результати додаються до промпту, щоб модель генерувала **обґрунтовану відповідь**.

## Потік роботи RAG

### 1. Підготовка документів (виконується один раз)

```
Файли → Розбиття на чанки → Ембеддинг (векторизація) → Збереження у векторне сховище
```

### 2. Відповідь на запитання (при кожному запиті)

```
Запитання → Ембеддинг запитання → Пошук схожих чанків → Додавання до промпту → Генерація відповіді
```

## Встановлення

```bash
dotnet add package Mythosia.AI.Rag
```

## Швидкий старт

Використовуйте `.WithRag()` на будь-якому `IAIService` для увімкнення RAG з fluent API:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

Документи автоматично розбиваються, ембеддяться та зберігаються. При запиті найбільш релевантні чанки витягуються й додаються до промпту.

## Додавання документів

Підтримується кілька типів джерел:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // локальний файл
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("Inline content can go here too.")   // текст напряму
)
```

## Власний провайдер ембеддингів

За замовчуванням RAG використовує вбудований локальний провайдер ембеддингів. Щоб використати окрему модель ембеддингів:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## Власне векторне сховище

За замовчуванням використовується сховище в пам'яті. Для продакшену підключіть персистентне векторне сховище:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## Параметри запиту

Тонке налаштування поведінки витягування для кожного запиту:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,          // кількість чанків для витягування
        MinScore = 0.7     // мінімальна оцінка схожості
    }
};

var response = await service.GetCompletionAsync("Your question", options: options);
```

## Наступні кроки

- [Гібридний пошук](rag-hybrid-search.md) — семантика + ключові слова
- [Переписування запитів](rag-query-rewriting.md) — оптимізація з урахуванням контексту діалогу
- [Переранжування](rag-reranking.md) — підвищення точності результатів
- [Налаштування пайплайну](rag-pipeline.md) — тонке керування процесом RAG
- [Агентний RAG](rag-agentic.md) — AI сам вирішує, коли і що шукати
- [Векторні сховища](vectordb-overview.md) — налаштування персистентного сховища
- [Розділювачі тексту](text-splitters.md) — налаштування розбиття документів
