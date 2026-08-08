# Prise en main

## Installation

Installez le package principal :

```bash
dotnet add package Mythosia.AI
```

Si vous prévoyez d'utiliser le streaming avec des opérateurs LINQ (ex. `ToListAsync`), ajoutez également :

```bash
dotnet add package System.Linq.Async
```

## Votre première complétion

Choisissez un fournisseur et créez une instance de service avec votre clé API et un `HttpClient` :

```csharp
using Mythosia.AI;

var http = new HttpClient();

// OpenAI
var service = new OpenAIService("votre-clé-openai", http);

// Anthropic
// var service = new AnthropicService("votre-clé-anthropic", http);

// Google
// var service = new GoogleAIService("votre-clé-google", http);
```

Appelez ensuite `GetCompletionAsync` :

```csharp
var response = await service.GetCompletionAsync("Bonjour !");
Console.WriteLine(response);
```

## Choisir un modèle

Chaque service utilise un modèle par défaut adapté, mais vous pouvez en spécifier un explicitement :

```csharp
var service = new OpenAIService("votre-clé-api", http)
{
    Model = AIModels.OpenAI.Gpt4_1
};
```

Consultez la [référence API](../../api/Mythosia.AI.Models.AIModels.yml) pour la liste complète des constantes de modèles disponibles.

## Prochaines étapes

- [Générer du texte](completions.md) — prompts système, historique de conversation, multimodal
- [Streaming](streaming.md) — sortie token par token et streaming de raisonnement
- [Appel de fonctions](function-calling.md) — laisser le modèle appeler votre code
- [Sortie structurée](structured-output.md) — désérialiser les réponses en types C#
