# Appel de fonctions

## À quoi sert l'appel de fonctions ?

Les LLM ne génèrent que du texte — ils ne peuvent pas consulter la météo, interroger une base de données ou appeler une API par eux-mêmes. **Sans** appel de fonctions, il faudrait parser manuellement l'intention du modèle :

```csharp
// ❌ Sans appel de fonctions — parsing manuel de l'intention
var reply = await service.GetCompletionAsync("Quel temps fait-il à Paris ?");
// reply = "Je devrais consulter un service météo pour ça."

// Il faut deviner que l'utilisateur veut la météo, extraire "Paris", appeler l'API soi-même
if (reply.Contains("météo"))
{
    var city = ExtractCity(reply); // regex ou correspondance de mots-clés fragile
    var weather = await weatherApi.GetAsync(city);
    // Re-demander avec les données météo injectées...
}
```

C'est fragile, peu maintenable et vous oblige à anticiper chaque intention possible. **Avec** l'appel de fonctions, le modèle décide lui-même **quand** appeler votre code et **avec quels arguments** :

```csharp
// ✅ Avec appel de fonctions — le modèle gère intention + extraction
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Retourne la météo actuelle pour un lieu",
        ("location", "La ville et le pays", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("Quel temps fait-il à Paris ?");
// Le modèle appelle get_weather("Paris, France"), obtient le résultat et répond naturellement.
```

Vous définissez **ce que** votre code sait faire ; le modèle décide **quand** et **comment** l'utiliser.

## Exemple rapide

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "Retourne la météo actuelle pour un lieu",
        ("location", "La ville et le pays", required: true),
        (string location) => $"Il fait beau à {location}, 22°C"
    );

var response = await service.GetCompletionAsync("Quel temps fait-il à Paris ?");
// Le modèle appelle get_weather("Paris, France") et intègre le résultat.
```

## Définir des fonctions avec des attributs

Pour des fonctions plus complexes, utilisez les attributs `[AiFunction]` et `[AiParameter]` :

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "Recherche dans le catalogue produits")]
    public string SearchProducts(
        [AiParameter("Requête de recherche", required: true)] string query,
        [AiParameter("Nombre maximum de résultats")] int limit = 5)
    {
        // ... votre implémentation
        return JsonSerializer.Serialize(results);
    }
}
```

Puis enregistrez-la :

```csharp
service.WithFunctions(new ProductFunctions());
```

## Politique d'appel de fonctions

Contrôlez quand le modèle est autorisé à appeler des fonctions :

```csharp
using Mythosia.AI.Models.Functions;

// Laisser le modèle décider (par défaut)
service.FunctionCallMode = FunctionCallMode.Auto;

// Forcer le modèle à toujours appeler une fonction
service.ForceFunctionName = "search_products";

// Désactiver l'appel de fonctions
service.FunctionCallMode = FunctionCallMode.None;
```

## Enregistrement en masse depuis une classe

Enregistrez toutes les méthodes annotées `[AiFunction]` d'un objet en une seule fois :

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // scanne les méthodes d'instance avec [AiFunction]
```

Pour les méthodes statiques :

```csharp
service.WithStaticFunctions<MyTools>();  // scanne les méthodes statiques avec [AiFunction]
```

## Gestionnaires de fonctions asynchrones

Toutes les surcharges de `WithFunction` ont des équivalents `WithFunctionAsync` qui acceptent `Func<..., Task<string>>` :

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "Récupère des données depuis une API externe",
    ("url", "L'URL à récupérer", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

Supporte de 0 à 3 paramètres, comme les variantes synchrones.

## Désactiver temporairement les fonctions

Désactivez l'appel de fonctions pour une seule requête sans supprimer les enregistrements :

```csharp
// Méthode d'extension — retourne le résultat sans fonctions
string answer = await service.AskWithoutFunctionsAsync("Réponds directement");

