# Генерация текста

## Одиночный запрос

Самый простой сценарий — отправить сообщение и получить ответ:

```csharp
var response = await service.GetCompletionAsync("Какая столица Франции?");
Console.WriteLine(response); // Париж
```

## Системный промпт

Задайте модели роль или инструкции через системный промпт:

```csharp
service.SystemPrompt = "Вы — лаконичный ассистент. Отвечайте одним предложением.";

var response = await service.GetCompletionAsync("Объясните рекурсию.");
```

## Многоходовый диалог

Сообщения накапливаются автоматически. Каждый вызов `GetCompletionAsync` добавляется в историю диалога:

```csharp
await service.GetCompletionAsync("Меня зовут Алиса.");
var response = await service.GetCompletionAsync("Как меня зовут?");
// → "Вас зовут Алиса."
```

Чтобы очистить историю:

```csharp
service.ClearMessages();
```

## Явное построение сообщений

С помощью `MessageBuilder` можно создать сообщение вручную:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("Кратко изложите этот текст: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Мультимодальность (изображения)

Провайдеры с поддержкой vision принимают изображения наряду с текстом:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.User("Что показано на этой диаграмме?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Быстрый вопрос (статический API)

Задайте вопрос одной строкой без создания экземпляра сервиса. Провайдер определяется автоматически по имени модели:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "Столица Франции?",
    model: AIModels.OpenAI.Gpt4oMini  // по умолчанию
);
```

Версия с изображением:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Опишите это изображение",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4Vision
);
```

## Удобные методы для изображений

Анализируйте изображения без `MessageBuilder` — чтение файла и определение MIME-типа происходит автоматически:

```csharp
// Из файла
var response = await service.GetCompletionWithImageAsync(
    "Что показано на этой диаграмме?", "diagram.png");

// Из URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Опишите это фото", "https://example.com/photo.jpg");
```

## Повторная генерация последнего ответа

Удаляет последний ответ AI и повторно отправляет последнее сообщение пользователя:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Используйте, когда предыдущий ответ вас не устроил.

## Подсчёт токенов

Оцените расход токенов перед отправкой запроса. Работает со **всеми провайдерами**:

```csharp
// Токены текущей истории диалога
uint conversationTokens = await service.GetInputTokenCountAsync();

// Токены конкретного промпта
uint promptTokens = await service.GetInputTokenCountAsync("Текст промпта");
```

OpenAI и большинство провайдеров используют локальную оценку на основе TikToken. Anthropic и Google вызывают нативные API для точного подсчёта.

## Fluent-цепочки сообщений

`BeginMessage()` предоставляет Fluent API для построения и отправки сообщений с текстом, изображениями, стримингом и настройками в одной цепочке:

```csharp
// Текст + изображение → отправка
string response = await service.BeginMessage()
    .AddText("Что показано на этой диаграмме?")
    .AddImage("diagram.png")
    .SendAsync();

// Одноразовый вопрос (не влияет на историю)
string answer = await service.BeginMessage()
    .AddText("Переведите это на русский")
    .SendOnceAsync();

// Стриминг
await service.BeginMessage()
    .AddText("Напишите стихотворение о весне")
    .StreamAsync(chunk => Console.Write(chunk));

// Пользовательский таймаут и настройки
string result = await service.BeginMessage()
    .AddText("Проанализируйте это изображение")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` также поддерживает `IAsyncEnumerable`:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("Расскажите историю").StreamAsync())
    Console.Write(chunk);
```

## Управление длиной и температурой

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // чем ниже, тем детерминированнее
```
