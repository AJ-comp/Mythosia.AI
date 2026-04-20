# AIRequestContext

## C'est quoi ?

`AIRequestContext` vous permet de modifier **ce que le modèle voit pour une seule requête** — injecter des instructions supplémentaires, ajouter des documents de référence, ou remplacer entièrement le message de l'utilisateur — sans modifier de façon permanente le message système ou l'historique du service.

## Le problème résolu

Prenons un pipeline RAG qui récupère des documents pertinents et doit les inclure dans le prompt. **Sans** `AIRequestContext`, il faudrait modifier le message système directement :

```csharp
// ❌ Sans AIRequestContext — pollution du message système
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\nUtilise le contexte suivant pour répondre :\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// Restaurer — mais ce contexte est maintenant aussi dans l'historique
service.SystemMessage = originalSystem;
```

Problèmes avec cette approche :

- Le contexte récupéré **fuite dans l'historique de conversation** — les requêtes suivantes le voient encore
- Restaurer le message système n'annule pas la pollution de l'historique
- Dans une application web multi-utilisateurs, muter un état partagé crée des conditions de course

**Avec** `AIRequestContext`, l'injection est limitée à exactement une requête :

```csharp
// ✅ Avec AIRequestContext — propre, limité, sans effets de bord
var answer = await service.GetCompletionAsync(userQuestion,
    new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nUtilise le contexte suivant pour répondre :\n{retrievedDocs}"
    });
```

Le message système n'est modifié que pour cet appel. La requête suivante voit le message système original. Aucun nettoyage nécessaire.

## Propriétés disponibles

### SystemMessagePrefix

Préfixe du texte au message système pour cette requête uniquement :

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "La date d'aujourd'hui est 2026-03-31.\n"
};

var response = await service.GetCompletionAsync("Quel jour sommes-nous ?", context);
```

**Quand l'utiliser :** Injecter des métadonnées dynamiques (date, fuseau horaire, info de session) qui changent par requête.

### SystemMessageSuffix

Ajoute du texte à la fin du message système pour cette requête uniquement :

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nRéponds toujours en français."
};

var response = await service.GetCompletionAsync("Bonjour !", context);
```

**Quand l'utiliser :** Ajouter des instructions comportementales par requête, du contexte RAG ou des préférences de langue.

### AdditionalMessages

Insère des messages supplémentaires dans la conversation pour cette requête uniquement — utile pour injecter des documents de référence ou des exemples few-shot :

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("Document de référence : La politique de remboursement autorise les retours dans les 30 jours.").Build()
    }
};

var response = await service.GetCompletionAsync("Suis-je éligible à un remboursement ?", context);
```

**Quand l'utiliser :** Fournir du matériel de référence, des exemples few-shot ou du contexte auxiliaire qui ne doit pas persister dans l'historique.

### RequestMessageOverride

Remplace complètement le message de l'utilisateur pour cette requête. Le prompt original est ignoré :

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"En te basant sur le contexte suivant, réponds à la question.\n\nContexte : {docs}\n\nQuestion : {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context);
```

**Quand l'utiliser :** Quand une couche middleware (RAG, réécriture de requête) doit reformuler entièrement le prompt avant de l'envoyer au modèle, tout en conservant la saisie originale dans l'historique.

> **💡 À noter :** Lorsque vous utilisez `.WithRag()`, le pipeline RAG exploite cette propriété automatiquement. Pour comprendre le mécanisme complet, consultez [Personnalisation du pipeline — Fonctionnement interne](rag-pipeline.md#fonctionnement-interne).

## Comparaison avant / après

### Scénario : RAG avec injection de date et contexte récupéré

**Sans AIRequestContext :**

```csharp
// ❌ Désordonné, avec état, source d'erreurs
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nAujourd'hui : {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nContexte :\n{retrievedChunks}";

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // supprimer l'exemple few-shot
```

**Avec AIRequestContext :**

```csharp
// ✅ Propre, sans état, sans effets de bord
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"Aujourd'hui : {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nContexte :\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
        }
    });
```

## Combiner avec AIRequestProfile

Les deux peuvent être passés ensemble pour un contrôle maximum sur une requête :

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nContexte :\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User("Exemple : ...").Build()
        }
    }
);
```

Consultez [AIRequestProfile](request-profiles.md) pour les détails sur le remplacement des paramètres de génération.

## Injection automatique avec `SystemMessageProvider`

### Le problème qu'il résout

Une appli de chat typique a plusieurs points d'entrée LLM qui ont tous besoin de la même baseline — date du jour, dossier actif, infos de session. **Sans** `SystemMessageProvider`, chaque point d'appel doit penser à construire et passer ce contexte :

```csharp
// ❌ Sans SystemMessageProvider — chaque point d'entrée doit se souvenir d'injecter
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Réponse principale du chat
var answer = await service.GetCompletionAsync(userMessage,
    new AIRequestContext { SystemMessageSuffix = today });

