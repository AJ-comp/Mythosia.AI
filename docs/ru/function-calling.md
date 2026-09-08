# Вызов функций

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

При использовании асинхронных инструментов `GetCompletionAsync` возвращает накопленные по порядку промежуточные независимые пояснения и итоговый текст после завершения запроса. Прежний `service.StreamAsync` и `run.StreamAsync()` выдают текст по мере поступления. `AIRun.Result` объединяет текстовые события Run независимо от того, читался ли поток.

Обработчики не получают токен отмены. Отмена выполнения, тайм-аут, ошибка или закрытие прежнего потока запроса ждут завершения уже запущенных обработчиков при очистке. Для Run остановка наблюдения этого не делает: отмените сам Run через `Cancel()`, его исходный токен или `DisposeAsync()`. Интеграция охватывает зарегистрированные обработчики функций; см. [официальное руководство API](https://developers.openai.com/api/docs/guides/async-tool-calling).

Потоковая обработка запускает обработчики после получения полных вызовов функций и проверки допустимой границы ответа. Неполные вызовы функций не исполняются. Затем модель может продолжать следующий раунд, пока выполняются асинхронные задания. Если новых вызовов нет, а задания ещё активны, Mythosia ждёт их результатов перед продолжением. Пока есть ожидающие вызовы, автоматическое резюмирование и повтор при переполнении контекста отключены, чтобы незавершённые вызовы не исчезли из истории.
