# Funcionalidades por Proveedor

## OpenAI (OpenAIService)

### Nivel de Esfuerzo de Reasoning

Los modelos GPT-5.x y la serie o3 soportan control de esfuerzo de reasoning:

```csharp
using Mythosia.AI.Models;

// Serie GPT-5.4
service.Model = AIModels.OpenAI.Gpt5_4;
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// o3
service.Model = AIModels.OpenAI.O3;
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### Texto a Voz

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "¡Hola, mundo!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Voz a Texto (Transcripción)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("grabacion.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "grabacion.mp3",
    language: "es"  // opcional, ISO-639-1
);
```

### Generación de Imágenes

```csharp
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "Una ciudad futurista de noche",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### Conteo de Tokens (API Nativa)

La implementación de Anthropic llama al endpoint oficial `messages/count_tokens`, devolviendo conteos **exactos** de tokens:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Tu prompt aquí");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Nivel de Razonamiento

Controla cuánto razonamiento interno realiza Gemini:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Opciones: Disabled, Low, Medium, High
```

---

## xAI (XAIService)

### Modo de Razonamiento

```csharp
using Mythosia.AI.Models;

service.ReasoningMode = GrokReasoning.High;
// Opciones: Off, Low, High
```

---

## Perplexity (PerplexityService)

### Búsqueda Web con Citas

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "¿Cuáles son los últimos avances en energía de fusión?",
    domainFilter: new[] { "nature.com", "science.org" },
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"Fuente: {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

Instala el paquete separado:

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

Modelos disponibles: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` y variantes.