// 2. Générateur de titres (ajouté plus tard)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 3. Résumeur (ajouté encore plus tard)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    new AIRequestContext { SystemMessageSuffix = today });

// 4. Appel d'agent — facile à oublier ! Le compilateur ne vous avertit pas
var agentResult = await service.RunAgentAsync(goal);  // ← date manquante, bug silencieux
```

Problèmes de cette approche :

- Le même snippet de construction de contexte est **dupliqué** à chaque point d'appel
- Les nouveaux points d'entrée (le `RunAgentAsync` ci-dessus) sont **faciles à manquer** — pas de vérification à la compilation
- Chaque nouvelle fonctionnalité qui ajoute un appel LLM doit se souvenir de la convention
- Les tests doivent reproduire le setup du contexte à chaque point d'appel

Avec `SystemMessageProvider`, vous enregistrez la baseline **une seule fois** et chaque appel sortant la récupère automatiquement :

```csharp
// ✅ Avec SystemMessageProvider — enregistrer une fois, appliqué partout
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// Tous reçoivent automatiquement la baseline — pas de boilerplate par appel
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← reçoit aussi la baseline

// Les points d'entrée streaming aussi — même baseline, pas de boilerplate par appel
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### Comment ça marche

Enregistrez le callback une seule fois via le helper fluent `WithSystemMessageProvider`. Chaque appel sortant (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) l'invoque automatiquement pour construire un contexte de base :

```csharp
// Typiquement à la construction du service / configuration DI
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### Surcharge async pour les providers basés sur IO

Quand le contexte de base provient d'une base de données, d'un cache ou d'un appel HTTP, utilisez la surcharge async pour que le provider n'ait pas à bloquer sur `.Result` / `.GetAwaiter().GetResult()`. La résolution de surcharge choisit la bonne par l'arity du lambda — aucun argument pour sync, un `CancellationToken` pour async :

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

Les chemins non-streaming (`GetCompletionAsync`, `RunAgentAsync`) ne prennent pas en charge l'annulation par conception — leurs signatures n'acceptent pas de `CancellationToken`, et `CancellationToken.None` est toujours passé au provider. Si votre provider a besoin d'annulation (p. ex. une requête DB longue), utilisez les chemins de streaming (`StreamAsync`, `RunAgentStreamAsync`), qui propagent le token de l'appelant jusqu'au callback du provider.

### Fusion avec un contexte per-call explicite

Quand un appel a un provider enregistré **et** passe aussi un `AIRequestContext` explicite, les deux fusionnent champ par champ :

| Champ | Règle de fusion |
|---|---|
| `SystemMessagePrefix` | explicite gagne si non-null, sinon provider |
| `SystemMessageSuffix` | explicite gagne si non-null, sinon provider |
| `RequestMessageOverride` | explicite gagne si non-null, sinon provider |
| `AdditionalMessages` | concaténés (provider d'abord, puis explicite) |

Justification : le cas courant est « le provider fournit une base, un appel spécifique veut remplacer un champ scalaire ou ajouter des messages supplémentaires » — l'override au niveau du champ garde la sémantique prévisible sans concaténation surprenante.

### Invocation par appel

Le provider est invoqué **une fois par requête**, afin que les valeurs de retour puissent refléter l'état à l'instant t (timestamp, session, etc.). Retourner `null` est un no-op — identique à laisser `SystemMessageProvider` non défini pour cet appel.

> Disponible dans Mythosia.AI v6.3.0+.
