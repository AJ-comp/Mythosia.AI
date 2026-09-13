# Perplexity: відповіді з джерелами, пошук та ембеддинги

Використовуйте Perplexity, коли відповіді потрібні свіжі відомості та джерела, які читач може перевірити. `PerplexityService` викликає Agent API, а окремі пошук та ембеддинги дають змогу побудувати отримання документів для обраної вами моделі відповідей.

## Спочатку оберіть завдання

Свіжа відповідь, список вебсторінок і вектори власного індексу — різні завдання. Оберіть відповідальний компонент, не викликаючи модель відповідей для кожного пошуку.

| Потреба | Компонент |
| --- | --- |
| Досліджена відповідь із джерелами | `PerplexityService` |
| Сторінки для іншої моделі чи інтерфейсу | `PerplexitySearchClient` |
| Вектори незалежних уривків для звичайного RAG | `PerplexityEmbeddingProvider` |
| Вектори з урахуванням сусідніх фрагментів одного документа | `PerplexityContextualizedEmbeddingProvider` |

Установіть `Mythosia.AI`, а для прикладів ембеддингів також `Mythosia.AI.Rag`. Передайте API-ключ і керований застосунком `HttpClient`. Приклади використовують ваші `apiKey`, `httpClient` і `cancellationToken`.

## Відповідь із пресетом Agent

Пресет поєднує модель, інструкції, інструменти, зусилля й бюджети, які підтримує провайдер. `Fast` підходить для швидких запитів, `Low` — щоденного пошуку, `Medium` — багатокрокових порівнянь, `High` / `XHigh` — глибокої роботи. `WideResearch` призначений для широких досліджень; для тривалих завдань рекомендовано фонове виконання. Це пресети, а не ID моделей.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "Порівняй сучасні методи переробки акумуляторів і наведи джерела.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Використовуйте `GetCompletionAsync` лише для відповіді, сервісний `StreamAsync` для наявного потоку або `StartRunAsync` для спостереження та скасування. `(await run.Result).Text` об'єднує виданий текст. `run.Citations` і `LastCitations` зберігають джерела навіть без читання подій цитування. Події міркувань містять лише оприлюднений провайдером вміст і залежать від моделі.

`AIRunResult.RequestedModel` — єдина явно вказана модель, що надсилається в запиті та фіксується на старті, з урахуванням перевизначення моделі провайдера. Значення `null`, якщо пресет, профіль або серверна маршрутизація обирають модель без єдиного явно заданого поля моделі (наприклад, список Perplexity `Models`). Воно незалежне від фактичної моделі відповіді в `Model`.

## Керування дослідженням та інструментами

`WithPerplexityOptions(...)` задає постійні параметри, які копіюються для кожного логічного запиту. Спільні `WithReasoning(...)` і `WithWebSearch(...)` діють на наступний запит, його раунди клієнтських функцій та виправлення типізованого виводу. Внутрішнє переписування RAG-запиту не успадковує пошук фінальної відповіді.

`UsePreset(...)` обирає пресет. Пресет/профіль задає власну модель; `ModelOverride` явно замінює її. `DisableWebSearch` прибирає лише стандартний інструмент адаптера, не гарантуючи вимкнення пошуку пресета. Модель може підтримувати `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`; `None` та явне зусилля для прямого Sonar відхиляються. Внутрішній `DisableReasoning` використовує низьке доступне зусилля чи пропускає параметр, без гарантії повного вимкнення міркувань.

