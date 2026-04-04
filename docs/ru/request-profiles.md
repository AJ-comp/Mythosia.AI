# AIRequestProfile

## Обзор

`AIRequestProfile` переопределяет параметры генерации — Temperature, MaxTokens, Stateless-режим, вызов функций — **только для одного запроса**. Глобальные настройки сервиса остаются без изменений.

## Проблема без AIRequestProfile

```csharp
// ❌ Без AIRequestProfile — ручное управление состоянием
var savedTemp = service.Temperature;
service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("Перепишите запрос: ...");

// Восстановление — легко забыть, небезопасно для потоков
service.Temperature = savedTemp;
```

```csharp
// ✅ С AIRequestProfile — чисто и безопасно
var rewritten = await service.GetCompletionAsync("Перепишите запрос: ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

## Доступные свойства

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

Все свойства необязательны — устанавливайте только то, что хотите переопределить.

## Встроенные профили

```csharp
// Переписывание запросов: низкая температура, малый лимит токенов, без сохранения в истории
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Суммаризация: чуть выше температура, умеренный лимит токенов
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Практические примеры

### Внутреннее переписывание запросов в RAG-пайплайне

```csharp
// Основной сервис настроен для пользовательского диалога
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// Переписать запрос с другими настройками — сервис не изменится
var betterQuery = await service.GetCompletionAsync(
    $"Перепишите для поиска: {userQuery}",
    RequestProfiles.QueryRewrite);

// Продолжить обычный диалог — по-прежнему Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### Отключение функций для конкретного шага

```csharp
// У сервиса зарегистрированы функции
service.WithFunction("search_web", "Поиск в вебе", ...);

// Для этого одного вызова пропустить вызов функций — просто ответить напрямую
var directAnswer = await service.GetCompletionAsync(
    "Сколько будет 2 + 2?",
    new AIRequestProfile { DisableFunctions = true });
```

## Совместное использование с AIRequestContext

Оба объекта можно передать вместе для максимального контроля:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nБудьте лаконичны." }
);
```

## Совмещение с AIRequestContext

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nОтвечайте кратко." }
);
```

Подробнее об инъекции контента — в разделе [AIRequestContext](request-contexts.md).
