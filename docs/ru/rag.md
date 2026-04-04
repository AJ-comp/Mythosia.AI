# RAG (генерация с извлечением)

## Что такое RAG

RAG (Retrieval-Augmented Generation) — технология, при которой AI-модель сначала **находит релевантную информацию в ваших документах**, а затем формирует ответ на её основе.

Представьте, что вы пишете реферат в библиотеке. Вместо того чтобы полагаться только на память, вы сначала находите нужные книги и используете их как опору. Именно так работает RAG.

## Зачем нужен RAG

LLM отвечает на основе обучающих данных, а значит:

- **Не знает свежую информацию** — данные после обучения ему недоступны
- **Не знает внутренних документов** — корпоративные политики и мануалы ему не видны
- **Галлюцинации** — может уверенно выдумывать факты

RAG решает эти проблемы: при поступлении вопроса сначала выполняется поиск по вашим документам, а результаты добавляются в промпт, чтобы модель генерировала **обоснованный ответ**.

## Поток работы RAG

### 1. Подготовка документов (выполняется один раз)

```
Файлы → Разбиение на чанки → Эмбеддинг (векторизация) → Сохранение в векторное хранилище
```

### 2. Ответ на вопрос (при каждом запросе)

```
Вопрос → Эмбеддинг вопроса → Поиск похожих чанков → Добавление в промпт → Генерация ответа
```

## Установка

```bash
dotnet add package Mythosia.AI.Rag
```

## Быстрый старт

В Mythosia.AI весь процесс настраивается одной строкой `.WithRag()`:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("Какова политика возврата?");
```

## Добавление документов

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // Локальный файл
    .AddDocument("https://example.com/doc.txt")   // URL
    .AddText("Инлайн-контент.")                   // Строка напрямую
)
```

## Свой провайдер эмбеддингов

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbeddingProvider(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## Свое векторное хранилище

По умолчанию используется in-memory хранилище. Для продакшена подключите постоянное:

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(connectionString, embedDimension: 1536);

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseVectorStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## Параметры запроса

```csharp
var options = new RagQueryOptions
{
    TopK = 5,
    ScoreThreshold = 0.7f
};

var response = await service.GetCompletionAsync("Вопрос", ragOptions: options);
```

## Дальнейшие шаги

- [Гибридный поиск](rag-hybrid-search.md) — семантика + ключевые слова
- [Переписывание запросов](rag-query-rewriting.md) — оптимизация с учётом контекста диалога
- [Переранжирование](rag-reranking.md) — повышение точности результатов
- [Настройка пайплайна](rag-pipeline.md) — тонкое управление процессом RAG
- [Агентный RAG](rag-agentic.md) — AI сам решает, когда и что искать
- [Векторные хранилища](../vectordb-overview.md) — постоянные хранилища
- [Разделители текста](text-splitters.md) — способы разбиения документов