| Параметр | Призначення |
| --- | --- |
| `Preset` / `ModelOverride` | Обрати конфігурацію дослідження або явно замінити модель ID формату провайдер/модель. |
| `MaxSteps` | Обмежити серверний цикл; нуль лишає значення провайдера. Окремо від `WithMaxRounds`, що обмежує продовження локальних функцій. |
| `ReasoningEffort` | Налаштувати зусилля міркувань. `Auto` пропускає параметр; рівні залежать від фактичної моделі. |
| `DisableWebSearch` / `Tools` | Налаштувати стандартний вебінструмент адаптера й явно задані серверні інструменти. |
| `Models` | Задати від однієї до п'яти резервних моделей за пріоритетом. Список замінює одну модель; усі кандидати мають підтримувати потрібні функції. |
| `Profile` | Використати збережену серверну конфігурацію, за потреби закріпивши версію. Не поєднується з `Preset`. |
| `ServiceTier` | Запросити стандартну, flex чи пріоритетну обробку. Провайдер може проігнорувати непідтримуваний рівень. |
| `Skills` | Передати вбудовані, inline чи раніше завантажені власні навички. Власні ресурси належать обліковому запису Perplexity. |
| `LanguagePreference` / `PromptCacheKey` | Задати мову відповіді чи підказку маршрутизації кешу. Влучання в кеш не гарантується. |
| `PreviousResponseId` / `Store` | Продовжити завершену відповідь або визначити доступність її отримання. Використовуйте `StatelessMode` і лише новий хід. `Store = false` не вимикає зберігання у провайдера. |

`PerplexityHostedTool` приймає підтримуваний `Type` і документовані JSON-сумісні `Parameters`: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox`, `mcp`. MCP-сервери й керовані конектори виконуються через провайдера; облікові дані, дозволи та ресурси мають відповідати з'єднанню. Функції застосунку реєструються через `Functions` / побудовник функцій. Серверні кроки та локальні обробники виконують різні сторони.

Фабрики `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, `Connector` створюють інструменти. MCP виконується без очікування схвалення; за потреби обмежте `allowedTools`. Connector — попередня функція провайдера для вже підключеної інтеграції.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "Прочитай документацію проєкту й порівняй відповідні можливості.");
```

Сумісність інструментів, міркувань, зображень і схем визначає модель. `WithFileSearch` не є адаптером векторного сховища Perplexity. Файли пісочниці, вкладення та віддалені дані MCP — окремі ресурси, які не перетворюються на спільне сховище файлового пошуку.

## Джерела, зображення та структуровані відповіді

Для JSON-полів використовуйте типізований completion або потік. Адаптер надсилає нативну схему й зберігає механізм виправлення. Нативні елементи відповіді та ID інструментів зберігаються для продовження; не видаляйте й не переставляйте історію протоколу вручну. Зображення передаються через `Message` та `ImageContent` як байти JPEG/PNG/WebP/GIF або HTTPS URL за підтримки моделі. Це вхідні зображення, а не генерація.

Початкові записи відповіді зберігаються в метаданих історії, але наступні запити повторюють лише дозволені вхідні елементи `message`, `function_call` та `function_call_output`; для продовження повного стану на боці провайдера використовуйте `PreviousResponseId`.

Цитати можуть позначати вебрезультати чи інші джерела провайдера. Позиції належать окремій відповіді й частині вмісту, а не об'єднаному результату Run. Зберігайте URL і заголовок для показу та перевірки; наявність джерела сама собою не підтверджує кожне твердження.

## Продовження тривалого завдання

Фонове виконання провайдера дозволяє дослідженню пережити тимчасове від'єднання або отримати результат пізніше за ID. Локальний `AIRun` керує поточним клієнтським виконанням, а фонова відповідь має окремий серверний життєвий цикл. Завершення читання потоку зупиняє лише спостереження. Для припинення віддаленої роботи явно скасуйте завдання.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "Порівняй сучасні методи переробки акумуляторів і наведи джерела.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` захоплює ввід без запису в історію та відхиляє активні локальні функції або `Store = false`. `GetResponseAsync` опитує один раз, `WaitForCompletionAsync` — до кінцевого стану. Збережіть `Id` і `LastSequenceNumber`; підключайтеся через `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`. `CancelAsync` скасовує серверну роботу; скасування токена читання/опитування завершує лише клієнтську операцію. `LastResponse` містить текст, статус, usage, цитати й `OutputJson`. Перевіряйте кінцевий статус перед використанням відповіді.

Файли пісочниці доступні через `ListFilesAsync` і `DownloadFileAsync(fileId)`. Сервіс також надає `GetAgentResponseAsync`, `GetResponseFilesAsync`, `GetResponseFileContentAsync`. Це отримання результатів відповіді, а не створення чи пошук векторного сховища.

