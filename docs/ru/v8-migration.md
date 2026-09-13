# Переход на Mythosia.AI 8

Эта версия позволяет хранить настройки запросов независимо, останавливать работу по просьбе пользователя и сохранять ответ вместе с расходом токенов и источниками. Она объединяет шесть изменений архитектуры, обновления поставщиков и моделей и исправления трёх adversarial-проверок в один основной выпуск.

Обновите вместе только используемые пакеты и пересоберите потребителей. Mythosia.AI автоматически подключает соответствующий Abstractions. Таблица сопоставляет опубликованную базу с совместимыми версиями этого выпуска.

| Пакет | Опубликованная база | Целевая версия |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` переходит с `1.0.0-preview` на стабильную версию `1.0.0`. Существующие API моделей, состояния, версии сервера и метрик сохраняются в самостоятельном пакете без зависимости от основного AI-пакета.

## Выбор по задаче

| Задача | Изменение и переход |
| --- | --- |
| Выявлять ошибки параметров изображений до отправки | Вместо строк используйте `ImageQuality`, `ImageBackground`, `ImageOutputFormat` и `ImageSize.Pixels(...)` / `ImageSize.Preset(...)`. Поддержка зависит от поставщика. |
| Готовить запросы без взаимного изменения настроек | Начинайте с `CreateRequest(...)` и сохраняйте новый builder каждого `With...`. Сеттеры сервиса по-прежнему меняют общие значения по умолчанию. |
| Возвращать данные из асинхронных инструментов | Методы с атрибутами возвращают объекты через `Task<T>` / `ValueTask<T>` и принимают внедряемый `CancellationToken`. Исключения считаются ошибками; строковые обработчики сохраняются. |
| Прекращать ожидание при отмене | Передавайте `cancellationToken` в completion, Run и поддерживаемые входы RAG. Он останавливает локальную работу и кооперативные инструменты, но не гарантирует остановку у поставщика и не отменяет внешние действия. |
| Хранить ответ, расход и источники вместе | `AIRun.Result` возвращает `Task<AIRunResult>`. Строка доступна через `(await run.Result).Text`. Результат собирается и без чтения потока. |
| Показывать подходящие модели элементы управления | Используйте `request.GetCapabilities()` или запросы сервиса/изображений. `Supported`, `Unsupported`, `Unknown` отражают локальные сведения библиотеки, а не доступ аккаунта в реальном времени. |

## Обновление вызовов и собственных поставщиков

Типы параметров изображений, `AIRun.Result` и изменённые сигнатуры отмены нарушают совместимость. Собственные реализации `IAIService` и override изменённых публичных перегрузок должны добавить и передать токен. Override поставщика `GetCompletionAsync(Message)` сохраняет сигнатуру и передаёт `RequestCancellationToken`. Собственный `AIRun` возвращает `AIRunResult`. GetCompletionAsync сохраняет строку, типизированная completion и `StructuredStreamRun<T>.Result` — типизированный результат. Входные StreamAsync сервиса/RAG остаются публичными в v8. RunAgentAsync и RunAgentStreamAsync сохраняют совместимое поведение и предупреждения obsolete. Для новых сценариев прогресса, отмены и поддерживаемых дополнительных указаний используйте Run.

## Один запрос: результат и необязательный прогресс

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

OpenAI использует пиксели, Google и xAI — `ImageSize.Preset(...)`. Меняйте Auto только при поддержке явного формата; сохраняйте по возвращённому `GeneratedImage.MediaType`.

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

Зарегистрированный инструмент может вернуть объект приложения, как ниже. Низкоуровневый `HandlerWithCancellation` по-прежнему возвращает `Task<string>`; новая обёртка объектов не нужна.

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

## Поставщики и проверка

Включены подготовленные интеграции Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash и Perplexity Agent, а также общие генерация и редактирование изображений OpenAI, Google и xAI. Удалённые константы и смена endpoint Perplexity могут потребовать изменений вызовов; подробности — в руководстве поставщиков и заметках пакетов.

Настраивайте исследование через `PerplexityAgentOptions`. Тесты Profile, Custom Skill и Connector подготовлены, но требуют зарегистрированных ресурсов. MCP остаётся preview. После начала освобождения вызовы завершаются с `ObjectDisposedException`; после остановки чтения новые вызовы завершаются с `McpException`, не ожидая бесконечно.

Три adversarial-проверки усилили копирование, результаты инструментов, отмену/очистку, подсчёт токенов, проверку ответов и жизненный цикл MCP. Третья добавила 43 регрессионных случая; все 2 703 теста прошли. Документация проверена на 13 языках. В этой проверке реальные API не вызывались; модульные тесты не подтверждают все интеграции, зависящие от ресурсов аккаунта.

## Подробные руководства

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
