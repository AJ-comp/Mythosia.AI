# Настройка пайплайна RAG

## Зачем настраивать пайплайн

Стандартный RAG-пайплайн хорошо работает из коробки, но реальные проекты часто требуют большего контроля:

- **Отладка** — какой этап тормозит? Не искажает ли переписывающий модуль запрос?
- **Инженерия промптов** — шаблон по умолчанию может не подходить по стилю или ограничениям вашей предметной области
- **Архитектура** — несколько сервисов с общим индексом экономят память и обеспечивают согласованность эмбеддингов
- **Инспекция** — иногда нужно увидеть, что возвращает поиск, *до* отправки в LLM

В этой главе рассматриваются инструменты, дающие вам такой контроль.

## Отслеживание прогресса

Отслеживайте текущий этап RAG через асинхронный колбэк для каждого запроса:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Этапы: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Your question", options);
```

Незаменимо для профилирования задержек — можно замерить время между этапами и найти узкие места.

## Пользовательский шаблон промпта

Управляйте тем, как извлечённый контекст вставляется в промпт, используя заполнители `{context}` и `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Use only the following information to answer the question.
        If the answer is not in the context, say "I don't know."

        Context:
        {context}

        Question: {question}
        """)
    .AddDocument("faq.txt")
)
```

Грамотно составленный шаблон значительно снижает галлюцинации, инструктируя модель оставаться в рамках предоставленного контекста.

## Общий RagStore

Постройте индекс один раз и переиспользуйте его в нескольких экземплярах сервисов — полезно для сравнения провайдеров или A/B-тестирования:

```csharp
// Строим один раз
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDocuments("docs/")
    .BuildAsync();

// Переиспользуем
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

Оба сервиса разделяют одни и те же эмбеддинги и векторный индекс — без дублирования хранилища и вычислений.

## Прямой запрос к RagStore

Запросите хранилище напрямую, без участия AI-сервиса, чтобы проверить качество извлечения:

```csharp
RagProcessedQuery result = await store.QueryAsync("What is the return policy?");

Console.WriteLine($"Rewritten query: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` содержит полностью собранный промпт, который был бы отправлен в LLM. Крайне полезно для отладки качества извлечения без траты токенов LLM.

## Как это работает изнутри

При вызове `.WithRag()` создаётся обёртка `RagEnabledService` вокруг вашего AIService. Она автоматически подключает RAG-пайплайн к вызову LLM. Ключевой механизм — [AIRequestContext](request-contexts.md).

### Полный поток

```
ragService.GetCompletionAsync("What is the return policy?")
    ↓
① RagEnabledService запускает RAG-пайплайн
   Переписывание запроса → Эмбеддинг → Извлечение → Сборка контекста
    ↓
② TemplateContextBuilder подставляет {context} и {question}
   → "Answer using the following info.\n[1] Returns within 30 days...\nQuestion: What is the return policy?"
    ↓
③ RagEnabledService создаёт AIRequestContext
   RequestMessageOverride = собранный промпт
    ↓
④ _innerService.GetCompletionAsync(исходное сообщение, context) вызывается
   → AIService сохраняет контекст в AsyncLocal
   → Исходный вопрос добавляется в историю диалога
    ↓
⑤ AIService.GetLatestMessages() заменяет последнее сообщение
   История диалога: "What is the return policy?" (оригинал сохранён)
   Что видит модель: собранный промпт (RequestMessageOverride)
```

### Почему именно такой дизайн

Ключевая идея — **разделение истории диалога и входных данных модели**:

- **В истории диалога хранится оригинальный вопрос** — чтобы уточняющие вопросы типа «а что насчёт того?» имели корректный контекст
- **Модель получает собранный промпт** — полный промпт с извлечёнными документами и вопросом
- **Состояние AIService не мутируется** — `AsyncLocal<T>` обеспечивает изоляцию для каждого запроса

Это практическое применение `RequestMessageOverride`, описанного в документации [AIRequestContext](request-contexts.md). RAG-пайплайн использует этот механизм автоматически — вам достаточно вызвать `.WithRag()`.

### В коде

Вот ключевой код внутри `RagEnabledService`, реализующий эту связку:

```csharp
// Внутри RagEnabledService.GetCompletionAsync
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← оригинальный вопрос (сохраняется в истории)
    context: BuildRequestContext(processed));    // ← собранный промпт (видит только модель)

// BuildRequestContext — создаёт AIRequestContext
private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)  // ← результат TemplateContextBuilder
    };
}
```

`AIService` сохраняет этот контекст в `AsyncLocal`, а `GetLatestMessages()` заменяет последнее сообщение на `RequestMessageOverride`. После завершения запроса контекст автоматически восстанавливается, гарантируя отсутствие влияния на последующие запросы.
