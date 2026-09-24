# Параметры генерации

> Grok 4.7 — ещё не опубликованное дополнение; см. [выбор модели, рассуждение и скорость обработки](providers.md#grok-47).

Для независимых настроек и повторного использования вариантов применяйте [билдер запросов](request-building.md). Вызывайте `CreateRequest(...)` перед `With...`. Свойства и fluent-методы сервиса сохраняют прежнее поведение.

## Значения сервиса по умолчанию и совместимые методы

Каждый экземпляр AI-сервиса предоставляет следующие свойства:

```csharp
service.Temperature = 0.7f;        // Случайность [0, 2]. Ниже — детерминированнее
service.TopP = 1.0f;               // Порог ядерной выборки
service.MaxTokens = 1024;          // Максимум выходных токенов
service.FrequencyPenalty = 0.0f;   // Штраф за повторяющиеся токены
service.PresencePenalty = 0.0f;    // Штраф за уже встречавшиеся токены
```

GPT-6 Astra не поддерживает `temperature` и `top_p`; Mythosia не отправляет их, даже если они заданы общими свойствами или профилем запроса. Максимальный вывод — 128 000 токенов. См. [настройки GPT-6](providers.md).

GPT-6 Sol/Luna передают `temperature` и `top_p` только при `ReasoningLevel.None`; иначе оба поля опускаются. Astra не поддерживает `None`. См. [выбор и настройку модели](providers.md#gpt-6-sol-luna).


Усилие рассуждения и источники для задачи задаются [общими параметрами рассуждения и поиска](reasoning-and-search.md); существующие настройки провайдеров сохраняются.

## Fluent-расширения

Возвращают `this`, поэтому допускают цепочку вызовов:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("Вы — полезный ассистент.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Метод | Описание |
|-------|----------|
| `.WithSystemMessage(string)` | Системный промпт |
| `.WithTemperature(float)` | Ограничение [0, 2] |
| `.WithMaxTokens(uint)` | Максимум выходных токенов |
| `.WithStatelessMode(bool)` | Отключить накопление истории |

## Режим без состояния

При активации каждый запрос независим — история не отправляется и не сохраняется:

```csharp
service.StatelessMode = true;

// Эквивалент:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Подходит для одноразовых запросов, где история не нужна.

## Одноразовые запросы

Выполняет запрос без влияния на историю и без использования истории:

```csharp
// Текстовый промпт
string response = await service.AskOnceAsync("Сколько будет 2+2?");

// Сообщение (мультимодальное)
string response = await service.AskOnceAsync(message);

// Изображение из файла
string response = await service.AskOnceWithImageAsync("Опишите", "photo.jpg");
```

## Смена модели

Меняет модель посреди сессии, сохраняя историю:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// Или расширением — сброс истории и новый старт:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Управление несколькими диалогами

Один экземпляр сервиса может вести несколько независимых веток диалога:

```csharp
// Начать новый блок
service.AddNewChat();
var chat1 = service.ActivateChat;

// Переключиться на другой блок
service.SetActivateChat(chat2Id);

// Доступ ко всем блокам
var allChats = service.ChatRequests;
```

## Состояние диалога

Получите последний ответ AI или краткую сводку текущей сессии:

```csharp
// Последний ответ AI (null, если его нет)
string? lastReply = service.GetLastAssistantResponse();

// Текстовая сводка состояния сервиса
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: You are a helpful assistant.
```

## Копирование настроек сервиса

Клонирует все настройки другого экземпляра сервиса без истории диалога:

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```

Perplexity: [Управление исследованием и инструментами](perplexity.md).
