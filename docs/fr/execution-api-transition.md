# Piloter les tâches d’IA en cours avec Run

> Ces API nécessitent `Mythosia.AI` 7.1.0 ou ultérieur, qui inclut `Mythosia.AI.Abstractions` 3.1.0 ou ultérieur. Les exemples RAG nécessitent `Mythosia.AI.Rag` 7.6.0 ou ultérieur.

## Pourquoi piloter une tâche pendant son exécution ?

La rédaction d’un rapport peut demander plusieurs recherches documentaires, appels d’API et étapes d’écriture. Pendant ce temps, l’utilisateur peut vouloir suivre la progression, arrêter le travail ou ajouter une contrainte comme « Ne retenir que les données de cette année ». L’application doit pouvoir rattacher ces actions à la tâche déjà en cours.

Run fournit un objet que l’application peut conserver pour cette tâche. Un écran de discussion peut ainsi afficher le texte reçu, signaler l’utilisation d’un outil, relier un bouton Arrêter à l’annulation et envoyer une instruction supplémentaire lorsque le modèle le permet. Toutes ces actions concernent la même exécution.

| Besoin de l’application | Utilisation conseillée |
| --- | --- |
| Recevoir une réponse terminée sans piloter le travail en cours | Conserver `GetCompletionAsync`, y compris ses surcharges typées et RAG. |
| Afficher le texte dès son arrivée et le récupérer à la fin | Démarrer un run avec `onText`, puis attendre `run.Result`. |
| Afficher l’activité des outils ou attendre un traitement de sortie asynchrone | Lire les événements de `run.StreamAsync()`. |
| Permettre à l’utilisateur d’arrêter le travail | Appeler `run.Cancel()` sur l’objet conservé. |
| Ajouter une contrainte avant la fin de la tâche | Vérifier `run.CanSteer`, puis utiliser `run.SteerAsync(...)` avec un modèle compatible. |

`StartRunAsync` démarre une tâche du modèle et renvoie un `AIRun`. La tâche continue que sa sortie soit observée ou non. Le même objet permet de suivre le flux, récupérer le résultat cumulé, annuler et, sur les modèles compatibles, transmettre des instructions en cours de réponse. `GetCompletionAsync`, y compris ses surcharges typées et RAG, reste une API publique pratique pour obtenir le résultat une fois le travail terminé.

## Afficher le texte avec un rappel

Dans une interface de discussion ou une console, afficher le texte dès son arrivée permet à l’utilisateur de suivre une réponse longue pendant sa rédaction.

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Lis les documents et rédige un rapport.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

string answer = await run.Result;
```

`onText` est un rappel `Action<string>` facultatif, enregistré avant le démarrage. Il reçoit le texte dans l’ordre et n’exécute pas les outils. Omettez-le si seul le résultat vous intéresse. Une exception dans le rappel annule le run et fait échouer `Result`. Ne transmettez pas de lambda `async` à `onText` : elle deviendrait `async void`, dont le run ne pourrait attendre ni le travail ni les erreurs. Utilisez le flux d’événements pour une sortie asynchrone. Les rappels ne sont pas automatiquement exécutés sur le thread de l’interface.

`Result` concatène les événements textuels du run, y compris le texte intermédiaire entre les appels d’outils et celui produit avant une instruction supplémentaire. Il ne déclenche pas une seconde requête au modèle et ne reformule pas la réponse. Les applications qui souhaitent seulement attendre le résultat peuvent conserver `GetCompletionAsync` si elles préfèrent sa sémantique de réponse existante.

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

string answer = await run.Result;
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
string answer = await run.Result;
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

string answer = await run.Result;
```

Les instructions en cours de réponse sont prises en charge par GPT-6 Astra sur une connexion WebSocket Responses. Les autres fournisseurs et les modèles non compatibles peuvent exécuter des runs ordinaires, mais `CanSteer` vaut false et le pilotage signale l’absence de prise en charge au lieu de créer silencieusement un tour de conversation ordinaire. `CanSteer` ne garantit pas que le run sera encore actif au moment d’un appel ultérieur.

Les runs Astra ouvrent un socket dédié. Le `HttpClient` fourni et ses gestionnaires de messages continuent de servir les appels HTTP et n’interceptent pas ce socket. Un transport personnalisé peut redéfinir `OpenAIService.ConnectRunWebSocketAsync`.

