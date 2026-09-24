# Настройка пайплайна RAG

<a id="indexing-validation"></a>

## Защита существующих документов при сбое индексации

Ошибочный пользовательский разделитель или ответ сервиса эмбеддингов не должен незаметно заменять доступный для поиска документ неполными или неверно сопоставленными данными. Конвейер проверяет каждый документ до начала сохранения, в том числе при использовании `onDocumentEmbedded`.

До вызова эмбеддингов, хранилища или обработчика сохранения значение `RagDocument.Id`, равное null, пустой строке или только пробельным символам, вызывает `ArgumentException`. Некорректный результат разделителя вызывает `InvalidOperationException`: null вместо списка или фрагмента, null в `Content` или `Metadata`, пустой или пробельный ID фрагмента либо повторяющиеся ID внутри документа. Повторы проверяются через `StringComparer.Ordinal` с учётом регистра. Значения фрагментов и метаданные копируются до первого вызова эмбеддингов.

Корректные пользовательские ID сохраняются без изменений. Они не генерируются, не обрезаются и не исправляются автоматически; коллизии пользовательских ID фрагментов разных документов не проверяются глобально. Используйте ID, уникальные в целевой коллекции, как в [примере разделителя](text-splitters.md). Зарезервированный ключ `document_id` нормализуется только в копии для хранения; исходные метаданные не меняются.

Некорректные ID, ошибки разделения и неверные пакеты эмбеддингов сохраняют прежние записи этого документа и не вызывают обработчик сохранения. Все пакеты документа должны пройти [проверку эмбеддингов](rag-embedding.md#embedding-validation) до начала записи. Это не отменяет уже завершённую обработку предыдущих документов в той же операции; откат после начала записи зависит от хранилища или обработчика.

Эти проверки и исправления порядка ответов не восстанавливают автоматически уже перезаписанный текст или сохранённые векторы, ошибочно сопоставленные с фрагментами; повторно проиндексируйте затронутые документы из исходных источников.

<a id="custom-persistence"></a>

## Заменять весь документ в callback сохранения

Если документ стал короче, upsert только новых фрагментов оставляет старый конец доступным для поиска. `onDocumentEmbedded` полностью заменяет стандартное сохранение: используйте нормализованный `document_id` из записей для замены всего документа. Callback получает по одному проверенному непустому документу:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

Успешное разбиение на ноль фрагментов не вызывает callback и не обращается к стандартному хранилищу. Явно удалите известный ID из собственного хранилища; `DeleteDocumentAsync` используйте только для хранилища конвейера. Атомарность и откат зависят от хранилища или callback.

<a id="url-documents"></a>

## Безопасное чтение документов по URL

Сервер может сжимать текстовый документ для передачи. `AddUrl` распаковывает `gzip`, `deflate` и Brotli (`br`) перед чтением текста и проверяет полноту сжатого потока. Успешной передачи HTTP недостаточно: обрезанные сжатые данные, ошибки распаковки или несовпадение предусмотренной форматом контрольной суммы прерывают загрузку до создания эмбеддингов или сохранения, оставляя прежние записи документа нетронутыми. Неподдерживаемые или многослойные значения `Content-Encoding` также отклоняются до создания эмбеддингов или сохранения.

Чтобы прекратить ожидание медленного URL-документа, передайте `cancellationToken` в `RagStore.BuildAsync`. Токен доходит до HTTP-запроса, чтения тела ответа и распаковки. Отмена кооперативная и не откатывает уже сохранённые документы.

<a id="custom-retriever"></a>

## Подключение поиска без обязательных эмбеддингов

Коды товаров удобно искать по словам, а вопросы с другой формулировкой — по смыслу. Выбранный компонент готовит только нужное представление: лексический поиск больше не требует предварительного эмбеддинга запроса.

- Раньше: каждая стратегия получала эмбеддинг запроса.
- Теперь: компонент готовит только нужное представление.

Реализуйте `IRagRetriever` для внешнего индекса или иного представления запроса. `RagRetrievalRequest` передаёт `Query` (полный семантический запрос), nullable `TextQuery` (лексическое переопределение), `TopK`, `Filter` и `ProgressAsync`. Встроенные компоненты используют `Query`, если `TextQuery` равен null; пустая строка пропускает текстовую ветвь. Собственный компонент отвечает за подготовку, фильтр, лимит результатов и отмену.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

Регистрируйте через `UseRetriever(...)` или `RagPipeline.SetRetriever(...)`. `IRetrievalStrategy` и `SetRetrievalStrategy(...)` сохраняются через адаптер, создающий эмбеддинг запроса. Результаты должны содержать текст и метаданные для переранжирования и контекста.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` пропускает эмбеддинг запроса. При загрузке документы по-прежнему разбиваются и векторизуются для существующего хранилища; это не чисто текстовая индексация. При отложенной инициализации первая операция может вызвать эмбеддинги документов.

Этап запроса `Embedding` зависит от компонента; лексический поиск его не сообщает. Собственный компонент может сообщать этапы через `request.ProgressAsync`. Эмбеддинги документов не меняются.

Диагностика пайплайна показывает, как извлекается контекст, а [Run](execution-api-transition.md) управляет следующей за поиском работой модели: выводом, отменой и поддерживаемыми дополнительными указаниями.

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
        // Этапы: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
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
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

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
   Переписывание запроса → Фильтрация → Эмбеддинг (при необходимости) → Извлечение → Сборка контекста
    ↓
② TemplateContextBuilder подставляет {context} и {question}
   → "Answer using the following info.\n[1] Returns within 30 days...\nQuestion: What is the return policy?"
    ↓
③ RagEnabledService создаёт AIRequestContext
   RequestMessageOverride = собранный промпт
    ↓
④ _innerService.GetCompletionAsync(исходное сообщение, context: context) вызывается
   → AIService сохраняет контекст в AsyncLocal
   → Исходный вопрос добавляется в историю диалога
    ↓
⑤ AIService.GetLatestMessages() заменяет исходный ввод текущего запроса
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
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
var original = new Message(ActorRole.User, query);
return await _innerService.GetCompletionAsync(
    original,
    context: BuildRequestContext(processed, original),
    cancellationToken: cancellationToken);

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
{
    var requestMessage = original.Clone();
    requestMessage.Content = processed.RequestMessageContent;
    if (original.HasMultimodalContent)
    {
        requestMessage.Contents = new List<MessageContent>
        {
            new TextContent(processed.RequestMessageContent)
        };
        requestMessage.Contents.AddRange(
            original.Contents.Where(content => !(content is TextContent)));
    }
    return new AIRequestContext { RequestMessageOverride = requestMessage };
}
```

`AIService` хранит контекст в `AsyncLocal`. `GetLatestMessages()` применяет `RequestMessageOverride` к исходному вводу текущего логического запроса, сохраняя последующие вызовы инструментов ассистентом и их результаты. Поэтому найденные документы и результаты инструментов передаются вместе в следующих запросах к модели. После завершения восстанавливается предыдущий контекст.
