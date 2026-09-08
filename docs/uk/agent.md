# Агент (цикл ReAct)

Для завдань із кількома інструментами корисні ліміт раундів і керування поточною роботою. Спільний цикл уже надає ці можливості; див. [керування завданням через Run](execution-api-transition.md).

## Навіщо потрібен агентний цикл

Звичайний виклик функцій може виконати **кілька функцій з однієї відповіді моделі як упорядкований пакет** і продовжити наступні раунди інструментів. API Agent оформлює цей механізм як цільовий цикл ReAct із явним **обмеженням кроків**, повертаючи моделі результати кожного пакета, доки вона не сформує остаточну відповідь:

- «Знайдіть три провідні AI-компанії та порівняйте їхні котирування» — потрібно кілька пошуків і запитів до біржових даних
- «Знайдіть політику, перевірте статус замовлення та визначте, чи підлягає він поверненню» — послідовні виклики різних інструментів

`GetCompletionAsync` і `StartRunAsync` уже виконують спільний цикл моделі й інструментів. Старі агентні методи додають ліміт раундів на виклик і спеціальне перетворення помилок, а не незалежний планувальник чи механізм виконання.

## Керування завданням через Run

```csharp
// Спочатку зареєструйте інструменти на сервісі.
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Знайдіть політику, перевірте замовлення й поясніть результат.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

## Приклади для попереднього агентного API

Наступні приклади збережено для сумісності. `RunAgentAsync` і `RunAgentStreamAsync` видають попередження `[Obsolete]`; їхній попередній типовий ліміт — 10 раундів. Для нового коду використовуйте Run, а під час перенесення збережіть потрібний ліміт і обробку помилок.

Зареєструйте функції та викличте `RunAgentAsync` із метою:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Пошук в інтернеті",
        ("query", "Пошуковий запит", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Отримує поточну ціну акції",
        ("ticker", "Тікер акції", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "Які поточні ціни акцій трьох провідних AI-компаній?",
    maxSteps: 10
);

Console.WriteLine(result);
```

## maxSteps

`maxSteps` обмежує кількість раундів LLM→виклик функції. Якщо за відведені кроки результат не отримано, викидається `AgentMaxStepsExceededException`:

```csharp
try
{
    string result = await service.RunAgentAsync("Дослідіть і підготуйте звіт...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    Console.WriteLine($"Дострокове завершення: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Керування поведінкою агентного циклу по раундах:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Малий таймаут, мало раундів
var fastResult = await service.RunAgentAsync(
    "Дослідіть і підготуйте звіт...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Великий таймаут, багато раундів
var complexResult = await service.RunAgentAsync(
    "Дослідіть і підготуйте звіт...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## Контекст запиту для окремого виклику

`RunAgentAsync` та `RunAgentStreamAsync` приймають необов'язковий `AIRequestContext`, який дозволяє вставити динамічний prefix/suffix системного повідомлення, довідкові документи або замінити повідомлення-ціль — **обмежено одним запуском агента**, без зміни системного повідомлення сервісу чи історії діалогу.

```csharp
string result = await service.RunAgentAsync(
    goal: "Знайди політику повернення і перевір, чи підходить замовлення #1234.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Сьогоднішня дата: {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nЗавжди посилайся на використаний пункт політики."
    });
```

Стримінгова версія приймає той самий параметр:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Дослідь ціни акцій трьох провідних AI-компаній.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Часовий пояс користувача: {userTz}\n"
    }))
{
    // обробити вміст
}
```

`AIRequestContext` передається через `AsyncLocal`, але це не робить історію діалогу й політики змінюваного сервісу безпечними для паралельних операцій. Для незалежних одночасних завдань використовуйте окремі екземпляри сервісу.

Повний перелік доступних властивостей див. у документі [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Доступно з Mythosia.AI v6.3.0.

## Як це працює

На кожному кроці:

1. LLM отримує мету + історію + опис функцій
2. Якщо LLM викликає функцію → виконуємо, додаємо результат до історії
3. Якщо LLM повертає текст → цикл завершується, текст повертається як відповідь
4. Якщо досягнуто ліміт кроків → `AgentMaxStepsExceededException`
