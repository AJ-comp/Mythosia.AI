# Observer les tâches longues avec Claude Fable 5.1

[Claude Opus 5.5](providers.md#claude-opus-55) est un ajout non publié : raisonnement toujours actif, effort medium par défaut et affichage omis. Demandez explicitement une progression lisible ; ses valeurs par défaut et règles de liaison diffèrent de Fable 5.1.

> Les contrôles de Fable 5.1 nécessitent `Mythosia.AI` 8.0.0 et `Mythosia.AI.Abstractions` 4.0.0 ou ultérieurs. Les API Run, raisonnement/recherche et GPT-6 Astra existantes conservent leurs versions minimales 7.1.0 / 3.1.0.

## Dans quels cas utiliser ces contrôles ?

Une recherche documentaire peut demander plusieurs recherches et appels d’outils avant de produire une réponse. L’application peut devoir afficher la progression, imposer une vérification pour le seul tour actuel ou reprendre après une modification du dialogue antérieur. Fable 5.1 fournit des contrôles pour ces situations, mais la réutilisation du raisonnement conservé fait aussi de l’historique une partie du contrat de requête.

Utilisez l’[API Run](execution-api-transition.md) pour observer et annuler la tâche, les [options communes de raisonnement et de recherche](reasoning-and-search.md) pour choisir l’effort et les sources, puis les réglages Claude ci-dessous pour la progression et l’historique. Les capacités natives d’un modèle ne signifient pas que Mythosia expose toutes les API du fournisseur.

## Choisir explicitement le modèle et l’effort

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` sélectionne `claude-fable-5-1`. `ClaudeMythos5_1` sélectionne `claude-mythos-5-1` et nécessite un accès Project Glasswing. Les constantes Fable 5 et Mythos 5 sont conservées. Les deux modèles 5.1 acceptent du texte et des images et produisent du texte, avec un contexte de 1M tokens et une sortie maximale de 128K tokens. [Présentation du modèle](https://platform.claude.com/docs/en/models/fable-5-1/overview).

L’effort natif par défaut du modèle est `high`, mais `ClaudeReasoningEffort.Auto` de Mythosia conserve la correspondance existante avec `ThinkingBudget` : les budgets activés donnent `High`, puis `XHigh` à partir de 32 768 et `Max` à partir de 100 000. Une demande de désactivation utilise un effort adaptatif faible et omet le raisonnement lisible. Choisissez explicitement `High` si vous voulez ce comportement ; `Auto` ne signifie pas que la bibliothèque omet toujours effort au profit du réglage natif.

## Afficher la progression entre les appels d’outils

`ClaudeThinkingDisplay.Updates` demande des messages de progression lisibles tout en masquant le raisonnement. `Summarized` inclut aussi le raisonnement résumé ; `Omitted` supprime les blocs thinking lisibles. Les mises à jour dépendent de leur génération par le modèle : aucun intervalle fixe n’est garanti. [Mises à jour de progression](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta).

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

Ces mises à jour utilisent l’événement existant `StreamingContentType.Reasoning`. Activez leur observation avec `StreamOptions.FullOptions` ou `StreamOptions.Default.WithReasoning()`. Après un appel sans streaming, lisez `service.LastThinkingContent`. Le texte de progression est distinct de la réponse finale et ne révèle pas la chaîne de pensée brute.

## Modifier les instructions d’un tour sans réécrire le passé

Les blocs thinking de Fable 5.1 sont liés au prompt système, aux outils et aux messages qui les précèdent au moment de leur création. Réécrire ces entrées en conservant le thinking ultérieur peut l’invalider. Une instruction limitée à un tour permet, par exemple, d’exiger une vérification de la politique d’assistance avant la réponse actuelle. Elle est ajoutée à la fin et reste dans l’historique, puis cesse de s’appliquer lorsqu’un message utilisateur ultérieur apparaît. Il n’est donc pas nécessaire de réécrire sans cesse le prompt système principal. L’effort et les instructions par tour sont deux contrôles distincts.

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

Les deux méthodes capturent les instructions pour la prochaine requête logique. Mythosia ajoute un message system après l’entrée utilisateur ou les résultats d’outils en conservant les messages précédents. `WithTurnInstruction` utilise `clear_at: "next_user_message"` ; au sein d’une même requête, la bibliothèque réajoute l’instruction après chaque tour de résultats d’outils afin qu’elle reste active jusqu’à la fin de la requête. `WithConversationInstruction` reste applicable aux tours suivants. Configurez ces méthodes avant le démarrage : elles ne sont pas `run.SteerAsync` et n’injectent pas d’instruction dans une réponse déjà en cours.

Pour ajuster l’effort entre requêtes tout en préservant un préfixe de cache réutilisable, utilisez `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)` de `Mythosia.AI.Extensions`. La bibliothèque envoie une mise à jour d’effort par message et la conserve dans l’historique. Les combinaisons admises sont décrites dans le [guide commun](reasoning-and-search.md). Pour 5.1, les préfixes/suffixes système par requête d’`AIRequestContext` deviennent des instructions de tour ajoutées à la fin, sans modifier un prompt système antérieur.

Fable 5.1 peut lire le thinking des anciens modèles Claude, mais ces modèles ne peuvent pas lire le sien. Mythos 5.1 offre les mêmes capacités 5.1 sans imposer la vérification de liaison au préfixe de Fable. Observez les changements d’historique, de modèle et les blocs supprimés au lieu de supposer que le même raisonnement subsiste. [Guide de migration](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## Diagnostiquer une modification volontaire de l’historique

`ThinkingPrefixMismatchBehavior = null` laisse la vérification à la politique du compte fournisseur. `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` demande explicitement une validation serveur. Les modifications utilisateur de l’historique, de `SystemMessage` ou des outils sont transmises à Anthropic ; avec `Error`, un préfixe incompatible produit une réponse 400 du fournisseur. Réessayer la même requête invalide ne la corrige pas.

Si l’application modifie volontairement du contenu antérieur et accepte de perdre le raisonnement concerné, choisissez `DropBlock`. Mythosia transmet ce contrôle à Anthropic ; la bibliothèque ne retire pas silencieusement le thinking avant la requête.

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` expose les champs `Type`, `Path` et `Reason` rapportés par le fournisseur, ainsi que `ResponseId` et `Model` pour leur attribution. `prefix_binding_mismatch` signale un préfixe modifié ; `model_binding_mismatch`, un thinking illisible par le modèle cible. Supprimer le raisonnement ne le répare pas. Gardez la transcription intacte si sa conservation est nécessaire, ou commencez une nouvelle conversation pour la réinitialiser.

