# Paramètres de génération

> Grok 4.7 est un ajout non publié ; consultez [le choix du modèle, le raisonnement et la vitesse](providers.md#grok-47).

Pour des paramètres indépendants et réutilisables, utilisez [le builder de requête](request-building.md). Appelez `CreateRequest(...)` avant `With...`. Les propriétés et méthodes fluent du service conservent leur comportement existant.

## Valeurs par défaut du service et méthodes de compatibilité

Toutes les instances de service IA exposent ces propriétés :

```csharp
service.Temperature = 0.7f;        // Aléatoire [0, 2]. Plus bas = plus déterministe
service.TopP = 1.0f;               // Seuil d'échantillonnage nucleus
service.MaxTokens = 1024;          // Tokens de sortie maximum
service.FrequencyPenalty = 0.0f;   // Pénalise les tokens répétés
service.PresencePenalty = 0.0f;    // Pénalise les tokens déjà présents
```

GPT-6 Astra ne prend pas en charge `temperature` ni `top_p` ; Mythosia les omet même si les propriétés communes ou un profil de requête les définissent. La sortie maximale est de 128 000 tokens. Voir la [configuration de GPT-6](providers.md).

GPT-6 Sol/Luna n’envoient `temperature` et `top_p` qu’avec `ReasoningLevel.None` ; sinon les deux sont omis. Astra ne prend pas en charge `None`. Voir [choix et réglages du modèle](providers.md#gpt-6-sol-luna).


Choisissez l’effort de raisonnement et les sources d’une tâche avec les [options communes de raisonnement et de recherche](reasoning-and-search.md) ; les paramètres existants du fournisseur restent disponibles.

## Méthodes d'extension fluentes

Ces méthodes retournent `this` pour permettre le chaînage :

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("Tu es un assistant serviable.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Méthode | Description |
|--------|-------------|
| `.WithSystemMessage(string)` | Définir le prompt système |
| `.WithTemperature(float)` | Limité à [0, 2] |
| `.WithMaxTokens(uint)` | Tokens de sortie maximum |
| `.WithStatelessMode(bool)` | Désactiver l'accumulation de l'historique |

## Mode sans état

Lorsqu'il est activé, chaque requête est indépendante — aucun historique de conversation n'est envoyé ni stocké :

```csharp
service.StatelessMode = true;

// Équivalent :
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Pratique pour des requêtes ponctuelles sans surcoût d'historique.

## Requêtes ponctuelles

Ces méthodes d'extension exécutent une requête unique sans affecter ni utiliser l'historique de conversation :

```csharp
// Prompt textuel
string response = await service.AskOnceAsync("Combien font 2+2 ?");

// Message (multimodal)
string response = await service.AskOnceAsync(message);

// Image depuis un chemin de fichier
string response = await service.AskOnceWithImageAsync("Décris ça", "photo.jpg");
```

## Changer de modèle

Changez de modèle en cours de session en conservant l'historique de conversation :

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// Ou via méthode d'extension — efface l'historique et repart à zéro :
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Gérer plusieurs conversations

Une seule instance de service peut gérer plusieurs fils de conversation indépendants :

```csharp
// Démarrer un nouveau bloc de conversation
service.AddNewChat();
var chat1 = service.ActivateChat;

// Basculer vers un autre bloc
service.SetActivateChat(chat2Id);

// Accéder à tous les blocs
var allChats = service.ChatRequests;
```

## Inspecter l'état de la conversation

Récupérez la dernière réponse de l'assistant ou un résumé rapide de la session en cours :

```csharp
// Obtenir le dernier message de l'assistant (ou null s'il n'y en a pas)
string? lastReply = service.GetLastAssistantResponse();

// Obtenir un résumé textuel de l'état actuel du service
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: Tu es un assistant serviable.
```

## Copier la configuration d'un service

Clonez tous les paramètres d'une autre instance de service (sans l'historique de conversation) :

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```

Perplexity: [Configurer la recherche et les outils](perplexity.md).
