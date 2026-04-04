# Générer du texte

## Requête simple

L'usage le plus basique — envoyer un message, recevoir une réponse :

```csharp
var response = await service.GetCompletionAsync("Quelle est la capitale de la France ?");
Console.WriteLine(response); // Paris
```

## Prompt système

Définissez un prompt système pour donner une personnalité ou des instructions au modèle :

```csharp
service.SystemPrompt = "Tu es un assistant concis. Réponds en une seule phrase.";

var response = await service.GetCompletionAsync("Explique la récursion.");
```

## Conversation multi-tours

Les messages s'accumulent automatiquement. Chaque appel à `GetCompletionAsync` complète l'historique de conversation :

```csharp
await service.GetCompletionAsync("Je m'appelle Alice.");
var response = await service.GetCompletionAsync("Comment je m'appelle ?");
// → "Tu t'appelles Alice."
```

Pour réinitialiser l'historique :

```csharp
service.ClearMessages();
```

## Construire des messages manuellement

Utilisez `MessageBuilder` pour créer des messages de façon explicite :

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.User("Résume ce texte : ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (entrée image)

Les fournisseurs qui prennent en charge la vision acceptent des images en plus du texte :

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagramme.png");

var message = MessageBuilder.User("Que montre ce diagramme ?")
    .WithImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Requête rapide (API statique)

Pour des requêtes ponctuelles sans instancier un service, utilisez la méthode statique `QuickAskAsync`. Le fournisseur est détecté automatiquement depuis le nom du modèle :

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "Quelle est la capitale de la France ?",
    model: AIModels.OpenAI.Gpt4oMini  // valeur par défaut
);
```

Variante avec image :

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Décris cette image",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4Vision
);
```

## Méthodes utilitaires pour les images

Analysez des images sans `MessageBuilder` — le service lit le fichier et détecte le type MIME automatiquement :

```csharp
// Depuis un chemin de fichier
var response = await service.GetCompletionWithImageAsync(
    "Que montre ce diagramme ?", "diagramme.png");

// Depuis une URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Décris cette photo", "https://example.com/photo.jpg");
```

## Régénérer le dernier message

Supprime la dernière réponse de l'assistant et renvoie le dernier message utilisateur :

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Pratique quand la réponse précédente n'était pas satisfaisante.

## Comptage de tokens

Estimez l'utilisation des tokens avant d'envoyer une requête. Disponible chez **tous les fournisseurs** :

```csharp
// Compter les tokens de l'historique de conversation actuel
uint conversationTokens = await service.GetInputTokenCountAsync();

// Compter les tokens d'un prompt spécifique
uint promptTokens = await service.GetInputTokenCountAsync("Votre prompt ici");
```

OpenAI et la plupart des fournisseurs utilisent une estimation locale basée sur TikToken. Anthropic et Google appellent leurs API natives de comptage de tokens pour des résultats exacts.

## Chaîne de messages fluente

`BeginMessage()` propose une API fluente pour construire et envoyer des messages en une seule chaîne — texte, images, streaming et configuration de politique inclus :

```csharp
// Texte + image → envoyer
string response = await service.BeginMessage()
    .AddText("Que montre ce diagramme ?")
    .AddImage("diagramme.png")
    .SendAsync();

// Requête ponctuelle (sans historique de conversation)
string answer = await service.BeginMessage()
    .AddText("Traduis ça en coréen")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("Écris un poème sur le printemps")
    .StreamAsync(chunk => Console.Write(chunk));

// Avec timeout et politique personnalisés
string result = await service.BeginMessage()
    .AddText("Analyse cette image")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` supporte aussi `IAsyncEnumerable` :

```csharp
await foreach (var chunk in service.BeginMessage().AddText("Raconte-moi une histoire").StreamAsync())
    Console.Write(chunk);
```

## Contrôler la longueur de sortie et la température

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // Plus bas = plus déterministe
```
