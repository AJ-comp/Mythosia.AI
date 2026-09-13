# Perplexity: ответы с источниками, поиск и эмбеддинги

Используйте Perplexity, когда ответу нужны свежие сведения и источники, которые читатель может проверить. `PerplexityService` вызывает Agent API, а отдельные поиск и эмбеддинги позволяют построить извлечение документов для выбранной вами модели ответов.

## Сначала выберите задачу

Свежий ответ, список веб-страниц и векторы собственного индекса — разные задачи. Выберите компонент, который выполняет нужную работу, вместо вызова модели ответов при каждом поиске.

| Задача | Компонент |
| --- | --- |
| Исследованный ответ с источниками | `PerplexityService` |
| Страницы для другой модели или интерфейса | `PerplexitySearchClient` |
| Векторы независимых фрагментов для обычного RAG | `PerplexityEmbeddingProvider` |
| Векторы с учётом соседних фрагментов одного документа | `PerplexityContextualizedEmbeddingProvider` |

Установите `Mythosia.AI`, а для примеров эмбеддингов также `Mythosia.AI.Rag`. Передайте API-ключ и управляемый приложением `HttpClient`. Примеры используют ваши `apiKey`, `httpClient` и `cancellationToken`.

## Ответ с пресетом Agent

Пресет объединяет модель, инструкции, инструменты, усилие и бюджеты, поддерживаемые провайдером. `Fast` подходит для быстрых запросов, `Low` — повседневного поиска, `Medium` — многошаговых сравнений, `High` / `XHigh` — углублённой работы. `WideResearch` предназначен для широких исследований; для длительных задач рекомендуется фоновое выполнение. Это пресеты, а не ID моделей.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "Сравни современные методы переработки аккумуляторов и укажи источники.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Используйте `GetCompletionAsync` только для результата, сервисный `StreamAsync` для существующего потока или `StartRunAsync` для наблюдения и отмены. `(await run.Result).Text` объединяет выданный текст. `run.Citations` и `LastCitations` сохраняют источники без чтения событий цитирования. События рассуждений содержат лишь раскрываемые провайдером сведения и зависят от модели.

`AIRunResult.RequestedModel` — единственная явно указанная модель, отправляемая в запросе и зафиксированная при запуске, с учётом переопределения модели провайдера. Значение `null`, если пресет, профиль или серверная маршрутизация выбирают модель без единого явно заданного поля модели (например, список Perplexity `Models`). Оно независимо от фактической модели ответа в `Model`.

## Управление исследованием и инструментами

`WithPerplexityOptions(...)` задаёт постоянные настройки, копируемые для каждого логического запроса. Общие `WithReasoning(...)` и `WithWebSearch(...)` действуют на следующий запрос, его раунды клиентских функций и исправления типизированного вывода. Внутреннее переписывание RAG-запроса не наследует поиск итогового ответа.

`UsePreset(...)` выбирает пресет. Пресет/профиль задаёт собственную модель; `ModelOverride` явно заменяет её. `DisableWebSearch` убирает только стандартный инструмент адаптера, не гарантируя отключение поиска пресета. Модель может поддерживать `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`; `None` и явное усилие для прямого Sonar отклоняются. Внутренний `DisableReasoning` использует доступное низкое усилие либо опускает настройку, без гарантии полного отключения рассуждений.

| Настройка | Назначение |
| --- | --- |
| `Preset` / `ModelOverride` | Выбрать исследовательскую конфигурацию либо явно заменить модель ID вида провайдер/модель. |
| `MaxSteps` | Ограничить серверный цикл; ноль оставляет значение провайдера. Отдельно от `WithMaxRounds`, ограничивающего продолжения локальных функций. |
| `ReasoningEffort` | Настроить усилие рассуждения. `Auto` опускает параметр; уровни зависят от выбранной модели. |
| `DisableWebSearch` / `Tools` | Настроить стандартный веб-инструмент адаптера и явно заданные серверные инструменты. |
| `Models` | Задать от одной до пяти резервных моделей по приоритету. Список заменяет одиночную модель; все кандидаты должны поддерживать требуемые функции. |
| `Profile` | Использовать сохранённую серверную конфигурацию, при необходимости закрепив версию. Несовместимо с `Preset`. |
| `ServiceTier` | Запросить стандартную, flex или приоритетную обработку. Неподдерживаемый уровень провайдер может проигнорировать. |
| `Skills` | Передать встроенные, inline или ранее загруженные пользовательские навыки. Пользовательские ресурсы принадлежат аккаунту Perplexity. |
| `LanguagePreference` / `PromptCacheKey` | Задать язык ответа или подсказку маршрутизации кэша. Попадание в кэш не гарантируется. |
| `PreviousResponseId` / `Store` | Продолжить завершённый ответ или настроить доступность его получения. Используйте `StatelessMode` и только новый ход. `Store = false` не отключает хранение у провайдера. |

