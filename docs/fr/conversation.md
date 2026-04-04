# Gestion des conversations

## Comment fonctionne l'historique de conversation

Chaque appel à `GetCompletionAsync` ou `StreamAsync` ajoute des messages à la liste interne du service. Le modèle a ainsi le contexte de tous les tours précédents.

```csharp
await service.GetCompletionAsync("Ma couleur préférée est le bleu.");
var reply = await service.GetCompletionAsync("Quelle est ma couleur préférée ?");
// → "Ta couleur préférée est le bleu."
```

Pour repartir à zéro :

```csharp
service.ClearMessages();
```

## Politique de résumé

### Pourquoi un résumé automatique ?

Chaque message de l'historique est envoyé au modèle à chaque requête. Au fil du temps, deux problèmes se posent :

1. **Coût** — un historique plus long signifie plus de tokens d'entrée facturés par requête
2. **Dépassement de contexte** — une fois que l'historique dépasse la fenêtre de contexte du modèle (ex. 128K tokens pour GPT-4o), les requêtes échouent complètement

Tronquer manuellement les anciens messages fait perdre du contexte potentiellement utile. **`SummaryConversationPolicy`** résout cela en condensant automatiquement les messages anciens en un résumé compact, tout en conservant les messages récents mot pour mot — le modèle garde l'essentiel de la conversation sans le coût en tokens.

### Déclenchement par nombre de messages

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // résumer quand l'historique dépasse 20 messages
    keepRecentCount: 5  // conserver les 5 messages les plus récents mot pour mot
);
```

### Déclenchement par nombre de tokens

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // résumer quand l'utilisation dépasse 3000 tokens
    keepRecentTokens: 1000  // conserver les messages récents jusqu'à 1000 tokens
);
```

### Déclenchement par les deux (condition OU)

Déclencher le résumé quand **soit** la limite de tokens **soit** le nombre de messages est dépassé :

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // optionnel, par défaut triggerTokens / 3
    keepRecentCount: 7       // optionnel, par défaut triggerCount / 4
);
```

Une fois configuré, le résumé se déclenche automatiquement lors de `GetCompletionAsync`. Aucun autre changement n'est nécessaire.

### Fonctionnement interne

1. Avant chaque complétion, la politique vérifie si la conversation dépasse le seuil configuré.
2. Si déclenchée, les messages anciens sont résumés en un texte concis via un appel LLM sans état.
3. Le résumé est injecté comme préfixe du message système — le modèle le perçoit comme contexte antérieur.
4. Les messages récents (contrôlés par `KeepRecentCount` ou `KeepRecentTokens`) sont conservés mot pour mot.

Avec les déclencheurs basés sur les tokens, la politique utilise automatiquement le **nombre réel de tokens d'entrée** rapporté par l'API (depuis la dernière réponse en streaming) plutôt qu'une estimation locale.

### Streaming

Le résumé ne se déclenche pas automatiquement pendant `StreamAsync`. Appelez-le explicitement avant :

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Continuons notre conversation..."))
    Console.Write(chunk.Content);
```

## Sauvegarder et restaurer le résumé

Persistez le résumé entre les sessions pour que le modèle retrouve son contexte après un redémarrage :

```csharp
// Sauvegarder
string saved = service.ConversationPolicy.CurrentSummary;
// → stocker en base de données, fichier, etc.

// Restaurer dans une nouvelle session
service.ConversationPolicy.LoadSummary(saved);
```
