# Генерация текста

Для независимых настроек и повторного использования вариантов применяйте [билдер запросов](request-building.md). Вызывайте `CreateRequest(...)` перед `With...`. Свойства и fluent-методы сервиса сохраняют прежнее поведение.

`GetCompletionAsync` остаётся удобным способом получить готовый ответ. Если нужно видеть прогресс или управлять ещё не завершённой задачей, используйте [Run](execution-api-transition.md).

<a id="completion-cancellation"></a>

## Отмена ответа, который больше не нужен

Если пользователь закрыл экран, нажал Стоп или истёк срок ожидания приложения, ответ может уже не понадобиться. Передайте `CancellationToken`, чтобы остановить связь и работу клиента и избежать лишних вызовов инструментов и модели. Для готового ответа по-прежнему подходит `GetCompletionAsync`; ради одной отмены Run не нужен.

### Before: вызывающая сторона не передаёт отмену

```csharp
string answer = await service.CreateRequest("Подведи итог документа.")
    .GetCompletionAsync();
```

### After: отмена пользователем или через 30 секунд

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Подведи итог документа.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Отменено.");
}
```

Храните источник токена во время вызова и свяжите Стоп или закрытие экрана с `cancellation.Cancel()`. Пример также планирует отмену через 30 секунд. После очистки вызывающая сторона получает `OperationCanceledException`. Срок через `CancellationTokenSource` тоже считается отменой вызывающей стороны; `FunctionCallingPolicy.TimeoutSeconds` сохраняет прежнее поведение ошибки тайм-аута.

Токен принимают перегрузки сервиса со строкой и `Message`, типизированный ответ, builder и `MessageChain.SendAsync` / `SendOnceAsync`. Старые вызовы без токена остаются допустимыми. Другие точки входа:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Подведи итог документа.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Верни название и автора в JSON.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Подведи итог документа.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Переведи это предложение.")
    .SendOnceAsync(cancellationToken: token);
```

Токен передаётся подготовке, отправке и чтению HTTP, локальным инструментам с поддержкой отмены и следующим раундам. После обнаружения отмены ожидающие инструменты и будущие раунды пропускаются. Очистка сохраняет соответствие записанных вызовов инструментов и результатов; уже запущенный инструмент, игнорирующий токен, может её задержать. Завершённые действия и история не откатываются. См. [контракт инструментов](function-calling.md#tool-execution-contract).

Остановка вычислений или оплаты у провайдера не гарантируется. OpenAI описывает закрытие соединения для обычных Responses; Google явно указывает отмену только на клиенте с оплатой соответствующего использования. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). Фоновая задача требует явного `CancelAsync()`; отмена `WaitForCompletionAsync(cancellationToken: ...)` прекращает только ожидание. Обычный запрос не превращается в фоновый. См. [Perplexity](perplexity.md).

<a id="completion-cancellation-migration"></a>

Это дополнение входит в Mythosia.AI 8.0.0. Вызовы без токена и прежние позиционные аргументы profile/context совместимы на уровне исходного кода, но потребителей нужно пересобрать. Собственные реализации `IAIService` должны добавить `CancellationToken cancellationToken = default` последним параметром обеих сигнатур и передавать его дальше. Провайдеры на основе `AIService` сохраняют override `GetCompletionAsync(Message)` и передают защищённый `RequestCancellationToken` транспорту. Сами builder и Run не требовали этого изменения интерфейса. Подклассы, переопределяющие изменённые public virtual перегрузки для ответов string/profile/context, методов изображений или `RunAgentAsync`, также должны добавить и передавать новый `CancellationToken`; прежнюю сигнатуру сохраняет только override провайдера с единственным `Message`. Делегаты, напрямую ссылающиеся на изменённую сигнатуру, могут потребовать явной лямбды с передачей или пропуском токена.

## Одиночный запрос

Самый простой сценарий — отправить сообщение и получить ответ:

```csharp
var response = await service.GetCompletionAsync("Какая столица Франции?");
Console.WriteLine(response); // Париж
```

## Системный промпт

Задайте модели роль или инструкции через системный промпт:

```csharp
service.SystemMessage = "Вы — лаконичный ассистент. Отвечайте одним предложением.";

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
service.ActivateChat.ClearMessages();
```

## Явное построение сообщений

С помощью `MessageBuilder` можно создать сообщение вручную:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Кратко изложите этот текст: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Мультимодальность (изображения)

Провайдеры с поддержкой vision принимают изображения наряду с текстом:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("Что показано на этой диаграмме?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

Для анализа графиков и снимков экрана, локальных функций или углублённой проверки ответа используйте [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). Рассуждение по умолчанию выключено; включайте его через `WithDeepSeekReasoning(...)` или `WithReasoning(...)` на запрос.

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
    model: AIModels.OpenAI.Gpt4_1
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

Perplexity: [Ответ с пресетом Agent / Источники, изображения и структурированные ответы](perplexity.md).
