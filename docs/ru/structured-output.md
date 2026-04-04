# Структурированный вывод

## Зачем нужен структурированный вывод

LLM по умолчанию возвращает свободный текст. Если приложению нужно **программно обработать** ответ — сохранить в базу, передать другому API или отрисовать в типизированном UI — текст приходится парсить вручную. Это приводит к хрупким регулярным выражениям, которые ломаются при малейшем изменении формулировки.

Структурированный вывод решает эту проблему: модель возвращает JSON, соответствующий схеме C#-типа. Mythosia.AI автоматически генерирует схему, встраивает её в промпт и десериализует ответ — включая **автовосстановление JSON** при незначительных ошибках формата.

### Когда использовать

- Извлечение сущностей, классификаций или структурированных данных из неструктурированного текста
- Построение типизированных API-ответов из AI-генерируемого контента
- Передача вывода AI в последующие пайплайны, ожидающие определённую структуру данных
- Любые сценарии, где нужен **надёжный, машиночитаемый** вывод от модели

## Пример без и с структурированным выводом

```csharp
// ❌ Без — хрупкий ручной парсинг
var text = await service.GetCompletionAsync("Какая погода в Москве?");
var tempMatch = Regex.Match(text, @"(\d+)");
// Если модель напишет "двадцать два" вместо "22"? 💥
```

```csharp
// ✅ С — типобезопасно и автоматически
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Какая погода в Москве?");

Console.WriteLine(result.City);         // Москва
Console.WriteLine(result.TemperatureC); // 22
```

## Коллекции

Типы коллекций поддерживаются без обёрток:

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Извлеките все имена и организации из текста: ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Стриминг + структурированный вывод

Стримьте текст в реальном времени и получите объект по завершении:

```csharp
var run = service.BeginStream("Создайте описание продукта").As<ProductDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);

ProductDto product = await run.Result;
```

## Политика структурированного вывода

```csharp
using Mythosia.AI.Models;

service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;  // Строгое соответствие схеме
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient; // Больше свободы модели
```
