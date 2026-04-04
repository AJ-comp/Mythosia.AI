# AIRequestProfile

## C'est quoi ?

`AIRequestProfile` vous permet de remplacer les paramètres de génération — température, tokens maximum, mode sans état, appel de fonctions — **pour une seule requête**. Les paramètres globaux du service restent intacts.

## Le problème résolu

Imaginez un chatbot configuré pour des conversations créatives :

```csharp
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.8f)
    .WithMaxTokens(2048)
    .WithSystemMessage("Tu es un assistant d'écriture créative.");
```

Votre pipeline RAG doit maintenant réécrire la requête de l'utilisateur avec une basse température et sans historique. **Sans** `AIRequestProfile`, il faudrait faire :

```csharp
// ❌ Sans AIRequestProfile — gestion manuelle de l'état
var savedTemp = service.Temperature;
var savedMax = service.MaxTokens;
var savedStateless = service.StatelessMode;

service.Temperature = 0.1f;
service.MaxTokens = 256;
service.StatelessMode = true;

var rewritten = await service.GetCompletionAsync("Réécris cette requête : ...");

// Tout restaurer — facile à oublier, pas thread-safe
service.Temperature = savedTemp;
service.MaxTokens = savedMax;
service.StatelessMode = savedStateless;
```

C'est verbeux, source d'erreurs et **casse dans les scénarios multi-threadés** (ex. un serveur web avec des utilisateurs simultanés). Si une exception est levée avant la restauration, le service se retrouve dans un état corrompu.

**Avec** `AIRequestProfile`, c'est une seule ligne :

```csharp
// ✅ Avec AIRequestProfile — propre et sûr
var rewritten = await service.GetCompletionAsync("Réécris cette requête : ...",
    new AIRequestProfile { Temperature = 0.1f, MaxTokens = 256, Stateless = true });
```

Les paramètres globaux du service ne sont jamais touchés. Aucun nettoyage nécessaire. Thread-safe.

## Propriétés disponibles

```csharp
var profile = new AIRequestProfile
{
    Temperature = 0.1f,       // Remplacer la température
    MaxTokens = 256,          // Remplacer les tokens de sortie maximum
    Stateless = true,         // Ne pas ajouter cet échange à l'historique
    DisableFunctions = true,  // Ignorer l'appel de fonctions pour cette requête
    DisableReasoning = true   // Ignorer le raisonnement pour cette requête
};

var response = await service.GetCompletionAsync("Votre prompt", profile);
```

Toutes les propriétés sont optionnelles — ne définissez que ce que vous souhaitez remplacer. Le reste utilise la valeur actuelle du service.

## Profils prédéfinis

Pour les scénarios courants, des profils intégrés sont fournis pour éviter la configuration manuelle :

```csharp
// Réécriture de requête : basse température, petit budget de tokens, sans état
var rewritten = await service.GetCompletionAsync(query, RequestProfiles.QueryRewrite);

// Résumé : température légèrement plus élevée, tokens modérés
var summary = await service.GetCompletionAsync(text, RequestProfiles.Summarization);
```

## Exemples concrets

### Réécriture interne de requête dans un pipeline RAG

```csharp
// Service principal configuré pour la conversation utilisateur
var service = new OpenAIService(apiKey, http)
    .WithTemperature(0.7f)
    .WithMaxTokens(4096);

// Réécrire la requête avec des paramètres différents — le service reste inchangé
var betterQuery = await service.GetCompletionAsync(
    $"Optimise pour la recherche : {userQuery}",
    RequestProfiles.QueryRewrite);

// Continuer la conversation normale — toujours Temperature 0.7, MaxTokens 4096
var answer = await service.GetCompletionAsync(userQuery);
```

### Désactiver les fonctions pour une étape spécifique

```csharp
// Le service a des fonctions enregistrées
service.WithFunction("search_web", "Rechercher sur le web", ...);

// Pour cet appel uniquement, ignorer les fonctions — répondre directement
var directAnswer = await service.GetCompletionAsync(
    "Combien font 2 + 2 ?",
    new AIRequestProfile { DisableFunctions = true });
```

## Combiner avec AIRequestContext

Les deux peuvent être passés ensemble pour un contrôle maximum sur une requête :

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: RequestProfiles.QueryRewrite,
    context: new AIRequestContext { SystemMessageSuffix = "\nSois concis." }
);
```

Consultez [AIRequestContext](request-contexts.md) pour les détails sur l'injection de contenu dans les requêtes.
