# Параметри генерації

## Загальні властивості

Кожен екземпляр AI-сервісу надає такі властивості:

```csharp
service.Temperature = 0.7f;        // Випадковість [0, 2]. Нижче — детермінованіше
service.TopP = 1.0f;               // Поріг ядерної вибірки
service.MaxTokens = 1024;          // Максимум вихідних токенів
service.FrequencyPenalty = 0.0f;   // Штраф за повторювані токени
service.PresencePenalty = 0.0f;    // Штраф за вже згадані токени
service.MaxMessageCount = 20;      // Розмір вікна діалогу
```

## Fluent-розширення

Повертають `this`, тому допускають ланцюжок викликів:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("Ви — корисний асистент.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Метод | Опис |
|-------|------|
| `.WithSystemMessage(string)` | Системний промпт |
| `.WithTemperature(float)` | Обмеження [0, 2] |
| `.WithMaxTokens(uint)` | Максимум вихідних токенів |
| `.WithStatelessMode(bool)` | Вимкнути накопичення історії |

## Режим без стану

При активації кожен запит незалежний — історія не надсилається й не зберігається:

```csharp
service.StatelessMode = true;

// Еквівалент:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Підходить для одноразових запитів, де історія не потрібна.

## Одноразові запити

Виконує запит без впливу на історію та без використання історії:

```csharp
// Текстовий промпт
string response = await service.AskOnceAsync("Скільки буде 2+2?");

// Повідомлення (мультимодальне)
string response = await service.AskOnceAsync(message);

// Зображення з файлу
string response = await service.AskOnceWithImageAsync("Опишіть", "photo.jpg");
```

## Зміна моделі

Змінює модель посеред сесії, зберігаючи історію:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// Або розширенням — скидання історії та новий старт:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Керування кількома діалогами

Один екземпляр сервісу може вести кілька незалежних гілок діалогу:

```csharp
// Почати новий блок
var chat1 = service.AddNewChat();

// Перемкнутися на інший блок
service.SetActivateChat(chat2Id);

// Доступ до всіх блоків
var allChats = service.ChatRequests;
```

## Стан діалогу

Отримайте останню відповідь AI або стислу зведення поточної сесії:

```csharp
// Остання відповідь AI (null, якщо її немає)
string? lastReply = service.GetLastAssistantResponse();

// Текстове зведення стану сервісу
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: You are a helpful assistant.
```

## Копіювання налаштувань сервісу

Клонує всі налаштування іншого екземпляра сервісу без історії діалогу:

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
