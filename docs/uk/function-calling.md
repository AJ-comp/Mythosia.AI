# Виклик функцій

> GPT-6 Sol/Luna: Потрібні Mythosia.AI 8.1.0 / Abstractions 4.1.0. [вибір моделі та вимоги](providers.md#gpt-6-sol-luna)

Для готової відповіді й кнопки Стоп передайте `cancellationToken` у `GetCompletionAsync`. Run потрібен для подій прогресу або підтримуваних додаткових вказівок. Див. [скасування відповіді](completions.md#completion-cancellation).

Для незалежних налаштувань і повторного використання варіантів застосовуйте [білдер запитів](request-building.md). Викликайте `CreateRequest(...)` перед `With...`. Властивості та fluent-методи сервісу зберігають попередню поведінку.

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

[Claude Fable 5.1](fable-5-1.md) підтримує повідомлення про перебіг роботи, інструкції для одного ходу та діагностику прив’язки thinking від `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 доступний за запрошенням. Обидва відхиляють примусовий вибір інструмента.

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

<a id="tool-execution-contract"></a>

## Повернення об’єктів з асинхронних інструментів і скасування роботи

Інструмент для читання файлів або бази даних часто повертає об’єкт після асинхронного введення-виведення. Кнопка зупинки має передавати скасування й цій операції. Синхронне повернення об’єктів уже підтримувалося; оновлення узгоджує з ним асинхронні результати та записує винятки як помилки.

Before: асинхронна функція мала сама серіалізувати результат. Повернення `Task<FileResult>` втрачало значення й передавало лише `"Success"`.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Прочитати текстовий файл")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After: поверніть об’єкт безпосередньо й передайте впроваджений токен скасування операції введення-виведення. Застосунку не потрібні нові обгортки результату або адаптери.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Прочитати текстовий файл")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

Реєстрація `[AiFunction]` підтримує звичайні об’єкти, `Task<T>` і `ValueTask<T>`; значення, які не є рядками, перетворюються на JSON. `string`, `Task<string>` і `ValueTask<string>` залишаються звичайним текстом без додаткових лапок JSON. Завершення `Task` і `ValueTask` без результату теж очікується. Синхронне повернення об’єктів зберігається. Повернення null стає `"Done"`; завершені `Task` / `ValueTask` без результату — `"Success"`.

Бібліотека також розпізнає асинхронні значення під час виконання: очікує `Task<T>`, повернений як `Task` або `object`, чи `ValueTask<T>`, повернений як `object`, і серіалізує результат за тими самими правилами. Кожен `ValueTask` використовується лише один раз.

Параметр `CancellationToken` передає бібліотека; його немає у схемі аргументів для моделі. Реєструйте методи через `WithFunctions(...)` або `WithStaticFunctions<T>()` сервісу чи конструктора запиту.

Методи інструментів з `async void` відхиляються під час реєстрації. Повертайте `Task` або `ValueTask`, щоб можна було дочекатися завершення, побачити помилки та закінчити очищення після скасування.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Прочитай report.txt і підсумуй його.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, скасування токена, переданого до `StartRunAsync`, або звільнення активного run передається локальним інструментам із підтримкою скасування. Функція повинна використовувати токен: код, що його ігнорує, не можна зупинити примусово. Ще не розпочаті виклики пропускаються з результатом скасування; уже розпочаті функції очікуються, щоб зберегти пари виклик/результат в історії. Скасований run не починає нового раунду моделі.

Якщо обробник скасування викидає виняток під час невдалого запуску run або звільнення з’єднання MCP, очищення сеансу чи транспорту все одно виконується. Початкова помилка й помилки очищення зберігаються, за потреби разом в `AggregateException`. Одночасні асинхронні виклики `McpConnection.DisposeAsync()` очікують те саме очищення. Транспорт закривається до очікування завершення циклу читання, щоб могли завершитися операції читання, яким потрібне закриття з’єднання.

Щоб пізній виклик інструмента не залишався в очікуванні під час завершення роботи, після початку звільнення з’єднання нові операції `InitializeAsync`, `RefreshToolsAsync` і `CallToolAsync` відхиляються з `ObjectDisposedException`. Відповідь із відповідним ID запиту, але неправильним форматом тіла пропускається, а запит залишається зареєстрованим. Його надалі можна завершити наступною коректною відповіддю, скасуванням з боку виклику або очищенням з’єднання. Якщо читання вже завершилося через закриття потоку сервером або помилку транспорту, нові операції завершуються з `McpException`, а не чекають відповіді, яка більше не може надійти. Для продовження створіть нове з’єднання.

За справжньої помилки генеруйте виняток. Виконавець запише `FunctionCallResult.IsError = true`, а не успішний рядок `"Error: ..."`. Навмисно повернутий рядок залишається звичайним результатом. Скасовані результати мають `IsCancelled = true` та `IsError = true`.

Для програмної реєстрації використовуйте перевантаження `WithHandler` із двома аргументами:

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Прочитати текстовий файл")
    .AddParameter("path", "string", "Шлях до файлу", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

Наявні рядкові обробники з одним аргументом підтримуються. У прямому визначенні можна задати `HandlerWithCancellation` типу `Func<Dictionary<string, object>, CancellationToken, Task<string>>`. Присвоєння `Handler` або `HandlerWithCancellation` замінює той самий обробник, а не реєструє два виконання. Цей низькорівневий API і далі повертає рядки; автоматична серіалізація об’єктів належить до реєстрації методів.

Це обробка повернення та скасування локальних .NET-функцій, яка не потребує власної можливості провайдера `AllowAsync`. Зупинка лише читача `run.StreamAsync(token)` завершує спостереження, а не run. Див. [настанову Run](execution-api-transition.md) і [протокол провайдера](https://developers.openai.com/api/docs/guides/async-tool-calling).

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

Mythosia надсилає `async: true` для GPT-6 Astra / Sol / Luna через Responses. Для моделей і API без підтримки поле пропускається, а виконання очікує результат того самого обробника, не змінюючи `AllowAsync`. Провайдер також має позначити фактичний виклик як асинхронний (`FunctionCall.IsAsync`); дозвіл не гарантує асинхронне виконання.

`WithFunctionAsync` реєструє асинхронний обробник .NET, а `FunctionExecutionMode.Parallel` керує локальним виконанням обробників. Жоден із них не вмикає цей дозвіл автоматично. `AllowAsync` дає моделі змогу працювати далі до отримання результату функції. `FunctionExecutionMode` і далі керує звичайними викликами. Дозволені асинхронні завдання можуть виконуватися одночасно навіть у режимі `Sequential`; для їхнього окремого пулу діє спільна межа `MaxConcurrency`.

Незавершені завдання належать поточному запиту `GetCompletionAsync`, попередньому `service.StreamAsync` або `AIRun`, створеному через `StartRunAsync`. Результат кожного інструмента передається з початковим ID виклику; успішне завершення чекає на обробку всіх очікуваних результатів. Це не окремий сервіс фонових завдань. Припинення читання `run.StreamAsync()` зупиняє лише спостереження, а не виконання Run.

За використання асинхронних інструментів `GetCompletionAsync` повертає накопичені за порядком проміжні незалежні пояснення й остаточний текст після завершення запиту. Попередній `service.StreamAsync` і `run.StreamAsync()` видають текст у міру надходження. `AIRunResult.Text` об’єднує текстові події Run незалежно від того, чи читався потік.

Локальні інструменти можуть повертати об’єкти через `Task<T>` / `ValueTask<T>` та отримувати впроваджений `CancellationToken`. `run.Cancel()` або початковий токен передає скасування інструментам, що його підтримують; зупинка лише читання потоку — ні. Винятки записуються як помилки. Скасування пропускає виклики в черзі, а очищення очікує запущені інструменти, які ігнорують токен. Див. [результати, помилки та скасування](function-calling.md#tool-execution-contract).

Потокова обробка запускає обробники після отримання повних викликів функцій і перевірки допустимої межі відповіді. Неповні виклики функцій не виконуються. Потім модель може продовжувати наступний раунд, поки працюють асинхронні завдання. Якщо нових викликів немає, а завдання ще активні, Mythosia чекає їхніх результатів перед продовженням. За наявності очікуваних викликів автоматичне підсумовування й повтор через переповнення контексту вимкнені, щоб незавершені виклики не зникли з історії.

Perplexity: [Керування дослідженням та інструментами](perplexity.md).