Mythosia conserve l’historique transmis pour éviter les modifications accidentelles dues au traitement interne de RAG/context. La compression locale automatique est bloquée pour les conversations Fable 5.1 ordinaires avec le comportement par défaut ou `Error`. `DropBlock` l’autorise, mais peut supprimer du raisonnement et ne garantit pas les accès au cache. L’option distincte `CachePreservation.Required` garde ses protections plus strictes de l’historique. Les options communes comme `WithWebSearch()` sont consommées après chaque requête. Les omettre au tour suivant modifie le tableau natif tools et peut créer une incompatibilité de préfixe. Réappliquez les mêmes réglages d’outils/recherche pour conserver l’historique ; utilisez `DropBlock` ou une nouvelle conversation pour un changement volontaire. Ces options ne sont pas reconduites automatiquement.

L’instantané de l’historique transmis appartient au service et à son `ChatBlock`. Copier seulement le `ChatBlock` vers un nouveau service ne transfère pas les instantanés antérieurs de RAG/context ou de system par tour. Poursuivez avec le même service et la même conversation pour conserver le raisonnement ; si vous avez déplacé seulement l’historique brut, commencez une nouvelle conversation sans supposer l’état préservé.

## Utiliser la sélection ordinaire des outils

Fable 5.1 et Mythos 5.1 refusent la sélection forcée d’outils. Laissez `ForceFunctionName` non défini et décrivez dans la requête quand utiliser l’outil enregistré. `FunctionsDisabled` reste disponible pour les tours qui ne doivent appeler aucun outil. Pour une réponse typée, utilisez l’API existante de sortie structurée plutôt que de forcer une fonction uniquement pour obtenir du JSON.

## Identifier les changements gérés par le serveur

| Option native | Bêta Anthropic requise |
| --- | --- |
| Effort par message | `mid-conversation-output-config-2026-07-01` |
| Message system limité à un tour | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Contrôles de liaison du thinking et `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia ajoute l’en-tête requis lorsque le réglage pris en charge correspondant est activé. Activer une bêta n’active pas toutes les autres. Cette intégration n’ajoute ni compaction serveur, ni blocs natifs d’ajout/suppression d’outils, ni repli automatique vers un autre modèle.

Les deux modèles nécessitent les conditions de conservation de 30 jours applicables du fournisseur ; le ZDR exige l’autorisation explicite d’Anthropic. Le thinking adaptatif reste toujours actif ; les `budget_tokens` manuels et sa désactivation ne sont pas disponibles. Les paramètres d’échantillonnage personnalisés ne sont pas envoyés. L’accès au compte et la conservation relèvent du serveur. [Conditions de migration](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

Anthropic applique le filigrane textuel, la provenance des médias pris en charge et le prix de lecture du cache. Aucune nouvelle option de requête Mythosia n’est nécessaire. L’intégration n’ajoute pas d’API de création de provenance des médias, d’interrupteur de filigrane ni de contrôle de facturation. Voir les [nouveautés de Fable 5.1](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1).
