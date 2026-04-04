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
