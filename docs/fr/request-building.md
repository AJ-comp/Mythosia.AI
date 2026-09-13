# Garder les paramètres de chaque requête indépendants

Un résumé peut demander une température basse, et un brouillon créatif une valeur plus élevée. Préparer le brouillon ne doit pas modifier le résumé déjà préparé. Utilisez `CreateRequest` pour configurer chaque appel ou décliner une requête commune en plusieurs variantes.

Pour obtenir réponse, jetons et sources ensemble, `await run.Result` renvoie un instantané `AIRunResult`. La chaîne est dans `result.Text`, sans lecture du flux. Ce changement appartient à Mythosia.AI 8.0.0 ; les types de retour de `GetCompletionAsync` et `StructuredStreamRun<T>.Result` restent identiques. [Résultat Run et migration](execution-api-transition.md#run-result).

Pour un résultat final et un bouton Arrêter, passez `cancellationToken` à `GetCompletionAsync`. Utilisez Run pour les événements de progression ou les instructions supplémentaires prises en charge. Voir [l’annulation](completions.md#completion-cancellation).

> Les exemples `CreateRequest` nécessitent Mythosia.AI 8.0.0 / Abstractions 4.0.0. Le builder n’existe pas dans l’ancienne version 7.1 qui a introduit Run et les options communes. Les anciens packages peuvent conserver les surcharges du service.

## Before : le service est partagé

Le `WithTemperature` existant du service modifie le service et renvoie la même instance. Les deux variables ci-dessous la référencent : la dernière valeur s’applique aux deux. Ces méthodes restent disponibles pour régler les valeurs par défaut du service.

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("Explique ce document."); // 0.8
```

## After : des variantes indépendantes

`CreateRequest` capture les valeurs par défaut. Chaque `With...` du builder renvoie un nouveau builder sans modifier l’original. L’exécution utilise les paramètres capturés sans écraser temporairement ceux du service.

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("Explique ce document.");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// Utilise 0.2 ; creative et les valeurs par défaut restent inchangés.
```

Conservez le builder renvoyé. Appeler `basis.WithTemperature(0.2f);` sans utiliser le résultat ne modifie pas `basis`.

Le builder valide les valeurs sans les corriger silencieusement : température 0–2, TopP 0–1, pénalités −2–2, sans NaN ni infini. Tokens, tours, concurrence et délai spécifié doivent être positifs. Une valeur invalide lève `ArgumentException` / `ArgumentOutOfRangeException`. L’ancien helper de température du service conserve son bornage.

## Rôle des objets

`AIService` gère la connexion au fournisseur, les valeurs par défaut et la conversation. Le type public `Mythosia.AI.Builders.AIRequestBuilder` propose l’API fluent. Le type interne `AIRequest` transmet les données et paramètres fixés à l’exécution. Aucun appel à `Build()` n’est requis : `GetCompletionAsync()` renvoie `Task<string>` et `StartRunAsync()` renvoie `Task<AIRun>`, pas un `AIRequest` comme réponse.

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## Démarrer un Run avec les mêmes paramètres

Utilisez `GetCompletionAsync()` pour la réponse terminée, ou `StartRunAsync()` pour la progression et les instructions en cours d’exécution sur un modèle compatible. Le prompt est passé à `CreateRequest`, pas de nouveau à la méthode d’exécution. `run.StreamAsync()` observe ce Run ; les conditions de prise en charge de `run.SteerAsync(...)` restent inchangées.

```csharp
await using var run = await service
    .CreateRequest("Explique ce document.")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Les outils locaux peuvent renvoyer des objets via `Task<T>` / `ValueTask<T>` et recevoir un `CancellationToken` injecté. `run.Cancel()` ou le jeton de démarrage atteint les outils coopératifs ; arrêter seulement le lecteur du flux ne suffit pas. Les exceptions sont des échecs. L’annulation ignore les appels en attente, et le nettoyage attend les outils démarrés qui ignorent le jeton. Voir [résultats, erreurs et annulation](function-calling.md#tool-execution-contract).

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## Réutiliser les profils et le contexte

`WithProfile` copie un `AIRequestProfile` et `WithContext` copie un `AIRequestContext`. Modifier ensuite les objets originaux n’altère pas la requête préparée. Le builder configure l’échantillonnage, les instructions système, le mode sans état, la politique des fonctions et les options de raisonnement et de recherche prises en charge. Les validations du fournisseur s’appliquent toujours.

`WithFunctions(params FunctionDefinition[])` ajoute des définitions copiées. Les extensions `WithFunctions(toolInstance)` et `WithStaticFunctions<T>()` de `Mythosia.AI.Extensions` acceptent les fonctions existantes avec attributs. Enregistrez avant `CreateRequest` pour les valeurs par défaut, après pour la requête. `CreateRequest` capture et consomme les anciennes options en attente pour le prochain appel ; réutilisez le builder pour les conserver.

```csharp
var request = service
    .CreateRequest("Reformule cette question pour la recherche.")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\nConserve le sens original."
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## Ce qui est copié et ce qui reste partagé

Les paramètres communs et ceux du fournisseur sont capturés à `CreateRequest`. Les modifications ultérieures des valeurs par défaut ne changent pas la requête préparée. Les contenus intégrés des messages, collections d’options prises en charge, profils, contextes et politiques sont copiés. Les handlers de fonctions, callbacks de contexte dynamique et contenus personnalisés conservent leurs références. Gardez les contenus personnalisés immuables ; les delegates peuvent lire un état externe. Le contexte dynamique est évalué lors de l’exécution.

Après la capture, vous pouvez libérer le `JsonDocument` d’origine ou modifier les valeurs `JsonNode` d’origine sans changer le JSON conservé dans les métadonnées de la requête ou les arguments d’appels de fonctions ; chaque exécution reçoit sa propre copie. Une chaîne `Items` cyclique ou dépassant 64 niveaux dans un schéma d’outil provoque une `ArgumentException` lors de la capture (`CreateRequest` ou `WithFunctions`) : le schéma invalide échoue avant l’exécution, sans épuiser la pile du processus.

La copie conserve aussi les dimensions et indices de départ des tableaux, ainsi que les règles de comparaison des clés des conteneurs standards `Dictionary<,>`, `SortedDictionary<,>` et `SortedList<,>`. Une recherche de clé insensible à la casse le reste donc dans la requête. La valeur vide `default(JsonElement)` (`Undefined`) est préservée. Les objets de métadonnées personnalisés inconnus conservent leurs références ; leur propriétaire doit éviter de les modifier ou coordonner les accès.

Les valeurs standard `ReadOnlyCollection<T>` et `ReadOnlyDictionary<TKey, TValue>` gardent leur type dans les tableaux et dictionnaires typés. Les collections sous-jacentes prises en charge sont copiées en préservant les vues en lecture seule, les références partagées et les cycles. `Hashtable` et `SortedList` non générique conservent aussi leurs règles de comparaison des clés.

Un builder n’est pas une conversation distincte. Il utilise la conversation active du service à l’exécution et ne fige pas l’historique lors de sa création. Les appels avec état mettent toujours à jour l’historique partagé. `WithStatelessMode()` évite de lire et d’accumuler cet historique. La limite d’un Run actif par service reste applicable : l’indépendance des paramètres ne garantit pas l’exécution parallèle sur le même service. Utilisez des services distincts pour des conversations concurrentes indépendantes.

## Appels existants et extensions

`GetCompletionAsync` et les points d’entrée existants restent disponibles. `BeginMessage()` / `MessageChain` conservent leur construction mutable des messages, mais exécutent via le nouveau chemin de requête. Préférez `CreateRequest` pour créer des variantes. Cette API appartient à `AIService` et à ses implémentations ; aucun membre obligatoire n’est ajouté à `IAIService`. Les appelants utilisant seulement l’abstraction ou un wrapper RAG gardent leurs API de profil, contexte et exécution.

[Construire les options du modèle avec des définitions partagées](model-capabilities.md).
