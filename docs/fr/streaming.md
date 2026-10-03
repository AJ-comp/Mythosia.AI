# Streaming

L’adaptateur de streaming par rappel annule et attend son producteur à la sortie anticipée. Annulation du Run, expiration, erreur du rappel et `DisposeAsync` attendent le nettoyage avant de terminer `Result` et de libérer le verrou. Un traitement non coopératif peut retarder cette fin ; les erreurs d’observation et de nettoyage sont conservées ensemble. `ContextRecoveryMaxRetries` utilise la valeur capturée. Arrêter seulement l’observation de `run.StreamAsync()` n’annule pas le Run. L’acquisition du corps d’une réponse SSE réussie présente une [limitation distincte d’annulation](#sse-acquisition-cancellation-limitation).

> Claude Sonnet 5.5: Nécessite Mythosia.AI 8.2.0 / Abstractions 4.2.0. [Configuration et migration](providers.md#claude-sonnet-55)

En adaptive, `ClaudeThinkingDisplay.Updates` demande la progression lisible des outils, et `Summarized` les résumés de raisonnement. Observez `StreamingContentType.Reasoning`, ou `LastThinkingContent` après une completion. Le helper adaptive utilise `Summarized` lorsque display est omis, contrairement aux réglages inchangés. `between_tools` renvoie automatiquement la progression, sans garantie d’intervalle fixe.

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

**Réponses d’erreur Claude :** L’annulation et le délai de la politique de requête interrompent aussi la lecture bloquée du corps d’une erreur HTTP pendant le streaming ou un Run. Après le nettoyage du Run, un autre peut démarrer sur le même service. L’annulation par l’appelant lève `OperationCanceledException` ; l’expiration du délai lève `AIServiceException`. L’annulation contrôle le transport local et le nettoyage coopératif, sans garantir l’arrêt du traitement ou de la facturation du fournisseur.

**Nettoyage des réponses Claude :** Le streaming Claude et Run attendent le nettoyage asynchrone du corps HTTP déjà obtenu, y compris pour les flux personnalisés qui nécessitent une libération asynchrone. Une exception ultérieure lors de la libération de la réponse ou de son contenu ne remplace ni une réussite, ni l’erreur de lecture d’origine, ni une annulation ; la libération de la réponse et du contenu d’origine reste tentée.

**Délais HTTP :** Pour le streaming de texte, de contenu ou par callback et les Runs qui passent par le chemin commun des tours de streaming, un `HttpClient.Timeout` identifiable (`TaskCanceledException` contenant une `TimeoutException`) devient une `AIServiceException` si ni l’annulation par l’appelant ni le délai de la politique de requête n’ont été déclenchés. `InnerException` conserve l’exception de transport d’origine : `run.Result` échoue donc en préservant la cause du dépassement de délai. L’annulation par l’appelant, les délais de la politique et les autres annulations de transport conservent leur comportement.

<a id="sse-acquisition-cancellation-limitation"></a>

## Limitation connue : acquisition du corps d’une réponse SSE réussie

Sur HTTP 200 SSE, un wrapper `HttpContent` qui met le corps en mémoire tampon dans un handler personnalisé peut bloquer `ReadAsStreamAsync` avant l’acquisition du flux et le nettoyage. L’annulation par l’appelant et le délai de la politique de requête peuvent laisser `run.Result` en attente, la réponse non libérée et le verrou du Run actif maintenu jusqu’à la fin de l’acquisition ; un autre Run est alors refusé comme déjà actif. Ce problème reste non corrigé et se distingue d’un nettoyage lent. Le `SocketsHttpHandler` par défaut a réussi les scénarios testés ; l’annulation du corps d’erreur HTTP a également réussi avec le wrapper. Utilisez un contenu de streaming ordinaire sans wrapper qui le met en mémoire tampon. La recherche web native de Claude présente une [limitation distincte de continuation](providers.md#claude-native-continuation-limitation).

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
