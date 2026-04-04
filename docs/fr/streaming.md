# Streaming

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
await foreach (var content in service.StreamAsync("Explique l'informatique quantique"))
{
    Console.Write(content.Content);
}
```

## Streaming de raisonnement

Tous les fournisseurs capables de raisonnement (OpenAI, Claude, Gemini, Grok, DeepSeek) partagent le même pattern. Passez `StreamOptions` avec le raisonnement activé :

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

`StreamingContentType.Reasoning` contient le raisonnement interne du modèle, `StreamingContentType.Text` la réponse finale.

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
await foreach (var content in service.StreamAsync("Explique l'informatique quantique"))
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

await foreach (var chunk in service.StreamAsync("Continuons notre conversation..."))
    Console.Write(chunk.Content);
```
