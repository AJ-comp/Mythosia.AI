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

[AiFunction("search_products", "Recherche dans le catalogue produits")]
public static string SearchProducts(
    [AiParameter("Requête de recherche", required: true)] string query,
    [AiParameter("Nombre maximum de résultats")] int limit = 5)
{
    // ... votre implémentation
    return JsonSerializer.Serialize(results);
}
```

Puis enregistrez-la :

```csharp
service.AddFunction(SearchProducts);
```

## Politique d'appel de fonctions

Contrôlez quand le modèle est autorisé à appeler des fonctions :

```csharp
using Mythosia.AI.Models.Functions;

// Laisser le modèle décider (par défaut)
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// Forcer le modèle à toujours appeler une fonction
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// Désactiver l'appel de fonctions
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
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

var fn = FunctionBuilder
    .Create("get_stock_price", "Retourne le cours actuel d'une action")
    .AddParameter("ticker", "Symbole boursier", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
