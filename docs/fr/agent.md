# Agent (boucle ReAct)

## Pourquoi une boucle agentique ?

Avec l'appel de fonctions classique, le modèle effectue **un seul** appel de fonction par requête, vous l'exécutez, et la conversation continue. Mais beaucoup de tâches réelles nécessitent **plusieurs étapes** que le modèle doit planifier et exécuter de manière autonome :

- « Recherche les 3 principales entreprises d'IA et compare leurs cours boursiers » — nécessite plusieurs recherches web et récupérations de cours
- « Trouve la politique applicable, vérifie le statut de la commande, puis dis-moi si j'ai droit à un remboursement » — nécessite d'enchaîner différents outils dans un ordre logique
- Le modèle peut avoir besoin de **réessayer ou d'affiner** une recherche si le premier résultat est insuffisant

Écrire cette boucle d'orchestration soi-même est fastidieux et source d'erreurs. La **boucle agentique** (pattern ReAct : Raisonner → Agir → Observer → Répéter) s'en charge automatiquement — le modèle décide quoi faire à chaque étape jusqu'à produire une réponse finale.

## Utilisation de base

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// Ou via méthodes d'extension :
service.WithMaxRounds(15).WithTimeout(60);
```

Politiques prédéfinies :

```csharp
service.WithFastPolicy();    // Timeout court, peu de cycles — tâches rapides
service.WithComplexPolicy(); // Timeout plus long, plus de cycles — recherche approfondie
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

Le contexte est propagé via `AsyncLocal`, ce qui permet à plusieurs exécutions d'agent concurrentes sur la même instance de service de ne pas interférer entre elles.

Consultez [AIRequestContext](request-contexts.md) pour la liste complète des propriétés disponibles (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Disponible à partir de Mythosia.AI v6.3.0.

## Comment ça fonctionne

À chaque étape :

1. Le LLM reçoit l'objectif + l'historique de conversation + les définitions de fonctions
2. Si le LLM appelle une fonction → l'exécuter, ajouter le résultat à l'historique
3. Si le LLM retourne une réponse textuelle → la boucle se termine, retourner cette réponse
4. Si le nombre d'étapes atteint `maxSteps` → lever `AgentMaxStepsExceededException`
