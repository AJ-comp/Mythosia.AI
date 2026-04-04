# Sortie structurée

## Pourquoi utiliser la sortie structurée ?

Par défaut, les LLM retournent du texte libre. Si votre application doit **traiter la réponse par programme** — la stocker en base de données, la transmettre à une autre API ou l'afficher dans une UI typée — il faut parser ce texte manuellement. Cela mène à des regex ou des `string.Contains` fragiles qui cassent dès que le modèle change de formulation.

La sortie structurée résout ce problème en demandant au modèle de retourner du JSON conforme au schéma d'un type C#. Mythosia.AI gère automatiquement la génération du schéma, l'injection dans le prompt et la désérialisation — y compris la **réparation automatique du JSON** pour les petites erreurs de formatage que le modèle peut produire.

### Quand l'utiliser

- Extraire des entités, des classifications ou des données structurées depuis du texte brut
- Construire des réponses API typées depuis du contenu généré par IA
- Alimenter des pipelines aval qui attendent des structures de données précises
- Tout scénario où vous avez besoin d'une sortie **fiable et lisible par machine**

## Le problème résolu

Supposons que vous deviez extraire des données météo depuis la réponse du modèle. **Sans** sortie structurée :

```csharp
// ❌ Sans sortie structurée — parsing manuel fragile
var text = await service.GetCompletionAsync("Quel temps fait-il à Paris ?");
// text = "Il fait beau à Paris avec une température de 22°C."

// Il faut parser ça soi-même...
var city = "Paris"; // en dur ? regex ?
var tempMatch = Regex.Match(text, @"(\d+)°C");
int temp = tempMatch.Success ? int.Parse(tempMatch.Groups[1].Value) : 0;
// Et si le modèle dit "vingt-deux degrés" au lieu de "22°C" ? 💥
```

Ça casse dès que le modèle change de formulation. **Avec** la sortie structurée :

```csharp
// ✅ Avec sortie structurée — typé, automatique
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Quel temps fait-il à Paris ?");

Console.WriteLine(result.City);         // Paris
Console.WriteLine(result.Condition);    // Ensoleillé
Console.WriteLine(result.TemperatureC); // 22
```

Le modèle reçoit l'instruction de retourner du JSON conforme à votre type C#. Mythosia.AI le désérialise automatiquement. Si le modèle produit un JSON légèrement malformé (virgule manquante, texte résiduel), la **réparation automatique** intégrée le corrige avant la désérialisation — sans gestion d'erreur manuelle.

## Usage de base

Passez un paramètre de type à `GetCompletionAsync` :

```csharp
public record WeatherResponse(string City, string Condition, int TemperatureC);

var result = await service.GetCompletionAsync<WeatherResponse>(
    "Quel temps fait-il à Paris ?");

Console.WriteLine(result.City);        // Paris
Console.WriteLine(result.Condition);   // Ensoleillé
Console.WriteLine(result.TemperatureC); // 22
```

## Collections

Les types collection fonctionnent directement — pas besoin de DTO enveloppant :

```csharp
public record Entity(string Name, string Type);

var entities = await service.GetCompletionAsync<List<Entity>>(
    "Extrais toutes les personnes et organisations de ce texte : ...");

foreach (var e in entities)
    Console.WriteLine($"{e.Type}: {e.Name}");
```

## Streaming + sortie structurée

Recevez le texte en temps réel tout en obtenant l'objet désérialisé final :

```csharp
var run = service.BeginStream("Génère un résumé produit").As<ProductDto>();

// Sortie en temps réel
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Résultat parsé final
ProductDto product = await run.Result;
```

## Politique de sortie structurée

Contrôlez le niveau d'exigence du modèle pour la sortie structurée :

```csharp
using Mythosia.AI.Models;

// Par défaut : demander au modèle de retourner du JSON conforme au schéma
service.StructuredOutputPolicy = StructuredOutputPolicy.Strict;

// Souple : donner plus de liberté au modèle, s'appuyer sur la réparation automatique
service.StructuredOutputPolicy = StructuredOutputPolicy.Lenient;
```
