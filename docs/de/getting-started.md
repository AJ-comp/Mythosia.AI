# Schnellstart

## Installation

Installiere das Kernpaket:

```bash
dotnet add package Mythosia.AI
```

Falls du Streaming mit LINQ-Operatoren (z. B. `ToListAsync`) nutzen möchtest, füge noch Folgendes hinzu:

```bash
dotnet add package System.Linq.Async
```

## Deine erste Vervollständigung

Wähle einen Anbieter und erstelle eine Service-Instanz mit deinem API-Key und einem `HttpClient`:

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("dein-openai-api-key", http);

// Anthropic
// var service = new AnthropicService("dein-anthropic-api-key", http);

// Google
// var service = new GoogleAIService("dein-google-api-key", http);
```

Dann rufst du `GetCompletionAsync` auf:

```csharp
var response = await service.GetCompletionAsync("Hallo!");
Console.WriteLine(response);
```

## Modell auswählen

Jeder Service verwendet standardmäßig ein sinnvolles Modell, aber du kannst auch explizit eines angeben:

```csharp
var service = new OpenAIService("dein-api-key", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

Alle verfügbaren Modellkonstanten findest du in der [API-Referenz](../api/Mythosia.AI.Models.AIModels.yml).

## Nächste Schritte

- [Textvervollständigung](completions.md) — System-Prompts, Gesprächsverlauf, multimodal
- [Streaming](streaming.md) — Token-für-Token-Ausgabe und Reasoning-Streaming
- [Funktionsaufruf](function-calling.md) — Das Modell deinen Code aufrufen lassen
- [Strukturierte Ausgabe](structured-output.md) — Antworten in C#-Typen deserialisieren