La réussite de `SteerAsync` indique que le serveur a accepté l’entrée dans sa file, sans garantir que le modèle l’a déjà appliquée. Continuez à observer le même run ou à attendre son résultat pendant la continuation. Le texte déjà livré et les actions terminées ne sont pas annulés ; les outils déjà démarrés ne sont pas arrêtés par le seul envoi d’une nouvelle instruction. La bibliothèque gère la continuation et l’association des résultats d’outils sur la même connexion. Consultez le [guide des instructions en cours de réponse](https://developers.openai.com/api/docs/guides/steering) et le [mode WebSocket](https://developers.openai.com/api/docs/guides/websocket-mode) d’OpenAI. Ne supposez pas que les entrées en attente liées à une connexion survivent à sa coupure et ne renvoyez pas aveuglément une instruction acceptée.

## Tâches avec outils et anciennes méthodes d’agent

Une demande comme « Vérifie la politique de remboursement et l’état de cette commande » nécessite plusieurs sources. Enregistrez les outils de recherche documentaire et de consultation des commandes, puis laissez le modèle choisir les appels nécessaires. Une limite de tours borne le nombre de demandes d’outils avant que le modèle doive terminer ou signaler une erreur.

Les appels de fonctions ordinaires permettent déjà plusieurs tours entre le modèle et les outils. `StartRunAsync` utilise les mêmes fonctions enregistrées et politiques d’exécution ; aucun mode agent distinct, planificateur ou commutateur `WithAgentic` n’est nécessaire.

`RunAgentAsync` et `RunAgentStreamAsync` restent appelables, mais portent désormais des avertissements `[Obsolete]`. Leurs signatures, la valeur par défaut `maxSteps = 10` et le comportement d’erreur historique à la limite des étapes sont conservés pendant la migration. Pour les nouveaux appels, utilisez :

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Trouve la politique, vérifie la commande et explique le résultat.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

La valeur générale par défaut de `FunctionCallingPolicy.MaxRounds` est 20 ; précisez donc 10 pour conserver la limite de l’ancien agent. `WithMaxRounds` configure une politique pour une seule requête et ne modifie pas `DefaultPolicy`. Configurez-la avant de commencer. Les anciennes méthodes d’agent copient plutôt la politique par défaut actuelle et lui appliquent le `maxSteps` de l’appel. Un nouveau run utilise le contrat commun des erreurs d’exécution et ne garantit pas la conversion historique en `AgentMaxStepsExceededException`/`PartialResponse`. Si ce contrat est nécessaire, conservez l’ancien appel jusqu’à avoir migré sa gestion des exceptions.

## RAG, MCP et limites des packages

- `RagEnabledService.StartRunAsync` accepte une chaîne ou un `Message`, `onText`, des `RagQueryOptions` par requête, `streamOptions` et l’annulation. Il effectue la recherche avant le run sous-jacent, préserve les images, l’audio et les métadonnées, conserve l’entrée originale dans l’historique et transmet le texte enrichi par le contexte de requête. Cet enrichissement reste rattaché à la question originale ; les résultats d’outils et les instructions ultérieures ne sont donc pas remplacés par le prompt RAG initial. Une instruction sur le run retourné met à jour le modèle, sans relancer automatiquement la recherche RAG.
- `WithAgenticRag` continue d’enregistrer un outil de recherche. Utilisé via `StartRunAsync`, il permet au modèle de demander d’autres recherches si nécessaire. L’enregistrement MCP avec `WithMcpServerAsync` reste également inchangé. Libérez les connexions MCP partagées séparément des runs qui les utilisent.
- `IAIRunService` est une capacité facultative de `Mythosia.AI.Abstractions` ; aucun nouveau membre obligatoire n’est ajouté à `IAIService`. Un service personnalisé doit implémenter `IAIRunService` pour démarrer un run RAG. Les services non compatibles sont refusés avant le début de l’indexation RAG.
- RAG conserve sa dépendance envers Abstractions ; les fournisseurs distribués séparément conservent leurs redéfinitions publiques de completion et leurs points d’extension accessibles. Ce changement ne déprécie aucune API de stockage vectoriel, de chargement documentaire ou d’administration de serveur.

Pour configurer le raisonnement, la recherche Web ou documentaire avant de démarrer le Run et afficher ses sources, consultez [Raisonnement et réponses avec sources](reasoning-and-search.md).

## Compatibilité et prochaine version majeure

| API | Statut actuel |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Publique et prise en charge, y compris les variantes d’interface, de fournisseur et RAG. |
| `StartRunAsync` / `AIRun` | API commune d’exécution et de pilotage. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Avertissements Obsolete ; comportement existant conservé pour la compatibilité. |
| `service.StreamAsync` et `StreamAsync` RAG recevant une entrée | Toujours appelables dans cette version mineure ; retrait public prévu à la prochaine version majeure. |
| `run.StreamAsync()` | Observation de la sortie d’une tâche existante, sans nouvelle entrée de requête. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | API de streaming typé conservée ; son `Stream()` de sortie seule n’est pas une ancienne méthode de requête du service. |

La prochaine transition majeure modifie les points d’entrée publics du streaming tout en conservant l’exécution et les points d’extension nécessaires aux fournisseurs. Rendre une méthode publique privée ou protégée rompt toujours la compatibilité source et binaire, même si son corps est conservé. Les assistants de chaînage de messages, d’appels ponctuels, de résumé, de reformulation de requêtes et de reclassement ne sont pas dépréciés au seul motif qu’ils utilisent les méthodes d’exécution existantes.
