# Потокова передача

## Базовий стримінг

`StreamAsync` дозволяє отримувати токени в міру їх генерації:

```csharp
await foreach (var token in service.StreamAsync("Розкажіть історію"))
{
    Console.Write(token);
}
```

## Стримінг із типом контенту

`StreamAsync` повертає об''єкти `StreamingContent` із текстом та інформацією про тип:

```csharp
await foreach (var content in service.StreamAsync("Поясніть квантові обчислення", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Стримінг міркувань

Усі провайдери з підтримкою міркувань (OpenAI, Claude, Gemini, Grok, DeepSeek) використовують єдиний патерн. Передайте `StreamOptions` з увімкненими міркуваннями:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Розв''яжіть: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Міркування] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

`StreamingContentType.Reasoning` містить внутрішній хід міркувань моделі, а `StreamingContentType.Text` — підсумкову відповідь.

## Стримінг зі структурованим виводом

Стрімте текст у реальному часі та отримайте десеріалізований об''єкт по завершенні:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Стримінг токенів у UI по мірі надходження
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Отримання парсеного результату після завершення
MyDto result = await run.Result;
```

## Витрата токенів

По завершенні стримінгу остання подія `Completion` містить об''єкт `TokenUsage` з детальною статистикою:

```csharp
await foreach (var content in service.StreamAsync("Поясніть квантові обчислення", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nВхідні токени:  {content.Usage.InputTokens}");
        Console.WriteLine($"Вихідні токени: {content.Usage.OutputTokens}");
        Console.WriteLine($"Усього:         {content.Usage.TotalTokens}");
    }
}
```

### Властивості TokenUsage

| Властивість | Опис |
|-------------|------|
| `InputTokens` | Кількість токенів у вході/промпті |
| `OutputTokens` | Кількість токенів у вихідній відповіді |
| `TotalTokens` | Вхідні + вихідні |
| `CachedInputTokens` | Токени, обслуговані з кешу (економія) |
| `CacheCreationTokens` | Токени, записані в кеш (Anthropic) |
| `ReasoningTokens` | Токени, використані для внутрішніх міркувань |
| `CacheHitRatio` | Частка влучень у кеш (0.0–1.0) |
| `VisibleOutputTokens` | Вихідні токени без урахування міркувань |

### Перевірка ефективності кешу

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"Влучення в кеш: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Без кешу: {content.Usage.NonCachedInputTokens}");
}
```

## Пресети StreamOptions

`StreamOptions` надає пресети та Fluent-білдер для керування вмістом потоку:

```csharp
// Повний набір — метадані, виклик функцій, міркування
await foreach (var c in service.StreamAsync("промпт", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Мінімальні витрати — лише текст, без метаданих
await foreach (var c in service.StreamAsync("промпт", StreamOptions.Minimal))
    Console.Write(c.Content);

// Сценарій із функціями
await foreach (var c in service.StreamAsync("промпт", StreamOptions.WithFunctions))
{ /* Обробка Text, FunctionCall, FunctionResult, Completion */ }
```

Fluent-білдер для власних комбінацій:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // Увімкнути хід думок
    .WithMetadata()        // Додати інформацію про модель у Completion
    .WithFunctionCalls();  // Увімкнути виклик функцій під час стримінгу
```

## Стримінг без збереження стану (StreamOnceAsync)

Стрімить відповідь без впливу на історію діалогу — потокова версія `AskOnceAsync`:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Перекладіть це французькою"))
    Console.Write(chunk);
```

Є перевантаження для `Message` із мультимодальним вводом:

```csharp
var message = MessageBuilder.Create().AddText("Опишіть це").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## Сумаризація перед стримінгом

Автоматична політика сумаризації не спрацьовує під час стримінгу. Викличте її явно перед `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Продовжимо нашу розмову...", StreamOptions.Default))
    Console.Write(chunk.Content);
```
