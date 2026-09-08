# Anbieterspezifische Funktionen

## OpenAI (OpenAIService)

> GPT-6 Astra und Async-Tool-Aufrufe werden ab `Mythosia.AI` 7.1.0 unterstützt; die gemeinsamen Typen sind ab `Mythosia.AI.Abstractions` 3.1.0 verfügbar.

Eine langsame Abfrage muss die Antwort nicht vollständig anhalten. Während etwa Wetterdaten geladen werden, kann das Modell bereits allgemeine Reisetipps formulieren, die nicht vom Ergebnis abhängen.

Mit `FunctionDefinition.AllowAsync = true` oder `FunctionBuilder.WithAsync()` erlaubst du GPT-6 Astra über Responses asynchrone Tool-Aufrufe. Standard ist `false`; nicht unterstützte Modelle warten auf das Ergebnis desselben Handlers. Beispiele und Details zur Lebensdauer des Requests stehen im [Leitfaden für Funktionsaufrufe](function-calling.md).

### Reasoning-Aufwand

GPT-6 Astra / GPT-5.x- und o3-Serienmodelle unterstützen die Steuerung des Reasoning-Aufwands. Stelle die Stufe ein, um Geschwindigkeit und Tiefe abzuwägen:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol ist das Flaggschiff; Terra und Luna sind kostengünstigere Optionen.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4-Serie
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2-Serie
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

GPT-6 Astra verwendet standardmäßig die Responses API; Funktionsaufrufe erfordern sie. `Auto` entspricht dem Bibliotheksstandard `Medium`; `None` und `Minimal` sind nicht verfügbar. `AIRequestProfile.DisableReasoning = true` verwendet `Low` im Modus `Standard` und lässt die Zusammenfassung der Schlussfolgerungen weg. Mit `Gpt6ReasoningMode.Pro` wird der Pro-Modus für dieselbe Modell-ID `gpt-6-astra` aktiviert.

Für gemeinsame Aufgaben können Sie [Reasoning und native Suche](reasoning-and-search.md) verwenden; die folgenden anbieterspezifischen Einstellungen bleiben verfügbar.

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
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "Eine futuristische Stadt bei Nacht",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### Token-Zählung (Native API)

`GetInputTokenCountAsync` ist bei allen Anbietern verfügbar (siehe [Textvervollständigung](completions.md#token-zählung)). Anthropics Implementierung ruft den offiziellen `messages/count_tokens`-Endpunkt auf und liefert **exakte** Token-Zahlen statt lokaler Schätzungen:

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

service.ReasoningEffort = GrokReasoning.High;
// Optionen: Auto, None, Low, Medium, High (modellabhängig)
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

Wähle beim Erstellen des Dienstes mit `EndpointPlatform` einen kompatiblen Endpunkt aus:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
