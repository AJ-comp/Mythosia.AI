# Piloter les tâches d’IA en cours avec Run

> GPT-6 Sol/Luna: Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0. [choix du modèle et prérequis](providers.md#gpt-6-sol-luna)

Pour un résultat final et un bouton Arrêter, passez `cancellationToken` à `GetCompletionAsync`. Utilisez Run pour les événements de progression ou les instructions supplémentaires prises en charge. Voir [l’annulation](completions.md#completion-cancellation).

Pour des paramètres indépendants et réutilisables, utilisez [le builder de requête](request-building.md). Appelez `CreateRequest(...)` avant `With...`. Les propriétés et méthodes fluent du service conservent leur comportement existant.

> Pour obtenir réponse, jetons et sources ensemble, `await run.Result` renvoie un instantané `AIRunResult`. La chaîne est dans `result.Text`, sans lecture du flux. Ce changement appartient à Mythosia.AI 8.0.0 ; les types de retour de `GetCompletionAsync` et `StructuredStreamRun<T>.Result` restent identiques. [Résultat Run et migration](#run-result).

> Les exemples `CreateRequest` nécessitent Mythosia.AI 8.0.0 / Abstractions 4.0.0. Le builder n’existe pas dans l’ancienne version 7.1 qui a introduit Run et les options communes. Les anciens packages peuvent conserver les surcharges du service.

Pour une requête sensible au temps d’attente, choisissez la [vitesse de traitement](request-building.md#inference-speed). `WithSpeed` conserve modèle et effort, tandis que `Processing` rapporte le mode réellement appliqué. Fast est payant sur les combinaisons compatibles.

## Pourquoi piloter une tâche pendant son exécution ?

La rédaction d’un rapport peut demander plusieurs recherches documentaires, appels d’API et étapes d’écriture. Pendant ce temps, l’utilisateur peut vouloir suivre la progression, arrêter le travail ou ajouter une contrainte comme « Ne retenir que les données de cette année ». L’application doit pouvoir rattacher ces actions à la tâche déjà en cours.

Run fournit un objet que l’application peut conserver pour cette tâche. Un écran de discussion peut ainsi afficher le texte reçu, signaler l’utilisation d’un outil, relier un bouton Arrêter à l’annulation et envoyer une instruction supplémentaire lorsque le modèle le permet. Toutes ces actions concernent la même exécution.

| Besoin de l’application | Utilisation conseillée |
| --- | --- |
| Recevoir la réponse finale avec annulation facultative | `GetCompletionAsync(..., cancellationToken: token)` |
| Afficher le texte dès son arrivée et le récupérer à la fin | Démarrer un run avec `onText`, puis attendre `run.Result`. |
| Afficher l’activité des outils ou attendre un traitement de sortie asynchrone | Lire les événements de `run.StreamAsync()`. |
| Permettre à l’utilisateur d’arrêter le travail | Appeler `run.Cancel()` sur l’objet conservé. |
| Ajouter une contrainte avant la fin de la tâche | Vérifier `run.CanSteer`, puis utiliser `run.SteerAsync(...)` avec un modèle compatible. |

`StartRunAsync` démarre une tâche du modèle et renvoie un `AIRun`. La tâche continue que sa sortie soit observée ou non. Le même objet permet de suivre le flux, récupérer le résultat cumulé, annuler et, sur les modèles compatibles, transmettre des instructions en cours de réponse. `GetCompletionAsync`, y compris ses surcharges typées et RAG, reste une API publique pratique pour obtenir le résultat une fois le travail terminé.

<a id="run-result"></a>

## Recevoir ensemble la réponse, les jetons et les sources

Même si un écran affiche uniquement la réponse terminée, il peut devoir enregistrer les jetons utilisés et les sources. Auparavant, `run.Result` renvoyait seulement une chaîne : il fallait collecter les événements du flux pour les jetons et lire les sources séparément. `AIRunResult` rassemble ces informations sans lecture du flux.

Before — ancien contrat Run

```csharp
string answer = await run.Result;
```

After — Mythosia.AI 8.0.0

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Analysez les documents.")
    .StartRunAsync();

AIRunResult result = await run.Result;
string answer = result.Text;
int? totalTokens = result.Usage?.TotalTokens;
var citations = result.Citations;
string? requestedModel = result.RequestedModel;
string? actualModel = result.Model;
var finishReason = result.FinishReason;
```

L’affichage immédiat du texte utilise le même résultat. Les rôles du callback, de `run.StreamAsync()`, de `SteerAsync` et de l’annulation restent identiques.

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Analysez les documents.")
    .StartRunAsync(onText: text => Console.Write(text));

AIRunResult result = await run.Result;
Console.WriteLine($"\nTokens: {result.Usage?.TotalTokens}");
```

Le résultat terminé est un instantané. `Usage` et les objets de citation sont copiés : les modifier ne change pas le résultat conservé, disponible même après libération du Run. Les filtres, l’arrêt du lecteur ou le dépassement du tampon d’observation ne suppriment aucune donnée du résultat.

`Usage` additionne une seule fois les valeurs rapportées par chaque ronde du modèle. Les événements de ronde et le total final ne sont pas comptés deux fois. Sans valeur rapportée, il vaut `null` ; les données absentes ne sont pas estimées. Les requêtes de résumé auxiliaires distinctes sont exclues : ce n’est pas le total de facturation ni celui du compte. Si certaines rondes seulement rapportent leur usage, la somme couvre uniquement ces rondes ; elle ne garantit pas un relevé complet.

Un `TotalTokens` explicitement fourni est conservé même si le détail des tokens d’entrée et de sortie est incomplet ; le cumul des tours additionne ces totaux déclarés.

Les compteurs de tokens restent de type `Int32`. Si leur somme entre les tours dépasse `Int32.MaxValue`, le flux ou Run échoue avec `OverflowException` au lieu de renvoyer des valeurs débordées. Le nettoyage se termine et `run.Result` ne reste pas en attente même si le cumul final échoue.

La séquence renvoyée par `run.StreamAsync()` ne peut être parcourue qu’une fois. La parcourir de nouveau, même en parallèle, déclenche `InvalidOperationException` ; le lecteur initial et l’exécution restent indépendants.

`Provider` identifie l’adaptateur et `RequestedModel` est le modèle unique explicitement envoyé dans la requête, capturé au départ, y compris une surcharge de modèle du fournisseur. Il vaut `null` si un preset, un profil ou le routage côté serveur choisit le modèle sans champ de modèle unique explicite (par exemple, une liste Perplexity `Models`). Cette valeur est indépendante du modèle réel de réponse dans `Model`. `Model` est l’identifiant réel rapporté dans la réponse de la dernière ronde, ou `null` ; le modèle demandé ne le remplace pas. `RoundCount` compte les rondes LLM de la bibliothèque, pas les outils individuels ni les étapes internes d’un agent hébergé. Un fournisseur personnalisé sans compteur donne `0`.

`FinishReason` utilise `AIFinishReason` (`Unknown`, `Stop`, `MaxTokens`, `ToolCalls`, `ContentFilter`, `Other`) ; `RawFinishReason` conserve la valeur terminale du fournisseur. Sans information : `Unknown`/`null`. Ces champs concernent les résultats réussis. Les erreurs existantes, notamment la limite de rondes, continuent de faire échouer `Result`. Une annulation lève toujours `OperationCanceledException`, sans résultat réussi de remplacement.

`Text` conserve tous les textes émis dans l’ordre, y compris les sorties intermédiaires et antérieures à une instruction supplémentaire. Le résultat attend l’exécution et le nettoyage. `Citations` est l’instantané final ; `run.Citations` reste lisible pendant l’exécution. Les positions des citations restent locales aux parties de contenu originales, pas au texte concaténé.

<a id="run-result-migration"></a>

**Migration majeure :** `AIRun.Result` passe de `Task<string>` à `Task<AIRunResult>`. Pour la chaîne, lire `(await run.Result).Text`. Les implémentations personnalisées de `AIRun` doivent adapter leur surcharge et construire `AIRunResult` ; les consommateurs doivent recompiler. Le résultat est dans `Mythosia.AI.Models.Runs` ; `TokenUsage` et `AIFinishReason` dans `Mythosia.AI.Models.Streaming`. `GetCompletionAsync` conserve `Task<string>` et `StructuredStreamRun<T>.Result` conserve `Task<T>`. Ces exemples visent Mythosia.AI 8.0.0, pas les premiers packages Run 7.1/3.1.

## Afficher le texte avec un rappel

Dans une interface de discussion ou une console, afficher le texte dès son arrivée permet à l’utilisateur de suivre une réponse longue pendant sa rédaction.

```csharp
await using var run = await service
    .CreateRequest("Lis les documents et rédige un rapport.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Les outils locaux peuvent renvoyer des objets via `Task<T>` / `ValueTask<T>` et recevoir un `CancellationToken` injecté. `run.Cancel()` ou le jeton de démarrage atteint les outils coopératifs ; arrêter seulement le lecteur du flux ne suffit pas. Les exceptions sont des échecs. L’annulation ignore les appels en attente, et le nettoyage attend les outils démarrés qui ignorent le jeton. Voir [résultats, erreurs et annulation](function-calling.md#tool-execution-contract).

`onText` est un rappel `Action<string>` facultatif, enregistré avant le démarrage. Il reçoit le texte dans l’ordre et n’exécute pas les outils. Omettez-le si seul le résultat vous intéresse. Une exception dans le rappel annule le run et fait échouer `Result`. Ne transmettez pas de lambda `async` à `onText` : elle deviendrait `async void`, dont le run ne pourrait attendre ni le travail ni les erreurs. Utilisez le flux d’événements pour une sortie asynchrone. Les rappels ne sont pas automatiquement exécutés sur le thread de l’interface.

`(await run.Result).Text` concatène les événements textuels du run, y compris le texte intermédiaire entre les appels d’outils et celui produit avant une instruction supplémentaire. Il ne déclenche pas une seconde requête au modèle et ne reformule pas la réponse. Les applications qui souhaitent seulement attendre le résultat peuvent conserver `GetCompletionAsync` si elles préfèrent sa sémantique de réponse existante.

## Lire les événements de texte, d’outils et d’utilisation

Lorsqu’une tâche recherche des documents ou appelle une API métier, le texte seul n’explique pas toujours l’attente. Les événements typés permettent d’afficher l’activité des outils avec la réponse et d’enregistrer les données d’utilisation fournies par le fournisseur.

```csharp
await using var run = await service.StartRunAsync(
    "Recherche dans les documents et explique le résultat.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[Appel d’un outil]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[Résultat d’outil reçu]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[Total des tokens : {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = (await run.Result).Text;
```

`run.StreamAsync()` accepte un jeton d’annulation facultatif pour l’observation, sans prompt. Il observe la tâche déjà démarrée par `StartRunAsync`. La bibliothèque exécute les gestionnaires de fonctions enregistrés ; ne relancez jamais un outil en réaction à son événement d’affichage. Les options d’affichage du texte ne désactivent pas les outils enregistrés du run.

Le rappel de démarrage et `run.StreamAsync()` peuvent observer le même run simultanément ; le flux d’événements accepte un seul lecteur. Par exemple, affichez le texte dans `onText` et ne traitez que les événements d’outils dans le flux pour éviter un affichage en double. Jusqu’à 1 024 événements non lus sont conservés, même avec un rappel. Un lecteur démarré tardivement reçoit les événements depuis le début tant qu’ils tiennent dans le tampon. Si la limite est dépassée, l’observation du flux échoue explicitement, tandis que le rappel, l’exécution et `Result` continuent. Le flux n’est pas un journal de relecture illimité. Il n’est jamais nécessaire de vider le flux pour attendre `Result`.

## Sortie asynchrone

Pour traiter la sortie de façon asynchrone, attendez l’opération dans le lecteur plutôt que d’utiliser un rappel `onText` asynchrone :

```csharp
using var writer = new StreamWriter("rapport.txt");
await using var run = await service.StartRunAsync(
    "Rédige un rapport.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = (await run.Result).Text;
```

## Annuler et libérer les ressources

- Sortir de `await foreach` ou annuler le jeton transmis uniquement à `run.StreamAsync(token)` arrête l’observation ; la tâche continue.
- `run.Cancel()`, le jeton transmis à `StartRunAsync` et la libération d’un run actif annulent l’exécution.
- `await using` garantit que `DisposeAsync()` attend le producteur et le nettoyage du fournisseur. Les outils qui ne prennent pas en charge l’annulation peuvent encore mettre du temps à terminer ; la libération n’annule pas les actions déjà effectuées.
- Un service autorise une seule tâche `StartRunAsync` active. Un démarrage simultané est refusé. Utilisez des services distincts pour des tâches concurrentes indépendantes et ne mélangez pas les anciens appels ni ne modifiez la configuration du service pendant un run actif.

Le run capture son entrée et la politique prévue pour cette requête avant l’exécution en arrière-plan. Les contenus intégrés de texte, d’image et d’audio ainsi que les tableaux d’octets des médias sont copiés. Les sous-classes personnalisées de `MessageContent` conservent leur identité et doivent rester inchangées jusqu’à la fin du run.

La valeur capturée de `FunctionCallingPolicy.TimeoutSeconds` définit une échéance unique pour la préparation et l’ensemble des tours modèle/outils. Son expiration produit une `AIServiceException` ; une annulation par l’utilisateur annule le résultat. Le nettoyage attend toujours les gestionnaires sans prise en charge de l’annulation.

## Envoyer une instruction pendant le travail

Supposons qu’un utilisateur lance la préparation d’un plan de projet, puis réalise qu’il doit tenir sur deux semaines. Le pilotage en cours d’exécution permet d’envoyer cette nouvelle contrainte pendant que le modèle travaille encore. Il convient aux corrections et aux changements de périmètre découverts pendant une tâche longue. Pour une nouvelle question après la fin de la tâche, démarrez normalement la requête suivante.

```csharp
await using var run = await service.StartRunAsync(
    "Prépare un plan de projet.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// Appeler depuis le gestionnaire d’instructions supplémentaires de l’interface pendant le run.
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("Ce run ne prend pas en charge les instructions supplémentaires.");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = (await run.Result).Text;
```

Les instructions en cours de réponse sont prises en charge par GPT-6 Astra / Sol / Luna sur une connexion WebSocket Responses. Les autres fournisseurs et les modèles non compatibles peuvent exécuter des runs ordinaires, mais `CanSteer` vaut false et le pilotage signale l’absence de prise en charge au lieu de créer silencieusement un tour de conversation ordinaire. `CanSteer` ne garantit pas que le run sera encore actif au moment d’un appel ultérieur.

Les runs GPT-6 ouvrent un socket dédié. Le `HttpClient` fourni et ses gestionnaires de messages continuent de servir les appels HTTP et n’interceptent pas ce socket. Un transport personnalisé peut redéfinir `OpenAIService.ConnectRunWebSocketAsync`.

La réussite de `SteerAsync` indique que le serveur a accepté l’entrée dans sa file, sans garantir que le modèle l’a déjà appliquée. Continuez à observer le même run ou à attendre son résultat pendant la continuation. Le texte déjà livré et les actions terminées ne sont pas annulés ; les outils déjà démarrés ne sont pas arrêtés par le seul envoi d’une nouvelle instruction. La bibliothèque gère la continuation et l’association des résultats d’outils sur la même connexion. Consultez le [guide des instructions en cours de réponse](https://developers.openai.com/api/docs/guides/steering) et le [mode WebSocket](https://developers.openai.com/api/docs/guides/websocket-mode) d’OpenAI. Ne supposez pas que les entrées en attente liées à une connexion survivent à sa coupure et ne renvoyez pas aveuglément une instruction acceptée.

## Tâches avec outils et anciennes méthodes d’agent

Une demande comme « Vérifie la politique de remboursement et l’état de cette commande » nécessite plusieurs sources. Enregistrez les outils de recherche documentaire et de consultation des commandes, puis laissez le modèle choisir les appels nécessaires. Une limite de tours borne le nombre de demandes d’outils avant que le modèle doive terminer ou signaler une erreur.

Les appels de fonctions ordinaires permettent déjà plusieurs tours entre le modèle et les outils. `StartRunAsync` utilise les mêmes fonctions enregistrées et politiques d’exécution ; aucun mode agent distinct, planificateur ou commutateur `WithAgentic` n’est nécessaire.

`RunAgentAsync` et `RunAgentStreamAsync` restent appelables, mais portent désormais des avertissements `[Obsolete]`. Leurs signatures, la valeur par défaut `maxSteps = 10` et le comportement d’erreur historique à la limite des étapes sont conservés pendant la migration. Pour les nouveaux appels, utilisez :

```csharp
await using var run = await service
    .CreateRequest("Trouve la politique, vérifie la commande et explique le résultat.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

La valeur générale par défaut de `FunctionCallingPolicy.MaxRounds` est 20 ; précisez donc 10 pour conserver la limite de l’ancien agent. `WithMaxRounds` configure une politique pour une seule requête et ne modifie pas `DefaultPolicy`. Configurez-la avant de commencer. Les anciennes méthodes d’agent copient plutôt la politique par défaut actuelle et lui appliquent le `maxSteps` de l’appel. Un nouveau run utilise le contrat commun des erreurs d’exécution et ne garantit pas la conversion historique en `AgentMaxStepsExceededException`/`PartialResponse`. Si ce contrat est nécessaire, conservez l’ancien appel jusqu’à avoir migré sa gestion des exceptions.

## RAG, MCP et limites des packages

- `RagEnabledService.StartRunAsync` accepte une chaîne ou un `Message`, `onText`, des `RagQueryOptions` par requête, `streamOptions` et l’annulation. Il effectue la recherche avant le run sous-jacent, préserve les images, l’audio et les métadonnées, conserve l’entrée originale dans l’historique et transmet le texte enrichi par le contexte de requête. Cet enrichissement reste rattaché à la question originale ; les résultats d’outils et les instructions ultérieures ne sont donc pas remplacés par le prompt RAG initial. Une instruction sur le run retourné met à jour le modèle, sans relancer automatiquement la recherche RAG.
- `WithAgenticRag` continue d’enregistrer un outil de recherche. Utilisé via `StartRunAsync`, il permet au modèle de demander d’autres recherches si nécessaire. L’enregistrement MCP avec `WithMcpServerAsync` reste également inchangé. Libérez les connexions MCP partagées séparément des runs qui les utilisent.
- `IAIRunService` est une capacité facultative de `Mythosia.AI.Abstractions` ; aucun nouveau membre obligatoire n’est ajouté à `IAIService`. Un service personnalisé doit implémenter `IAIRunService` pour démarrer un run RAG. Les services non compatibles sont refusés avant le début de l’indexation RAG.
- RAG conserve sa dépendance envers Abstractions ; les fournisseurs distribués séparément conservent leurs redéfinitions publiques de completion et leurs points d’extension accessibles. Ce changement ne déprécie aucune API de stockage vectoriel, de chargement documentaire ou d’administration de serveur.

Pour configurer le raisonnement, la recherche Web ou documentaire avant de démarrer le Run et afficher ses sources, consultez [Raisonnement et réponses avec sources](reasoning-and-search.md).

## Migrer vers Mythosia.AI 8

| API | Statut actuel |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Publique et prise en charge, y compris les variantes d’interface, de fournisseur et RAG. |
| `StartRunAsync` / `AIRun` | API commune d’exécution et de pilotage. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Avertissements Obsolete ; comportement existant conservé pour la compatibilité. |
| `service.StreamAsync` et `StreamAsync` RAG recevant une entrée | Les StreamAsync de service/RAG avec entrée restent publics en v8. Utilisez StartRunAsync pour les nouveaux contrôles ; run.StreamAsync() observe uniquement un run existant. |
| `run.StreamAsync()` | Observation de la sortie d’une tâche existante, sans nouvelle entrée de requête. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | API de streaming typé conservée ; son `Stream()` de sortie seule n’est pas une ancienne méthode de requête du service. |

[Migrer vers Mythosia.AI 8](v8-migration.md).

Perplexity: [Poursuivre une tâche longue](perplexity.md).

[Construire les options du modèle avec des définitions partagées](model-capabilities.md).