Для вбудованих навичок Office використовуйте фоновий шлях із цього посібника: `StartBackgroundAsync`, потім `WaitForCompletionAsync` / `GetResponseAsync` і методи файлів. Записи внутрішніх інструментів у таких відповідях можуть не відрізнятися від звичайних викликів локальних функцій.

Фонове надсилання, отримання, скасування та перепідключення потоку не вмикають `SteerAsync` під час відповіді чи нативні асинхронні клієнтські інструменти. Перепідключення спостерігає наявну відповідь без повторного надсилання завдання. Зберігайте ID відповіді та курсор провайдера.

## Пошук без генерації відповіді

`PerplexitySearchClient` отримує сторінки для власного ранжування, інтерфейсу чи іншого LLM. Він не викликає модель відповідей і не змінює історію `PerplexityService`.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "методи переробки акумуляторів",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` приймає один або кілька запитів. Доступні Web/People, країна, домени, мови, діапазони публікації/оновлення й давність. Оберіть `ContentSize` або явні `MaxTokens` / `MaxTokensPerPage`, не обидва варіанти. Результат містить ранг, заголовок, URL, уривок і дати провайдера. Ранг — порядок повернення, а не оцінка релевантності.

`ContentSize` підтримується лише для пошуку Web. Для People його слід пропустити: клієнт відхилить це поєднання до надсилання.

## Вектори для власного індексу

Стандартні ембеддинги обробляють уривки незалежно й реалізують `IEmbeddingProvider` для наявного RAG-побудовника. Контекстні зберігають порядок сусідніх фрагментів і групи документів. Окремий API запобігає об'єднанню непов'язаних документів у плоский ввід.

| Константа моделі | ID провайдера | Повна розмірність |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Повернення приймаються протягом 30 днів.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("До якого терміну можна повернути покупку?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Повернення приймаються протягом 30 днів.", "Збережіть чек для запиту повернення коштів." },
    new[] { "Звичайна доставка триває три дні.", "Експрес-доставка доступна в робочі дні." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "До якого терміну можна повернути покупку?", cancellationToken);
```

Використовуйте однакові модель, розмірність і кодування для документів та запитів. `GetQueryEmbeddingAsync` надсилає запит як окремий документ тій самій контекстній моделі. Результати зберігають порядок документів і фрагментів без автоматичного підключення до плоского RAG-побудовника.

Float API декодує base64 signed-int8 вектори й нормалізує їх для подібності. Явний binary API повертає упаковані біти з відстанню Геммінга, ніколи не перетворюючи їх неявно на координати float. Повна розмірність — 1024 для 0.6B і 2560 для 4B; зменшення підкоряється обмеженням провайдера. Обмеження пакетів, довжини, загальних токенів і частоти облікового запису зберігаються.

Двійкові методи: `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync` і контекстні `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`. `PerplexityBinaryEmbedding` надає `Dimensions`, копію `ToArray()` та `HammingDistance`; менша відстань означає більшу подібність. Розмірність має ділитися на вісім. Максимум 512 стандартних текстів або 512 документів і 16 000 контекстних фрагментів. Провайдер перевіряє 32K токенів на текст/документ і 120K загалом.

## Перенесення наявного Sonar-коду

Випуск навмисно видаляє старий адаптер до оголошеного вимкнення кінцевих точок 27 вересня 2026 року. `PerplexityService` звертається до `/v1/agent`; `AIModels.Perplexity.Sonar` тепер означає `perplexity/sonar`. Старі Sonar-методи пошуку й типи відповідей видалені. Використовуйте спільні completion/Run/цитати, пресети Agent та окремий `PerplexitySearchClient`.

Рекомендована відповідність: Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`. Однакові тексти, вартість і поведінка не гарантуються. Динамічні пресети оновлюються; за потреби закріпіть модель або версію профілю.

Нативне керування під час відповіді, нативні асинхронні клієнтські інструменти й `CachePreservation.Required` не підтримуються. Router/Gateway поза цією інтеграцією. Доступність залежить від провайдера, моделі й облікового запису; посібник не стверджує, що всі поєднання перевірені платними реальними викликами.

Для профілів, власних skills і конекторів потрібні заздалегідь зареєстровані ресурси облікового запису. Форми запитів покриті модульними тестами; успішні реальні виклики з цими ресурсами не перевірено.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
