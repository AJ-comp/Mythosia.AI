# Генерація тексту

Для незалежних налаштувань і повторного використання варіантів застосовуйте [білдер запитів](request-building.md). Викликайте `CreateRequest(...)` перед `With...`. Властивості та fluent-методи сервісу зберігають попередню поведінку.

`GetCompletionAsync` залишається зручним способом отримати готову відповідь. Якщо потрібно бачити перебіг або керувати ще не завершеним завданням, використовуйте [Run](execution-api-transition.md).

<a id="completion-cancellation"></a>

## Скасування відповіді, яка вже не потрібна

Якщо користувач закрив екран, натиснув Стоп або минув час очікування застосунку, відповідь може вже не знадобитися. Передайте `CancellationToken`, щоб зупинити зв’язок і роботу клієнта та уникнути зайвих викликів інструментів і моделі. Для готової відповіді й надалі підходить `GetCompletionAsync`; лише для скасування Run не потрібен.

### Before: викликач не передає сигнал скасування

```csharp
string answer = await service.CreateRequest("Підсумуй цей документ.")
    .GetCompletionAsync();
```

### After: скасування користувачем або через 30 секунд

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Підсумуй цей документ.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Скасовано.");
}
```

Зберігайте джерело токена під час виклику й пов’яжіть Стоп або закриття екрана з `cancellation.Cancel()`. Приклад також планує скасування через 30 секунд. Після очищення викликач отримує `OperationCanceledException`. Строк через `CancellationTokenSource` також є скасуванням викликача; `FunctionCallingPolicy.TimeoutSeconds` зберігає попередню поведінку помилки тайм-ауту.

Токен приймають перевантаження сервісу з рядком і `Message`, типізована відповідь, builder та `MessageChain.SendAsync` / `SendOnceAsync`. Попередні виклики без токена залишаються доступними. Інші точки входу:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Підсумуй цей документ.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Поверни назву й автора в JSON.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Підсумуй цей документ.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Переклади це речення.")
    .SendOnceAsync(cancellationToken: token);
```

Токен надходить до підготовки, надсилання й читання HTTP, локальних інструментів із підтримкою скасування та наступних раундів. Після виявлення скасування інструменти в черзі та майбутні раунди пропускаються. Очищення зберігає пари записаних викликів і результатів; запущений інструмент, що ігнорує токен, може його затримати. Завершені дії та історія не скасовуються. Див. [контракт інструментів](function-calling.md#tool-execution-contract).

Зупинка обчислень чи оплати в провайдера не гарантується. OpenAI описує закриття з’єднання для звичайних Responses; Google прямо вказує на скасування лише клієнта з оплатою відповідного використання. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). Фонове завдання потребує явного `CancelAsync()`; скасування `WaitForCompletionAsync(cancellationToken: ...)` зупиняє лише очікування. Звичайний запит не стає фоновим. Див. [Perplexity](perplexity.md).

<a id="completion-cancellation-migration"></a>

Це доповнення входить до Mythosia.AI 8.0.0. Виклики без токена та попередні позиційні аргументи profile/context сумісні на рівні коду, але споживачів потрібно перебудувати. Власні реалізації `IAIService` мають додати `CancellationToken cancellationToken = default` останнім параметром обох сигнатур і передавати його далі. Провайдери на основі `AIService` зберігають override `GetCompletionAsync(Message)` і передають захищений `RequestCancellationToken` транспорту. Самі builder та Run не потребували цієї зміни інтерфейсу. Підкласи, що перевизначають змінені public virtual перевантаження для відповідей string/profile/context, методів зображень або `RunAgentAsync`, також мають додати й передавати новий `CancellationToken`; попередню сигнатуру зберігає лише override провайдера з одним `Message`. Делегати, що прямо посилаються на змінену сигнатуру, можуть потребувати явної лямбди з передаванням або пропуском токена.

## Одиночний запит

Найпростіший сценарій — надіслати повідомлення й отримати відповідь:

```csharp
var response = await service.GetCompletionAsync("Яка столиця Франції?");
Console.WriteLine(response); // Париж
```

## Системний промпт

Задайте моделі роль або інструкції через системний промпт:

```csharp
service.SystemMessage = "Ви — лаконічний асистент. Відповідайте одним реченням.";

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
service.ActivateChat.ClearMessages();
```

## Явна побудова повідомлень

За допомогою `MessageBuilder` можна створити повідомлення вручну:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Стисло перекажіть цей текст: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Мультимодальність (зображення)

Провайдери з підтримкою vision приймають зображення разом із текстом:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("Що зображено на цій діаграмі?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

Для аналізу графіків і знімків екрана, локальних функцій або поглибленої перевірки відповіді використовуйте [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). Міркування типово вимкнене; вмикайте через `WithDeepSeekReasoning(...)` або `WithReasoning(...)` для запиту.

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
    model: AIModels.OpenAI.Gpt4_1
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

Perplexity: [Відповідь із пресетом Agent / Джерела, зображення та структуровані відповіді](perplexity.md).
