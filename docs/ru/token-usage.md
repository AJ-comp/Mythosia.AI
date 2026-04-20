# Использование токенов

Использование токенов показывает, сколько токенов запрос к модели потратил на вход, выход, кэш и рассуждение. В Mythosia.AI эти данные приходят в `TokenUsage` внутри streaming-событий.

Это особенно важно, когда ответ не ограничивается одним вызовом LLM. Обычный ответ чаще всего укладывается в один раунд. Агент или сценарий с function calling может сначала вызвать модель, затем выполнить инструмент, а потом снова вызвать модель уже с результатом инструмента. Поэтому полезно различать два значения.

- `RoundUsage` показывает расход одного LLM-раунда.
- `Completion.Usage` показывает накопленный расход всего stream-запуска.

## Что такое раунд?

«Раунд» — это одна полная поездка туда и обратно к модели: приложение отправляет промпт, модель отвечает, обмен завершён. Обычное сообщение в чате — это ровно один раунд.

Function calling и агенты добавляют дополнительные раунды автоматически. Вот конкретный пример — пользователь спрашивает: *«Какая сейчас погода в Москве?»*

**Раунд 1 — выбор инструмента**

Приложение отправляет сообщение пользователя модели. Модель не знает текущей погоды, поэтому вместо прямого ответа возвращает запрос на вызов функции: *«Пожалуйста, вызовите `GetWeather("Moscow")`».* Ответ модели здесь заканчивается.

**Между раундами**

Приложение выполняет `GetWeather("Moscow")` и получает результат: `«15°C, облачно»`.

**Раунд 2 — финальный ответ**

Приложение отправляет результат функции обратно модели как новое сообщение. Теперь у модели есть всё необходимое, и она пишет финальный ответ пользователю: *«Сейчас в Москве 15°C и облачно».*

Одно сообщение пользователя породило два LLM-раунда. Если бы модели потребовалось вызвать ещё один инструмент, был бы третий раунд.

`RoundUsage` срабатывает после каждого отдельного раунда и содержит только токены этого раунда. `Completion.Usage` срабатывает один раз в конце и содержит сумму по всем раундам.

## Зачем это нужно

Для индикатора контекста в чат-интерфейсе обычно нужен последний `RoundUsage.Usage.TotalTokens`. Это значение ближе всего к ответу на вопрос: "каким будет размер следующего входа модели, если разговор продолжится прямо сейчас?"

Для логов, диагностики и анализа стоимости используйте `Completion.Usage.TotalTokens`. Это накопленное значение за весь run, включая все раунды function calling или агента.

Для настройки производительности полезны поля кэша и рассуждения. По ним видно, переиспользовал ли provider вход из кэша и сколько токенов ушло на скрытое reasoning.

## Модель событий

| Событие | Значение | Где использовать |
|---|---|---|
| `StreamingContentType.RoundUsage` | Расход только что завершенного LLM-раунда | Индикатор контекста, отладка по раундам |
| `StreamingContentType.Completion` | Финальное событие с накопленным расходом | Логи, диагностика, отчеты по стоимости |

`RoundUsage.Usage` не является накопительным значением. Если раунд 1 потратил 10 100 токенов, а раунд 2 потратил 14 000, итоговый `Completion.Usage.TotalTokens` может быть 24 100, но последний `RoundUsage.Usage.TotalTokens` останется 14 000.

| Свойство | Значение |
|---|---|
| `RoundIndex` | Номер LLM-раунда, начиная с 1 |
| `IsFinalRound` | `true`, если это последний LLM-раунд в stream |

События usage появляются, когда provider возвращает данные об использовании. Для них не нужно включать `IncludeMetadata = true`.

## Итоговое накопленное использование

Используйте `Completion.Usage`, когда нужен общий расход streaming-запроса.

```csharp
await foreach (var chunk in service.StreamAsync("Объясни квантовые вычисления", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

Для одного LLM-раунда это значение обычно близко к `RoundUsage`. Для агента это сумма всех LLM-раундов.

## Индикатор токенов в UI

Для индикатора размера контекста используйте последний `RoundUsage`.

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

Последний раунд модели видит самое свежее состояние разговора, включая результаты инструментов, добавленные во время run. Поэтому последний `RoundUsage.TotalTokens` лучше всего подходит для чат-интерфейса.

## Function Calling и агенты

В сценариях с function calling модель может запускаться несколько раз. Читайте каждый `RoundUsage`, храните последний для UI, а в конце используйте `Completion.Usage` для общего итога.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}
```

## Кэш и reasoning

Если provider возвращает эти данные, `TokenUsage` также содержит поля кэша и reasoning.

| Свойство | Значение |
|---|---|
| `InputTokens` | Токены prompt/input |
| `OutputTokens` | Токены, сгенерированные моделью |
| `TotalTokens` | Вход + выход в рамках события |
| `CachedInputTokens` | Входные токены, взятые из кэша |
| `CacheCreationTokens` | Токены, записанные в кэш |
| `ReasoningTokens` | Токены скрытого внутреннего reasoning |
| `VisibleOutputTokens` | Выходные токены без reasoning |

## Замечания по provider-ам

Разные provider-ы прикрепляют usage к разным chunk-ам stream-а. Mythosia.AI нормализует это в события `RoundUsage` и финальный `Completion`.

Самый тонкий случай - Gemini: usage может прийти в text- или status-chunk-е, а иногда даже после function-call chunk-а. Библиотека дочитывает stream достаточно долго, чтобы собрать usage перед переходом к следующему раунду.

В клиентском коде лучше использовать нормализованные `RoundUsage` и `Completion.Usage`, а не разбирать provider-specific metadata вручную.
