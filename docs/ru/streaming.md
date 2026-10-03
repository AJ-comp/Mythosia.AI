# Потоковая передача

Адаптер потоковой выдачи через callback отменяет и дожидается производителя при раннем выходе. Отмена Run, тайм-аут, ошибка наблюдателя и `DisposeAsync` ожидают очистки до завершения `Result` и освобождения блокировки. Работа, игнорирующая отмену, может задержать завершение; обе ошибки наблюдения и очистки сохраняются. `ContextRecoveryMaxRetries` берётся из снимка запроса. Прекращение только наблюдения `run.StreamAsync()` не отменяет Run. При получении тела успешного SSE-ответа действует отдельное [ограничение отмены](#sse-acquisition-cancellation-limitation).

> Claude Sonnet 5.5: Требуются Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Настройка и миграция](providers.md#claude-sonnet-55)

В adaptive используйте `ClaudeThinkingDisplay.Updates` для читаемого прогресса инструментов или `Summarized` для сводки рассуждений. Читайте `StreamingContentType.Reasoning`, а после обычного завершения — `LastThinkingContent`. Метод adaptive без аргумента display использует `Summarized`, в отличие от неизменённых настроек. `between_tools` возвращает прогресс автоматически; фиксированный интервал не гарантирован.

> Grok 4.7: Требуются Mythosia.AI 8.1.0 / Abstractions 4.1.0. [выбор модели, рассуждение и скорость обработки](providers.md#grok-47)

Чтобы получить ответ, расход и источники вместе, используйте снимок `AIRunResult`, возвращаемый `await run.Result`. Строка находится в `result.Text`; читать поток не требуется. Изменение входит в Mythosia.AI 8.0.0. Типы возврата `GetCompletionAsync` и `StructuredStreamRun<T>.Result` сохраняются. [Результат Run и миграция](execution-api-transition.md#run-result).


Для независимых настроек и повторного использования вариантов применяйте [билдер запросов](request-building.md). Вызывайте `CreateRequest(...)` перед `With...`. Свойства и fluent-методы сервиса сохраняют прежнее поведение.

Показывайте текст по мере поступления, чтобы пользователь видел ход подготовки ответа. Как добавить события инструментов и кнопку остановки, описано в [руководстве по Run](execution-api-transition.md).

```csharp
await using var run = await service.StartRunAsync(
    "Подведите итоги по документам.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

**Ответы Claude с ошибкой:** Отмена и тайм-аут политики запроса прерывают и зависшее чтение тела HTTP-ошибки при потоковой передаче или Run. После очистки Run можно запустить следующий Run на том же сервисе. Отмена вызывающей стороной приводит к `OperationCanceledException`, тайм-аут политики — к `AIServiceException`. Отмена управляет локальным транспортом и кооперативной очисткой, но не гарантирует прекращения обработки или начисления платы у провайдера.

**Очистка ответов Claude:** Потоковая передача Claude и Run ожидают асинхронной очистки уже полученного тела HTTP-ответа, в том числе для пользовательских потоков, требующих асинхронного освобождения ресурсов. Последующее исключение при освобождении ответа или его содержимого не заменяет успешное завершение, исходную ошибку чтения или отмену; попытка освободить исходный ответ и его содержимое всё равно выполняется.

**Тайм-ауты HTTP:** При потоковой передаче текста, содержимого или через callback, а также в Run, использующих общий путь раундов потоковой передачи, распознаваемый `HttpClient.Timeout` (`TaskCanceledException` с внутренним `TimeoutException`) преобразуется в `AIServiceException`, если не сработали отмена вызывающей стороной и тайм-аут политики запроса. `InnerException` сохраняет исходное транспортное исключение, поэтому `run.Result` завершается с ошибкой, сохраняя причину тайм-аута. Поведение отмены вызывающей стороной, тайм-аутов политики и других транспортных отмен не меняется.

<a id="sse-acquisition-cancellation-limitation"></a>

## Известное ограничение: получение тела успешного SSE-ответа

При HTTP 200 SSE буферизующая обёртка `HttpContent` в пользовательском обработчике может заблокировать `ReadAsStreamAsync` до получения потока тела и начала очистки. Отмена вызывающей стороной и тайм-аут политики запроса могут оставить `run.Result` незавершённым, ответ неосвобождённым, а блокировку активного Run удерживаемой до окончания получения потока; следующий Run отклоняется как уже активный. Эта проблема пока не исправлена и отличается от медленной очистки. Стандартный `SocketsHttpHandler` прошёл проверенные сценарии; отмена чтения тела HTTP-ошибки также сработала с этой обёрткой. Используйте обычное потоковое содержимое без буферизующей обёртки. Для нативного веб-поиска Claude действует отдельное [ограничение продолжения](providers.md#claude-native-continuation-limitation).

## Примеры совместимости со старым API

Входные StreamAsync сервиса/RAG остаются публичными в v8. Для нового управления выполнением используйте StartRunAsync; run.StreamAsync() лишь наблюдает существующий run.

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
await foreach (var content in service.StreamAsync("Объясните квантовые вычисления", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Стриминг рассуждений

OpenAI, Claude, Gemini, Grok и DeepSeek Flash передают рассуждение провайдера по одной схеме стриминга. Включите рассуждение в сервисе или запросе, затем наблюдайте через `StreamOptions.WithReasoning()`:

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

Gemini 3.7/3.8 Flash используют существующие события стриминга и Run. `StreamingContentType.Reasoning` содержит предоставленные провайдером сводки или сообщения о ходе работы, если они возвращены; полного внутреннего рассуждения это не гарантирует. `StreamOptions.WithReasoning()` выбирает этот вывод, а `WithReasoning(ReasoningLevel...)` у сервиса управляет усилием.

Grok 4.6 также может передавать необязательные сводки рассуждения через эти события. Параметр потока выбирает видимый вывод, а `WithReasoning(ReasoningLevel...)` — усилие для одной задачи. Отсутствие сводок не означает отключение рассуждения. См. [настройку Grok](providers.md#xai-xaiservice).

DeepSeek Flash передаёт `reasoning_content` через те же события после включения рассуждения. `StreamOptions.WithReasoning()` управляет наблюдением; `WithDeepSeekReasoning(...)` или `WithReasoning(...)` сервиса — рассуждением. См. [DeepSeek](providers.md#deepseek-deepseekservice).

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
await foreach (var content in service.StreamAsync("Объясните квантовые вычисления", StreamOptions.Default))
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

Считайте показанные фрагменты предварительными, пока `run.Result` не завершится успешно. Общий путь потоковой обработки, совместимый с OpenAI, и путь потоковой обработки DeepSeek отклоняют новый текст, рассуждения или данные инструментов после явного завершения, а также изменение причины завершения: `run.Result` выбрасывает исключение, неудачный раунд не сохраняется в истории, а его инструменты не выполняются. Такая обработка ошибки не отменяет предыдущие раунды или действия, уже выполненные во внешних системах. Последняя дельта может прийти в первом событии завершения; последующее событие только со статистикой использования также допускается.

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

await foreach (var chunk in service.StreamAsync("Продолжим наш разговор...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [Продолжение длительной задачи / Цитаты могут указывать на веб-результаты или другие источники провайдера. Позиции относятся к отдельному ответу и части содержимого, не к объединённому результату Run. Сохраняйте URL и заголовок для показа и проверки; наличие источника само по себе не подтверждает каждое утверждение.](perplexity.md).
