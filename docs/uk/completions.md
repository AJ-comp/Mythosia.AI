# Генерація тексту

## Одиночний запит

Найпростіший сценарій — надіслати повідомлення й отримати відповідь:

```csharp
var response = await service.GetCompletionAsync("Яка столиця Франції?");
Console.WriteLine(response); // Париж
```

## Системний промпт

Задайте моделі роль або інструкції через системний промпт:

```csharp
service.SystemPrompt = "Ви — лаконічний асистент. Відповідайте одним реченням.";

var response = await service.GetCompletionAsync("Поясніть рекурсію.");
```

## Багатоходовий діалог

Повідомлення накопичуються автоматично. Кожен виклик `GetCompletionAsync` додається до історії діалогу:

```csharp
await service.GetCompletionAsync("Мене звати Аліса.");
var response = await service.GetCompletionAsync("Як мене звати?");
// → "Вас звати Аліса."
```

Щоб очистити історію:

```csharp
service.ClearMessages();
```

## Явна побудова повідомлень

За допомогою `MessageBuilder` можна створити повідомлення вручну:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("Стисло перекажіть цей текст: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Мультимодальність (зображення)

Провайдери з підтримкою vision приймають зображення разом із текстом:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("Що зображено на цій діаграмі?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Швидке запитання (статичний API)

Задайте питання одним рядком без створення екземпляра сервісу. Провайдер визначається автоматично за назвою моделі:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "Столиця Франції?",
    model: AIModels.OpenAI.Gpt4oMini  // за замовчуванням
);
```

Версія із зображенням:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Опишіть це зображення",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4Vision
);
```

## Зручні методи для зображень

Аналізуйте зображення без `MessageBuilder` — читання файлу й визначення MIME-типу відбувається автоматично:

```csharp
// З файлу
var response = await service.GetCompletionWithImageAsync(
    "Що зображено на цій діаграмі?", "diagram.png");

// З URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Опишіть це фото", "https://example.com/photo.jpg");
```

## Повторна генерація останньої відповіді

Видаляє останню відповідь AI та повторно надсилає останнє повідомлення користувача:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Використовуйте, коли попередня відповідь вас не влаштувала.

## Підрахунок токенів

Оцініть витрату токенів перед відправкою запиту. Працює з **усіма провайдерами**:

```csharp
// Токени поточної історії діалогу
uint conversationTokens = await service.GetInputTokenCountAsync();

// Токени конкретного промпту
uint promptTokens = await service.GetInputTokenCountAsync("Текст промпту");
```

OpenAI та більшість провайдерів використовують локальну оцінку на основі TikToken. Anthropic і Google викликають нативні API для точного підрахунку.

## Fluent-ланцюжки повідомлень

`BeginMessage()` надає Fluent API для побудови та відправки повідомлень із текстом, зображеннями, стримінгом і налаштуваннями в одному ланцюжку:

```csharp
// Текст + зображення → відправка
string response = await service.BeginMessage()
    .AddText("Що зображено на цій діаграмі?")
    .AddImage("diagram.png")
    .SendAsync();

// Одноразове питання (не впливає на історію)
string answer = await service.BeginMessage()
    .AddText("Перекладіть це українською")
    .SendOnceAsync();

// Стримінг
await service.BeginMessage()
    .AddText("Напишіть вірш про весну")
    .StreamAsync(chunk => Console.Write(chunk));

// Власний таймаут і налаштування
string result = await service.BeginMessage()
    .AddText("Проаналізуйте це зображення")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` також підтримує `IAsyncEnumerable`:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("Розкажіть історію").StreamAsync())
    Console.Write(chunk);
```

## Керування довжиною та температурою

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // що нижче, то детермінованіше
```
