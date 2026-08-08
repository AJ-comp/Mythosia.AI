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
    context: new AIRequestContext
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

var response = await service.GetCompletionAsync("Какая сегодня дата?", context: context);
```

**Когда использовать:** Для внедрения динамических метаданных (дата, часовой пояс пользователя, информация о сессии), которые меняются от запроса к запросу.

### SystemMessageSuffix

Добавляет текст после системного сообщения:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nВсегда отвечайте на русском языке."
};

var response = await service.GetCompletionAsync("Hello!", context: context);
```

**Когда использовать:** Для добавления поведенческих инструкций, контекста RAG или языковых предпочтений к конкретному запросу.

### AdditionalMessages

Вставляет дополнительные сообщения в диалог только для этого запроса — полезно для внедрения справочных документов или few-shot примеров:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.Create().AddText("Справка: возврат возможен в течение 30 дней.").Build()
    }
};

var response = await service.GetCompletionAsync("Могу ли я вернуть товар?", context: context);
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

await service.GetCompletionAsync(userQuery, context: context);
```

**Когда использовать:** Когда промежуточный слой (RAG, переписывание запросов) должен полностью переформулировать промпт перед отправкой модели, сохраняя при этом исходный ввод пользователя в истории диалога.

> **💡 Примечание:** При использовании `.WithRag()` RAG-пайплайн автоматически задействует это свойство. Подробнее о внутреннем механизме см. [Настройка пайплайна — Внутренний механизм](rag-pipeline.md#как-это-работает-изнутри).

## Сравнение «до» и «после»

### Сценарий: RAG с внедрением даты и извлечённым контекстом

**Без AIRequestContext:**

```csharp
// ❌ Неаккуратно, зависит от состояния, подвержено ошибкам
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nСегодня: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nКонтекст:\n{retrievedChunks}";

var fewShotIndex = service.ActivateChat.Messages.Count;
service.ActivateChat.Messages.Add(MessageBuilder.Create().AddText(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.ActivateChat.Messages.RemoveAt(fewShotIndex); // удалить few-shot пример
```

**С AIRequestContext:**

```csharp
// ✅ Чисто, без изменения состояния, без побочных эффектов
var answer = await service.GetCompletionAsync(userQuery,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Сегодня: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nКонтекст:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText(fewShotExample).Build()
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
            MessageBuilder.Create().AddText("Пример: ...").Build()
        }
    }
);
```

Подробнее о переопределении параметров генерации см. [AIRequestProfile](request-profiles.md).

> **💡 Примечание:** При использовании `.WithRag()` RAG-пайплайн задействует это свойство автоматически. Подробности — в разделе [Настройка пайплайна — Внутреннее устройство](rag-pipeline.md#как-это-работает-изнутри).

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
            MessageBuilder.Create().AddText("Пример: ...").Build()
        }
    }
);
```

Подробнее о параметрах генерации — в [AIRequestProfile](request-profiles.md).

## Автоматическая инъекция через `SystemMessageProvider`

### Какую проблему решает

Типичное чат-приложение имеет несколько точек входа в LLM, которым нужна одна и та же базовая подложка — сегодняшняя дата, активная папка, информация о сессии. **Без** `SystemMessageProvider` каждое место вызова должно помнить о построении и передаче этого контекста:

```csharp
// ❌ Без SystemMessageProvider — каждая точка входа должна помнить об инъекции
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Основной ответ чата
var answer = await service.GetCompletionAsync(userMessage,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 2. Генератор заголовков (добавлен позже)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 3. Суммаризатор (добавлен ещё позже)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 4. Вызов agent — легко забыть! Компилятор не предупредит
var agentResult = await service.RunAgentAsync(goal);  // ← дата отсутствует, тихий баг
```

Проблемы такого подхода:

- Один и тот же сниппет построения контекста **дублируется** в каждом месте вызова
- Новые точки входа (`RunAgentAsync` выше) **легко пропустить** — нет проверки на этапе компиляции
- Каждая новая фича, добавляющая вызов LLM, должна помнить о соглашении
- Тесты также должны воспроизводить настройку контекста в каждом месте вызова

С `SystemMessageProvider` вы регистрируете базовую подложку **один раз**, и каждый исходящий вызов получает её автоматически:

```csharp
// ✅ С SystemMessageProvider — регистрируется один раз, применяется везде
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// Все эти вызовы автоматически получают подложку — без boilerplate на каждый вызов
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← тоже получает подложку

// Потоковые точки входа тоже — та же подложка, без boilerplate на каждый вызов
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### Как это работает

Зарегистрируйте колбэк один раз через fluent-хелпер `WithSystemMessageProvider`. Каждый исходящий вызов (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) автоматически вызывает его для построения базового контекста:

```csharp
// Обычно при создании сервиса / настройке DI
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

