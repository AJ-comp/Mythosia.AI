# Потоковая передача

## Базовый стриминг

`StreamAsync` позволяет получать токены по мере их генерации:

```csharp
await foreach (var token in service.StreamAsync("Расскажите историю"))
{
    Console.Write(token);
}
```

## Стриминг с типом контента

`StreamAsync` возвращает объекты `StreamingContent` с текстом и информацией о типе:

```csharp
await foreach (var content in service.StreamAsync("Объясните квантовые вычисления"))
{
    Console.Write(content.Content);
}
```

## Стриминг рассуждений

Все провайдеры с поддержкой рассуждений (OpenAI, Claude, Gemini, Grok, DeepSeek) используют единый паттерн. Передайте `StreamOptions` с включёнными рассуждениями:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Решите: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Размышление] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

`StreamingContentType.Reasoning` содержит внутренний ход рассуждений модели, а `StreamingContentType.Text` — итоговый ответ.

## Стриминг со структурированным выводом

Стримьте текст в реальном времени и получите десериализованный объект по завершении:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Стриминг токенов в UI по мере поступления
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Получение парсированного результата после завершения
MyDto result = await run.Result;
```

## Расход токенов

По завершении стриминга последнее событие `Completion` содержит объект `TokenUsage` с подробной статистикой:

```csharp
await foreach (var content in service.StreamAsync("Объясните квантовые вычисления"))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nВходные токены:  {content.Usage.InputTokens}");
        Console.WriteLine($"Выходные токены: {content.Usage.OutputTokens}");
        Console.WriteLine($"Всего:           {content.Usage.TotalTokens}");
    }
}
```

### Свойства TokenUsage

| Свойство | Описание |
|----------|----------|
| `InputTokens` | Количество токенов во входе/промпте |
| `OutputTokens` | Количество токенов в выходном ответе |
| `TotalTokens` | Входные + выходные |
| `CachedInputTokens` | Токены, обслуженные из кэша (экономия) |
| `CacheCreationTokens` | Токены, записанные в кэш (Anthropic) |
| `ReasoningTokens` | Токены, использованные для внутренних рассуждений |
| `CacheHitRatio` | Доля попаданий в кэш (0.0–1.0) |
| `VisibleOutputTokens` | Выходные токены без учёта рассуждений |

### Проверка эффективности кэша

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"Попадание в кэш: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Без кэша: {content.Usage.NonCachedInputTokens}");
}
```

## Пресеты StreamOptions

`StreamOptions` предоставляет пресеты и Fluent-билдер для управления содержимым потока:

```csharp
// Полный набор — метаданные, вызов функций, рассуждения
await foreach (var c in service.StreamAsync("промпт", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Минимальные накладные расходы — только текст, без метаданных
await foreach (var c in service.StreamAsync("промпт", StreamOptions.Minimal))
    Console.Write(c.Content);

// Сценарий с функциями
await foreach (var c in service.StreamAsync("промпт", StreamOptions.WithFunctions))
{ /* Обработка Text, FunctionCall, FunctionResult, Completion */ }
```

Fluent-билдер для пользовательских комбинаций:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // Включить ход мыслей
    .WithMetadata()        // Добавить информацию о модели в Completion
    .WithFunctionCalls();  // Включить вызов функций во время стриминга
```

## Стриминг без сохранения состояния (StreamOnceAsync)

Стримит ответ без влияния на историю диалога — потоковая версия `AskOnceAsync`:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Переведите это на французский"))
    Console.Write(chunk);
```

Есть перегрузка для `Message` с мультимодальным вводом:

```csharp
var message = MessageBuilder.Create().AddText("Опишите это").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## Суммаризация перед стримингом

Автоматическая политика суммаризации не срабатывает во время стриминга. Вызовите её явно перед `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Продолжим наш разговор..."))
    Console.Write(chunk.Content);
```