`PerplexityHostedTool` принимает поддерживаемый `Type` и документированные JSON-совместимые `Parameters`: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox`, `mcp`. MCP-серверы и управляемые коннекторы работают через провайдера; учётные данные, разрешения и ресурсы должны соответствовать подключению. Функции приложения регистрируются через `Functions` / построитель функций. Серверные шаги и локальные обработчики исполняются разными сторонами.

Фабрики `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, `Connector` создают инструменты. MCP исполняется без ожидания одобрения; при необходимости ограничьте `allowedTools`. Connector — предварительная функция провайдера для уже подключённой интеграции.

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
    "Прочитай документацию проекта и сравни соответствующие возможности.");
```

Совместимость инструментов, рассуждений, изображений и схем определяется моделью. `WithFileSearch` не является адаптером векторного хранилища Perplexity. Файлы песочницы, вложения и удалённые MCP-данные остаются отдельными ресурсами и не превращаются в общее хранилище файлового поиска.

## Источники, изображения и структурированные ответы

Для JSON-полей используйте типизированный completion или поток. Адаптер отправляет нативную схему и сохраняет механизм исправления. Нативные элементы ответа и ID инструментов сохраняются для продолжения; не удаляйте и не переставляйте историю протокола вручную. Изображения передаются через `Message` и `ImageContent` как байты JPEG/PNG/WebP/GIF или HTTPS URL, если модель поддерживает их. Это входные изображения, а не генерация.

Исходные записи ответа сохраняются в метаданных истории, но последующие запросы повторяют только разрешённые входные элементы `message`, `function_call` и `function_call_output`; для продолжения полного состояния на стороне провайдера используйте `PreviousResponseId`.

Цитаты могут указывать на веб-результаты или другие источники провайдера. Позиции относятся к отдельному ответу и части содержимого, не к объединённому результату Run. Сохраняйте URL и заголовок для показа и проверки; наличие источника само по себе не подтверждает каждое утверждение.

## Продолжение длительной задачи

Фоновое выполнение провайдера позволяет исследованию пережить временный разрыв соединения или получить результат позже по ID. Локальный `AIRun` управляет текущим клиентским выполнением; фоновый ответ имеет отдельный серверный жизненный цикл. Завершение чтения потока прекращает только наблюдение. Для остановки удалённой работы явно отмените задание.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "Сравни современные методы переработки аккумуляторов и укажи источники.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` захватывает ввод без записи в историю и отклоняет активные локальные функции или `Store = false`. `GetResponseAsync` опрашивает один раз, `WaitForCompletionAsync` — до конечного состояния. Сохраните `Id` и `LastSequenceNumber`; переподключайтесь через `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`. `CancelAsync` отменяет серверную работу; отмена токена чтения/опроса завершает только клиентскую операцию. `LastResponse` содержит текст, статус, usage, цитаты и `OutputJson`. Проверяйте конечный статус перед использованием ответа.

Файлы песочницы доступны через `ListFilesAsync` и `DownloadFileAsync(fileId)`. Сервис также предоставляет `GetAgentResponseAsync`, `GetResponseFilesAsync`, `GetResponseFileContentAsync`. Это получение результатов ответа, не создание или поиск векторного хранилища.

