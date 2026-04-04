# AIRequestProfile

## Огляд

`AIRequestProfile` перевизначає параметри генерації — Temperature, MaxTokens, Stateless-режим, виклик функцій — **лише для одного запиту**. Глобальні налаштування сервісу залишаються без змін.

## Проблема без AIRequestProfile

```csharp
// ❌ Без AIRequestProfile — ручне керування станом
var savedTemp = service.Temperature;
service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("Перепишіть запит: ...");

service.Temperature = savedTemp; // Легко забути, небезпечно для потоків
```

```csharp
// ✅ З AIRequestProfile — чисто й безпечно
var rewritten = await service.GetCompletionAsync("Перепишіть запит: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

## Доступні властивості

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,
    MaxTokens = 256,
    Stateless = true,
    DisableFunctions = true,
    DisableReasoning = true
};

var response = await service.GetCompletionAsync("промпт", profile);
```

Усі властивості необов''язкові — встановлюйте лише те, що хочете перевизначити.

## Вбудовані профілі

```csharp
// Переписування запитів: низька температура, малий ліміт токенів, без збереження в історії
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Сумаризація: трохи вища температура, помірний ліміт токенів
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Практичні приклади

### Внутрішнє переписування запитів у RAG-пайплайні

```csharp
// Основний сервіс налаштований для користувацького діалогу
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// Переписати запит з іншими налаштуваннями — сервіс не зміниться
var betterQuery = await service.GetCompletionAsync(
    $"Перепишіть для пошуку: {userQuery}",
    RequestProfiles.QueryRewrite);

// Продовжити звичайний діалог — досі Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### Вимкнення функцій для конкретного кроку

```csharp
// У сервісу зареєстровані функції
service.WithFunction("search_web", "Пошук у вебі", ...);

// Для цього одного виклику пропустити виклик функцій — просто відповісти напряму
var directAnswer = await service.GetCompletionAsync(
    "Скільки буде 2 + 2?",
    new AIRequestProfile { DisableFunctions = true });
```

## Спільне використання з AIRequestContext

Обидва об'єкти можна передати разом для максимального контролю:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nБудьте лаконічні." }
);
```

## Поєднання з AIRequestContext

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nВідповідайте стисло." }
);
```

Детальніше про ін''єкцію контенту — в розділі [AIRequestContext](request-contexts.md).
