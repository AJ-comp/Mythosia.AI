# Générer du texte

Pour des paramètres indépendants et réutilisables, utilisez [le builder de requête](request-building.md). Appelez `CreateRequest(...)` avant `With...`. Les propriétés et méthodes fluent du service conservent leur comportement existant.

Si l’application a seulement besoin de la réponse terminée, `GetCompletionAsync` reste adapté. Pour afficher la progression, annuler ou ajouter des instructions pendant le travail lorsque le modèle le permet, consultez le [guide Run](execution-api-transition.md).

<a id="completion-cancellation"></a>

## Annuler une réponse devenue inutile

Si une personne ferme un écran, appuie sur Arrêter ou dépasse le délai prévu par l’application, la réponse peut devenir inutile. Transmettez un `CancellationToken` pour arrêter la communication et le travail côté client, puis éviter les outils et appels de modèle suivants. `GetCompletionAsync` reste adapté au résultat final ; une simple annulation ne nécessite pas de Run.

### Before : aucun signal transmis par l’appelant

```csharp
string answer = await service.CreateRequest("Résume ce document.")
    .GetCompletionAsync();
```

### After : annulation utilisateur ou après 30 secondes

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Résume ce document.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Annulé.");
}
```

Conservez la source pendant l’appel et reliez le bouton Arrêter ou la fermeture à `cancellation.Cancel()`. Cet exemple prévoit aussi une annulation après 30 secondes. L’appelant reçoit une `OperationCanceledException` après nettoyage. Un délai fixé avec `CancellationTokenSource` est aussi une annulation de l’appelant ; `FunctionCallingPolicy.TimeoutSeconds` conserve son comportement d’erreur de délai existant.

Les surcharges du service pour chaîne et `Message`, la réponse typée, le builder et `MessageChain.SendAsync` / `SendOnceAsync` acceptent le jeton. Les appels qui l’omettent restent utilisables. Autres points d’entrée :

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Résume ce document.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Renvoie le titre et l’auteur en JSON.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Résume ce document.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Traduis cette phrase.")
    .SendOnceAsync(cancellationToken: token);
```

Le jeton atteint la préparation, l’envoi et la lecture HTTP, les outils locaux coopératifs et les tours suivants. Dès que l’annulation est constatée, les outils en attente et les tours futurs sont ignorés. Le nettoyage conserve les paires appel/résultat enregistrées ; un outil démarré qui ignore le jeton peut le retarder. Les actions terminées et l’historique ne sont pas effacés. Voir le [contrat des outils](function-calling.md#tool-execution-contract).

L’arrêt du calcul ou de la facturation du fournisseur n’est pas garanti. OpenAI décrit la fermeture de connexion pour les Responses ordinaires ; Google précise que l’arrêt est côté client et que l’usage applicable reste facturé. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). Un travail en arrière-plan demande son `CancelAsync()` explicite ; annuler `WaitForCompletionAsync(cancellationToken: ...)` arrête seulement l’attente. Une complétion ordinaire ne devient pas un travail en arrière-plan. Voir [Perplexity](perplexity.md).

<a id="completion-cancellation-migration"></a>

Cet ajout appartient à Mythosia.AI 8.0.0. Les appels sans jeton et les arguments positionnels profile/context restent valides au niveau source, mais les consommateurs doivent être recompilés. Les implémentations personnalisées de `IAIService` doivent ajouter `CancellationToken cancellationToken = default` à la fin des deux signatures et le transmettre. Les fournisseurs dérivés de `AIService` gardent leur override `GetCompletionAsync(Message)` et transmettent le `RequestCancellationToken` protégé au transport. Le builder et Run seuls ne nécessitaient pas ce changement d’interface. Les sous-classes qui redéfinissent les surcharges public virtual modifiées pour les complétions chaîne/profile/context, les helpers d’image ou `RunAgentAsync` doivent aussi ajouter et transmettre le nouveau `CancellationToken` ; seul l’override fournisseur à un seul `Message` garde sa signature. Les délégués liés directement à une signature modifiée peuvent nécessiter une lambda explicite qui transmet ou omet le jeton.

## Requête simple

L'usage le plus basique — envoyer un message, recevoir une réponse :

```csharp
var response = await service.GetCompletionAsync("Quelle est la capitale de la France ?");
Console.WriteLine(response); // Paris
```

## Prompt système

Définissez un prompt système pour donner une personnalité ou des instructions au modèle :

```csharp
service.SystemMessage = "Tu es un assistant concis. Réponds en une seule phrase.";

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
service.ActivateChat.ClearMessages();
```

## Construire des messages manuellement

Utilisez `MessageBuilder` pour créer des messages de façon explicite :

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Résume ce texte : ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (entrée image)

Les fournisseurs qui prennent en charge la vision acceptent des images en plus du texte :

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagramme.png");

var message = MessageBuilder.Create().AddText("Que montre ce diagramme ?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

Pour analyser graphiques et captures, appeler vos fonctions ou approfondir une première réponse, utilisez [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). Le raisonnement reste désactivé par défaut ; activez-le avec `WithDeepSeekReasoning(...)` ou `WithReasoning(...)` par requête.

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
    model: AIModels.OpenAI.Gpt4_1
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

Perplexity: [Répondre avec un préréglage Agent / Sources, images et réponses structurées](perplexity.md).
