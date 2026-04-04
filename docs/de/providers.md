# Anbieterspezifische Funktionen

## OpenAI (OpenAIService)

### Reasoning-Aufwand

GPT-5.x- und o3-Serienmodelle unterstützen die Steuerung des Reasoning-Aufwands. Stelle die Stufe ein, um Geschwindigkeit und Tiefe abzuwägen:

```csharp
using Mythosia.AI.Models;

// GPT-5.4-Serie
service.Model = AIModels.OpenAI.Gpt5_4;
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2-Serie
service.Model = AIModels.OpenAI.Gpt5_2;
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.Model = AIModels.OpenAI.O3;
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### Text-to-Speech

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Hallo, Welt!",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("ausgabe.mp3", audio);
```

### Speech-to-Text (Transkription)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("aufnahme.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "aufnahme.mp3",
    language: "de"  // optional, ISO-639-1
);
```

### Bildgenerierung

```csharp
// Bild als Bytes erhalten
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "Eine futuristische Stadt bei Nacht",
    size: "1024x1024"
);

// Bild als URL erhalten
string imageUrl = await service.GenerateImageUrlAsync(
    prompt: "Eine futuristische Stadt bei Nacht",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### Token-Zählung (Native API)

`GetInputTokenCountAsync` ist bei allen Anbietern verfügbar (siehe [Textvervollständigung](completions.md#token-zahlung)). Anthropics Implementierung ruft den offiziellen `messages/count_tokens`-Endpunkt auf und liefert **exakte** Token-Zahlen statt lokaler Schätzungen:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Dein Prompt hier");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Denk-Niveau

Steuere, wie viel internes Reasoning Gemini durchführt:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Optionen: Disabled, Low, Medium, High
```

Höhere Stufen produzieren gründlichere Antworten, erhöhen aber Latenz und Token-Nutzung.

---

## xAI (XAIService)

### Reasoning-Modus

```csharp
using Mythosia.AI.Models;

service.ReasoningMode = GrokReasoning.High;
// Optionen: Off, Low, High
```

---

## Perplexity (PerplexityService)

### Websuche mit Quellenangaben

Sonar-Modelle können im Web suchen und Quellenangaben zusammen mit der Antwort zurückgeben:

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "Was sind die neuesten Entwicklungen in der Kernfusionsenergie?",
    domainFilter: new[] { "nature.com", "science.org" },  // optional
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"Quelle: {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

Installiere das separate Paket:

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

Verfügbare Modelle: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` und Varianten.

Die Eigenschaft `EndpointPlatform` ermöglicht den Wechsel zwischen Alibaba Cloud und kompatiblen Endpunkten:

```csharp
service.EndpointPlatform = EndpointPlatform.AlibabaCloud;
```
