# Эмбеддинг

> 📍 **Пайплайн вопрос-ответ:** [Переписывание запросов](rag-query-rewriting.md) → **`Эмбеддинг`** → [Фильтрация](rag-filtering.md) → [Поиск](rag-hybrid-search.md) → [Переранжирование](rag-reranking.md) → [Построение контекста](rag-context-build.md)

## Что такое эмбеддинг?

Эмбеддинг — это процесс преобразования текста в **числовые векторы** (массивы чисел), которые отражают смысл. В этом векторном пространстве **тексты с похожим значением оказываются рядом друг с другом**.

Представьте города на карте: географически близкие города расположены рядом. Точно так же фразы «Как отменить подписку?» и «Хочу прекратить членство» порождают близкие векторы — несмотря на разные слова.

В RAG-пайплайне эмбеддинг используется дважды:

1. **Индексация документов** — каждый чанк векторизуется и сохраняется в хранилище
2. **На этапе запроса** — вопрос пользователя векторизуется для поиска по сходству

Эта страница посвящена эмбеддингу запроса (шаг 2).

## Встроенные провайдеры

### OpenAI

```csharp
var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Краткая форма через билдер:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (локально)

Запуск эмбеддингов локально через [Ollama](https://ollama.com/):

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

### vLLM (самостоятельный хостинг)

Для команд с собственным сервером [vLLM](https://docs.vllm.ai/):

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (без API)

Легковесный провайдер на основе хеширования признаков. Не требует ключа API, подходит для **прототипирования**:

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

## Пакетная обработка

При индексации чанки обрабатываются пакетами:

```csharp
var options = new RagPipelineOptions
{
    EmbeddingBatchSize = 100   // по умолчанию: 100 чанков за вызов
};
```

## Размерность

| Провайдер | Модель | Размерность по умолчанию |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 |
| Local | (хеширование) | 1024 |

## Собственный провайдер

Реализуйте `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Вызов вашего API
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Пакетный вызов
    }
}
```

## Внутренний механизм

```
Вопрос пользователя (string) → EmbeddingProvider.GetEmbeddingAsync() → Вектор запроса (float[])
```

Этот вектор передаётся на следующий этап ([Фильтрация](rag-filtering.md)), а затем в [Поиск](rag-hybrid-search.md).

## Следующие шаги

- [Фильтрация](rag-filtering.md) — ограничить область поиска
- [Гибридный поиск](rag-hybrid-search.md) — совместить векторный и ключевой поиск
- [Настройка пайплайна](rag-pipeline.md) — совместное использование провайдеров
