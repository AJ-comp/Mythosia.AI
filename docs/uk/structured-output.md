# Структурований вивід

## Навіщо потрібен структурований вивід

LLM за замовчуванням повертає вільний текст. Якщо застосунку потрібно **програмно обробити** відповідь — зберегти в базу, передати іншому API або відмалювати в типізованому UI — текст доводиться парсити вручну. Це призводить до крихких регулярних виразів, що ламаються при найменшій зміні формулювання.

Структурований вивід вирішує цю проблему: модель повертає JSON, що відповідає схемі C#-типу. Mythosia.AI автоматично генерує схему, вбудовує її в промпт і десеріалізує відповідь — включно з **автовідновленням JSON** при незначних помилках формату.

### Коли використовувати

- Витягування сутностей, класифікацій або структурованих даних з неструктурованого тексту
- Побудова типізованих API-відповідей з AI-генерованого контенту
- Передача виводу AI в подальші пайплайни, що очікують певну структуру даних
- Будь-які сценарії, де потрібен **надійний, машиночитний** вивід від моделі

## Приклад без і зі структурованим виводом

```csharp
// ❌ Без — крихкий ручний парсинг
var text = await service.GetCompletionAsync("Яка погода в Києві?");
var tempMatch = Regex.Match(text, @"(\d+)");
// Якщо модель напише "двадцять два" замість "22"? 💥
```

```csharp
// ✅ З — типобезпечно й автоматично
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Яка погода в Києві?");

Console.WriteLine(result.City);         // Київ
Console.WriteLine(result.TemperatureC); // 22
```

## Колекції

Типи колекцій підтримуються без обгорток:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Витягніть усі імена та організації з тексту: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Стримінг + структурований вивід

Стрімте текст у реальному часі та отримайте об''єкт по завершенні:

```csharp
var run = service.BeginStream("Створіть опис продукту").As<ProductDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);

ProductDto product = await run.Result;
```

## Політика структурованого виводу

```csharp
using Mythosia.AI.Models;

service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;  // Суворе дотримання схеми
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient; // Більше свободи моделі
```
