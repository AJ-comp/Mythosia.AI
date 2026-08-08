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
