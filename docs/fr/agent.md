# Agent (boucle ReAct)

Pour obtenir réponse, jetons et sources ensemble, `await run.Result` renvoie un instantané `AIRunResult`. La chaîne est dans `result.Text`, sans lecture du flux. Ce changement appartient à Mythosia.AI 8.0.0 ; les types de retour de `GetCompletionAsync` et `StructuredStreamRun<T>.Result` restent identiques. [Résultat Run et migration](execution-api-transition.md#run-result).


Pour des paramètres indépendants et réutilisables, utilisez [le builder de requête](request-building.md). Appelez `CreateRequest(...)` avant `With...`. Les propriétés et méthodes fluent du service conservent leur comportement existant.

> Les exemples `CreateRequest` nécessitent Mythosia.AI 8.0.0 / Abstractions 4.0.0. Le builder n’existe pas dans l’ancienne version 7.1 qui a introduit Run et les options communes. Les anciens packages peuvent conserver les surcharges du service.

Rechercher une politique et vérifier une commande peut demander plusieurs appels d’outils. Le [guide Run](execution-api-transition.md) montre comment suivre ce travail, l’annuler et ajouter des instructions lorsque le modèle le permet.

## Pourquoi une boucle agentique ?

Certaines questions demandent plusieurs sources : le modèle choisit un outil, examine son résultat puis appelle éventuellement d’autres outils. La boucle commune entre modèle et outils répète ces étapes jusqu’à la réponse, avec une limite de tours pour borner l’exécution :

- « Recherche les 3 principales entreprises d'IA et compare leurs cours boursiers » — nécessite plusieurs recherches web et récupérations de cours
- « Trouve la politique applicable, vérifie le statut de la commande, puis dis-moi si j'ai droit à un remboursement » — nécessite d'enchaîner différents outils dans un ordre logique
- Le modèle peut avoir besoin de **réessayer ou d'affiner** une recherche si le premier résultat est insuffisant

`GetCompletionAsync` et `StartRunAsync` exécutent déjà la boucle commune entre modèle et outils. Les anciennes méthodes d’agent ajoutent une limite de tours par appel et une traduction spécifique des erreurs, sans planificateur ni moteur d’exécution indépendant.

## Démarrer une tâche avec outils via Run

```csharp
// Enregistrer les fonctions sur le service avant de démarrer la tâche.
await using var run = await service
    .CreateRequest("Trouve la politique, vérifie la commande et explique le résultat.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

Les outils locaux peuvent renvoyer des objets via `Task<T>` / `ValueTask<T>` et recevoir un `CancellationToken` injecté. `run.Cancel()` ou le jeton de démarrage atteint les outils coopératifs ; arrêter seulement le lecteur du flux ne suffit pas. Les exceptions sont des échecs. L’annulation ignore les appels en attente, et le nettoyage attend les outils démarrés qui ignorent le jeton. Voir [résultats, erreurs et annulation](function-calling.md#tool-execution-contract).

## Ancienne API d’agent : exemples de compatibilité

Les exemples suivants documentent `RunAgentAsync` et `RunAgentStreamAsync`, qui restent appelables avec des avertissements `[Obsolete]`. Les nouveaux appels peuvent utiliser `StartRunAsync` ; le [guide Run](execution-api-transition.md) détaille les différences de limites de tours et de traitement des erreurs.

Enregistrez les fonctions, puis appelez `RunAgentAsync` avec un objectif :

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Recherche des informations sur le web",
        ("query", "Requête de recherche", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Récupère le cours actuel d'une action",
        ("ticker", "Symbole boursier", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "Quel est le cours actuel des 3 principales entreprises d'IA ?",
    maxSteps: 10
);

Console.WriteLine(result);
```

Le modèle appelle les fonctions selon ses besoins, observe les résultats et décide de la prochaine étape — jusqu'à produire une réponse textuelle finale.

## maxSteps

`maxSteps` plafonne le nombre de cycles LLM→appel de fonction. Si l'agent n'a pas terminé dans cette limite, `AgentMaxStepsExceededException` est levée :

```csharp
try
{
    string result = await service.RunAgentAsync("Recherche et résume...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponse contient ce que le modèle a produit jusqu'à présent
    Console.WriteLine($"Arrêté prématurément : {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Contrôlez le comportement de la boucle agentique à chaque cycle :

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// RunAgentAsync utilise DefaultPolicy et le paramètre explicite maxSteps.
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "Recherche et résume...", maxSteps: 15);
```

Politiques prédéfinies :

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Timeout court, peu de cycles — tâches rapides
var fastResult = await service.RunAgentAsync(
    "Recherche et résume...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Timeout plus long, plus de cycles — recherche approfondie
var complexResult = await service.RunAgentAsync(
    "Recherche et résume...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## Contexte de requête par appel

`RunAgentAsync` et `RunAgentStreamAsync` acceptent un `AIRequestContext` optionnel permettant d'injecter un prefix/suffix dynamique dans le system message, des documents de référence, ou de remplacer le message objectif — **limité à une seule exécution d'agent**, sans modifier le system message du service ni l'historique de conversation.

```csharp
string result = await service.RunAgentAsync(
    goal: "Trouve la politique de remboursement et vérifie si la commande #1234 est éligible.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"La date d'aujourd'hui est {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nCite toujours la section de la politique utilisée."
    });
```

La variante streaming accepte le même paramètre :

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Recherche le cours des 3 principales entreprises d'IA.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Fuseau horaire utilisateur : {userTz}\n"
    }))
{
    // gérer le contenu
}
```

`AIRequestContext` se propage via `AsyncLocal`, mais cela ne rend pas sûres les modifications simultanées de l’historique et des politiques du service. Utilisez des instances distinctes pour les tâches concurrentes indépendantes.

Consultez [AIRequestContext](request-contexts.md) pour la liste complète des propriétés disponibles (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Disponible à partir de Mythosia.AI v6.3.0.

## Comment ça fonctionne

À chaque étape :

1. Le LLM reçoit l'objectif + l'historique de conversation + les définitions de fonctions
2. Si le LLM appelle une fonction → l'exécuter, ajouter le résultat à l'historique
3. Si le LLM retourne une réponse textuelle → la boucle se termine, retourner cette réponse
4. Si le nombre d'étapes atteint `maxSteps` → lever `AgentMaxStepsExceededException`
