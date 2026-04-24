# AIRequestContext

## Огляд

`AIRequestContext` **змінює те, що бачить модель, лише для одного запиту** — додавання інструкцій, довідкових документів або повна заміна повідомлення користувача — без постійної зміни системного повідомлення чи історії діалогу.

## Проблема без AIRequestContext

```csharp
// ❌ Без — забруднює системне повідомлення
var originalSystem = service.SystemMessage;
service.SystemMessage = originalSystem + $"\n\nВідповідайте за контекстом:\n{retrievedDocs}";
var answer = await service.GetCompletionAsync(userQuestion);
service.SystemMessage = originalSystem;
```

```csharp
// ✅ З AIRequestContext — чисто й без побічних ефектів
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nВідповідайте за контекстом:\n{retrievedDocs}"
    });
```

## Доступні властивості

### SystemMessagePrefix

Додає текст перед системним повідомленням для цього запиту:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "Сьогодні 2026-03-31.\n"
};

var response = await service.GetCompletionAsync("Яка сьогодні дата?", context);
```

**Коли використовувати:** Для впровадження динамічних метаданих (дата, часовий пояс користувача, інформація про сесію), що змінюються від запиту до запиту.

### SystemMessageSuffix

Додає текст після системного повідомлення:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nЗавжди відповідайте українською мовою."
};

var response = await service.GetCompletionAsync("Hello!", context);
```

**Коли використовувати:** Для додавання поведінкових інструкцій, контексту RAG або мовних уподобань до конкретного запиту.

### AdditionalMessages

Вставляє додаткові повідомлення в діалог лише для цього запиту — корисно для впровадження довідкових документів або few-shot прикладів:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("Довідка: повернення можливе протягом 30 днів.").Build()
    }
};

var response = await service.GetCompletionAsync("Чи можу я повернути товар?", context);
```

**Коли використовувати:** Для надання довідкових матеріалів, few-shot прикладів або допоміжного контексту, який не повинен зберігатися в історії діалогу.

### RequestMessageOverride

Повністю замінює повідомлення користувача для цього запиту. Оригінальний промпт ігнорується:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"Дайте відповідь на запитання за контекстом.\n\nКонтекст: {docs}\n\nЗапитання: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context);
```

**Коли використовувати:** Коли проміжний шар (RAG, переписування запитів) повинен повністю переформулювати промпт перед відправкою моделі, зберігаючи при цьому оригінальне введення користувача в історії діалогу.

> **💡 Примітка:** При використанні `.WithRag()` RAG-пайплайн автоматично задіює цю властивість. Докладніше про внутрішній механізм див. [Налаштування пайплайну — Внутрішній механізм](rag-pipeline.md#внутрішній-механізм).

## Порівняння «до» та «після»

### Сценарій: RAG з впровадженням дати та отриманим контекстом

**Без AIRequestContext:**

```csharp
// ❌ Неохайно, залежить від стану, схильне до помилок
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nСьогодні: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nКонтекст:\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // видалити few-shot приклад
```

**З AIRequestContext:**

```csharp
// ✅ Чисто, без зміни стану, без побічних ефектів
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"Сьогодні: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nКонтекст:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
        }
    });
```

## Спільне використання з AIRequestProfile

Обидва об'єкти можна передати разом для максимального контролю над одним запитом:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nКонтекст:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("Приклад: ...").Build()
        }
    }
);
```

Докладніше про перевизначення параметрів генерації див. [AIRequestProfile](request-profiles.md).
```

> **💡 Примітка:** При використанні `.WithRag()` RAG-пайплайн задіює цю властивість автоматично. Деталі — в розділі [Налаштування пайплайну — Внутрішня будова](rag-pipeline.md#внутрішня-будова).

## Поєднання з AIRequestProfile

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nКонтекст:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("Приклад: ...").Build()
        }
    }
);
```

Детальніше про параметри генерації — в [AIRequestProfile](request-profiles.md).

## Автоматична інʼєкція через `SystemMessageProvider`

### Яку проблему розвʼязує

Типовий чат-застосунок має кілька точок входу в LLM, яким потрібна та сама базова підложка — сьогоднішня дата, активна тека, інформація сесії. **Без** `SystemMessageProvider` кожне місце виклику має памʼятати про побудову й передачу цього контексту:

```csharp
// ❌ Без SystemMessageProvider — кожна точка входу має памʼятати про інʼєкцію
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Основна відповідь чату
var answer = await service.GetCompletionAsync(userMessage,
    new AIRequestContext { SystemMessageSuffix = today });

// 2. Генератор заголовків (доданий пізніше)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 3. Сумаризатор (доданий ще пізніше)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 4. Виклик agent — легко забути! Компілятор не попередить
var agentResult = await service.RunAgentAsync(goal);  // ← дата відсутня, тихий баг
```

Проблеми такого підходу:

- Той самий сніпет побудови контексту **дублюється** в кожному місці виклику
- Нові точки входу (`RunAgentAsync` вище) **легко пропустити** — немає перевірки під час компіляції
- Кожна нова фіча, що додає виклик LLM, має памʼятати про конвенцію
- Тести також мають відтворювати налаштування контексту в кожному місці виклику

З `SystemMessageProvider` ви реєструєте базову підложку **один раз**, і кожен вихідний виклик отримує її автоматично:

```csharp
// ✅ З SystemMessageProvider — реєстрація один раз, застосовується всюди
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// Усі ці виклики автоматично отримують підложку — без boilerplate на кожен виклик
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← також отримує підложку

