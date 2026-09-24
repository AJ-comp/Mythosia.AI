# Показувати функції, які підтримує вибрана модель

> Grok 4.7: Потрібні Mythosia.AI 8.1.0 / Abstractions 4.1.0. [вибір моделі, міркування та швидкість обробки](providers.md#grok-47)

> GPT-6 Sol/Luna: Потрібні Mythosia.AI 8.1.0 / Abstractions 4.1.0. [вибір моделі та вимоги](providers.md#gpt-6-sol-luna)

Для [Claude Opus 5.5](providers.md#claude-opus-55) capabilities надають `Low`–`Max`, зокрема `XHigh`; `None`, `Minimal` і `ThinkingToggle` не підтримуються. `MaxOutputTokens` дорівнює 128000. Приховування тексту не вимикає мислення. Потрібні Mythosia.AI 8.1.0 / Abstractions 4.1.0.

Інтерфейс чату має пропонувати міркування, пошук, інструменти й зображення відповідно до підключення. Списки моделей у кожному застосунку дублюють правила бібліотеки та розходяться зі зміною провайдера, протоколу чи розгортання. Знімки можливостей надають інтерфейсу й перевіркам виконання спільні визначення моделей.

API належить Mythosia.AI 8.0.0. Знімки — незмінні локальні описи відомої підтримки, а не перевірка облікового запису чи сервера наживо. Типи містяться в `Mythosia.AI.Models.Capabilities`.

Для запитів із важливим часом очікування вибирайте [швидкість обробки](request-building.md#inference-speed). `WithSpeed` зберігає модель і зусилля; `Processing` показує застосований режим. Fast є платною опцією для підтримуваних поєднань.

## Before / After

Before: застосунок підтримує власні списки. Нижче наведено код застосунку, а не API бібліотеки.

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After: перевірте налаштований запит і виберіть підтримувані параметри. Модель викликається лише останнім completion; перегляд можливостей не викликає API.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Поясни документи.");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` розрізняє `Supported`, `Unsupported` і `Unknown`. Для власного розгортання чи серверного вибору може бракувати даних: `Unknown` не означає відсутність підтримки. Приклад вмикає додаткові міркування лише за відомої підтримки. Інакше застосунок вирішує зберегти типові параметри або дозволити спробу запиту.

`request.GetCapabilities()` читає модель, параметри провайдера й профіль, зафіксовані будівником. `service.GetCapabilities()` перевіряє типові налаштування сервісу, не споживаючи параметри наступного виклику. Вони не надсилають HTTP, не викликають callbacks контексту чи перевірки виконання, не змінюють історію та не запускають роботу. Списки доступні лише для читання. Запит до сервісу також переглядає налаштування функцій, що очікують наступного виклику, зберігаючи їх для фактичного запиту. Перевірка не серіалізує типові значення параметрів функцій або параметри розміщених інструментів і не виконує підготовку профілю до запуску чи резервування бюджету токенів.

Можливості описують підтримку підключення, а не ввімкнені налаштування. Важливі провайдер, API-протокол і режим, не лише ім’я моделі. Ідентифікатор враховує перевизначення та перетворення Qwen/Ollama; без єдиної вибраної моделі він може бути `null`. Приклад Chat UI оновлює елементи керування за активним підключенням і поточними налаштуваннями, включно із зареєстрованими інструментами, а не лише за каталогом моделей. Підтримка семплювання може залежати від режиму міркування та наявності інструментів; після зміни цих налаштувань перевірте можливості знову. Невідома підтримка відображається окремо від відсутньої.

| API | Значення |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | Підтримка й рівні спільного `WithReasoning`. |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | Власні налаштування міркувань провайдера та запропоновані бюджети. |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | Потік, інструменти, нативні асинхронні інструменти та вказівки під час роботи. |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | Хостований пошук, зміна міркувань зі збереженням кешу, вхідні зображення та структурований вивід. |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | Підтримувані параметри sampling і відома межа вихідних токенів, що допускає null. |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | Mythosia.AI 8.1.0 / Abstractions 4.1.0: режими Supported/Unsupported/Unknown; права акаунта перевіряються окремо. |
| `Provider`, `Model` | Провайдер і надіслана модель; ідентифікатори можуть бути невідомі. |

`ReasoningLevels` стосується спільного `WithReasoning`, `NativeReasoningLevels` — власних налаштувань провайдера. `ThinkingBudgetPresets` пропонує варіанти для UI, а не всі допустимі бюджети чи повний числовий діапазон. `AsyncFunctionCalling` означає нативне асинхронне виконання інструментів, а не просто локальний `Task` чи паралельний запуск. `StructuredOutput` охоплює спільний API типізованого виводу, включно з резервним шляхом через промпт і виправлення, та не гарантує нативного обмеженого декодування. Обидва списки рівнів використовують `ReasoningLevel`; бюджети є цілими числами.

Знімок не гарантує доступ облікового запису або готовність сервера та не робить неправильні поєднання допустимими. Перевірки й помилки виконання зберігаються. Перед додатковою вказівкою перевірте `run.CanSteer`: підтримка моделі не гарантує, що Run ще активний.

## Перевіряти генерацію зображень окремо

Модель зображення обирається незалежно від чату. `service.GetImageCapabilities(imageModel)` перевіряє вказану модель; без аргументу — типову модель зображень провайдера. Будівник чату її не обирає. `Generation`, `Editing` і `Mask` допомагають показувати відповідні дії.

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`, `Backgrounds`, `OutputFormats`, `SizeKinds`, `Resolutions` і `AspectRatios` — типізовані списки лише для читання. `MaxImages` і `MaxInputImages` — відомі межі, що допускають null. Список не гарантує будь-які поєднання: перевірки розміру, формату, якості, маски й моделі залишаються. Власні чи невідомі моделі не оголошуються непідтримуваними.

У Google `Resolutions` та `AspectRatios` залежать від вибраної моделі зображень і також визначають перевірку генерації та редагування. Див. [таблицю моделей](providers.md#google-image-options), зокрема обережне обмеження Flash-Lite до 1K. Непідтримувані явні значення відхиляються до HTTP; невідомі власні моделі зберігають `Unknown` і загальну перевірку параметрів провайдера.

Власний `AIService` з надійними визначеннями може перевизначити protected `ResolveRequestCapabilities()`. Типовий результат — `AIModelCapabilities.Unknown`. Відсутність у каталозі не робить розгортання непідтримуваним. `IAIService` не має нових обов’язкових членів; методи належать `AIService` і його будівнику.

Якщо профіль власного провайдера змінює прапорці нативного режиму, перевизначте `ApplyCapabilityRequestProfile(AIRequestProfile)` і застосовуйте через `SetExecutionSetting(...)` лише прапорці, потрібні для визначення можливостей. Типовий хук нічого не робить. Спільні налаштування профілю вже захоплені builder; перевірка не викликає `ApplyRequestProfile` або `ApplyProviderSpecificRequestProfile`. Хук не повинен виконувати валідацію, зворотні виклики, серіалізацію, резервування бюджету або змінювати стан сервісу чи коду виклику. Тимчасові налаштування відновлюються після перевірки, зокрема й за винятку в перевизначеному методі.

[Налаштування запитів](request-building.md) · [Провайдери й зображення](providers.md) · [Керування Run](execution-api-transition.md)
