# Можливості провайдерів

## OpenAI (OpenAIService)

> Підтримка GPT-6 Astra та асинхронних викликів інструментів доступна з `Mythosia.AI` 7.1.0; спільні типи включено до `Mythosia.AI.Abstractions` 3.1.0.

Якщо інструмент довго завантажує дані, GPT-6 Astra може тим часом продовжувати незалежні пояснення або інші частини завдання. `FunctionDefinition.AllowAsync = true` або `FunctionBuilder.WithAsync()` дозволяє асинхронні виклики для GPT-6 Astra через Responses. За замовчуванням використовується `false`; моделі без підтримки чекають результату того самого обробника. Приклади та життєвий цикл запиту описано в [посібнику з виклику функцій](function-calling.md).

Як задавати рівень міркування для різних провайдерів і використовувати актуальну інформацію або проіндексовані документи, пояснює [посібник із міркування та пошуку](reasoning-and-search.md). У ньому наведено підтримувані моделі, умови збереження кешу та обмеження поєднань.

### Рівень міркувань

Баланс між швидкістю та глибиною аналізу:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol — флагманська модель; Terra і Luna — економніші варіанти.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High;

service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High;
```

GPT-6 Astra за замовчуванням використовує Responses API; виклики функцій потребують цього API. `Auto` відповідає типовому значенню бібліотеки — `Medium`; значення `None` і `Minimal` недоступні. `AIRequestProfile.DisableReasoning = true` задає рівень `Low` у режимі `Standard` і пропускає підсумок міркувань. Виберіть `Gpt6ReasoningMode.Pro` для режиму Pro з тим самим ідентифікатором моделі `gpt-6-astra`.

### Перетворення тексту в мовлення (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Привіт!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Розпізнавання мовлення (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "uk"
);
```

### Генерація зображень

```csharp
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "Нічне місто майбутнього",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### Підрахунок токенів (нативний API)

`GetInputTokenCountAsync` доступний у всіх провайдерів ([див. генерація тексту](completions.md#підрахунок-токенів)). Anthropic викликає офіційний ендпоінт `messages/count_tokens`, повертаючи **точну** кількість токенів:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Текст промпту");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Глибина міркувань

Керує обсягом внутрішніх міркувань Gemini:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Варіанти: Disabled, Low, Medium, High
```

---

## xAI (XAIService)

### Режим міркувань

```csharp
using Mythosia.AI.Models;

service.ReasoningEffort = GrokReasoning.High;
// Варіанти: Auto, None, Low, Medium, High (залежно від моделі)
```

---

## Perplexity (PerplexityService)

### Веб-пошук із цитуванням

Моделі Sonar шукають в інтернеті та повертають джерела разом із відповіддю:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "Останні досягнення в термоядерній енергетиці?",
    domainFilter: new[] { "nature.com", "science.org" },
    recencyFilter: "week"
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
    Console.WriteLine($"Джерело: {citation.Url}");
```

---

## Alibaba / Qwen (QwenService)

Встановіть окремий пакет:

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

Доступні моделі: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` та їхні варіанти.

Під час створення сервісу виберіть сумісний ендпоінт за допомогою `EndpointPlatform`:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
