# Вызов функций

Для готового ответа и кнопки Стоп передайте `cancellationToken` в `GetCompletionAsync`. Run нужен для событий прогресса или поддерживаемых дополнительных указаний. См. [отмену ответа](completions.md#completion-cancellation).

Для независимых настроек и повторного использования вариантов применяйте [билдер запросов](request-building.md). Вызывайте `CreateRequest(...)` перед `With...`. Свойства и fluent-методы сервиса сохраняют прежнее поведение.

## Зачем нужен вызов функций

LLM генерирует только текст — он не может сам проверить погоду, обратиться к базе данных или вызвать API. **Без** вызова функций намерение модели приходится разбирать вручную:

```csharp
// ❌ Без вызова функций — ручной разбор намерения
var reply = await service.GetCompletionAsync("Какая погода в Москве?");
// reply = "Чтобы узнать погоду, нужно обратиться к погодному сервису."

if (reply.Contains("погод"))
{
    var city = ExtractCity(reply); // хрупкие регулярные выражения
    var weather = await weatherApi.GetAsync(city);
}
```

**С** вызовом функций модель сама решает, **когда** вызвать код и **какие аргументы** передать:

```csharp
// ✅ С вызовом функций — модель сама определяет намерение и аргументы
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Получает текущую погоду для указанного города",
        ("location", "Город и страна", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("Какая погода в Москве?");
// Модель вызывает get_weather("Москва, Россия"), получает результат и формирует ответ.
```

## Быстрый пример

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Получает текущую погоду для указанного города",
        ("location", "Город и страна", required: true),
        (string location) => $"Погода в {location}: ясно, 22°C"
    );

var response = await service.GetCompletionAsync("Какая погода в Москве?");
```

## Определение функций через атрибуты

Для сложных функций используйте атрибуты `[AiFunction]` и `[AiParameter]`:

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Поиск по каталогу товаров")]
    public string SearchProducts(
        [AiParameter("Поисковый запрос", required: true)] string query,
        [AiParameter("Максимум результатов")] int limit = 5)
    {
        return JsonSerializer.Serialize(results);
    }
}
```

Затем зарегистрируйте:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Политика вызова функций

Управляйте тем, когда модель может вызывать функции:

```csharp
using Mythosia.AI.Models.Functions;

// Модель решает сама (по умолчанию)
service.FunctionCallMode = FunctionCallMode.Auto;

// Всегда вызывать функцию
service.ForceFunctionName = "search_products";

// Отключить вызов функций
service.FunctionCallMode = FunctionCallMode.None;
```

[Claude Fable 5.1](fable-5-1.md) поддерживает сообщения о ходе работы, инструкции для одного хода и диагностику привязки thinking начиная с `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 доступен по приглашению. Оба отклоняют принудительный выбор инструмента.

## Массовая регистрация из класса

Регистрация всех методов с `[AiFunction]` одним вызовом:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // Сканирует экземплярные методы с [AiFunction]

// Для статических методов
service.WithStaticFunctions<MyTools>();
```

## Асинхронные обработчики

