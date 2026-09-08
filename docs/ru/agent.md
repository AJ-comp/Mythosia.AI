# Агент (цикл ReAct)

Для задач с несколькими инструментами полезны лимит раундов и управление текущей работой. Общий цикл уже предоставляет эти возможности; см. [управление задачей через Run](execution-api-transition.md).

## Зачем нужен агентный цикл

Обычный вызов функций может выполнить **несколько функций из одного ответа модели как упорядоченный пакет** и продолжить следующие раунды инструментов. API Agent оформляет этот механизм как целевой цикл ReAct с явным **ограничением шагов**, возвращая модели результаты каждого пакета, пока она не сформирует окончательный ответ:

- «Найдите три ведущие AI-компании и сравните их котировки» — нужно несколько поисков и запросов к биржевым данным
- «Найдите политику, проверьте статус заказа и определите, подлежит ли он возврату» — последовательные вызовы разных инструментов

`GetCompletionAsync` и `StartRunAsync` уже выполняют общий цикл модели и инструментов. Старые агентные методы добавляют лимит раундов на вызов и специальное преобразование ошибок, а не независимый планировщик или механизм исполнения.

## Управление задачей через Run

```csharp
// Сначала зарегистрируйте инструменты на сервисе.
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Найдите политику, проверьте заказ и объясните результат.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

## Примеры для прежнего агентного API

Следующие примеры сохранены для совместимости. `RunAgentAsync` и `RunAgentStreamAsync` выдают предупреждения `[Obsolete]`; их прежний лимит по умолчанию — 10 раундов. Для нового кода используйте Run, а при переносе сохраните нужный лимит и обработку ошибок.

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
    TimeoutSeconds = 30
};

service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Малый таймаут, мало раундов
var fastResult = await service.RunAgentAsync(
    "Исследуйте и подготовьте отчёт...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Большой таймаут, много раундов
var complexResult = await service.RunAgentAsync(
    "Исследуйте и подготовьте отчёт...", maxSteps: service.DefaultPolicy.MaxRounds);
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

`AIRequestContext` передаётся через `AsyncLocal`, но это не делает историю диалога и политики изменяемого сервиса безопасными для параллельных операций. Для независимых одновременных задач используйте отдельные экземпляры сервиса.

Полный список доступных свойств см. в документе [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Доступно с Mythosia.AI v6.3.0.

## Как это работает

На каждом шаге:

1. LLM получает цель + историю + описание функций
2. Если LLM вызывает функцию → выполняем, добавляем результат в историю
3. Если LLM возвращает текст → цикл завершается, текст возвращается как ответ
4. Если достигнут лимит шагов → `AgentMaxStepsExceededException`
