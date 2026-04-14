# Funcionalidades por Provedor

## OpenAI (OpenAIService)

### Nível de Esforço de Reasoning

Os modelos GPT-5.x e a série o3 suportam controle de esforço de reasoning:

```csharp
using Mythosia.AI.Models;

// Série GPT-5.4
service.Model = AIModels.OpenAI.Gpt5_4;
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// o3
service.Model = AIModels.OpenAI.O3;
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### Texto para Fala

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Olá, mundo!",
    voice: "alloy",
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### Fala para Texto (Transcrição)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("gravacao.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "gravacao.mp3",
    language: "pt"  // opcional, ISO-639-1
);
```

### Geração de Imagens

```csharp
byte[] imageBytes = await service.GenerateImageAsync(
    prompt: "Uma cidade futurista à noite",
    size: "1024x1024"
);
```

---

## Anthropic (AnthropicService)

### Contagem de Tokens (API Nativa)

A implementação da Anthropic chama o endpoint oficial `messages/count_tokens`, retornando contagens **exatas** de tokens:

```csharp
uint tokens = await service.GetInputTokenCountAsync("Seu prompt aqui");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Nível de Pensamento

Controle o quanto de reasoning interno o Gemini realiza:

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Opções: Disabled, Low, Medium, High
```

---

## xAI (XAIService)

### Modo de Reasoning

```csharp
using Mythosia.AI.Models;

service.ReasoningMode = GrokReasoning.High;
// Opções: Off, Low, High
```

---

## Perplexity (PerplexityService)

### Busca na Web com Citações

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "Quais são os últimos desenvolvimentos em energia de fusão?",
    domainFilter: new[] { "nature.com", "science.org" },
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"Fonte: {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

Instale o pacote separado:

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

Modelos disponíveis: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` e variantes.
