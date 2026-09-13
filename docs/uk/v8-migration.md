# Перехід на Mythosia.AI 8

Ця версія допомагає зберігати налаштування запитів незалежно, зупиняти роботу на прохання користувача й отримувати відповідь разом із використанням токенів та джерелами. Вона об’єднує шість змін архітектури, оновлення постачальників і моделей та виправлення трьох adversarial-перевірок в один основний випуск.

Оновіть разом лише потрібні пакети та перебудуйте їхніх споживачів. Mythosia.AI автоматично додає відповідний Abstractions. Таблиця зіставляє опубліковану базу із сумісними версіями цього випуску.

| Пакет | Опублікована база | Цільова версія |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` переходить із `1.0.0-preview` на стабільну версію `1.0.0`. Наявні API моделей, стану, версії сервера й метрик зберігаються в самостійному пакеті без залежності від основного AI-пакета.

## Обрати зміну за потребою

| Потреба | Зміна та перехід |
| --- | --- |
| Виявляти помилки параметрів зображення до надсилання | Замініть рядки на `ImageQuality`, `ImageBackground`, `ImageOutputFormat` і `ImageSize.Pixels(...)` / `ImageSize.Preset(...)`. Підтримка залежить від постачальника. |
| Готувати запити без взаємного впливу налаштувань | Почніть із `CreateRequest(...)` і зберігайте новий builder кожного `With...`. Сеттери сервісу й надалі змінюють спільні типові значення. |
| Повертати дані з асинхронних інструментів | Методи з атрибутами повертають об’єкти через `Task<T>` / `ValueTask<T>` і приймають впроваджений `CancellationToken`. Винятки вважаються помилками; рядкові обробники залишаються. |
| Не чекати далі після скасування | Передайте `cancellationToken` у completion, Run і підтримувані входи RAG. Він зупиняє локальну роботу та кооперативні інструменти, але не гарантує зупинки у постачальника чи скасування зовнішніх дій. |
| Зберігати відповідь, використання й джерела разом | `AIRun.Result` повертає `Task<AIRunResult>`. Для рядка використовуйте `(await run.Result).Text`. Результат збирається й без читання потоку. |
| Показувати доречні для моделі елементи керування | Використовуйте `request.GetCapabilities()` або запити сервісу/зображень. `Supported`, `Unsupported`, `Unknown` — локальні відомості бібліотеки, а не перевірка доступу облікового запису наживо. |

## Оновлення викликів і власних постачальників

Типи параметрів зображень, `AIRun.Result` та змінені сигнатури скасування порушують сумісність. Власні реалізації `IAIService` та override змінених публічних перевантажень мають додати й передавати токен. Override постачальника `GetCompletionAsync(Message)` зберігає сигнатуру та передає `RequestCancellationToken`. Власний `AIRun` повертає `AIRunResult`. GetCompletionAsync зберігає рядок, типізована completion і `StructuredStreamRun<T>.Result` — типізований результат. Вхідні StreamAsync сервісу/RAG залишаються публічними у v8. RunAgentAsync і RunAgentStreamAsync зберігають сумісну поведінку та попередження obsolete. Для нових сценаріїв прогресу, скасування та підтримуваних додаткових вказівок використовуйте Run.

## Один запит: результат і необов’язковий прогрес

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAI використовує пікселі, Google та xAI — `ImageSize.Preset(...)`. Змінюйте Auto лише за підтримки явного формату; зберігайте згідно з поверненим `GeneratedImage.MediaType`.

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

Зареєстрований інструмент може повернути об’єкт застосунку, як нижче. Низькорівневий `HandlerWithCancellation` і далі повертає `Task<string>`; нова обгортка об’єктів не потрібна.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## Постачальники та перевірка

Включено підготовлені інтеграції Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash та Perplexity Agent, а також спільну генерацію й редагування зображень OpenAI, Google та xAI. Видалені константи й зміна endpoint Perplexity можуть вимагати оновлення викликів; подробиці — у посібнику постачальників і нотатках пакетів.

Налаштовуйте дослідження через `PerplexityAgentOptions`. Тести Profile, Custom Skill і Connector підготовлені, але потребують зареєстрованих ресурсів. MCP залишається preview. Після початку звільнення виклики завершуються з `ObjectDisposedException`; після зупинки читання нові виклики завершуються з `McpException` без нескінченного очікування.

Три adversarial-перевірки посилили копіювання, результати інструментів, скасування/очищення, облік токенів, перевірку відповідей і життєвий цикл MCP. Третя додала 43 регресійні випадки; усі 2 703 тести пройшли. Документація перевірена 13 мовами. Під час цієї перевірки реальні API не викликалися; модульні тести не доводять усі інтеграції, залежні від ресурсів облікового запису.

## Докладні посібники

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
