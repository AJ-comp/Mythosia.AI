# Налаштування пайплайну RAG

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
        // Етапи: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
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
   Переписування запиту → Ембеддинг → Витягування → Збирання контексту
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
⑤ AIService.GetLatestMessages() замінює останнє повідомлення
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
// Усередині RagEnabledService.GetCompletionAsync
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← оригінальне запитання (зберігається в історії)
    context: BuildRequestContext(processed));    // ← зібраний промпт (бачить лише модель)

// BuildRequestContext — створює AIRequestContext
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

`AIService` зберігає цей контекст у `AsyncLocal`, а `GetLatestMessages()` замінює останнє повідомлення на `RequestMessageOverride`. Після завершення запиту контекст автоматично відновлюється, гарантуючи відсутність впливу на наступні запити.
