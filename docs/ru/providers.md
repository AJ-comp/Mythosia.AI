# Возможности провайдеров

## OpenAI (OpenAIService)

### Уровень рассуждений

Баланс между скоростью и глубиной анализа:

```csharp
using Mythosia.AI.Models;

service.Model = AIModels.OpenAI.Gpt5_4;
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High;

service.Model = AIModels.OpenAI.Gpt5_2;
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

service.Model = AIModels.OpenAI.O3;
service.Gpt5ReasoningEffort = Gpt5Reasoning.High;
```

### Преобразование текста в речь (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Привет!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Распознавание речи (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "ru"
);
```

### Генерация изображений

```csharp
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "Ночной город будущего",
    size: "1024x1024"
);

string imageUrl = await service.GenerateImageUrlAsync(
    prompt: "Ночной город будущего",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### Подсчёт токенов (нативный API)

`GetInputTokenCountAsync` доступен у всех провайдеров ([см. генерация текста](completions.md#подсчёт-токенов)). Anthropic вызывает официальный эндпоинт `messages/count_tokens`, возвращая **точное** количество токенов:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Текст промпта");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Глубина рассуждений

Управляет объёмом внутренних рассуждений Gemini:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Варианты: Disabled, Low, Medium, High
```

---

## xAI (XAIService)

### Режим рассуждений

```csharp
using Mythosia.AI.Models;

service.ReasoningMode = GrokReasoning.High;
// Варианты: Off, Low, High
```

---

## Perplexity (PerplexityService)

### Веб-поиск с цитированием

Модели Sonar ищут в интернете и возвращают источники вместе с ответом:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "Последние достижения в термоядерной энергетике?",
    domainFilter: new[] { "nature.com", "science.org" },
    recencyFilter: "week"
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
    Console.WriteLine($"Источник: {citation.Url}");
```

---

## Alibaba / Qwen (QwenService)

Установите отдельный пакет:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

Доступные модели: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` и их варианты.

Свойство `EndpointPlatform` позволяет переключаться между Alibaba Cloud и совместимыми эндпоинтами:

```csharp
service.EndpointPlatform = EndpointPlatform.AlibabaCloud;
```
