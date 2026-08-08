# Ембеддинг

> 📍 **Пайплайн запитання-відповіді:** [Переписування запитів](rag-query-rewriting.md) → **`Ембеддинг`** → [Фільтрація](rag-filtering.md) → [Пошук](rag-hybrid-search.md) → [Переранжування](rag-reranking.md) → [Побудова контексту](rag-context-build.md)

## Що таке ембеддинг?

Ембеддинг — це процес перетворення тексту в **числові вектори** (масиви чисел), що відображають зміст. У цьому векторному просторі **тексти зі схожим значенням опиняються поруч**.

Уявіть міста на карті: географічно близькі міста розташовані поруч. Так само фрази «Як скасувати підписку?» та «Хочу припинити членство» породжують близькі вектори — попри різні слова.

У RAG-пайплайні ембеддинг використовується двічі:

1. **Індексація документів** — кожен чанк векторизується та зберігається
2. **На етапі запиту** — питання користувача векторизується для пошуку за схожістю

Ця сторінка присвячена ембеддингу запиту (крок 2).

## Вбудовані провайдери

### OpenAI

```csharp
var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Скорочений запис через Builder:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

### Ollama (локально)

Запуск ембеддингів локально через [Ollama](https://ollama.com/):

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

### vLLM (власний хостинг)

Для команд із власним сервером [vLLM](https://docs.vllm.ai/):

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (без API)

Легкий провайдер на основі хешування ознак, без ключа API та зовнішніх сервісів. Проте якість ембедингів значно поступається нейромережевим моделям, тому **не рекомендується для робочого використання**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **Порада:** Використовуйте натомість `OpenAIEmbeddingProvider` з моделлю `text-embedding-3-small`. Вартість майже нульова, а результати значно кращі.

## Пакетна обробка

При індексації чанки обробляються пакетами:

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // за замовчуванням: 100 чанків за виклик
pipeline.Options = options;
```

## Розмірність

| Провайдер | Модель | Розмірність за замовчуванням |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 (32–2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (хешування) | 1024 |

## Власний провайдер

Реалізуйте `IEmbeddingProvider`:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Виклик вашого API
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Пакетний виклик
    }
}
```

## Внутрішній механізм

```
Питання користувача (string) → EmbeddingProvider.GetEmbeddingAsync() → Вектор запиту (float[])
```

Цей вектор передається на наступний етап ([Фільтрація](rag-filtering.md)), а потім до [Пошуку](rag-hybrid-search.md).

## Наступні кроки

- [Фільтрація](rag-filtering.md) — обмежити область пошуку
- [Гібридний пошук](rag-hybrid-search.md) — поєднати векторний і ключовий пошук
- [Налаштування пайплайну](rag-pipeline.md) — спільне використання провайдерів