Для встроенных навыков Office используйте фоновый путь из этого руководства: `StartBackgroundAsync`, затем `WaitForCompletionAsync` / `GetResponseAsync` и методы файлов. Следы внутренних инструментов в таких ответах могут быть неотличимы от обычных вызовов локальных функций.

Фоновая отправка, получение, отмена и переподключение потока не включают `SteerAsync` во время ответа или нативные асинхронные клиентские инструменты. Переподключение продолжает наблюдение существующего ответа без повторной отправки задачи. Сохраняйте ID ответа и курсор провайдера.

## Поиск без генерации ответа

`PerplexitySearchClient` получает страницы для собственного ранжирования, интерфейса или другого LLM. Он не вызывает модель ответов и не меняет историю `PerplexityService`.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "методы переработки аккумуляторов",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` принимает один или несколько запросов. Доступны Web/People, страна, домены, языки, диапазоны публикации/обновления и давность. Выберите `ContentSize` либо явные `MaxTokens` / `MaxTokensPerPage`, не оба варианта. Результат содержит ранг, заголовок, URL, фрагмент и даты провайдера. Ранг — порядок возврата, а не оценка релевантности.

`ContentSize` поддерживается только для поиска Web. Для People его нужно опустить: клиент отклонит это сочетание до отправки.

## Векторы для собственного индекса

Стандартные эмбеддинги обрабатывают фрагменты независимо и реализуют `IEmbeddingProvider` для существующего RAG-построителя. Контекстные сохраняют порядок соседних фрагментов и группы документов. Отдельный API предотвращает объединение несвязанных документов в плоский вход.

| Константа модели | ID провайдера | Полная размерность |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Возврат принимается в течение 30 дней.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("До какого срока можно вернуть покупку?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Возврат принимается в течение 30 дней.", "Сохраните чек для запроса возврата денег." },
    new[] { "Обычная доставка занимает три дня.", "Экспресс-доставка доступна в рабочие дни." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "До какого срока можно вернуть покупку?", cancellationToken);
```

Документы и запросы должны использовать одинаковые модель, размерность и кодировку. `GetQueryEmbeddingAsync` передаёт запрос как отдельный документ той же контекстной модели. Результаты сохраняют порядок документов и фрагментов, без автоматического подключения к плоскому RAG-построителю.

Float API декодирует base64 signed-int8 векторы и нормализует их для сходства. Явный binary API возвращает упакованные биты с расстоянием Хэмминга, никогда не превращая их неявно в координаты float. Полная размерность — 1024 для 0.6B и 2560 для 4B; уменьшение следует ограничениям провайдера. Ограничения пакетов, длины, общего числа токенов и частоты аккаунта сохраняются.

Двоичные методы: `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync` и контекстные `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`. `PerplexityBinaryEmbedding` предоставляет `Dimensions`, копию `ToArray()` и `HammingDistance`; меньшее расстояние означает большее сходство. Размерность должна делиться на восемь. Максимум 512 стандартных текстов либо 512 документов и 16 000 контекстных фрагментов. Провайдер проверяет 32K токенов на текст/документ и 120K суммарно.

## Перенос существующего Sonar-кода

Выпуск намеренно удаляет прежний адаптер до объявленного отключения эндпоинтов 27 сентября 2026 года. `PerplexityService` обращается к `/v1/agent`; `AIModels.Perplexity.Sonar` теперь означает `perplexity/sonar`. Старые Sonar-методы поиска и типы ответов удалены. Используйте общие completion/Run/цитаты, пресеты Agent и отдельный `PerplexitySearchClient`.

Рекомендуемое соответствие: Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`. Одинаковые тексты, стоимость и поведение не гарантируются. Динамические пресеты обновляются; при необходимости закрепите модель или версию профиля.

Нативное управление во время ответа, нативные асинхронные клиентские инструменты и `CachePreservation.Required` не поддерживаются. Router/Gateway вне этой интеграции. Доступность зависит от провайдера, модели и аккаунта; руководство не утверждает, что все сочетания проверены платными реальными вызовами.

Для профилей, пользовательских skills и коннекторов нужны заранее зарегистрированные ресурсы аккаунта. Формы запросов покрыты модульными тестами; успешные реальные вызовы с этими ресурсами не проверялись.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
