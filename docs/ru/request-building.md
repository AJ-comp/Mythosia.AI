# Независимые настройки каждого запроса

> Grok 4.7: Требуются Mythosia.AI 8.1.0 / Abstractions 4.1.0. [выбор модели, рассуждение и скорость обработки](providers.md#grok-47)

Для краткого пересказа может требоваться низкая температура, а для творческого черновика — высокая. Подготовка черновика не должна менять уже подготовленный запрос пересказа. Используйте `CreateRequest` для разных настроек вызовов и создания вариантов общего запроса.

Чтобы получить ответ, расход и источники вместе, используйте снимок `AIRunResult`, возвращаемый `await run.Result`. Строка находится в `result.Text`; читать поток не требуется. Изменение входит в Mythosia.AI 8.0.0. Типы возврата `GetCompletionAsync` и `StructuredStreamRun<T>.Result` сохраняются. [Результат Run и миграция](execution-api-transition.md#run-result).

Для готового ответа и кнопки Стоп передайте `cancellationToken` в `GetCompletionAsync`. Run нужен для событий прогресса или поддерживаемых дополнительных указаний. См. [отмену ответа](completions.md#completion-cancellation).

> Примеры с `CreateRequest` требуют Mythosia.AI 8.0.0 / Abstractions 4.0.0. В прежней версии 7.1, добавившей Run и общие параметры, билдера нет. Старые пакеты могут использовать прежние перегрузки сервиса.

## Before: общий экземпляр сервиса

Существующий `WithTemperature` сервиса меняет его настройки и возвращает тот же экземпляр. Обе переменные ниже ссылаются на него, поэтому последнее значение применяется к обеим. Эти методы остаются доступны для настройки значений по умолчанию.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Объясни этот документ."); // 0.8
```

## After: независимые варианты запроса

`CreateRequest` сохраняет значения по умолчанию. Каждый `With...` билдера возвращает новый билдер без изменения исходного. Выполнение использует настройки запроса напрямую, не перезаписывая временно настройки сервиса.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Объясни этот документ.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Используется 0.2; creative и настройки сервиса не меняются.
```

Используйте возвращённый билдер. Если отбросить результат `basis.WithTemperature(0.2f);`, объект `basis` не изменится.

Билдер проверяет значения, а не исправляет их молча: температура 0–2, TopP 0–1, штрафы −2–2; NaN и бесконечность запрещены. Лимиты токенов, раундов, параллелизма и заданный тайм-аут должны быть положительными. Ошибочные значения вызывают `ArgumentException` / `ArgumentOutOfRangeException`. Прежний helper температуры сервиса продолжает ограничивать диапазон.

## Роли объектов

`AIService` хранит подключение к провайдеру, значения по умолчанию и состояние диалога. Публичный `Mythosia.AI.Builders.AIRequestBuilder` предоставляет fluent API. Внутренний `AIRequest` передаёт зафиксированные входные данные и настройки на выполнение. Вызывать `Build()` не нужно: `GetCompletionAsync()` возвращает `Task<string>`, а `StartRunAsync()` — `Task<AIRun>`. Ответом не является `AIRequest`.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Запуск Run с теми же настройками

Используйте `GetCompletionAsync()` для готового ответа и `StartRunAsync()` для прогресса и дополнительных инструкций во время поддерживаемого выполнения. Текст запроса передаётся в `CreateRequest`, а не повторно в метод выполнения. `run.StreamAsync()` наблюдает этот Run; ограничения поддержки `run.SteerAsync(...)` сохраняются.

```csharp
await using var run = await service
    .CreateRequest("Объясни этот документ.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Локальные инструменты могут возвращать объекты через `Task<T>` / `ValueTask<T>` и получать внедрённый `CancellationToken`. `run.Cancel()` или исходный токен передаёт отмену поддерживающим её инструментам; остановка только чтения потока — нет. Исключения записываются как ошибки. При отмене ожидающие вызовы пропускаются, а очистка ждёт начатые инструменты, игнорирующие токен. См. [результаты, ошибки и отмена](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Повторное использование профилей и контекста

`WithProfile` копирует `AIRequestProfile`, а `WithContext` — `AIRequestContext`. Изменение исходных объектов позже не влияет на подготовленный запрос. Билдер настраивает сэмплирование, системные инструкции, режим без состояния, политику функций и поддерживаемые рассуждения и поиск. Проверки возможностей провайдера сохраняются.

`WithFunctions(params FunctionDefinition[])` добавляет копии определений. `WithFunctions(toolInstance)` и `WithStaticFunctions<T>()` из `Mythosia.AI.Extensions` поддерживают существующие функции с атрибутами. Регистрация до `CreateRequest` задаёт настройки сервиса, после — запроса. `CreateRequest` забирает и расходует ожидающие параметры следующего вызова; для их повторного использования сохраните билдер.

```csharp
var request = service
    .CreateRequest("Переформулируй этот вопрос для поиска.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nСохрани исходный смысл."
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## Копируемые настройки и общее состояние

Общие настройки и значения провайдера фиксируются при `CreateRequest`; последующие изменения сервиса не меняют запрос. Встроенное содержимое сообщений, поддерживаемые коллекции настроек, профили, контексты и политики копируются. Обработчики функций, динамические callbacks контекста и пользовательское содержимое сообщений сохраняют ссылки. Не изменяйте пользовательское содержимое; делегаты могут читать внешнее состояние. Динамический контекст вычисляется во время выполнения.

После захвата можно освободить исходный `JsonDocument` или изменить исходные значения `JsonNode`: JSON в метаданных запроса и аргументах вызовов функций останется прежним, а каждый запуск получит отдельную копию. Циклическая цепочка `Items` в схеме инструмента или вложенность свыше 64 уровней вызывает `ArgumentException` при захвате (`CreateRequest` или `WithFunctions`). Неверная схема отклоняется до выполнения с обычной ошибкой, а не приводит к исчерпанию стека процесса.

Копирование также сохраняет размерность и начальные индексы массивов, а также правила сравнения ключей стандартных контейнеров `Dictionary<,>`, `SortedDictionary<,>` и `SortedList<,>`. Поэтому поиск ключа без учёта регистра остаётся таким же внутри запроса. Пустое значение `default(JsonElement)` (`Undefined`) сохраняется без изменений. Неизвестные пользовательские объекты метаданных сохраняют ссылки; их владелец должен не изменять их или согласовывать доступ.

Стандартные значения `ReadOnlyCollection<T>` и `ReadOnlyDictionary<TKey, TValue>` сохраняют свой тип внутри типизированных массивов и словарей. Поддерживаемые базовые коллекции копируются с сохранением представлений только для чтения, общих ссылок и циклов. `Hashtable` и необобщённый `SortedList` также сохраняют правила сравнения ключей.

Билдер не создаёт отдельный диалог. Он использует активный диалог сервиса на момент выполнения и не фиксирует историю при создании. Вызовы с состоянием обновляют общую историю. `WithStatelessMode()` отключает чтение и накопление истории. Ограничение одного активного Run на сервис сохраняется. Независимость настроек не гарантирует параллельные вызовы одного сервиса; для независимых одновременных диалогов используйте отдельные сервисы.

## Существующие вызовы и расширения

`GetCompletionAsync` и существующие точки входа сохраняются. `BeginMessage()` / `MessageChain` сохраняют изменяемое построение сообщений, а выполнение используют через новый путь запросов. Для повторного использования вариантов применяйте `CreateRequest`. API принадлежит `AIService` и его реализациям; обязательных членов в `IAIService` не добавляется. Клиенты абстракции и RAG-обёртки продолжают использовать существующие API профиля, контекста и выполнения.

[Формировать настройки модели по общим определениям возможностей](model-capabilities.md).

<a id="inference-speed"></a>

## Выбрать скорость обработки под задачу

Для пользователя, ожидающего ответ на экране, может быть оправдана платная обработка с низкой задержкой; фоновый отчёт может выполняться обычно. `WithSpeed` выбирает режим, сохраняя модель и уровень рассуждений. Требуются Mythosia.AI 8.1.0 / Abstractions 4.1.0.

`ProviderDefault` ничего не переопределяет и сохраняет настройки сервиса/провайдера; проект уже может использовать Fast по умолчанию. `Standard` явно запрашивает обычную обработку. `Fast` выбирает платный режим низкой задержки и может увеличить стоимость. Сохраняйте возвращённый builder: три ветви независимы, исходный запрос не меняется.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

Перед показом настройки проверьте `GetSpeedSupport(InferenceSpeed.Fast)`. `StandardSpeed` и `FastSpeed` тоже различают Supported, Unsupported и Unknown. Локальный Supported не проверяет права аккаунта, доступную мощность или задержку. Неподдерживаемые и неизвестные явные Standard/Fast завершаются ошибкой без незаметной смены модели или усилия. `ProviderDefault` сохраняет прежний путь.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` сохраняет неизменяемые `AIProcessingInfo` даже без чтения потока. `RequestIndex` начинается с 1 и нумерует попытки инференса провайдера, включая серверные продолжения, а не раунды инструментов или HTTP-запросы; продолжения, повторы и исправления формата могут добавлять записи. Если сервер не сообщил распознанный режим, включая неудачные попытки, `AppliedSpeed` равен null. `RawAppliedMode` и `ResponseId` сохраняют полученные значения. `IsDowngraded` истинен только при запросе Fast и явном ответе Standard; false не подтверждает Fast.

После обычного completion сразу читайте `AIService.LastProcessing`; следующий логический запрос заменяет это представление. Полученные записи остаются неизменяемыми. Расширение сервиса действует на следующий логический запрос с его инструментами, а не меняет постоянный стандарт. Вспомогательные сводки, внутреннее переписывание запросов и внутренние профили не наследуют скорость основного запроса и не смешивают с ним наблюдения.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

Это режим обработки провайдера, не измеренные токены в секунду. OpenAI, xAI и Google могут понизить режим на сервере; Mythosia не повторяет автоматически с другой скоростью. Anthropic fast mode требует доступа к прямой Claude API; смена скорости может сбросить кеш промпта. Gemini Developer API priority требует Tier 2/3. Отдельно проверяйте модель, API, доступ и цены. Настройка не относится к генерации изображений, эмбеддингам и нативным Batch API. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

Для ссылки `IAIService` используйте `GetLastProcessing()` из `Mythosia.AI.Extensions`. Метод читает необязательный `IAIProcessingInfoService` и возвращает пустой список, если диагностика недоступна. Обязательных членов в `IAIService` не добавляется. В RAG `RagEnabledService.WithSpeed(...)` настраивает следующий ответ после поиска, а `LastProcessing` описывает этот ответ. Внутреннее переписывание отделено; Run предоставляет те же записи `Processing`.

Ниже перечислены явно поддерживаемые Fast-модели. Standard проверяйте отдельно через `GetSpeedSupport(InferenceSpeed.Standard)`. Неперечисленные модели, сторонние адреса и OpenAI-совместимые провайдеры не получают платные режимы автоматически.

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — другие известные модели Claude, включая Sonnet 5 | `speed` и beta fast-mode не отправляются | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

Для этих остальных моделей Claude режим Standard использует прежний обычный запрос. Если сервер не сообщил режим обработки, `AppliedSpeed` остаётся null; Standard не выводится только из значения запроса.
