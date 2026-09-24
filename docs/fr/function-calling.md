# Appel de fonctions

> GPT-6 Sol/Luna: Nécessite Mythosia.AI 8.1.0 / Abstractions 4.1.0. [choix du modèle et prérequis](providers.md#gpt-6-sol-luna)

Pour un résultat final et un bouton Arrêter, passez `cancellationToken` à `GetCompletionAsync`. Utilisez Run pour les événements de progression ou les instructions supplémentaires prises en charge. Voir [l’annulation](completions.md#completion-cancellation).

Pour des paramètres indépendants et réutilisables, utilisez [le builder de requête](request-building.md). Appelez `CreateRequest(...)` avant `With...`. Les propriétés et méthodes fluent du service conservent leur comportement existant.

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

[Claude Fable 5.1](fable-5-1.md) ajoute le suivi de progression, les instructions limitées à un tour et le diagnostic des liens du raisonnement à partir de `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0. Mythos 5.1 nécessite une invitation. Les deux refusent la sélection forcée d’outils.

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

<a id="tool-execution-contract"></a>

## Renvoyer des objets depuis les outils asynchrones et annuler le travail

Un outil de fichier ou de base de données renvoie souvent un objet après une entrée/sortie asynchrone. Un bouton Arrêter doit aussi atteindre cette opération encore active. Le retour synchrone d’objets était déjà pris en charge ; cette mise à jour aligne les retours asynchrones et enregistre les exceptions comme des échecs.

Before : un outil asynchrone devait sérialiser lui-même son résultat. Renvoyer `Task<FileResult>` perdait la valeur et produisait seulement `"Success"`.

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "Lire un fichier texte")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After : renvoyez directement l’objet et transmettez le jeton d’annulation injecté à l’opération d’entrée/sortie. Aucun nouveau type enveloppant le résultat ni adaptateur n’est nécessaire dans l’application.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Lire un fichier texte")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

L’enregistrement `[AiFunction]` accepte les objets, `Task<T>` et `ValueTask<T>` ; les valeurs autres que des chaînes sont converties en JSON. `string`, `Task<string>` et `ValueTask<string>` restent du texte brut sans guillemets JSON supplémentaires. Les `Task` et `ValueTask` sans résultat sont également attendus. Les retours synchrones d’objets restent disponibles. Un retour null devient `"Done"` ; un `Task` / `ValueTask` terminé sans résultat devient `"Success"`.

La bibliothèque reconnaît aussi les valeurs asynchrones à l’exécution : un `Task<T>` renvoyé comme `Task` ou `object`, ou un `ValueTask<T>` renvoyé comme `object`, est attendu puis sérialisé selon les mêmes règles. Chaque `ValueTask` n’est consommé qu’une fois.

La bibliothèque fournit le paramètre `CancellationToken` et l’exclut du schéma d’arguments présenté au modèle. Enregistrez les méthodes avec `WithFunctions(...)` ou `WithStaticFunctions<T>()` sur le service ou le constructeur de requête.

Les méthodes d’outil `async void` sont refusées à l’enregistrement. Renvoyez `Task` ou `ValueTask` pour permettre d’attendre la fin, d’observer les erreurs et de terminer le nettoyage après annulation.

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("Lis report.txt et résume-le.")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`, l’annulation du jeton transmis à `StartRunAsync` ou la libération d’un run actif atteint les outils locaux qui coopèrent. La fonction doit utiliser le jeton : le code qui l’ignore ne peut pas être arrêté de force. Les appels non démarrés sont ignorés avec un résultat d’annulation ; les fonctions déjà démarrées sont attendues pour préserver les paires appel/résultat dans l’historique. Une exécution annulée reste annulée et ne lance pas de nouveau tour du modèle.

Si un callback d’annulation lève une exception lors d’un démarrage de run échoué ou de la libération d’une connexion MCP, le nettoyage de la session ou du transport est tout de même tenté. L’erreur initiale et les erreurs de nettoyage sont conservées, ensemble dans une `AggregateException` si nécessaire. Les appels asynchrones simultanés à `McpConnection.DisposeAsync()` attendent le même nettoyage. Le transport est fermé avant d’attendre la fin de la boucle de lecture, ce qui libère les lectures qui nécessitent la fermeture de la connexion.

Pour éviter qu’un appel d’outil tardif reste en attente pendant la fermeture, dès que la libération de la connexion commence, les nouvelles opérations `InitializeAsync`, `RefreshToolsAsync` et `CallToolAsync` sont rejetées avec `ObjectDisposedException`. Une réponse dont l’ID correspond mais dont le corps est mal formé est ignorée sans supprimer la requête en attente : une réponse valide ultérieure, l’annulation par l’appelant ou le nettoyage de la connexion peuvent toujours terminer l’appel. Si la lecture est déjà terminée parce que le serveur a fermé le flux ou qu’une lecture du transport a échoué, les nouvelles opérations échouent avec `McpException` au lieu d’attendre une réponse qui ne peut plus arriver ; créez une nouvelle connexion pour continuer.

Laissez les véritables échecs lever une exception. L’exécution les enregistre avec `FunctionCallResult.IsError = true` au lieu d’une chaîne `"Error: ..."` considérée comme un succès. Une chaîne volontairement renvoyée reste un résultat normal. Les résultats annulés portent `IsCancelled = true` et `IsError = true`.

Pour un enregistrement programmatique, utilisez la surcharge de `WithHandler` à deux arguments :

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("Lire un fichier texte")
    .AddParameter("path", "string", "Chemin du fichier", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

Les gestionnaires de chaînes à un argument restent pris en charge. Une définition directe peut définir `HandlerWithCancellation` avec `Func<Dictionary<string, object>, CancellationToken, Task<string>>`. Définir `Handler` ou `HandlerWithCancellation` remplace le même gestionnaire, sans enregistrer deux exécutions. Cette API de bas niveau renvoie toujours des chaînes ; la sérialisation automatique d’objets appartient à l’enregistrement des méthodes.

Il s’agit des retours et de l’annulation de fonctions .NET locales, sans dépendance envers le `AllowAsync` natif du fournisseur. Arrêter seulement le lecteur `run.StreamAsync(token)` arrête l’observation, pas le run. Consultez le [guide Run](execution-api-transition.md) et le [protocole du fournisseur](https://developers.openai.com/api/docs/guides/async-tool-calling).

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

Mythosia envoie `async: true` pour GPT-6 Astra / Sol / Luna via Responses. Avec les modèles et API non compatibles, ce champ est omis et le résultat du même gestionnaire est attendu, sans modifier `AllowAsync`. Le fournisseur doit aussi signaler l’appel réel comme asynchrone (`FunctionCall.IsAsync`) : l’autorisation ne garantit pas une exécution asynchrone.

`WithFunctionAsync` enregistre un gestionnaire .NET asynchrone et `FunctionExecutionMode.Parallel` règle l’exécution locale des gestionnaires. Aucun des deux n’active automatiquement cette autorisation. `AllowAsync` permet au modèle de continuer avant de recevoir le résultat de la fonction. `FunctionExecutionMode` continue de régler les appels ordinaires. Les tâches asynchrones autorisées peuvent se chevaucher même en mode `Sequential` et partagent une limite distincte définie par `MaxConcurrency`.

Les tâches en attente appartiennent à la requête active : `GetCompletionAsync`, l’ancien `service.StreamAsync` ou un `AIRun` démarré avec `StartRunAsync`. Chaque résultat est associé plus tard à son identifiant d’appel d’origine. La réussite de la requête ou de `run.Result` attend le traitement des résultats en attente. Aucune session publique de tâches d’arrière-plan ne subsiste indépendamment de la requête.

Avec les outils asynchrones, `GetCompletionAsync` renvoie à la fin de la requête les explications intermédiaires indépendantes et le texte final, cumulés dans l’ordre. `StreamAsync` émet le texte de chaque ronde au fur et à mesure de son arrivée. `run.StreamAsync()` transmet également le texte à son arrivée ; `(await run.Result).Text` concatène tous les événements textuels du run.

Les outils locaux peuvent renvoyer des objets via `Task<T>` / `ValueTask<T>` et recevoir un `CancellationToken` injecté. `run.Cancel()` ou le jeton de démarrage atteint les outils coopératifs ; arrêter seulement le lecteur du flux ne suffit pas. Les exceptions sont des échecs. L’annulation ignore les appels en attente, et le nettoyage attend les outils démarrés qui ignorent le jeton. Voir [résultats, erreurs et annulation](function-calling.md#tool-execution-contract).

En streaming, les gestionnaires démarrent après confirmation d’appels de fonctions complets et d’une fin valide de réponse du fournisseur. Le tour suivant du modèle peut alors avancer pendant les tâches asynchrones ; les appels incomplets ne déclenchent aucune exécution. Si aucun nouvel appel n’est renvoyé alors que des tâches restent en attente, Mythosia attend leurs résultats. Les résumés et nouvelles tentatives automatiques en cas de dépassement du contexte restent désactivés pendant les appels en attente pour préserver les appels inachevés dans l’historique.

Perplexity: [Configurer la recherche et les outils](perplexity.md).
