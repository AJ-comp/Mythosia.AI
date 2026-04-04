# Агент (цикл ReAct)

## Зачем нужен агентный цикл

При обычном вызове функций модель делает **один** вызов за запрос. Но многие реальные задачи требуют **нескольких шагов**, когда модель самостоятельно планирует и выполняет действия:

- «Найдите три ведущие AI-компании и сравните их котировки» — нужно несколько поисков и запросов к биржевым данным
- «Найдите политику, проверьте статус заказа и определите, подлежит ли он возврату» — последовательные вызовы разных инструментов

**Агентный цикл** (паттерн ReAct: рассуждение → действие → наблюдение → повтор) автоматизирует эту оркестрацию — модель сама решает, что делать на каждом шаге, пока не придёт к ответу.

## Базовое использование

Зарегистрируйте функции и вызовите `RunAgentAsync` с целью:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Поиск в интернете",
        ("query", "Поисковый запрос", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Получает текущую цену акции",
        ("ticker", "Тикер акции", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "Каковы текущие цены акций трёх ведущих AI-компаний?",
    maxSteps: 10
);

Console.WriteLine(result);
```

## maxSteps

`maxSteps` ограничивает число раундов LLM→вызов функции. Если за отведённые шаги результат не получен, выбрасывается `AgentMaxStepsExceededException`:

```csharp
try
{
    string result = await service.RunAgentAsync("Исследуйте и подготовьте отчёт...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    Console.WriteLine($"Досрочное завершение: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Управление поведением агентного цикла по раундам:

```csharp
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

service.WithFastPolicy();    // Малый таймаут, мало раундов
service.WithComplexPolicy(); // Большой таймаут, много раундов
```

## Как это работает

На каждом шаге:

1. LLM получает цель + историю + описание функций
2. Если LLM вызывает функцию → выполняем, добавляем результат в историю
3. Если LLM возвращает текст → цикл завершается, текст возвращается как ответ
4. Если достигнут лимит шагов → `AgentMaxStepsExceededException`
