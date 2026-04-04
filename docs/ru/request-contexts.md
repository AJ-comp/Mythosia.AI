# AIRequestContext

## Обзор

`AIRequestContext` **изменяет то, что видит модель, только для одного запроса** — добавление инструкций, справочных документов или полная замена пользовательского сообщения — без постоянного изменения системного сообщения или истории диалога.

## Проблема без AIRequestContext

```csharp
// ❌ Без — загрязняет системное сообщение
var originalSystem = service.SystemMessage;
service.SystemMessage = originalSystem + $"\n\nОтвечайте по контексту:\n{retrievedDocs}";
var answer = await service.GetCompletionAsync(userQuestion);
service.SystemMessage = originalSystem; // но история уже загрязнена
```

```csharp
// ✅ С AIRequestContext — чисто и без побочных эффектов
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nОтвечайте по контексту:\n{retrievedDocs}"
    });
```

## Доступные свойства

### SystemMessagePrefix

Добавляет текст перед системным сообщением для этого запроса:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "Сегодня 2026-03-31.\n"
};

var response = await service.GetCompletionAsync("Какая сегодня дата?", context);
```

**Когда использовать:** Для внедрения динамических метаданных (дата, часовой пояс пользователя, информация о сессии), которые меняются от запроса к запросу.

### SystemMessageSuffix

Добавляет текст после системного сообщения:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nВсегда отвечайте на русском языке."
};

var response = await service.GetCompletionAsync("Hello!", context);
```

**Когда использовать:** Для добавления поведенческих инструкций, контекста RAG или языковых предпочтений к конкретному запросу.

### AdditionalMessages

Вставляет дополнительные сообщения в диалог только для этого запроса — полезно для внедрения справочных документов или few-shot примеров:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("Справка: возврат возможен в течение 30 дней.").Build()
    }
};

var response = await service.GetCompletionAsync("Могу ли я вернуть товар?", context);
```

**Когда использовать:** Для предоставления справочных материалов, few-shot примеров или вспомогательного контекста, который не должен сохраняться в истории диалога.

### RequestMessageOverride

Полностью заменяет пользовательское сообщение для этого запроса. Исходный промпт игнорируется:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"Ответьте на вопрос по контексту.\n\nКонтекст: {docs}\n\nВопрос: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context);
```

**Когда использовать:** Когда промежуточный слой (RAG, переписывание запросов) должен полностью переформулировать промпт перед отправкой модели, сохраняя при этом исходный ввод пользователя в истории диалога.

> **💡 Примечание:** При использовании `.WithRag()` RAG-пайплайн автоматически задействует это свойство. Подробнее о внутреннем механизме см. [Настройка пайплайна — Внутренний механизм](rag-pipeline.md#внутренний-механизм).

## Сравнение «до» и «после»

### Сценарий: RAG с внедрением даты и извлечённым контекстом

**Без AIRequestContext:**

```csharp
// ❌ Неаккуратно, зависит от состояния, подвержено ошибкам
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nСегодня: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nКонтекст:\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // удалить few-shot пример
```

**С AIRequestContext:**

```csharp
// ✅ Чисто, без изменения состояния, без побочных эффектов
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"Сегодня: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nКонтекст:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
        }
    });
```

## Совместное использование с AIRequestProfile

Оба объекта можно передать вместе для максимального контроля над одним запросом:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nКонтекст:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("Пример: ...").Build()
        }
    }
);
```

Подробнее о переопределении параметров генерации см. [AIRequestProfile](request-profiles.md).
```

> **💡 Примечание:** При использовании `.WithRag()` RAG-пайплайн задействует это свойство автоматически. Подробности — в разделе [Настройка пайплайна — Внутреннее устройство](rag-pipeline.md#внутреннее-устройство).

## Совмещение с AIRequestProfile

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nКонтекст:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("Пример: ...").Build()
        }
    }
);
```

Подробнее о параметрах генерации — в [AIRequestProfile](request-profiles.md).
