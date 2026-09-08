# Funcionalidades por Provedor

## OpenAI (OpenAIService)

> O suporte ao GPT-6 Astra e às chamadas assíncronas de ferramentas está disponível a partir de `Mythosia.AI` 7.1.0, com tipos compartilhados em `Mythosia.AI.Abstractions` 3.1.0.

Uma consulta lenta não precisa interromper toda a resposta. Enquanto os dados do clima são carregados, por exemplo, o modelo pode explicar dicas gerais de viagem que não dependem do resultado.

`FunctionDefinition.AllowAsync = true` ou `FunctionBuilder.WithAsync()` permite habilitar chamadas assíncronas para GPT-6 Astra via Responses. O padrão é `false`; modelos sem suporte aguardam o resultado do mesmo handler. Veja exemplos e o ciclo de vida da solicitação no [guia de chamadas de função](function-calling.md).

### Nível de Esforço de Reasoning

Os modelos GPT-6 Astra / GPT-5.x e a série o3 suportam controle de esforço de reasoning:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol é o modelo principal; Terra e Luna são opções mais econômicas.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// Série GPT-5.4
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

O GPT-6 Astra usa a API Responses por padrão; chamadas de função exigem essa API. `Auto` equivale ao padrão da biblioteca, `Medium`; `None` e `Minimal` não estão disponíveis. `AIRequestProfile.DisableReasoning = true` usa `Low` no modo `Standard` e omite o resumo do raciocínio. Selecione `Gpt6ReasoningMode.Pro` para executar o modo Pro com o mesmo ID de modelo `gpt-6-astra`.

Para tarefas comuns entre provedores, use [raciocínio e busca nativa](reasoning-and-search.md); as configurações específicas apresentadas a seguir continuam disponíveis.

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
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "Uma cidade futurista à noite",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
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

service.ReasoningEffort = GrokReasoning.High;
// Opções: Auto, None, Low, Medium, High (depende do modelo)
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