// Ou basculer la propriété
service.WithoutFunctions();  // définit FunctionsDisabled = true
```

## Utiliser FunctionBuilder

Construisez des définitions de fonctions de façon programmatique :

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("Retourne le cours actuel d'une action")
    .AddParameter("ticker", "string", "Symbole boursier", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

## Appels d’outils asynchrones

Une consultation lente ne doit pas forcément suspendre toute la réponse. Pendant le chargement de la météo, par exemple, le modèle peut déjà formuler des conseils de voyage généraux qui ne dépendent pas du résultat. Les appels d’outils asynchrones permettent ce travail indépendant ; les affirmations qui nécessitent le résultat doivent toujours l’attendre.

GPT-6 Astra et les appels d’outils asynchrones sont pris en charge à partir de `Mythosia.AI` 7.1.0, avec les types partagés dans `Mythosia.AI.Abstractions` 3.1.0.

`FunctionDefinition.AllowAsync` vaut `false` par défaut. Passez-le à `true` ou appelez `FunctionBuilder.WithAsync()` uniquement si le modèle peut continuer à travailler pendant l’exécution de cette fonction. `WithAsync(false)` désactive cette autorisation. La même définition et le même gestionnaire restent réutilisables entre fournisseurs.

L’enregistrement par attribut accepte la même autorisation : `[AiFunction("lookup", "Consulter les données", AllowAsync = true)]`.

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("Renvoie un exemple de météo pour Séoul")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "Consulte l’exemple de météo pour Séoul. En attendant, indique trois indispensables à emporter en voyage.");
```

Mythosia envoie `async: true` pour GPT-6 Astra via Responses. Avec les modèles et API non compatibles, ce champ est omis et le résultat du même gestionnaire est attendu, sans modifier `AllowAsync`. Le fournisseur doit aussi signaler l’appel réel comme asynchrone (`FunctionCall.IsAsync`) : l’autorisation ne garantit pas une exécution asynchrone.

`WithFunctionAsync` enregistre un gestionnaire .NET asynchrone et `FunctionExecutionMode.Parallel` règle l’exécution locale des gestionnaires. Aucun des deux n’active automatiquement cette autorisation. `AllowAsync` permet au modèle de continuer avant de recevoir le résultat de la fonction. `FunctionExecutionMode` continue de régler les appels ordinaires. Les tâches asynchrones autorisées peuvent se chevaucher même en mode `Sequential` et partagent une limite distincte définie par `MaxConcurrency`.

Les tâches en attente appartiennent à la requête active : `GetCompletionAsync`, l’ancien `service.StreamAsync` ou un `AIRun` démarré avec `StartRunAsync`. Chaque résultat est associé plus tard à son identifiant d’appel d’origine. La réussite de la requête ou de `run.Result` attend le traitement des résultats en attente. Aucune session publique de tâches d’arrière-plan ne subsiste indépendamment de la requête.

Avec les outils asynchrones, `GetCompletionAsync` renvoie à la fin de la requête les explications intermédiaires indépendantes et le texte final, cumulés dans l’ordre. `StreamAsync` émet le texte de chaque ronde au fur et à mesure de son arrivée. `run.StreamAsync()` transmet également le texte à son arrivée ; `run.Result` concatène tous les événements textuels du run.

Les gestionnaires ne reçoivent pas de jeton d’annulation. Après une annulation, une expiration ou une erreur, le nettoyage attend donc les gestionnaires déjà démarrés. Arrêter prématurément l’ancien flux de service termine son exécution ; arrêter `run.StreamAsync()` termine seulement l’observation. Pour annuler le run, appelez `run.Cancel()` ou libérez-le. L’intégration concerne les gestionnaires enregistrés ; consultez le [protocole de l’API](https://developers.openai.com/api/docs/guides/async-tool-calling) et le [guide Run](execution-api-transition.md).

En streaming, les gestionnaires démarrent après confirmation d’appels de fonctions complets et d’une fin valide de réponse du fournisseur. Le tour suivant du modèle peut alors avancer pendant les tâches asynchrones ; les appels incomplets ne déclenchent aucune exécution. Si aucun nouvel appel n’est renvoyé alors que des tâches restent en attente, Mythosia attend leurs résultats. Les résumés et nouvelles tentatives automatiques en cas de dépassement du contexte restent désactivés pendant les appels en attente pour préserver les appels inachevés dans l’historique.
