# Streaming

> Grok 4.7: Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0. [le choix du modèle, le raisonnement et la vitesse](providers.md#grok-47)

Pour obtenir réponse, jetons et sources ensemble, `await run.Result` renvoie un instantané `AIRunResult`. La chaîne est dans `result.Text`, sans lecture du flux. Ce changement appartient à Mythosia.AI 8.0.0 ; les types de retour de `GetCompletionAsync` et `StructuredStreamRun<T>.Result` restent identiques. [Résultat Run et migration](execution-api-transition.md#run-result).


Pour des paramètres indépendants et réutilisables, utilisez [le builder de requête](request-building.md). Appelez `CreateRequest(...)` avant `With...`. Les propriétés et méthodes fluent du service conservent leur comportement existant.

Afficher le texte dès son arrivée permet de lire une réponse longue pendant sa rédaction. `StartRunAsync` permet aussi d’annuler cette même tâche et d’envoyer des instructions sur les modèles compatibles ; consultez le [guide Run](execution-api-transition.md).

```csharp
await using var run = await service.StartRunAsync(
    "Résume le document.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

## Exemples de compatibilité avec l’ancienne API

Les StreamAsync de service/RAG avec entrée restent publics en v8. Utilisez StartRunAsync pour les nouveaux contrôles ; run.StreamAsync() observe uniquement un run existant.

## Streaming de base

Utilisez `StreamAsync` pour recevoir les tokens au fur et à mesure de leur génération :

```csharp
await foreach (var token in service.StreamAsync("Raconte-moi une histoire"))
{
    Console.Write(token);
}
```

## Streaming avec type de contenu

`StreamAsync` peut retourner des objets `StreamingContent` qui portent à la fois le texte et son type :

```csharp
await foreach (var content in service.StreamAsync("Explique l'informatique quantique", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Streaming de raisonnement

OpenAI, Claude, Gemini, Grok et DeepSeek Flash exposent le raisonnement du fournisseur selon le même schéma de streaming. Activez le raisonnement dans le service ou la requête, puis observez-le avec `StreamOptions.WithReasoning()` :

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Résoudre : 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Réflexion] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

Gemini 3.7/3.8 Flash utilisent les événements existants de streaming et de Run. `StreamingContentType.Reasoning` contient les résumés ou indications de progression exposés par le fournisseur lorsqu’il en renvoie, sans garantir l’accès à tout le raisonnement interne. `StreamOptions.WithReasoning()` sélectionne cette sortie ; `WithReasoning(ReasoningLevel...)` sur le service règle l’effort.

Grok 4.6 peut aussi fournir des résumés de raisonnement facultatifs via ces événements. L’option du flux sélectionne l’affichage ; `WithReasoning(ReasoningLevel...)` choisit l’effort d’une tâche. L’absence de résumé ne signifie pas que le raisonnement est désactivé. Voir la [configuration Grok](providers.md#xai-xaiservice).

DeepSeek Flash expose `reasoning_content` via les mêmes événements après activation du raisonnement. `StreamOptions.WithReasoning()` règle l’observation ; `WithDeepSeekReasoning(...)` ou le `WithReasoning(...)` du service règle le raisonnement. Voir [DeepSeek](providers.md#deepseek-deepseekservice).

## Streaming avec sortie structurée

Recevez le texte en temps réel et obtenez un objet désérialisé à la fin :

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Diffuser les tokens vers l'UI au fur et à mesure
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Résultat complètement parsé après la fin du streaming
MyDto result = await run.Result;
```

## Utilisation des tokens

À la fin du streaming, l'événement `Completion` contient un objet `TokenUsage` avec des métriques détaillées :

```csharp
await foreach (var content in service.StreamAsync("Explique l'informatique quantique", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nTokens en entrée :  {content.Usage.InputTokens}");
        Console.WriteLine($"Tokens en sortie : {content.Usage.OutputTokens}");
        Console.WriteLine($"Total tokens :     {content.Usage.TotalTokens}");
    }
}
```

### Propriétés de TokenUsage

| Propriété | Description |
|---|---|
| `InputTokens` | Tokens dans l'entrée / le prompt |
| `OutputTokens` | Tokens dans la sortie / la complétion |
| `TotalTokens` | Entrée + Sortie |
| `CachedInputTokens` | Tokens servis depuis le cache (coût réduit) |
| `CacheCreationTokens` | Tokens écrits en cache (Anthropic) |
| `ReasoningTokens` | Tokens utilisés pour le raisonnement interne |
| `CacheHitRatio` | Taux de succès du cache (0,0–1,0) |
| `VisibleOutputTokens` | Tokens de sortie hors raisonnement |

### Vérifier l'efficacité du cache

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"Taux de cache : {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Entrée non mise en cache : {content.Usage.NonCachedInputTokens}");
}
```

## Préréglages StreamOptions

`StreamOptions` propose des préréglages et un builder fluent pour contrôler ce que le stream produit :

```csharp
// Complet — métadonnées, appels de fonctions, raisonnement
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Minimal — texte uniquement, sans métadonnées
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// Scénarios avec appel de fonctions
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* gérer Text, FunctionCall, FunctionResult, Completion */ }
```

Builder fluent pour des combinaisons personnalisées :

```csharp
var options = new StreamOptions()
    .WithReasoning()       // inclure la chaîne de pensée
    .WithMetadata()        // inclure les infos du modèle dans Completion
    .WithFunctionCalls();  // activer l'appel de fonctions pendant le stream
```

Considérez les fragments affichés comme provisoires jusqu’à la réussite de `run.Result`. Le chemin de streaming commun compatible avec OpenAI et le chemin de streaming de DeepSeek rejettent tout nouveau texte, raisonnement ou donnée d’outil après une fin explicite, ainsi qu’un changement du motif de fin : `run.Result` lève une exception, le tour échoué n’est pas enregistré dans l’historique et ses outils ne sont pas exécutés. Ce traitement de l’échec n’annule ni les tours précédents ni les actions déjà exécutées à l’extérieur. Le dernier delta peut accompagner le premier événement de fin ; un événement ultérieur contenant uniquement des données d’utilisation reste accepté.

## Streaming sans état (StreamOnceAsync)

Streamez une réponse sans affecter l'historique de conversation — l'équivalent streaming de `AskOnceAsync` :

```csharp
await foreach (var chunk in service.StreamOnceAsync("Traduis ça en français"))
    Console.Write(chunk);
```

Accepte aussi un `Message` pour les entrées multimodales :

```csharp
var message = MessageBuilder.Create().AddText("Décris ça").AddImage("photo.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## Résumé de conversation avant le streaming

La politique de résumé automatique ne se déclenche pas pendant `StreamAsync`. Appelez `ApplySummaryPolicyIfNeededAsync` explicitement avant :

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continuons notre conversation...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [Poursuivre une tâche longue / Les citations peuvent désigner des pages web ou d'autres sources du fournisseur. Les positions sont locales à une réponse et à une partie de contenu, pas au résultat Run concaténé. Conservez URL et titre pour l'affichage et la vérification ; une source retournée ne valide pas automatiquement toutes les affirmations.](perplexity.md).