// Потокові точки входу теж — та сама підложка, без boilerplate на кожен виклик
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### Як це працює

Зареєструйте колбек один раз через fluent-хелпер `WithSystemMessageProvider`. Кожен вихідний виклик (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) автоматично викликає його для побудови базового контексту:

```csharp
// Зазвичай при створенні сервісу / налаштуванні DI
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### Async-перевантаження для IO-провайдерів

Коли базовий контекст надходить з бази даних, кешу чи HTTP-виклику, використовуйте async-перевантаження, щоб провайдеру не довелося блокуватися на `.Result` / `.GetAwaiter().GetResult()`. Вирішення перевантаження обирає потрібну за арністю лямбди — без аргумента для sync, один `CancellationToken` для async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

Непотокові шляхи (`GetCompletionAsync`, `RunAgentAsync`) не підтримують скасування за дизайном — їхні сигнатури не приймають `CancellationToken`, і provider завжди отримує `CancellationToken.None`. Якщо вашому provider потрібне скасування (наприклад, довгий DB-запит), використовуйте потокові шляхи (`StreamAsync`, `RunAgentStreamAsync`), які пробрасують токен того, хто викликає, аж до колбека provider.

### Злиття з явним per-call контекстом

Коли виклик має зареєстрований provider **і** також передає явний `AIRequestContext`, обидва зливаються по полях:

| Поле | Правило злиття |
|---|---|
| `SystemMessagePrefix` | явне перемагає, якщо non-null, інакше provider |
| `SystemMessageSuffix` | явне перемагає, якщо non-null, інакше provider |
| `RequestMessageOverride` | явне перемагає, якщо non-null, інакше provider |
| `AdditionalMessages` | конкатенація (спочатку provider, потім явне) |

Обґрунтування: типовий випадок — «provider надає базу, конкретний виклик хоче замінити одне скалярне поле або додати додаткові повідомлення» — поле-рівневий override зберігає семантику передбачуваною без несподіваної конкатенації.

### Виклик на кожен запит

Provider викликається **один раз на запит**, тож значення, що повертаються, можуть відображати актуальний стан (timestamp, сесія тощо). Повернення `null` — no-op, ідентично тому, якби `SystemMessageProvider` не був встановлений для цього виклику.

### Підсумок: коли обирати цей інструмент — перетин трьох умов

Якщо зробити крок назад від наведених прикладів та правил злиття, `SystemMessageProvider` — це спеціалізований інструмент для випадку, коли **одночасно виконуються три умови**:

1. **Базовий контекст має бути присутнім у всіх викликах LLM** — не хочеться пам'ятати про ручну ін'єкцію в кожній точці входу
2. **Значення має обчислюватися динамічно в момент виклику** — поточний час, активна тека, увійшлий користувач та інші значення, які неможливо зафіксувати під час запуску
3. **Постійний стан (`SystemMessage`, історія розмови) не повинен забруднюватися** — значення не повинно просочуватися в наступні виклики

Якщо відсутня хоча б одна з трьох умов, правильна відповідь — простіший інструмент:

| Ситуація | Правильний інструмент | Причина |
|---|---|---|
| Базовий контекст **фіксований (не змінюється)** протягом сесії | `service.SystemMessage = "..."` | Одноразового присвоєння достатньо, provider не потрібен |
| **Лише один конкретний виклик** потребує особливої обробки | Явно передати `AIRequestContext` у точці виклику | Не спільний базовий контекст, а разова ін'єкція |
| Спільний + динамічний + без забруднення **(усі три)** | **`SystemMessageProvider`** | Спеціалізований інструмент для цього потрійного перетину |

#### Чому це не суперечить принципу «одноразовості» `AIRequestContext`

Суть `AIRequestContext` — не в тому, що він «використовується лише раз», а в тому, що **«ніколи не забруднює постійний стан»**. `SystemMessageProvider` — це фабрика, яка **повторно виконує колбек на кожному запиті**, створюючи **абсолютно новий `AIRequestContext`, обмежений цим запитом**. Отриманий контекст усе ще per-request scoped, значення ніколи не просочується в історію розмови, а на наступному виклику колбек виконується знову, відображаючи **актуальне на той момент** значення. Тобто provider не порушує принцип дизайну `AIRequestContext` — він лише **автоматизує його**.

Конкретно: реєстрація provider нижче **не** змінює ні `service.SystemMessage`, ні `service.Messages`:

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- Після півночі повторний запуск provider на наступному виклику автоматично відображає **нову дату** (не статично)
- Через тиждень в історії розмови не знайдеться «Today is ...», вбитого в минулі запити
- Навіть при використанні спільного сервісу в багатокористувацькому середовищі кожен виклик породжує власний незалежний контекст

> Доступно в Mythosia.AI v6.3.0+.
