# Налаштування пайплайну RAG

<a id="indexing-validation"></a>

## Захист наявних документів у разі збою індексації

Помилковий користувацький розділювач або відповідь сервісу ембеддингів не мають непомітно замінювати доступний для пошуку документ неповними чи неправильно зіставленими даними. Конвеєр перевіряє кожен документ до початку збереження, зокрема й за використання `onDocumentEmbedded`.

До виклику ембеддингів, сховища або обробника збереження значення `RagDocument.Id`, що є null, порожнім рядком або лише пробільними символами, спричиняє `ArgumentException`. Некоректний результат розділювача спричиняє `InvalidOperationException`: null замість списку або фрагмента, null у `Content` чи `Metadata`, порожній або пробільний ID фрагмента чи повторні ID в межах документа. Дублікати перевіряються через `StringComparer.Ordinal` з урахуванням регістру. Значення фрагментів і метадані копіюються до першого виклику ембеддингів.

Коректні користувацькі ID зберігаються без змін. Вони не генеруються, не обрізаються й не виправляються автоматично; колізії користувацьких ID фрагментів різних документів не виявляються глобально. Використовуйте ID, унікальні в цільовій колекції, як у [прикладі розділювача](text-splitters.md). Зарезервований ключ `document_id` нормалізується лише в копії для зберігання; початкові метадані не змінюються.

