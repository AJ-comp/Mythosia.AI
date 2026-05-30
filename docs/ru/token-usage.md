# Использование токенов

Использование токенов показывает, сколько токенов запрос к модели потратил на вход, выход, кэш и рассуждение. В Mythosia.AI эти данные приходят в `TokenUsage` внутри streaming-событий.

Это особенно важно, когда ответ не ограничивается одним вызовом LLM. Обычный ответ чаще всего укладывается в один раунд. Агент или сценарий с function calling может сначала вызвать модель, затем выполнить инструмент, а потом снова вызвать модель уже с результатом инструмента. Поэтому полезно различать два значения.

- `RoundUsage` показывает расход одного LLM-раунда.
- `Completion.Usage` показывает накопленный расход всего stream-запуска.

> [!NOTE]
> Эта страница предполагает, что вы уже знаете, что такое **LLM-раунд**. Вкратце: один раунд = один обмен запрос–ответ между приложением и моделью. При function calling одно сообщение пользователя может породить несколько раундов. Пошаговое объяснение см. в [Основные понятия — Что такое раунд?](core-concepts.md#что-такое-раунд).

## Зачем это нужно

Для индикатора контекста в чат-интерфейсе обычно нужен последний `RoundUsage.Usage.InputTokens`. Это значение ближе всего к ответу на вопрос: "каким будет размер следующего входа модели, если разговор продолжится прямо сейчас?"

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
        UpdateContextTokenMeter(chunk.Usage.InputTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

Последний раунд модели видит самое свежее состояние разговора, включая результаты инструментов, добавленные во время run. Поэтому последний `RoundUsage.Usage.InputTokens` лучше всего подходит для чат-интерфейса.

<a id="how-context-size-changes"></a>

## Как меняется размер контекста

Думайте о размере контекста как о размере входа последнего вызова модели, а не как о накопительной сумме. Более поздний раунд уже содержит элементы разговора, сохранившиеся из предыдущих раундов. Если складывать входы разных раундов, вы дважды посчитаете один и тот же prompt, определения инструментов и историю.

Например:

| Шаг | Что добавлено перед этим вызовом модели | Примерные входные токены | Индикатор контекста UI |
|---|---|---:|---:|
| Раунд 1 | System prompt, инструменты, история, сообщение пользователя | 20 000 | 20 000 |
| Между раундами | Вывод tool call — 100 токенов; результат инструмента — 5 000 токенов | нет LLM-вызова | без изменений |
| Раунд 2 | Вход раунда 1 + сообщение tool call + результат инструмента | 25 100 + overhead | 25 100 + overhead |
| Вывод раунда 2 | Модель генерирует 3 000 токенов, и нужен еще один раунд | нет LLM-вызова | без изменений |
| Раунд 3 | Вход раунда 2 + вывод раунда 2, плюс новый результат инструмента, если он есть | 28 100 + overhead | 28 100 + overhead |
| Вывод раунда 3 | Модель генерирует финальный ответ на 2 000 токенов | нет LLM-вызова | без изменений |
| Следующее сообщение пользователя | Предыдущий финальный ответ и новое сообщение пользователя теперь входят в следующий input | около 30 100 + новое сообщение + overhead | заменяется `InputTokens` нового раунда |

Если раунд 3 является финальным, индикатор контекста должен показывать примерно **28 100 + overhead**, а не 30 100 и не сумму всех раундов. Финальный ответ на 2 000 токенов повлияет на следующий вызов модели, потому что станет частью истории разговора.

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
        Console.WriteLine($"Round {chunk.RoundIndex}: input={latestRound.InputTokens}, total={latestRound.TotalTokens} tokens");
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

## Зачем использовать нормализованные события

Разные provider-ы прикрепляют usage к разным chunk-ам stream-а. Самый тонкий случай — Gemini: usage может прийти в text- или status-chunk-е, а иногда даже после function-call chunk-а, поэтому библиотека дочитывает stream достаточно долго, чтобы собрать usage перед переходом к следующему раунду. Mythosia.AI берёт на себя эти различия между provider-ами и нормализует их в события `RoundUsage` и финальный `Completion.Usage`, поэтому в клиентском коде не разбирайте provider-specific metadata вручную, а используйте нормализованные `RoundUsage` и `Completion.Usage`.
