# Виклик функцій

## Навіщо потрібен виклик функцій

LLM генерує лише текст — він не може сам перевірити погоду, звернутися до бази даних або викликати API. **Без** виклику функцій намір моделі доводиться розбирати вручну:

```csharp
// ❌ Без виклику функцій — ручний розбір наміру
var reply = await service.GetCompletionAsync("Яка погода в Києві?");

if (reply.Contains("погод"))
{
    var city = ExtractCity(reply); // крихкі регулярні вирази
    var weather = await weatherApi.GetAsync(city);
}
```

**З** викликом функцій модель сама вирішує, **коли** викликати код і **які аргументи** передати:

```csharp
// ✅ З викликом функцій — модель сама визначає намір і аргументи
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Отримує поточну погоду для вказаного міста",
        ("location", "Місто та країна", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("Яка погода в Києві?");
```

## Швидкий приклад

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Отримує поточну погоду для вказаного міста",
        ("location", "Місто та країна", required: true),
        (string location) => $"Погода в {location}: ясно, 22°C"
    );

var response = await service.GetCompletionAsync("Яка погода в Києві?");
```

## Визначення функцій через атрибути

Для складних функцій використовуйте атрибути `[AiFunction]` та `[AiParameter]`:

```csharp
using Mythosia.AI.Attributes;

[AiFunction("search_products", "Пошук по каталогу товарів")]
public static string SearchProducts(
    [AiParameter("Пошуковий запит", required: true)] string query,
    [AiParameter("Максимум результатів")] int limit = 5)
{
    return JsonSerializer.Serialize(results);
}
```

Потім зареєструйте:

```csharp
service.AddFunction(SearchProducts);
```

## Політика виклику функцій

Керуйте тим, коли модель може викликати функції:

```csharp
using Mythosia.AI.Models.Functions;

service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;     // Модель вирішує (за замовчуванням)
service.FunctionCallingPolicy = FunctionCallingPolicy.Required; // Завжди викликати
service.FunctionCallingPolicy = FunctionCallingPolicy.None;     // Вимкнути
```

## Масова реєстрація з класу

Реєстрація всіх методів із `[AiFunction]` одним викликом:

```csharp
var tools = new MyTools();
service.WithFunctions(tools);

service.WithStaticFunctions<MyTools>();  // Для статичних методів
```

## Асинхронні обробники

Для кожного `WithFunction` є аналог `WithFunctionAsync`, що приймає `Func<..., Task<string>>`:

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Отримує дані із зовнішнього API",
    ("url", "URL для запиту", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

## Тимчасове вимкнення функцій

Вимкніть виклик функцій для одного запиту без видалення реєстрації:

```csharp
string answer = await service.AskWithoutFunctionsAsync("Відповідайте напряму");

service.WithoutFunctions();  // FunctionsDisabled = true
```

## FunctionBuilder

Програмне створення визначень функцій:

```csharp
using Mythosia.AI.Builders;

var fn = FunctionBuilder
    .Create("get_stock_price", "Повертає поточну ціну акції")
    .AddParameter("ticker", "Тікер акції", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
