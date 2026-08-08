# Керування діалогом

## Як працює історія діалогу

Кожен виклик `GetCompletionAsync` або `StreamAsync` додається до внутрішнього списку повідомлень сервісу. Таким чином модель має контекст усіх попередніх ходів.

```csharp
await service.GetCompletionAsync("Мій улюблений колір — синій.");
var reply = await service.GetCompletionAsync("Який мій улюблений колір?");
// → "Ваш улюблений колір — синій."
```

Щоб почати заново:

```csharp
service.ActivateChat.ClearMessages();
```

## Політика сумаризації

### Навіщо потрібна автоматична сумаризація

Усі повідомлення історії надсилаються моделі з кожним запитом. У міру зростання діалогу виникають дві проблеми:

1. **Вартість** — довга історія означає більше вхідних токенів на запит
2. **Перевищення контексту** — якщо історія виходить за контекстне вікно моделі, запит просто не пройде

**`SummaryConversationPolicy`** автоматично стискає старі повідомлення в лаконічне резюме, зберігаючи останні повідомлення в оригіналі.

### За кількістю повідомлень

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);
```

### За кількістю токенів

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);
```

### Комбінований тригер (OR)

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,
    keepRecentCount: 7
);
```

Після налаштування сумаризація відбувається автоматично при виклику `GetCompletionAsync`.

### Як це працює

1. Перед кожним зверненням до моделі політика перевіряє, чи не перевищує діалог заданий поріг.
2. Якщо поріг перевищено, старі повідомлення сумаризуються в лаконічний текст за допомогою stateless-виклику LLM.
3. Резюме впроваджується як префікс системного повідомлення — модель сприймає його як попередній контекст.
4. Останні повідомлення (керовані параметрами `KeepRecentCount` або `KeepRecentTokens`) зберігаються дослівно.

При використанні токенних тригерів політика автоматично використовує **фактичну кількість вхідних токенів**, повідомлену API (з останньої стримінгової відповіді), замість локальної оцінки, що забезпечує точне спрацювання.

### Стримінг

Під час `StreamAsync` сумаризація автоматично не спрацьовує. Викличте її явно заздалегідь:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Продовжимо..."))
    Console.Write(chunk.Content);
```

## Збереження та відновлення резюме

Зберігайте резюме між сесіями, щоб модель пам''ятала контекст після перезапуску:

```csharp
// Зберегти
string saved = service.ConversationPolicy.CurrentSummary;

// Відновити в новій сесії
service.ConversationPolicy.LoadSummary(saved);
```
