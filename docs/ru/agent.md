# Агент (цикл ReAct)

## Зачем нужен агентный цикл

Обычный вызов функций может выполнить **несколько функций из одного ответа модели как упорядоченный пакет** и продолжить следующие раунды инструментов. API Agent оформляет этот механизм как целевой цикл ReAct с явным **ограничением шагов**, возвращая модели результаты каждого пакета, пока она не сформирует окончательный ответ:

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
service.DefaultPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

service.WithFastPolicy();    // Малый таймаут, мало раундов
service.WithComplexPolicy(); // Большой таймаут, много раундов
```

## Контекст запроса для отдельного вызова

`RunAgentAsync` и `RunAgentStreamAsync` принимают необязательный `AIRequestContext`, позволяющий внедрить динамический prefix/suffix системного сообщения, справочные документы или заменить сообщение-цель — **ограничено одним запуском агента**, без изменения системного сообщения сервиса или истории диалога.

```csharp
string result = await service.RunAgentAsync(
    goal: "Найди политику возврата и проверь, подходит ли под неё заказ #1234.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Сегодняшняя дата: {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nВсегда указывай использованный пункт политики."
    });
```

Стримящая версия принимает тот же параметр:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Исследуй цены акций трёх ведущих AI-компаний.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Часовой пояс пользователя: {userTz}\n"
    }))
{
    // обработать содержимое
}
```

Контекст распространяется через `AsyncLocal`, поэтому параллельные запуски агента в одном экземпляре сервиса не мешают друг другу.

Полный список доступных свойств см. в документе [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Доступно с Mythosia.AI v6.3.0.

## Как это работает

На каждом шаге:

1. LLM получает цель + историю + описание функций
2. Если LLM вызывает функцию → выполняем, добавляем результат в историю
3. Если LLM возвращает текст → цикл завершается, текст возвращается как ответ
4. Если достигнут лимит шагов → `AgentMaxStepsExceededException`
