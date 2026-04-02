# Provider-Specific Features

## OpenAI (OpenAIService)

### Reasoning Effort

GPT-5.x and o3 series models support reasoning effort control. Set the level to trade off speed vs. depth:

```csharp
using Mythosia.AI.Models;

// GPT-5.4 series
service.Model = AIModels.OpenAI.Gpt5_4;
service.ReasoningLevel = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 series
service.Model = AIModels.OpenAI.Gpt5_2;
service.ReasoningLevel = Gpt5_2Reasoning.Medium;

// o3
service.Model = AIModels.OpenAI.O3;
service.ReasoningLevel = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### Text-to-Speech

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Hello, world!",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Speech-to-Text (Transcription)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "en"  // optional, ISO-639-1
);
```

### Image Generation

```csharp
// Get image as bytes
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "A futuristic city at night",
    size: "1024x1024"
);

// Get image as URL
string imageUrl = await service.GenerateImageUrlAsync(
    prompt: "A futuristic city at night",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### Token Counting

Estimate token usage before sending a request:

```csharp
// Count tokens for a specific prompt
uint tokens = await service.GetInputTokenCountAsync("Your prompt here");

// Count tokens for the current conversation
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Thinking Level

Control how much internal reasoning Gemini performs:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Options: Disabled, Low, Medium, High
```

Higher levels produce more thorough responses but increase latency and token usage.

---

## xAI (XAIService)

### Reasoning Mode

```csharp
using Mythosia.AI.Models;

service.ReasoningMode = GrokReasoning.High;
// Options: Off, Low, High
```

---

## Perplexity (PerplexityService)

### Web Search with Citations

Sonar models can search the web and return source citations alongside the response:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "What are the latest developments in fusion energy?",
    domainFilter: new[] { "nature.com", "science.org" },  // optional
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"Source: {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

Install the separate package:

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

Available models: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3`, and variants.

The `EndpointPlatform` property lets you switch between Alibaba Cloud and compatible endpoints:

```csharp
service.EndpointPlatform = EndpointPlatform.AlibabaCloud;
```
