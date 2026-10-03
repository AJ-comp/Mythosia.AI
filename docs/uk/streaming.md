# Потокова передача

Адаптер потокової передачі через callback скасовує виробника й чекає на нього при ранньому виході. Скасування Run, тайм-аут, помилка спостерігача та `DisposeAsync` чекають очищення перед завершенням `Result` і звільненням блокування. Робота, що ігнорує скасування, може затримати завершення; обидві помилки зберігаються. `ContextRecoveryMaxRetries` використовує збережене значення. Припинення лише спостереження `run.StreamAsync()` не скасовує Run. Під час отримання тіла успішної SSE-відповіді діє окреме [обмеження скасування](#sse-acquisition-cancellation-limitation).

> Claude Sonnet 5.5: Потрібні Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Налаштування та міграція](providers.md#claude-sonnet-55)

В adaptive використовуйте `ClaudeThinkingDisplay.Updates` для читабельного поступу інструментів або `Summarized` для стислих міркувань. Читайте `StreamingContentType.Reasoning`, а після звичайного завершення — `LastThinkingContent`. Метод adaptive без аргументу display використовує `Summarized`, на відміну від незмінених налаштувань. `between_tools` повертає поступ автоматично; фіксований інтервал не гарантовано.

> Grok 4.7: Потрібні Mythosia.AI 8.1.0 / Abstractions 4.1.0. [вибір моделі, міркування та швидкість обробки](providers.md#grok-47)

Для відповіді, витрат і джерел разом використовуйте знімок `AIRunResult`, який повертає `await run.Result`. Рядок міститься в `result.Text`; читати потік не потрібно. Це зміна Mythosia.AI 8.0.0; типи повернення `GetCompletionAsync` і `StructuredStreamRun<T>.Result` збережено. [Результат Run і міграція](execution-api-transition.md#run-result).


Для незалежних налаштувань і повторного використання варіантів застосовуйте [білдер запитів](request-building.md). Викликайте `CreateRequest(...)` перед `With...`. Властивості та fluent-методи сервісу зберігають попередню поведінку.

Показуйте текст у міру надходження, щоб користувач бачив перебіг підготовки відповіді. Як додати події інструментів і кнопку зупинки, описано в [настанові щодо Run](execution-api-transition.md).

```csharp
await using var run = await service.StartRunAsync(
    "Підсумуйте документи.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

**Відповіді Claude з помилкою:** Скасування й тайм-аут політики запиту переривають також зависле читання тіла HTTP-помилки під час потокової передачі або Run. Після очищення Run можна запустити наступний Run на тому самому сервісі. Скасування з боку виклику спричиняє `OperationCanceledException`, тайм-аут політики — `AIServiceException`. Скасування керує локальним транспортом і кооперативним очищенням, але не гарантує припинення обробки або нарахування плати провайдером.

**Очищення відповідей Claude:** Потокова передача Claude та Run очікують асинхронного очищення вже отриманого тіла HTTP-відповіді, зокрема для користувацьких потоків, що потребують асинхронного звільнення ресурсів. Подальший виняток під час звільнення відповіді або її вмісту не замінює успішне завершення, початкову помилку читання чи скасування; спроба звільнити початкову відповідь та її вміст усе одно виконується.

**Тайм-аути HTTP:** Для потокової передачі тексту, вмісту або через callback і Run, що використовують спільний шлях раундів потокової передачі, розпізнаваний `HttpClient.Timeout` (`TaskCanceledException` із внутрішнім `TimeoutException`) перетворюється на `AIServiceException`, якщо не спрацювали скасування з боку виклику та тайм-аут політики запиту. `InnerException` зберігає початковий транспортний виняток, тому `run.Result` завершується помилкою зі збереженням причини тайм-ауту. Поведінка скасування з боку виклику, тайм-аутів політики та інших транспортних скасувань залишається незмінною.

<a id="sse-acquisition-cancellation-limitation"></a>

## Відоме обмеження: отримання тіла успішної SSE-відповіді

За HTTP 200 SSE буферизувальна обгортка `HttpContent` у користувацькому обробнику може заблокувати `ReadAsStreamAsync` до отримання потоку тіла та початку очищення. Скасування з боку виклику й тайм-аут політики запиту можуть залишити `run.Result` незавершеним, відповідь невивільненою, а блокування активного Run утримуваним до завершення отримання потоку; наступний Run відхиляється як уже активний. Цю проблему ще не виправлено, і вона відрізняється від повільного очищення. Стандартний `SocketsHttpHandler` пройшов перевірені сценарії; скасування читання тіла HTTP-помилки також спрацювало з цією обгорткою. Використовуйте звичайний потоковий вміст без буферизувальної обгортки. Для нативного вебпошуку Claude діє окреме [обмеження продовження](providers.md#claude-native-continuation-limitation).

## Приклади сумісності зі старим API

Вхідні StreamAsync сервісу/RAG залишаються публічними у v8. Для нового керування виконанням використовуйте StartRunAsync; run.StreamAsync() лише спостерігає наявний run.

## Базовий стримінг

`StreamAsync` дозволяє отримувати токени в міру їх генерації:

```csharp
await foreach (var token in service.StreamAsync("Розкажіть історію"))
{
    Console.Write(token);
}
```

## Стримінг із типом контенту

`StreamAsync` повертає об''єкти `StreamingContent` із текстом та інформацією про тип:

```csharp
await foreach (var content in service.StreamAsync("Поясніть квантові обчислення", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Стримінг міркувань

OpenAI, Claude, Gemini, Grok і DeepSeek Flash передають міркування провайдера за однією схемою стримінгу. Увімкніть міркування в сервісі або запиті, потім спостерігайте через `StreamOptions.WithReasoning()`:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Розв''яжіть: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Міркування] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

Gemini 3.7/3.8 Flash використовують наявні події стримінгу та Run. `StreamingContentType.Reasoning` містить надані постачальником підсумки або повідомлення про перебіг роботи, якщо їх повернуто; повних внутрішніх міркувань це не гарантує. `StreamOptions.WithReasoning()` вибирає цей вивід, а `WithReasoning(ReasoningLevel...)` у сервісі керує рівнем.

Grok 4.6 також може передавати необов'язкові підсумки міркування через ці події. Параметр потоку обирає видимий вивід, а `WithReasoning(ReasoningLevel...)` — зусилля для одного завдання. Відсутність підсумків не означає вимкнення міркування. Див. [налаштування Grok](providers.md#xai-xaiservice).

DeepSeek Flash передає `reasoning_content` через ті самі події після ввімкнення міркування. `StreamOptions.WithReasoning()` керує спостереженням; `WithDeepSeekReasoning(...)` або `WithReasoning(...)` сервісу — міркуванням. Див. [DeepSeek](providers.md#deepseek-deepseekservice).

## Стримінг зі структурованим виводом

Стрімте текст у реальному часі та отримайте десеріалізований об''єкт по завершенні:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Стримінг токенів у UI по мірі надходження
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Отримання парсеного результату після завершення
MyDto result = await run.Result;
```

## Витрата токенів

По завершенні стримінгу остання подія `Completion` містить об''єкт `TokenUsage` з детальною статистикою:

```csharp
await foreach (var content in service.StreamAsync("Поясніть квантові обчислення", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nВхідні токени:  {content.Usage.InputTokens}");
        Console.WriteLine($"Вихідні токени: {content.Usage.OutputTokens}");
        Console.WriteLine($"Усього:         {content.Usage.TotalTokens}");
    }
}
```

### Властивості TokenUsage

| Властивість | Опис |
|-------------|------|
| `InputTokens` | Кількість токенів у вході/промпті |
| `OutputTokens` | Кількість токенів у вихідній відповіді |
| `TotalTokens` | Вхідні + вихідні |
| `CachedInputTokens` | Токени, обслуговані з кешу (економія) |
| `CacheCreationTokens` | Токени, записані в кеш (Anthropic) |
| `ReasoningTokens` | Токени, використані для внутрішніх міркувань |
| `CacheHitRatio` | Частка влучень у кеш (0.0–1.0) |
| `VisibleOutputTokens` | Вихідні токени без урахування міркувань |

### Перевірка ефективності кешу

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"Влучення в кеш: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Без кешу: {content.Usage.NonCachedInputTokens}");
}
```

## Пресети StreamOptions

`StreamOptions` надає пресети та Fluent-білдер для керування вмістом потоку:

```csharp
// Повний набір — метадані, виклик функцій, міркування
await foreach (var c in service.StreamAsync("промпт", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Мінімальні витрати — лише текст, без метаданих
await foreach (var c in service.StreamAsync("промпт", StreamOptions.Minimal))
    Console.Write(c.Content);

// Сценарій із функціями
await foreach (var c in service.StreamAsync("промпт", StreamOptions.WithFunctions))
{ /* Обробка Text, FunctionCall, FunctionResult, Completion */ }
```

Fluent-білдер для власних комбінацій:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // Увімкнути хід думок
    .WithMetadata()        // Додати інформацію про модель у Completion
    .WithFunctionCalls();  // Увімкнути виклик функцій під час стримінгу
```

Вважайте показані фрагменти попередніми, доки `run.Result` не завершиться успішно. Спільний шлях потокової обробки, сумісний з OpenAI, і шлях потокової обробки DeepSeek відхиляють новий текст, міркування або дані інструментів після явного завершення, а також зміну причини завершення: `run.Result` викидає виняток, невдалий раунд не зберігається в історії, а його інструменти не виконуються. Така обробка помилки не скасовує попередні раунди або дії, вже виконані в зовнішніх системах. Остання дельта може надійти в першій події завершення; подальша подія лише зі статистикою використання також дозволена.

## Стримінг без збереження стану (StreamOnceAsync)

Стрімить відповідь без впливу на історію діалогу — потокова версія `AskOnceAsync`:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Перекладіть це французькою"))
    Console.Write(chunk);
```

Є перевантаження для `Message` із мультимодальним вводом:

```csharp
var message = MessageBuilder.Create().AddText("Опишіть це").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## Сумаризація перед стримінгом

Автоматична політика сумаризації не спрацьовує під час стримінгу. Викличте її явно перед `StreamAsync`:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Продовжимо нашу розмову...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [Продовження тривалого завдання / Цитати можуть позначати вебрезультати чи інші джерела провайдера. Позиції належать окремій відповіді й частині вмісту, а не об'єднаному результату Run. Зберігайте URL і заголовок для показу та перевірки; наявність джерела сама собою не підтверджує кожне твердження.](perplexity.md).
