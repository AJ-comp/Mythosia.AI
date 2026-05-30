# Utilisation des tokens

L'utilisation des tokens indique combien de tokens une requête a consommés pour l'entrée, la sortie, le cache et le raisonnement. Dans Mythosia.AI, ces données sont exposées via `TokenUsage` sur les événements de streaming.

C'est surtout important quand une réponse ne se limite pas à un seul appel LLM. Une réponse simple tient souvent en un seul round. Un agent ou un flux avec function calling peut appeler le modèle, exécuter un outil, puis rappeler le modèle avec le résultat. Il y a donc deux chiffres à distinguer.

- `RoundUsage` décrit l'utilisation d'un seul round LLM.
- `Completion.Usage` décrit l'utilisation cumulée de tout le stream.

> [!NOTE]
> Cette page suppose que tu sais déjà ce qu'est un **round LLM**. En résumé : un round = un aller-retour requête–réponse entre ton app et le modèle. Les flux de function calling peuvent produire plusieurs rounds pour un seul message utilisateur. Pour une explication pas à pas, consulte [Concepts fondamentaux — Qu'est-ce qu'un round ?](core-concepts.md#quest-ce-quun-round).

## Pourquoi c'est utile

Pour une jauge de contexte dans une interface de chat, utilisez le dernier `RoundUsage.Usage.InputTokens`. C'est la valeur la plus proche de "combien pèserait l'entrée du prochain appel LLM si la conversation continuait maintenant".

Pour les logs, le diagnostic et l'analyse des coûts, utilisez `Completion.Usage.TotalTokens`. Cette valeur reste cumulative sur tout le run, y compris quand un agent déclenche plusieurs rounds.

Pour l'optimisation, les champs liés au cache et au raisonnement aident à voir si le provider a réutilisé une partie de l'entrée ou s'il a dépensé des tokens supplémentaires en raisonnement interne.

## Modèle d'événements

| Événement | Signification | Usage recommandé |
|---|---|---|
| `StreamingContentType.RoundUsage` | Utilisation du round LLM qui vient de se terminer | Jauge de contexte UI, debug round par round |
| `StreamingContentType.Completion` | Événement final avec utilisation cumulée | Logs, diagnostic, rapports de coût |

`RoundUsage.Usage` n'est pas cumulatif. Si le round 1 consomme 10 100 tokens et le round 2 en consomme 14 000, `Completion.Usage.TotalTokens` peut valoir 24 100, tandis que le dernier `RoundUsage.Usage.TotalTokens` reste à 14 000.

| Propriété | Signification |
|---|---|
| `RoundIndex` | Numéro du round LLM, à partir de 1 |
| `IsFinalRound` | `true` si ce round est le dernier du stream |

Les données de tokens sont émises lorsque le provider les retourne. Il n'est pas nécessaire d'activer `IncludeMetadata = true` pour recevoir les événements d'usage.

## Utilisation cumulée finale

Lisez `Completion.Usage` quand vous voulez le total du stream.

```csharp
await foreach (var chunk in service.StreamAsync("Explique l'informatique quantique", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

Pour un seul round LLM, cette valeur est généralement proche du `RoundUsage`. Pour un agent, elle additionne tous les rounds LLM.

## Jauge de tokens dans l'UI

Pour une jauge de taille de contexte, utilisez le dernier `RoundUsage`.

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.InputTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

Le dernier round du modèle voit l'état le plus récent de la conversation, y compris les résultats d'outils ajoutés pendant le run. C'est pourquoi le dernier `RoundUsage.Usage.InputTokens` est la meilleure valeur pour une UI de chat.

<a id="how-context-size-changes"></a>

## Comment la taille du contexte évolue

Considérez la taille du contexte comme la taille de l'entrée du dernier appel au modèle, et non comme un total cumulé. Un round ultérieur contient déjà les éléments de conversation conservés depuis les rounds précédents. Additionner les entrées des rounds compterait donc deux fois le même prompt, les mêmes définitions d'outils et le même historique.

Par exemple :

| Étape | Ce qui est ajouté avant cet appel au modèle | Tokens d'entrée approximatifs | Jauge de contexte UI |
|---|---|---:|---:|
| Round 1 | Prompt système, outils, historique, message utilisateur | 20 000 | 20 000 |
| Entre les rounds | La sortie du tool call fait 100 tokens ; le résultat d'outil fait 5 000 tokens | pas d'appel LLM | inchangée |
| Round 2 | Entrée du round 1 + message de tool call + résultat d'outil | 25 100 + overhead | 25 100 + overhead |
| Sortie du round 2 | Le modèle génère 3 000 tokens et un autre round est nécessaire | pas d'appel LLM | inchangée |
| Round 3 | Entrée du round 2 + sortie du round 2, plus tout nouveau résultat d'outil | 28 100 + overhead | 28 100 + overhead |
| Sortie du round 3 | Le modèle génère une réponse finale de 2 000 tokens | pas d'appel LLM | inchangée |
| Message utilisateur suivant | La réponse finale précédente et le nouveau message utilisateur font maintenant partie de l'entrée suivante | environ 30 100 + nouveau message + overhead | remplacée par les `InputTokens` du nouveau round |

Si le round 3 est le round final, la jauge de contexte devrait donc afficher environ **28 100 + overhead**, pas 30 100 ni la somme de tous les rounds. La réponse finale de 2 000 tokens influence le prochain appel au modèle parce qu'elle devient de l'historique de conversation.

## Function Calling et agents

Dans un flux avec function calling, le modèle peut être appelé plusieurs fois. Gardez le dernier `RoundUsage` pour l'UI, puis utilisez `Completion.Usage` à la fin pour le total.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function result: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: input={latestRound.InputTokens}, total={latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}

Console.WriteLine($"UI meter: {latestRound?.InputTokens}");
Console.WriteLine($"Run total: {cumulative?.TotalTokens}");
```

## Cache et raisonnement

Lorsque le provider les fournit, `TokenUsage` contient aussi des champs liés au cache et au raisonnement.

| Propriété | Signification |
|---|---|
| `InputTokens` | Tokens dans le prompt ou l'entrée |
| `OutputTokens` | Tokens générés par le modèle |
| `TotalTokens` | Entrée + sortie pour la portée de l'événement |
| `CachedInputTokens` | Tokens d'entrée servis depuis le cache |
| `CacheCreationTokens` | Tokens écrits dans le cache |
| `ReasoningTokens` | Tokens utilisés pour le raisonnement masqué |
| `VisibleOutputTokens` | Tokens de sortie hors raisonnement |

## Pourquoi utiliser les événements normalisés

Chaque provider accroche ses données d'usage à des chunks différents. Le cas le plus délicat est Gemini : l'usage peut arriver sur un chunk de texte ou de statut, parfois même après un chunk de function call — la bibliothèque continue donc de lire le stream assez longtemps pour capturer cet usage avant de passer au round suivant. Mythosia.AI absorbe ces différences entre providers et les normalise en événements `RoundUsage` et `Completion.Usage`, donc côté application, au lieu de parser vous-même les metadata propres à chaque provider, utilisez les événements normalisés `RoundUsage` et `Completion.Usage`.