Для каждого `WithFunction` есть аналог `WithFunctionAsync`, принимающий `Func<..., Task<string>>`:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Получает данные из внешнего API",
    ("url", "URL для запроса", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

## Временное отключение функций

Отключите вызов функций для одного запроса без удаления регистрации:

```csharp
// Расширение — возвращает результат без функций
string answer = await service.AskWithoutFunctionsAsync("Ответьте напрямую");

// Или переключение свойства
service.WithoutFunctions();  // FunctionsDisabled = true
```

## FunctionBuilder

Программное создание определений функций:

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Возвращает текущую цену акции")
    .AddParameter("ticker", "string", "Тикер акции", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

<a id="tool-execution-contract"></a>

## Возврат объектов из асинхронных инструментов и отмена работы

Инструмент для чтения файла или базы данных часто возвращает объект после асинхронного ввода-вывода. Кнопка остановки должна передавать отмену и этой операции. Синхронный возврат объектов уже поддерживался; обновление согласует с ним асинхронный возврат и записывает исключения как ошибки.

Before: асинхронная функция должна была сама сериализовать результат. При возврате `Task<FileResult>` значение терялось, а вместо него передавалось `"Success"`.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Прочитать текстовый файл")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: верните объект напрямую и передайте внедрённый токен отмены операции ввода-вывода. Приложению не нужны новые обёртки результата или адаптеры.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Прочитать текстовый файл")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

Регистрация через `[AiFunction]` поддерживает обычные объекты, `Task<T>` и `ValueTask<T>`; нестроковые значения преобразуются в JSON. `string`, `Task<string>` и `ValueTask<string>` остаются исходным текстом без дополнительных кавычек JSON. Завершение `Task` и `ValueTask` без результата тоже ожидается. Синхронный возврат объектов сохраняется. Возврат null превращается в `"Done"`; завершённые `Task` / `ValueTask` без результата — в `"Success"`.

Библиотека распознаёт асинхронные значения и во время выполнения: ожидает `Task<T>`, возвращённый как `Task` или `object`, либо `ValueTask<T>`, возвращённый как `object`, и сериализует результат по тем же правилам. Каждый `ValueTask` используется только один раз.

Параметр `CancellationToken` передаёт библиотека; в схему аргументов для модели он не входит. Используйте `WithFunctions(...)` или `WithStaticFunctions<T>()` у сервиса или конструктора запроса.

Методы инструментов с `async void` отклоняются при регистрации. Возвращайте `Task` или `ValueTask`, чтобы можно было дождаться завершения, обработать ошибки и закончить очистку после отмены.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Прочитай report.txt и составь краткое изложение.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, отмена токена, переданного в `StartRunAsync`, или освобождение активного run передаётся локальным инструментам с поддержкой отмены. Функция должна использовать токен: код, игнорирующий его, нельзя остановить принудительно. Ещё не начатые вызовы пропускаются с результатом отмены; уже начатые функции ожидаются для сохранения пар вызов/результат в истории. Отменённый run не начинает новый раунд модели.

Если обработчик отмены выбрасывает исключение при неудачном запуске run или освобождении соединения MCP, очистка сеанса или транспорта всё равно выполняется. Исходная ошибка и ошибки очистки сохраняются, при необходимости вместе в `AggregateException`. Одновременные асинхронные вызовы `McpConnection.DisposeAsync()` ожидают одну и ту же очистку. Транспорт закрывается до ожидания завершения цикла чтения, чтобы могли завершиться операции чтения, которым необходимо закрытие соединения.

Чтобы поздний вызов инструмента не оставался в ожидании при завершении работы, после начала освобождения соединения новые операции `InitializeAsync`, `RefreshToolsAsync` и `CallToolAsync` отклоняются с `ObjectDisposedException`. Ответ с подходящим ID запроса, но неверным форматом тела пропускается, а запрос остаётся зарегистрированным. Его по-прежнему можно завершить последующим корректным ответом, отменой вызывающей стороны или очисткой соединения. Если чтение уже завершилось из-за закрытия потока сервером или ошибки транспорта, новые операции завершаются с `McpException`, а не ждут ответа, который больше не может прийти. Для продолжения создайте новое соединение.

При реальной ошибке выбрасывайте исключение. Исполнитель запишет `FunctionCallResult.IsError = true`, а не успешную строку `"Error: ..."`. Намеренно возвращённая строка остаётся обычным результатом. У отменённых результатов устанавливаются `IsCancelled = true` и `IsError = true`.

При программной регистрации используйте перегрузку `WithHandler` с двумя аргументами:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Прочитать текстовый файл")
    .AddParameter("path", "string", "Путь к файлу", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

Прежние строковые обработчики с одним аргументом поддерживаются. В прямом определении можно задать `HandlerWithCancellation` типа `Func<Dictionary<string, object>, CancellationToken, Task<string>>`. Присваивание `Handler` или `HandlerWithCancellation` заменяет один и тот же обработчик, а не регистрирует два выполнения. Этот низкоуровневый API по-прежнему возвращает строки; автоматическая сериализация объектов относится к регистрации методов.

Это обработка возврата и отмены локальных .NET-функций, не требующая нативной возможности провайдера `AllowAsync`. Остановка только чтения `run.StreamAsync(token)` прекращает наблюдение, а не run. См. [руководство Run](execution-api-transition.md) и [протокол провайдера](https://developers.openai.com/api/docs/guides/async-tool-calling).

## Асинхронные вызовы инструментов

Медленный запрос к базе данных или внешнему API не всегда должен останавливать всю работу модели. Например, пока загружается погода, модель может дать общие советы для поездки, не зависящие от результата. Асинхронный вызов инструмента позволяет продолжать такую независимую часть работы, а конкретный результат учесть после его получения.

Поддержка GPT-6 Astra и асинхронных вызовов инструментов доступна с `Mythosia.AI` 7.1.0; общие типы включены в `Mythosia.AI.Abstractions` 3.1.0.

По умолчанию `FunctionDefinition.AllowAsync` равен `false`. Задайте `true` или вызовите `FunctionBuilder.WithAsync()`, только если модель может продолжать работу во время выполнения этой функции. `WithAsync(false)` отключает разрешение. Одно определение функции и обработчик можно использовать с разными провайдерами.

При регистрации через атрибут доступно то же разрешение: `[AiFunction("lookup", "Запросить данные", AllowAsync = true)]`.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Возвращает пример погоды в Сеуле")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Проверь пример погоды в Сеуле. Пока ждёшь, назови три необходимые вещи для поездки.");
```

Mythosia отправляет `async: true` для GPT-6 Astra через Responses. Для неподдерживаемых моделей и API поле пропускается, а выполнение ждёт результата того же обработчика, не меняя `AllowAsync`. Провайдер также должен пометить фактический вызов как асинхронный (`FunctionCall.IsAsync`); разрешение не гарантирует асинхронное выполнение.

`WithFunctionAsync` регистрирует асинхронный обработчик .NET, а `FunctionExecutionMode.Parallel` управляет локальным выполнением обработчиков. Ни один из них не включает это разрешение автоматически. `AllowAsync` позволяет модели продолжать работу до получения результата функции. `FunctionExecutionMode` по-прежнему управляет обычными вызовами. Разрешённые асинхронные задания могут выполняться одновременно даже в режиме `Sequential`; на их отдельный пул распространяется общий предел `MaxConcurrency`.

Незавершённые задания принадлежат текущему запросу `GetCompletionAsync`, прежнему `service.StreamAsync` или `AIRun`, созданному через `StartRunAsync`. Результат каждого инструмента передаётся с исходным ID вызова; успешное окончание ждёт обработки всех ожидающих результатов. Это не отдельный сервис фоновых заданий. Прекращение чтения `run.StreamAsync()` останавливает только наблюдение, а не выполнение Run.

При использовании асинхронных инструментов `GetCompletionAsync` возвращает накопленные по порядку промежуточные независимые пояснения и итоговый текст после завершения запроса. Прежний `service.StreamAsync` и `run.StreamAsync()` выдают текст по мере поступления. `AIRunResult.Text` объединяет текстовые события Run независимо от того, читался ли поток.

Локальные инструменты могут возвращать объекты через `Task<T>` / `ValueTask<T>` и получать внедрённый `CancellationToken`. `run.Cancel()` или исходный токен передаёт отмену поддерживающим её инструментам; остановка только чтения потока — нет. Исключения записываются как ошибки. При отмене ожидающие вызовы пропускаются, а очистка ждёт начатые инструменты, игнорирующие токен. См. [результаты, ошибки и отмена](function-calling.md#tool-execution-contract).

Потоковая обработка запускает обработчики после получения полных вызовов функций и проверки допустимой границы ответа. Неполные вызовы функций не исполняются. Затем модель может продолжать следующий раунд, пока выполняются асинхронные задания. Если новых вызовов нет, а задания ещё активны, Mythosia ждёт их результатов перед продолжением. Пока есть ожидающие вызовы, автоматическое резюмирование и повтор при переполнении контекста отключены, чтобы незавершённые вызовы не исчезли из истории.

Perplexity: [Управление исследованием и инструментами](perplexity.md).
