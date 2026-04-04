# Агент (цикл ReAct)

## Навіщо потрібен агентний цикл

При звичайному виклику функцій модель робить **один** виклик за запит. Але багато реальних задач потребують **кількох кроків**, де модель самостійно планує та виконує дії:

- «Знайдіть три провідні AI-компанії та порівняйте їхні котирування» — потрібно кілька пошуків і запитів до біржових даних
- «Знайдіть політику, перевірте статус замовлення та визначте, чи підлягає він поверненню» — послідовні виклики різних інструментів

**Агентний цикл** (патерн ReAct: міркування → дія → спостереження → повтор) автоматизує цю оркестрацію — модель сама вирішує, що робити на кожному кроці, доки не дійде до відповіді.

## Базове використання

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

service.WithFastPolicy();    // Малий таймаут, мало раундів
service.WithComplexPolicy(); // Великий таймаут, багато раундів
```

## Як це працює

На кожному кроці:

1. LLM отримує мету + історію + опис функцій
2. Якщо LLM викликає функцію → виконуємо, додаємо результат до історії
3. Якщо LLM повертає текст → цикл завершується, текст повертається як відповідь
4. Якщо досягнуто ліміт кроків → `AgentMaxStepsExceededException`
