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
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Пошук по каталогу товарів")]
    public string SearchProducts(
        [AiParameter("Пошуковий запит", required: true)] string query,
        [AiParameter("Максимум результатів")] int limit = 5)
    {
        return JsonSerializer.Serialize(results);
    }
}
```

Потім зареєструйте:

```csharp
service.WithFunctions(new ProductFunctions());
```

## Політика виклику функцій

Керуйте тим, коли модель може викликати функції:

```csharp
using Mythosia.AI.Models.Functions;

service.FunctionCallMode = FunctionCallMode.Auto;        // Модель вирішує (за замовчуванням)
service.ForceFunctionName = "search_products";            // Примусово викликати конкретну функцію
service.FunctionCallMode = FunctionCallMode.None;        // Вимкнути
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
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Повертає поточну ціну акції")
    .AddParameter("ticker", "string", "Тікер акції", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

## Асинхронні виклики інструментів

Повільний запит до бази даних або зовнішнього API не завжди має зупиняти всю роботу моделі. Наприклад, поки завантажується погода, модель може дати загальні поради для подорожі, що не залежать від результату. Асинхронний виклик інструмента дозволяє продовжувати таку незалежну частину роботи, а конкретний результат врахувати після його отримання.

Підтримка GPT-6 Astra та асинхронних викликів інструментів доступна з `Mythosia.AI` 7.1.0; спільні типи включено до `Mythosia.AI.Abstractions` 3.1.0.

За замовчуванням `FunctionDefinition.AllowAsync` має значення `false`. Установіть `true` або викличте `FunctionBuilder.WithAsync()`, лише якщо модель може продовжувати роботу під час виконання цієї функції. `WithAsync(false)` вимикає дозвіл. Одне визначення функції та обробник можна використовувати з різними провайдерами.

Під час реєстрації через атрибут доступний той самий дозвіл: `[AiFunction("lookup", "Запитати дані", AllowAsync = true)]`.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Повертає приклад погоди в Сеулі")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Перевір приклад погоди в Сеулі. Поки чекаєш, назви три необхідні речі для подорожі.");
```

Mythosia надсилає `async: true` для GPT-6 Astra через Responses. Для моделей і API без підтримки поле пропускається, а виконання очікує результат того самого обробника, не змінюючи `AllowAsync`. Провайдер також має позначити фактичний виклик як асинхронний (`FunctionCall.IsAsync`); дозвіл не гарантує асинхронне виконання.

`WithFunctionAsync` реєструє асинхронний обробник .NET, а `FunctionExecutionMode.Parallel` керує локальним виконанням обробників. Жоден із них не вмикає цей дозвіл автоматично. `AllowAsync` дає моделі змогу працювати далі до отримання результату функції. `FunctionExecutionMode` і далі керує звичайними викликами. Дозволені асинхронні завдання можуть виконуватися одночасно навіть у режимі `Sequential`; для їхнього окремого пулу діє спільна межа `MaxConcurrency`.

Незавершені завдання належать поточному запиту `GetCompletionAsync`, попередньому `service.StreamAsync` або `AIRun`, створеному через `StartRunAsync`. Результат кожного інструмента передається з початковим ID виклику; успішне завершення чекає на обробку всіх очікуваних результатів. Це не окремий сервіс фонових завдань. Припинення читання `run.StreamAsync()` зупиняє лише спостереження, а не виконання Run.

За використання асинхронних інструментів `GetCompletionAsync` повертає накопичені за порядком проміжні незалежні пояснення й остаточний текст після завершення запиту. Попередній `service.StreamAsync` і `run.StreamAsync()` видають текст у міру надходження. `AIRun.Result` об’єднує текстові події Run незалежно від того, чи читався потік.

Обробники не отримують токена скасування. Скасування виконання, тайм-аут, помилка чи закриття попереднього потоку запиту чекають завершення вже запущених обробників під час очищення. Для Run зупинка спостереження цього не робить: скасуйте сам Run через `Cancel()`, його початковий токен або `DisposeAsync()`. Інтеграція охоплює зареєстровані обробники функцій; див. [офіційну настанову API](https://developers.openai.com/api/docs/guides/async-tool-calling).

Потокова обробка запускає обробники після отримання повних викликів функцій і перевірки допустимої межі відповіді. Неповні виклики функцій не виконуються. Потім модель може продовжувати наступний раунд, поки працюють асинхронні завдання. Якщо нових викликів немає, а завдання ще активні, Mythosia чекає їхніх результатів перед продовженням. За наявності очікуваних викликів автоматичне підсумовування й повтор через переповнення контексту вимкнені, щоб незавершені виклики не зникли з історії.
