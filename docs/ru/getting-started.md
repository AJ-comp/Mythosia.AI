# Быстрый старт

## Установка

Установите основной пакет:

```bash
dotnet add package Mythosia.AI
```

Для стриминга с LINQ-операторами (например, `ToListAsync`) дополнительно установите:

```bash
dotnet add package System.Linq.Async
```

## Первый ответ от AI

Выберите провайдера и создайте экземпляр сервиса с API-ключом и `HttpClient`:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("your-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("your-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("your-google-api-key", http);
```

Затем вызовите `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("Привет!");
Console.WriteLine(response);
```

## Выбор модели

Каждый сервис использует модель по умолчанию, но вы можете указать нужную явно:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

Все доступные константы моделей перечислены в [справочнике API](../../api/Mythosia.AI.Models.AIModels.yml).

## Дальнейшие шаги

- [Генерация текста](completions.md) — системные промпты, история диалога, мультимодальность
- [Потоковая передача](streaming.md) — потокенный вывод и стриминг рассуждений
- [Вызов функций](function-calling.md) — модель вызывает ваш код
- [Структурированный вывод](structured-output.md) — десериализация ответов в C#-типы
