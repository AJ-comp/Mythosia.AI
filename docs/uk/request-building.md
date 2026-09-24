# Незалежні налаштування кожного запиту

> Grok 4.7 — ще не опубліковане доповнення; див. [вибір моделі, міркування та швидкість обробки](providers.md#grok-47).

Для стислого викладу може бути потрібна низька температура, а для творчої чернетки — висока. Підготовка чернетки не повинна змінювати вже підготовлений запит викладу. Використовуйте `CreateRequest` для різних налаштувань викликів і створення варіантів спільного запиту.

Для відповіді, витрат і джерел разом використовуйте знімок `AIRunResult`, який повертає `await run.Result`. Рядок міститься в `result.Text`; читати потік не потрібно. Це зміна Mythosia.AI 8.0.0; типи повернення `GetCompletionAsync` і `StructuredStreamRun<T>.Result` збережено. [Результат Run і міграція](execution-api-transition.md#run-result).

Для готової відповіді й кнопки Стоп передайте `cancellationToken` у `GetCompletionAsync`. Run потрібен для подій прогресу або підтримуваних додаткових вказівок. Див. [скасування відповіді](completions.md#completion-cancellation).

> Приклади з `CreateRequest` потребують Mythosia.AI 8.0.0 / Abstractions 4.0.0. У попередній версії 7.1, що додала Run і спільні параметри, білдера немає. Старі пакети можуть використовувати попередні перевантаження сервісу.

## Before: спільний екземпляр сервісу

Наявний `WithTemperature` сервісу змінює його налаштування і повертає той самий екземпляр. Обидві змінні нижче посилаються на нього, тому останнє значення застосовується до обох. Ці методи залишаються доступними для налаштування типових значень сервісу.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Поясни цей документ."); // 0.8
```

## After: незалежні варіанти запиту

`CreateRequest` зберігає типові значення. Кожен `With...` білдера повертає новий білдер без зміни початкового. Виконання використовує налаштування запиту без тимчасового перезапису налаштувань сервісу.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Поясни цей документ.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Використовується 0.2; creative і налаштування сервісу не змінюються.
```

Використовуйте повернений білдер. Якщо відкинути результат `basis.WithTemperature(0.2f);`, об’єкт `basis` залишиться незмінним.

Білдер перевіряє значення, а не виправляє їх мовчки: температура 0–2, TopP 0–1, штрафи −2–2; NaN і нескінченність заборонені. Ліміти токенів, раундів, паралельності й заданий тайм-аут мають бути додатними. Хибні значення викликають `ArgumentException` / `ArgumentOutOfRangeException`. Попередній helper температури сервісу й далі обмежує діапазон.

## Ролі об’єктів

`AIService` зберігає з’єднання з провайдером, типові значення та стан розмови. Публічний `Mythosia.AI.Builders.AIRequestBuilder` надає fluent API. Внутрішній `AIRequest` передає зафіксовані вхідні дані й налаштування на виконання. Викликати `Build()` не потрібно: `GetCompletionAsync()` повертає `Task<string>`, а `StartRunAsync()` — `Task<AIRun>`. Відповіддю не є `AIRequest`.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Запуск Run із тими самими налаштуваннями

Використовуйте `GetCompletionAsync()` для готової відповіді, а `StartRunAsync()` — для прогресу й додаткових інструкцій під час підтримуваного виконання. Текст запиту передається в `CreateRequest`, а не повторно в метод виконання. `run.StreamAsync()` спостерігає цей Run; умови підтримки `run.SteerAsync(...)` не змінюються.

```csharp
await using var run = await service
    .CreateRequest("Поясни цей документ.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Локальні інструменти можуть повертати об’єкти через `Task<T>` / `ValueTask<T>` та отримувати впроваджений `CancellationToken`. `run.Cancel()` або початковий токен передає скасування інструментам, що його підтримують; зупинка лише читання потоку — ні. Винятки записуються як помилки. Скасування пропускає виклики в черзі, а очищення очікує запущені інструменти, які ігнорують токен. Див. [результати, помилки та скасування](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Повторне використання профілів і контексту

`WithProfile` копіює `AIRequestProfile`, а `WithContext` — `AIRequestContext`. Подальша зміна початкових об’єктів не впливає на підготовлений запит. Білдер налаштовує семплювання, системні інструкції, режим без стану, політику функцій і підтримувані міркування та пошук. Перевірки можливостей провайдера залишаються.

`WithFunctions(params FunctionDefinition[])` додає копії визначень. `WithFunctions(toolInstance)` та `WithStaticFunctions<T>()` з `Mythosia.AI.Extensions` підтримують наявні функції з атрибутами. Реєстрація до `CreateRequest` задає налаштування сервісу, після — запиту. `CreateRequest` забирає та споживає очікувані параметри наступного виклику; для повторного використання збережіть білдер.

```csharp
var request = service
    .CreateRequest("Переформулюй це питання для пошуку.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nЗбережи початковий зміст."
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## Скопійовані налаштування і спільний стан

Спільні налаштування й типові значення провайдера фіксуються під час `CreateRequest`. Подальші зміни сервісу не змінюють запит. Вбудований вміст повідомлень, підтримувані колекції опцій, профілі, контексти й політики копіюються. Обробники функцій, динамічні callbacks контексту й власний вміст повідомлень зберігають посилання. Не змінюйте власний вміст; делегати можуть читати зовнішній стан. Динамічний контекст обчислюється під час виконання.

Після захоплення можна звільнити початковий `JsonDocument` або змінити початкові значення `JsonNode`: JSON у метаданих запиту й аргументах викликів функцій залишиться незмінним, а кожен запуск отримає окрему копію. Циклічний ланцюжок `Items` у схемі інструмента або вкладеність понад 64 рівні спричиняє `ArgumentException` під час захоплення (`CreateRequest` або `WithFunctions`). Неправильна схема відхиляється до виконання зі звичайною помилкою, а не виснажує стек процесу.

Копіювання також зберігає кількість вимірів і початкові індекси масивів, а також правила порівняння ключів стандартних контейнерів `Dictionary<,>`, `SortedDictionary<,>` і `SortedList<,>`. Тому пошук ключа без урахування регістру залишається таким самим усередині запиту. Порожнє значення `default(JsonElement)` (`Undefined`) зберігається без змін. Невідомі користувацькі об’єкти метаданих зберігають посилання; їхній власник має уникати змін або узгоджувати доступ.

Стандартні значення `ReadOnlyCollection<T>` і `ReadOnlyDictionary<TKey, TValue>` зберігають свій тип у типізованих масивах і словниках. Підтримувані базові колекції копіюються зі збереженням представлень лише для читання, спільних посилань і циклів. `Hashtable` і неузагальнений `SortedList` також зберігають правила порівняння ключів.

Білдер не створює окрему розмову. Він використовує активну розмову сервісу на момент виконання й не фіксує історію під час створення. Виклики зі станом оновлюють спільну історію. `WithStatelessMode()` вимикає читання та накопичення історії. Обмеження одного активного Run на сервіс залишається. Незалежність налаштувань не гарантує паралельних викликів одного сервісу; для незалежних одночасних розмов використовуйте окремі сервіси.

## Наявні виклики й розширення

`GetCompletionAsync` і наявні точки входу зберігаються. `BeginMessage()` / `MessageChain` залишають змінюване створення повідомлень, а виконання використовує новий шлях запитів. Для повторного використання варіантів обирайте `CreateRequest`. API належить `AIService` та його реалізаціям; обов’язкові члени в `IAIService` не додаються. Клієнти абстракції та RAG-обгортки зберігають наявні API профілю, контексту й виконання.

[Створювати налаштування моделі за спільними визначеннями можливостей](model-capabilities.md).

<a id="inference-speed"></a>

## Обрати швидкість обробки відповідно до завдання

Для користувача, який чекає відповіді на екрані, може бути виправдана платна обробка з малою затримкою; фоновий звіт може виконуватися звичайно. `WithSpeed` вибирає режим, зберігаючи модель і рівень міркувань. Це неопублікована функція з відповідними змінами core й abstractions; у випущених 8.0.0 / 4.0.0 її немає.

`ProviderDefault` нічого не перевизначає та зберігає налаштування сервісу/провайдера; проєкт уже може мати Fast за замовчуванням. `Standard` явно запитує звичайну обробку. `Fast` вибирає платний режим малої затримки й може збільшити вартість. Зберігайте повернений builder: три гілки незалежні, початковий запит не змінюється.

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

Перед показом параметра перевірте `GetSpeedSupport(InferenceSpeed.Fast)`. `StandardSpeed` і `FastSpeed` також розрізняють Supported, Unsupported та Unknown. Локальний Supported не перевіряє права акаунта, потужність чи затримку. Непідтримувані або невідомі явні Standard/Fast завершуються помилкою без прихованої зміни моделі чи зусилля. `ProviderDefault` зберігає попередній шлях.

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

`AIRunResult.Processing` зберігає незмінні `AIProcessingInfo` навіть без читання потоку. `RequestIndex` починається з 1 і нумерує спроби інференсу провайдера, включно із серверними продовженнями, не раунди інструментів чи HTTP-запити; продовження, повтори та виправлення формату можуть додавати записи. Якщо сервер не повідомив розпізнаний режим, зокрема для невдалих спроб, `AppliedSpeed` дорівнює null. `RawAppliedMode` і `ResponseId` зберігають отримані значення. `IsDowngraded` істинний лише для запиту Fast з явним звітом Standard; false не підтверджує Fast.

Після звичайного completion одразу читайте `AIService.LastProcessing`; наступний логічний запит замінює це подання. Отримані записи залишаються незмінними. Розширення сервісу діє на наступний логічний запит та його інструменти, а не змінює постійне значення. Допоміжні підсумки, внутрішнє переписування запитів і внутрішні профілі не успадковують швидкість головного запиту та не змішують спостереження з ним.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

Це режим обробки провайдера, не виміряні токени за секунду. OpenAI, xAI та Google можуть знизити режим на сервері; Mythosia не повторює автоматично з іншою швидкістю. Anthropic fast mode потребує доступу до прямої Claude API; зміна швидкості може скинути кеш промпта. Gemini Developer API priority потребує Tier 2/3. Окремо перевіряйте модель, API, доступ і ціни. Параметр не стосується створення зображень, ембедингів та нативних Batch API. [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

Для посилання `IAIService` використовуйте `GetLastProcessing()` із `Mythosia.AI.Extensions`. Метод читає необов’язковий `IAIProcessingInfoService` і повертає порожній список, якщо діагностика недоступна. Обов’язкових членів до `IAIService` не додається. У RAG `RagEnabledService.WithSpeed(...)` налаштовує наступну відповідь після пошуку, а `LastProcessing` описує її. Внутрішнє переписування відокремлено; Run надає ті самі записи `Processing`.

Нижче перелічено явно підтримувані Fast-моделі. Standard перевіряйте окремо через `GetSpeedSupport(InferenceSpeed.Standard)`. Неперелічені моделі, сторонні адреси й OpenAI-сумісні провайдери не отримують платні режими автоматично.

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
| Anthropic — інші відомі моделі Claude, включно із Sonnet 5 | `speed` і beta fast-mode не надсилаються | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

Для цих інших моделей Claude режим Standard використовує попередній звичайний запит. Якщо сервер не повідомив режим обробки, `AppliedSpeed` залишається null; Standard не визначається лише зі значення запиту.
