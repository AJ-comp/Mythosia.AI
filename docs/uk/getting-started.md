# Швидкий старт

## Встановлення

Встановіть основний пакет:

```bash
dotnet add package Mythosia.AI
```

Для стримінгу з LINQ-операторами (наприклад, `ToListAsync`) додатково встановіть:

```bash
dotnet add package System.Linq.Async
```

## Перша відповідь від AI

Оберіть провайдера та створіть екземпляр сервісу з API-ключем і `HttpClient`:

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

Потім викличте `GetCompletionAsync`:

```csharp
var response = await service.GetCompletionAsync("Привіт!");
Console.WriteLine(response);
```

## Вибір моделі

Кожен сервіс використовує модель за замовчуванням, але ви можете вказати потрібну явно:

```csharp
var service = new OpenAIService("your-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

Усі доступні константи моделей наведено у [довіднику API](../../api/Mythosia.AI.Models.AIModels.yml).

## Подальші кроки

- [Генерація тексту](completions.md) — системні промпти, історія діалогу, мультимодальність
- [Потокова передача](streaming.md) — потокенний вивід та стримінг міркувань
- [Виклик функцій](function-calling.md) — модель викликає ваш код
- [Структурований вивід](structured-output.md) — десеріалізація відповідей у C#-типи
