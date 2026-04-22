# Використання токенів

Використання токенів показує, скільки токенів запит до моделі витратив на вхід, вихід, кеш і reasoning. У Mythosia.AI ці дані приходять через `TokenUsage` у streaming-подіях.

Це особливо важливо, коли відповідь не завершується одним викликом LLM. Звичайна відповідь часто має один раунд. Agent або flow з function calling може викликати модель, виконати інструмент, а потім ще раз викликати модель із результатом інструмента. Тому варто розрізняти два значення.

- `RoundUsage` показує usage одного LLM-раунду.
- `Completion.Usage` показує накопичений usage усього stream-запуску.

> [!NOTE]
> Ця сторінка передбачає, що ви вже знаєте, що таке **LLM-раунд**. Коротко: один раунд = один обмін запит–відповідь між застосунком і моделлю. Function calling може породжувати кілька раундів для одного повідомлення користувача. Покрокове пояснення див. у [Основні поняття — Що таке раунд?](core-concepts.md#що-таке-раунд).

## Навіщо це потрібно

Для індикатора контексту в чат-інтерфейсі зазвичай потрібен останній `RoundUsage.Usage.TotalTokens`. Це значення найближче до відповіді на питання: "яким буде розмір наступного входу моделі, якщо розмова продовжиться зараз?"

Для логів, діагностики й аналізу вартості використовуйте `Completion.Usage.TotalTokens`. Це накопичене значення для всього run, включно з усіма раундами function calling або agent.

Для налаштування продуктивності допомагають поля кешу й reasoning. Вони показують, чи provider повторно використав вхід із кешу і скільки токенів пішло на приховане reasoning.

## Модель подій

| Подія | Значення | Основне використання |
|---|---|---|
| `StreamingContentType.RoundUsage` | Usage щойно завершеного LLM-раунду | Індикатор контексту, debug по раундах |
| `StreamingContentType.Completion` | Фінальна подія з накопиченим usage | Логи, діагностика, звіти про вартість |

`RoundUsage.Usage` не є накопиченим значенням. Якщо раунд 1 використав 10 100 токенів, а раунд 2 використав 14 000, фінальний `Completion.Usage.TotalTokens` може бути 24 100, але останній `RoundUsage.Usage.TotalTokens` залишиться 14 000.

| Властивість | Значення |
|---|---|
| `RoundIndex` | Номер LLM-раунду, починаючи з 1 |
| `IsFinalRound` | `true`, якщо це останній LLM-раунд у stream |

Usage-події з'являються, коли provider повертає usage-дані. Для цього не потрібно вмикати `IncludeMetadata = true`.

## Фінальне накопичене використання

Використовуйте `Completion.Usage`, коли потрібен загальний usage streaming-запиту.

```csharp
await foreach (var chunk in service.StreamAsync("Поясни квантові обчислення", StreamOptions.FullOptions))
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

Для одного LLM-раунду це значення зазвичай близьке до `RoundUsage`. Для agent це сума всіх LLM-раундів.

## Індикатор токенів в UI

Для індикатора розміру контексту використовуйте останній `RoundUsage`.

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

Останній раунд моделі бачить найсвіжіший стан розмови, включно з результатами інструментів, доданими під час run. Тому останній `RoundUsage.TotalTokens` найкраще підходить для чат-інтерфейсу.

## Function Calling та agents

У flow з function calling модель може запускатися кілька разів. Читайте кожен `RoundUsage`, зберігайте останній для UI, а в кінці використовуйте `Completion.Usage` для загального підсумку.

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

## Кеш і reasoning

Якщо provider повертає ці дані, `TokenUsage` також містить поля кешу й reasoning.

| Властивість | Значення |
|---|---|
| `InputTokens` | Токени prompt/input |
| `OutputTokens` | Токени, згенеровані моделлю |
| `TotalTokens` | Вхід + вихід у межах події |
| `CachedInputTokens` | Вхідні токени, отримані з кешу |
| `CacheCreationTokens` | Токени, записані в кеш |
| `ReasoningTokens` | Токени прихованого внутрішнього reasoning |
| `VisibleOutputTokens` | Вихідні токени без reasoning |

## Навіщо використовувати нормалізовані події

Різні provider-и прикріплюють usage до різних stream-chunk-ів. Найбільше нюансів має Gemini: usage може прийти в text або status chunk, іноді навіть після function-call chunk, тому бібліотека дочитує stream достатньо довго, щоб зібрати usage перед переходом до наступного раунду. Mythosia.AI бере на себе ці відмінності між provider-ами й нормалізує їх у події `RoundUsage` і фінальний `Completion.Usage`, тож у клієнтському коді не парсіть provider-specific metadata вручну, а використовуйте нормалізовані `RoundUsage` і `Completion.Usage`.