### Async-перегрузка для IO-провайдеров

Когда базовый контекст приходит из базы данных, кэша или HTTP-вызова, используйте async-перегрузку, чтобы провайдеру не приходилось блокироваться на `.Result` / `.GetAwaiter().GetResult()`. Разрешение перегрузки выбирает нужную по арности лямбды — без аргумента для sync, один `CancellationToken` для async:

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

Непотоковые пути (`GetCompletionAsync`, `RunAgentAsync`) не поддерживают отмену по дизайну — их сигнатуры не принимают `CancellationToken`, и provider всегда получает `CancellationToken.None`. Если вашему provider нужна отмена (например, долгий DB-запрос), используйте потоковые пути (`StreamAsync`, `RunAgentStreamAsync`), которые пробрасывают токен вызывающего вплоть до колбэка provider.

### Слияние с явным per-call контекстом

Когда у вызова есть зарегистрированный provider **и** также передан явный `AIRequestContext`, они объединяются по полям:

| Поле | Правило слияния |
|---|---|
| `SystemMessagePrefix` | явное побеждает, если non-null, иначе provider |
| `SystemMessageSuffix` | явное побеждает, если non-null, иначе provider |
| `RequestMessageOverride` | явное побеждает, если non-null, иначе provider |
| `AdditionalMessages` | конкатенация (сначала provider, затем явное) |

Обоснование: типичный случай — «provider предоставляет базу, конкретный вызов хочет заменить одно скалярное поле или добавить дополнительные сообщения» — пофилд-оверрайд сохраняет семантику предсказуемой без неожиданной конкатенации.

### Вызов на каждый запрос

Provider вызывается **один раз за запрос**, так что возвращаемые значения могут отражать состояние на данный момент (timestamp, сессия и т. п.). Возврат `null` — no-op, идентично тому, как если бы `SystemMessageProvider` не был установлен для этого вызова.

### Итог: когда выбирать этот инструмент — пересечение трёх условий

Если сделать шаг назад от приведённых примеров и правил слияния, `SystemMessageProvider` — это специализированный инструмент для случая, когда **одновременно выполняются три условия**:

1. **Базовый контекст должен присутствовать во всех вызовах LLM** — не хочется помнить о ручной инъекции в каждой точке входа
2. **Значение должно вычисляться динамически в момент вызова** — текущее время, активная папка, вошедший пользователь и другие значения, которые нельзя зафиксировать при запуске
3. **Постоянное состояние (`SystemMessage`, история диалога) не должно загрязняться** — значение не должно просачиваться в последующие вызовы

Если отсутствует хотя бы одно из трёх условий, правильный ответ — более простой инструмент:

| Ситуация | Правильный инструмент | Причина |
|---|---|---|
| Базовый контекст **фиксирован (не меняется)** в течение сессии | `service.SystemMessage = "..."` | Однократного присваивания достаточно, provider не нужен |
| **Только один конкретный вызов** требует особой обработки | Явно передать `AIRequestContext` в точке вызова | Не общий базовый контекст, а разовая инъекция |
| Общий + динамический + без загрязнения **(все три)** | **`SystemMessageProvider`** | Специализированный инструмент для этого тройного пересечения |

#### Почему это не противоречит принципу «однократности» `AIRequestContext`

Суть `AIRequestContext` — не в том, что он «используется лишь однажды», а в том, что **«никогда не загрязняет постоянное состояние»**. `SystemMessageProvider` — это фабрика, которая **повторно выполняет колбэк на каждом запросе**, создавая **совершенно новый `AIRequestContext`, ограниченный этим запросом**. Получаемый контекст по-прежнему per-request scoped, значение никогда не просачивается в историю диалога, а при следующем вызове колбэк выполняется снова, отражая **актуальное на тот момент** значение. То есть provider не нарушает принцип проектирования `AIRequestContext` — он просто **автоматизирует его**.

Конкретно: регистрация provider ниже **не** изменяет ни `service.SystemMessage`, ни `service.ActivateChat.Messages`:

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- После полуночи повторный запуск provider на следующем вызове автоматически отразит **новую дату** (не статично)
- Через неделю в истории диалога не окажется «Today is ...», впаянного в прошлые запросы
- Даже при использовании общего сервиса в многопользовательской среде каждый вызов порождает собственный независимый контекст

> Доступно в Mythosia.AI v6.3.0+.
