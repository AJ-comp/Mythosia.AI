# Независимые настройки каждого запроса

> Claude Sonnet 5.5: Требуются Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Настройка и миграция](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: Нужны Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Выбор модели и миграция](providers.md#gpt-61-sol)

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

Claude совместно определяет модель, назначение запроса, рассуждение и привязку thinking. Для вспомогательных профилей, например `RequestProfiles.Summarization` или `RequestProfiles.QueryRewrite`, унаследованная привязка исключается только при `DisableReasoning = true`, назначении, отличном от `Default`, и фактическом выполнении без состояния. Это не даёт унаследованной политике повторно включить рассуждение или отклонить запрос, у которого нет префикса диалога для сохранения. Модели с отключаемым thinking отключают его; Opus 5.5, Fable 5.1 и Mythos 5.1 с обязательным рассуждением используют `Low` без читаемого thinking. Sonnet 5.5 использует `between_tools` с высоким уровнем усилий.

Completion, потоковая и структурированная выдача и Run используют одну подготовку запроса: сохраняют настройки, один раз выполняют фактическую обработку профиля, затем проверяют полученные общие и нативные параметры провайдера. Это происходит до автоматического резюмирования, добавления нового ввода в историю и открытия соединения. Переопределения профиля в пользовательском провайдере участвуют в реально проверяемых настройках. Claude также отклоняет на этом этапе используемый ручной `ThinkingBudget`, достигающий или превышающий лимит вывода модели; допустимые профили и общие настройки рассуждения сохраняют приоритет.

Вызовы приложения начинают независимые логические запросы, в том числе обычные вызовы из `SystemMessageProvider` или callback-функций инструментов и вызовы с повторным использованием того же `AIRequestProfile` либо `Message`. Повторное использование объекта не объединяет выполнение. Делегирование со стороны фреймворка, этапы инструментов, повторы и исправления формата продолжают исходный запрос, применяя его профиль один раз. Обычный дочерний запрос получает собственные опции и настройки сервиса по умолчанию; builder сохраняет ранее зафиксированные настройки. После успеха, ошибки или отмены выполнение родительского запроса восстанавливается. Переопределённые методы провайдера, передающие вызов фреймворка дальше, следуют [правилам адаптеров ниже](#provider-request-adapters).

Встроенные провайдеры сохраняют собственную копию встроенного содержимого входного сообщения. Повторное использование `Message` применяет контекст и инструкции нового вызова, не переписывая принятую историю. Пользовательское содержимое и неподдерживаемые объекты метаданных остаются ответственностью владельца. Это не обеспечивает безопасные параллельные вызовы для одного диалога.

После возврата из `StartRunAsync` Run сохраняет собственную копию итоговых настроек. Восстановление профиля вызывающего кода не меняет активный Run; хуки выполнения профиля по-прежнему вызываются только один раз.

Вспомогательные запросы без состояния используют отдельный диалог и не наследуют схему вывода, размещённые у провайдера инструменты или одноразовые параметры родительского запроса. Изоляция никогда не отменяет проверку нативных параметров, в том числе для Run OpenAI и Perplexity. Настройки, сообщения, `CurrentSummary` и данные наблюдения родительского запроса сохраняются. Запросы с состоянием сохраняют проверки привязки и диалога. Существующие публичные API не меняются.

Внутренние автоматические запросы на резюмирование диалога также исключают callback `SystemMessageProvider` и контекст родительского запроса, чтобы унаследованный `RequestMessageOverride` не заменил инструкцию резюмирования. Запросы приложения по-прежнему используют динамический контекст, включая явные просьбы кратко изложить текст.

Запросы без состояния также пропускают автоматическое суммирование родительского диалога, в том числе при вызове существующей перегрузки `GetCompletionAsync(string, profile)`, как и перегрузка `Message` и построитель запросов. `CurrentSummary` и сообщения родительского диалога не меняются. Запросы с состоянием сохраняют обычное автоматическое суммирование.

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## Передача запросов в пользовательском провайдере

Когда фреймворк вызывает виртуальный адаптер провайдера, его первый вызов соответствующей точки входа базового класса продолжает подготовленный запрос, даже если переопределённый метод заменяет входной `Message`. Зафиксированные настройки builder и применённые профили сохраняются при такой передаче. Стандартный адаптер потоковой выдачи через callback также продолжает тот же подготовленный запрос.

Адаптер может передать изменённый `AIRequestProfile`: одинаковые значения повторно не применяются, а изменения заменяют прежний слой профиля на основе сохранённых настроек. Повторная проверка выполняется до автоматического резюме и отправки, в том числе при переходе в режим без состояния. Вспомогательные запросы OpenAI без состояния не проверяют чужую сохранённую историю и не сбрасывают защиту родительского диалога.

Замена переданного профиля сохраняет последующие локальные добавления, удаления и правки инструментов, изменения политики и явные присваивания настроек, даже если скалярное значение не изменилось. Изменение другого поля профиля не возвращает инструмент, удалённый адаптером. Значения сервиса по умолчанию не считываются повторно.

Для непрозрачных пользовательских объектов настроек адаптер должен заменять значение через `SetExecutionSetting`, а не менять внутренние поля. Библиотека не исследует произвольные объекты приложения и не вызывает их сериализаторы для отслеживания изменений профиля.

Сжатие Claude сохраняет зависимости вызов/результат в `RequestMessageOverride` и `AdditionalMessages`, включая серверные инструменты, а также связанный префикс thinking Mythos 5.1. Старые записи параллельных инструментов группируются один раз и в истории, и в дополнительных сообщениях; принадлежность каждой записи сохраняется.

Независимый вспомогательный вызов той же точки входа базового класса перед передачей запроса неоднозначен: фреймворк не может определить, является ли он продолжением. Оберните этот вспомогательный вызов вместе с его `await` в область, создаваемую защищённым методом `BeginIndependentRequestScope()`; при потоковой выдаче сохраняйте область открытой на протяжении всего перечисления. Вспомогательный вызов начинает работу с настройками сервиса по умолчанию, а освобождение области восстанавливает внешние настройки, функции, контекст и ожидающее делегирование. Обычные вложенные вызовы из callback-функций контекста или инструментов уже независимы и не требуют этой области.

Например, подкласс конкретного провайдера может переформулировать текст перед его передачей:

```csharp
public override async Task<string> GetCompletionAsync(
    Message message, AIRequestProfile? profile = null,
    AIRequestContext? context = null, CancellationToken cancellationToken = default)
{
    string rewritten;
    using (BeginIndependentRequestScope())
    {
        rewritten = await base.GetCompletionAsync(
            new Message(ActorRole.User, message.Content),
            RequestProfiles.QueryRewrite,
            cancellationToken: cancellationToken);
    }

    var replacement = new Message(message.Role, rewritten);
    return await base.GetCompletionAsync(replacement, profile, context, cancellationToken);
}
```

Эта область разделяет состояние выполнения запросов; она не изолирует историю диалога и не разрешает одновременное использование сервиса. В примере используется профиль без состояния `QueryRewrite`, чтобы вспомогательный вызов не затрагивал родительский диалог.

При сжатии Claude проверяет сохранённую каноническую историю в формате передачи, включая подписанные блоки thinking, добавленные через `AIRequestContext.AdditionalMessages`. Привязка thinking по умолчанию защищает этот префикс от автоматического или явно запрошенного сжатия посредством резюмирования. Явная настройка `ClaudeThinkingPrefixMismatchBehavior.DropBlock` разрешает сжатие там, где оно поддерживается; остальные ограничения диалога продолжают действовать.

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
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
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
