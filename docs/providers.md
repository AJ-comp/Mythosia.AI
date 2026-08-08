# Provider-Specific Features

## OpenAI (OpenAIService)

### Reasoning Effort

GPT-5.x and o3 series models support reasoning effort control. Set the level to trade off speed vs. depth:

```csharp
using Mythosia.AI.Models;

// GPT-5.6: the alias routes to Sol; Terra lowers cost; Luna targets efficient volume.
service.ChangeModel(AIModels.OpenAI.Gpt5_6);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4 series
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 series
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.WithO3Parameters(Gpt5Reasoning.High); // Minimal, Low, Medium, High
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
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;

var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "A futuristic city at night",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### Token Counting (Native API)

`GetInputTokenCountAsync` is available on all providers (see [Basic Completions](completions.md#token-counting)). Anthropic's implementation calls the official `messages/count_tokens` endpoint, returning **exact** token counts rather than local estimation:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Your prompt here");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

The default chat model is `AIModels.Google.Gemini3_6Flash`. The current catalogue also includes Gemini 3.5 Flash/Flash-Lite, Gemini 3.1 Pro Preview/Flash-Lite, Gemini 3 Flash Preview, and the Gemini 2.5 family.

### Thinking Level

Control how much internal reasoning Gemini performs:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Options: Auto, Minimal, Low, Medium, High
```

Gemini 3 thinking is always on. `Auto` keeps the provider default; Pro models do not accept `Minimal`. Gemini 2.5 uses `ThinkingBudget` instead (`-1` dynamic, `0` off on Flash/Lite, and at least `128` on Pro).

Gemini 3.6 Flash and Gemini 3.5 Flash-Lite do not accept the legacy sampling fields, so `Temperature`, `TopP`, `TopK`, and `candidateCount` are omitted automatically.

### Safety Thresholds

Safety settings are omitted by default so Google can apply its current defaults. Configure only the categories your application owns:

```csharp
service.HarassmentSafetyThreshold = GeminiSafetyThreshold.BlockMediumAndAbove;
service.HateSpeechSafetyThreshold = GeminiSafetyThreshold.BlockOnlyHigh;
service.SexuallyExplicitSafetyThreshold = GeminiSafetyThreshold.Off;
service.DangerousContentSafetyThreshold = GeminiSafetyThreshold.ProviderDefault;
```

### Image Generation and Editing

`GoogleAIService` also implements `IImageGenerationService`. Its independent default image model is `AIModels.Google.Images.Gemini3_1FlashImage`; the Flash-Lite and Pro image IDs are available as explicit request overrides.

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Google;

IImageGenerationService images = new GoogleAIService(apiKey, httpClient);

var result = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A clean product photo on a neutral background",
    Size = "2K",
    OutputFormat = "jpeg"
});
```

Gemini supports reference-image editing through `EditImagesAsync`, but it does not expose a separate mask parameter or a guaranteed multi-image count parameter.
The GenerateContent response format has an explicit JPEG selector but no PNG selector. Set `OutputFormat = "jpeg"` for deterministic JPEG output. `png` and `auto` leave format selection to Gemini, so treat the returned `GeneratedImage.MediaType` as authoritative.

---

## xAI (XAIService)

### Reasoning Mode

```csharp
using Mythosia.AI.Models;

service.ReasoningEffort = GrokReasoning.High;
// Options: Auto, None, Low, Medium, High (model-dependent)
```

`XAIService` defaults to Grok 4.5. Grok 4.3 accepts `None` through `High`; Grok 4.5 accepts `Low` through `High`, defaults to high when omitted, and cannot disable reasoning.

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
    .UseMaxModel();
```

Available models include `QwenMax`, `QwenPlus`, `QwenTurbo`, and the size-specific Qwen 3 and Qwen 3.5 constants in `AlibabaModels`.

Choose a compatible endpoint when constructing the service:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
