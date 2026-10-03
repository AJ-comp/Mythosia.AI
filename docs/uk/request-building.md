# Незалежні налаштування кожного запиту

> Claude Sonnet 5.5: Потрібні Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Налаштування та міграція](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: Потрібні Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Вибір моделі та міграція](providers.md#gpt-61-sol)

> Grok 4.7: Потрібні Mythosia.AI 8.1.0 / Abstractions 4.1.0. [вибір моделі, міркування та швидкість обробки](providers.md#grok-47)

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

Claude разом визначає модель, призначення запиту, міркування та прив’язку thinking. Для допоміжних профілів, як-от `RequestProfiles.Summarization` або `RequestProfiles.QueryRewrite`, успадковану прив’язку пропускають лише за `DisableReasoning = true`, призначення, відмінного від `Default`, і фактичного виконання без стану. Це не дозволяє успадкованій політиці повторно ввімкнути міркування або відхилити запит, який не має префікса діалогу для збереження. Моделі з можливістю вимкнення thinking вимикають його; Opus 5.5, Fable 5.1 і Mythos 5.1 з обов’язковим міркуванням використовують `Low` без читабельного thinking. Sonnet 5.5 використовує `between_tools` із високим рівнем зусиль.

Completion, потокове й структуроване виведення та Run використовують єдину підготовку запиту: зберігають налаштування, один раз виконують фактичну обробку профілю, потім перевіряють отримані спільні й нативні параметри постачальника. Це відбувається до автоматичного підсумовування, додавання нового вводу до історії та відкриття з'єднання. Перевизначення профілю в користувацькому постачальнику беруть участь у реально перевірених налаштуваннях. Claude також відхиляє на цьому етапі використовуваний ручний `ThinkingBudget`, що досягає або перевищує ліміт виведення моделі; допустимі профілі та спільні налаштування міркування зберігають пріоритет.

Виклики застосунку починають незалежні логічні запити, зокрема звичайні виклики з `SystemMessageProvider` або callback-функцій інструментів і виклики з повторним використанням того самого `AIRequestProfile` чи `Message`. Повторне використання об'єкта не об'єднує виконання. Делегування з боку фреймворку, етапи інструментів, повтори та виправлення формату продовжують початковий запит, застосовуючи профіль один раз. Звичайний дочірній запит отримує власні опції та стандартні налаштування сервісу; builder зберігає зафіксовані налаштування. Після успіху, помилки або скасування виконання батьківського запиту відновлюється. Перевизначені методи постачальника, які передають виклик фреймворку далі, дотримуються [правил адаптерів нижче](#provider-request-adapters).

Вбудовані провайдери зберігають власну копію вбудованого вмісту вхідного повідомлення. Повторне використання `Message` застосовує контекст та інструкції нового виклику, не переписуючи прийняту історію. Власник і надалі відповідає за користувацький вміст і непідтримувані об'єкти метаданих. Це не забезпечує безпечних паралельних викликів для однієї розмови.

Після повернення з `StartRunAsync` Run зберігає власну копію підсумкових налаштувань. Відновлення профілю виклику не змінює активний Run; хуки виконання профілю, як і раніше, викликаються лише один раз.

Допоміжні запити без стану використовують окрему розмову й не успадковують схему виведення, розміщені в постачальника інструменти чи одноразові параметри батьківського запиту. Ізоляція ніколи не пропускає нативну перевірку, зокрема для Run OpenAI та Perplexity. Налаштування, повідомлення, `CurrentSummary` і дані спостереження батьківського запиту зберігаються. Запити зі станом зберігають перевірки прив'язки й розмови. Наявні публічні API не змінюються.

Внутрішні автоматичні запити на підсумовування розмови також виключають callback `SystemMessageProvider` і контекст батьківського запиту, щоб успадкований `RequestMessageOverride` не замінив інструкцію підсумовування. Запити застосунку й надалі використовують динамічний контекст, зокрема явні прохання підсумувати текст.

Запити без стану також пропускають автоматичне підсумовування батьківського діалогу, зокрема під час виклику наявного перевантаження `GetCompletionAsync(string, profile)`, як і перевантаження `Message` та побудовник запитів. `CurrentSummary` і повідомлення батьківського діалогу не змінюються. Запити зі станом зберігають звичайне автоматичне підсумовування.

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## Передавання запитів у користувацькому постачальнику

Коли фреймворк викликає віртуальний адаптер постачальника, його перший виклик відповідної точки входу базового класу продовжує підготовлений запит, навіть якщо перевизначений метод замінює вхідний `Message`. Зафіксовані параметри builder і застосовані профілі зберігаються під час такого передавання. Стандартний адаптер потокового виведення через callback також продовжує той самий підготовлений запит.

Адаптер може передати змінений `AIRequestProfile`: однакові значення не застосовуються повторно, а зміни замінюють попередній шар профілю на основі збережених налаштувань. Повторна перевірка відбувається до автоматичного підсумовування й надсилання, зокрема при переході до режиму без стану. Допоміжні запити OpenAI без стану не перевіряють чужу збережену історію та не скидають захист батьківської розмови.

Заміна переданого профілю зберігає подальші локальні додавання, видалення та зміни інструментів, зміни політики й явні присвоєння налаштувань, навіть коли скалярне значення не змінилося. Зміна іншого поля профілю не повертає інструмент, видалений адаптером. Типові значення сервісу не зчитуються повторно.

Для непрозорих користувацьких об’єктів налаштувань адаптер має замінювати значення через `SetExecutionSetting`, а не змінювати внутрішні поля. Бібліотека не досліджує довільні об’єкти застосунку та не викликає їхні серіалізатори для відстеження змін профілю.

Стиснення Claude зберігає залежності виклик/результат у `RequestMessageOverride` і `AdditionalMessages`, включно із серверними інструментами, та зв’язаний префікс thinking Mythos 5.1. Старі записи паралельних інструментів групуються один раз як в історії, так і в додаткових повідомленнях; належність кожного запису зберігається.

Незалежний допоміжний виклик тієї самої точки входу базового класу перед передаванням запиту неоднозначний: фреймворк не може визначити, чи це продовження. Обгорніть цей допоміжний виклик разом із його `await` в область, створену захищеним методом `BeginIndependentRequestScope()`; для потокового виведення залишайте область відкритою протягом усього перебирання. Допоміжний виклик починає роботу зі стандартних налаштувань сервісу, а звільнення області відновлює зовнішні налаштування, функції, контекст і делегування, що очікує виконання. Звичайні вкладені виклики з callback-функцій контексту або інструментів уже незалежні й не потребують цієї області.

Наприклад, підклас конкретного постачальника може переформулювати текст перед його передаванням:

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

Ця область розділяє стан виконання запитів; вона не ізолює історію розмови й не дозволяє одночасне використання сервісу. У прикладі використано профіль без стану `QueryRewrite`, щоб допоміжний виклик не впливав на батьківську розмову.

Під час стискання Claude перевіряє збережену канонічну історію у форматі передавання, зокрема підписані блоки thinking, додані через `AIRequestContext.AdditionalMessages`. Стандартна прив'язка thinking захищає цей префікс від автоматичного або явно запитаного стискання шляхом підсумовування. Явне налаштування `ClaudeThinkingPrefixMismatchBehavior.DropBlock` дозволяє стискання там, де воно підтримується; решта обмежень розмови залишається чинною.

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

Для користувача, який чекає відповіді на екрані, може бути виправдана платна обробка з малою затримкою; фоновий звіт може виконуватися звичайно. `WithSpeed` вибирає режим, зберігаючи модель і рівень міркувань. Потрібні Mythosia.AI 8.1.0 / Abstractions 4.1.0.

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
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
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
