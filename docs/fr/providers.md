# Fonctionnalités par fournisseur

## OpenAI (OpenAIService)

### Niveau d'effort de raisonnement

Les modèles GPT-5.x et de la série o3 prennent en charge le contrôle de l'effort de raisonnement. Ajustez le niveau pour trouver le bon équilibre entre vitesse et profondeur :

```csharp
using Mythosia.AI.Models;

// GPT-5.6 : Sol est le modèle phare ; Terra et Luna sont des options plus économiques.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// Série GPT-5.4
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// Série GPT-5.2
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

// o3
service.ChangeModel(AIModels.OpenAI.O3);
service.Gpt5ReasoningEffort = Gpt5Reasoning.High; // Minimal, Low, Medium, High
```

### Synthèse vocale

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "Bonjour le monde !",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("sortie.mp3", audio);
```

### Transcription audio

```csharp
byte[] audioData = await File.ReadAllBytesAsync("enregistrement.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "enregistrement.mp3",
    language: "fr"  // optionnel, ISO-639-1
);
```

### Génération d'images

```csharp
var result = await ((IImageGenerationService)service).GenerateImagesAsync(
    new ImageGenerationRequest
    {
        Prompt = "Une ville futuriste de nuit",
        Size = "1024x1024"
    });

GeneratedImage image = result.Images[0];
byte[] imageBytes = image.Data;
string? imageUrl = image.Url;
```

---

## Anthropic (AnthropicService)

### Comptage de tokens (API native)

`GetInputTokenCountAsync` est disponible chez tous les fournisseurs (voir [Générer du texte](completions.md#comptage-de-tokens)). L'implémentation d'Anthropic appelle l'endpoint officiel `messages/count_tokens`, retournant des comptes **exacts** plutôt qu'une estimation locale :

```csharp
uint tokens = await service.GetInputTokenCountAsync("Votre prompt ici");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

### Niveau de réflexion

Contrôlez la quantité de raisonnement interne effectué par Gemini :

```csharp
using Mythosia.AI.Models.Enums;

service.ThinkingLevel = GeminiThinkingLevel.High;
// Options : Disabled, Low, Medium, High
```

Les niveaux élevés produisent des réponses plus approfondies mais augmentent la latence et l'utilisation des tokens.

---

## xAI (XAIService)

### Mode de raisonnement

```csharp
using Mythosia.AI.Models;

service.ReasoningEffort = GrokReasoning.High;
// Options : Auto, None, Low, Medium, High (selon le modèle)
```

---

## Perplexity (PerplexityService)

### Recherche web avec citations

Les modèles Sonar peuvent effectuer des recherches web et retourner des citations avec la réponse :

```csharp
SonarSearchResponse result = await service.GetCompletionWithSearchAsync(
    prompt: "Quelles sont les dernières avancées en énergie de fusion ?",
    domainFilter: new[] { "nature.com", "science.org" },  // optionnel
    recencyFilter: "week"  // day, week, month, year
);

Console.WriteLine(result.Content);

foreach (var citation in result.Citations)
{
    Console.WriteLine($"Source : {citation.Url}");
}
```

---

## Alibaba / Qwen (QwenService)

Installez le package séparé :

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

Modèles disponibles : `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` et variantes.

Choisissez un endpoint compatible lors de la création du service avec `EndpointPlatform` :

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```