Некоректні ID, помилки розділення й недійсні пакети ембеддингів зберігають попередні записи цього документа та не викликають обробник збереження. Усі пакети документа мають пройти [перевірку ембеддингів](rag-embedding.md#embedding-validation) до початку запису. Це не скасовує вже завершене опрацювання попередніх документів у тій самій операції; відкат після початку запису залежить від сховища або обробника.

Ці перевірки та виправлення порядку відповідей не відновлюють автоматично вже перезаписаний текст або збережені вектори, помилково зіставлені з фрагментами; повторно проіндексуйте уражені документи з початкових джерел.

<a id="custom-persistence"></a>

## Замінювати весь документ у callback збереження

Якщо документ став коротшим, upsert лише нових фрагментів залишає старий кінець доступним для пошуку. `onDocumentEmbedded` повністю замінює стандартне збереження: використовуйте нормалізований `document_id` із записів для заміни цілого документа. Callback отримує по одному перевіреному непорожньому документу:

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

Успішне розбиття на нуль фрагментів не викликає callback і не звертається до стандартного сховища. Явно видаліть відомий ID із власного сховища; `DeleteDocumentAsync` використовуйте лише для сховища конвеєра. Атомарність та відкат залежать від сховища або callback.

<a id="url-documents"></a>

## Безпечне читання документів за URL

Сервер може стискати текстовий документ для передавання. `AddUrl` розпаковує `gzip`, `deflate` і Brotli (`br`) перед читанням тексту та перевіряє повноту стисненого потоку. Успішного передавання HTTP недостатньо: обрізані стиснені дані, помилки розпакування або невідповідність передбаченої форматом контрольної суми переривають завантаження до створення ембеддингів або збереження, залишаючи попередні записи документа недоторканими. Непідтримувані або багатошарові значення `Content-Encoding` також відхиляються до створення ембеддингів або збереження.

Щоб припинити очікування повільного URL-документа, передайте `cancellationToken` у `RagStore.BuildAsync`. Токен доходить до HTTP-запиту, читання тіла відповіді та розпакування. Скасування є кооперативним і не відкочує вже збережені документи.

<a id="custom-retriever"></a>

## Підключення пошуку без обов’язкових ембеддингів

Коди товарів зручно шукати за словами, а питання з іншим формулюванням — за змістом. Вибраний компонент готує лише потрібне представлення: лексичний пошук більше не вимагає попереднього ембеддингу запиту.

- Раніше: кожна стратегія отримувала ембеддинг запиту.
- Тепер: компонент готує лише потрібне представлення.

Реалізуйте `IRagRetriever` для зовнішнього індексу або іншого представлення. `RagRetrievalRequest` містить `Query` (повний семантичний запит), nullable `TextQuery` (лексичне перевизначення), `TopK`, `Filter` і `ProgressAsync`. Вбудовані компоненти використовують `Query`, якщо `TextQuery` є null; порожній рядок пропускає текстову гілку. Власний компонент відповідає за підготовку, фільтр, ліміт результатів і скасування.

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

Реєструйте через `UseRetriever(...)` або `RagPipeline.SetRetriever(...)`. `IRetrievalStrategy` та `SetRetrievalStrategy(...)` зберігаються через адаптер, що створює ембеддинг запиту. Результати повинні містити текст і метадані для переранжування та контексту.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` пропускає ембеддинг запиту. Документи досі розбиваються і векторизуються для наявного сховища; це не суто текстове індексування. Відкладена ініціалізація може викликати ембеддинги документів під час першого питання.

Етап запиту `Embedding` залежить від компонента; лексичний пошук його не повідомляє. Власний компонент може повідомляти етапи через `request.ProgressAsync`. Ембеддинги документів не змінюються.

Діагностика пайплайну показує, як отримується контекст, а [Run](execution-api-transition.md) керує наступною роботою моделі: виводом, скасуванням і підтримуваними додатковими вказівками.

## Навіщо налаштовувати пайплайн

Стандартний RAG-пайплайн добре працює з коробки, але реальні проєкти часто потребують більшого контролю:

- **Налагодження** — який етап гальмує? Чи не спотворює модуль переписування запит?
- **Інженерія промптів** — шаблон за замовчуванням може не підходити за стилем або обмеженнями вашої предметної області
- **Архітектура** — кілька сервісів із спільним індексом заощаджують пам'ять та забезпечують узгодженість ембеддингів
- **Інспекція** — іноді потрібно побачити, що повертає пошук, *до* відправки в LLM

У цьому розділі розглядаються інструменти, що дають вам такий контроль.

## Відстеження прогресу

Відстежуйте поточний етап RAG через асинхронний колбек для кожного запиту:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Етапи: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Your question", options);
```

Незамінно для профілювання затримок — можна виміряти час між етапами й знайти вузькі місця.

## Користувацький шаблон промпту

Керуйте тим, як витягнутий контекст вставляється в промпт, використовуючи заповнювачі `{context}` та `{question}`:

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

Грамотно складений шаблон значно зменшує галюцинації, інструктуючи модель залишатися в межах наданого контексту.

## Спільний RagStore

Побудуйте індекс один раз і перевикористовуйте його в кількох екземплярах сервісів — корисно для порівняння провайдерів або A/B-тестування:

```csharp
// Будуємо один раз
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// Перевикористовуємо
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

Обидва сервіси поділяють ті самі ембеддинги та векторний індекс — без дублювання сховища й обчислень.

## Прямий запит до RagStore

Запитайте сховище напряму, без участі AI-сервісу, щоб перевірити якість витягування:

```csharp
RagProcessedQuery result = await store.QueryAsync("What is the return policy?");

Console.WriteLine($"Rewritten query: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` містить повністю зібраний промпт, який був би надісланий до LLM. Вкрай корисно для налагодження якості витягування без витрат токенів LLM.

## Як це працює зсередини

При виклику `.WithRag()` створюється обгортка `RagEnabledService` навколо вашого AIService. Вона автоматично підключає RAG-пайплайн до виклику LLM. Ключовий механізм — [AIRequestContext](request-contexts.md).

### Повний потік

```
ragService.GetCompletionAsync("What is the return policy?")
    ↓
① RagEnabledService запускає RAG-пайплайн
   Переписування запиту → Фільтрація → Ембеддинг (за потреби) → Витягування → Збирання контексту
    ↓
② TemplateContextBuilder підставляє {context} та {question}
   → "Answer using the following info.\n[1] Returns within 30 days...\nQuestion: What is the return policy?"
    ↓
③ RagEnabledService створює AIRequestContext
   RequestMessageOverride = зібраний промпт
    ↓
④ _innerService.GetCompletionAsync(вихідне повідомлення, context: context) викликається
   → AIService зберігає контекст в AsyncLocal
   → Вихідне запитання додається до історії діалогу
    ↓
⑤ AIService.GetLatestMessages() замінює початковий ввід поточного запиту
   Історія діалогу: "What is the return policy?" (оригінал збережено)
   Що бачить модель: зібраний промпт (RequestMessageOverride)
```

### Чому саме такий дизайн

Ключова ідея — **розділення історії діалогу та вхідних даних моделі**:

- **В історії діалогу зберігається оригінальне запитання** — щоб уточнювальні запитання на кшталт «а що щодо того?» мали коректний контекст
- **Модель отримує зібраний промпт** — повний промпт з витягнутими документами та запитанням
- **Стан AIService не мутується** — `AsyncLocal<T>` забезпечує ізоляцію для кожного запиту

Це практичне застосування `RequestMessageOverride`, описаного в документації [AIRequestContext](request-contexts.md). RAG-пайплайн використовує цей механізм автоматично — вам достатньо викликати `.WithRag()`.

### У коді

Ось ключовий код усередині `RagEnabledService`, що реалізує цей зв'язок:

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

`AIService` зберігає контекст в `AsyncLocal`. `GetLatestMessages()` застосовує `RequestMessageOverride` до початкового вводу поточного логічного запиту, зберігаючи подальші виклики інструментів асистентом та їхні результати. Так знайдені документи й результати інструментів разом надходять у наступних запитах до моделі. Після завершення відновлюється попередній контекст.
