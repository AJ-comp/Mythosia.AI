# Paramètres de génération

## Propriétés communes

Toutes les instances de service IA exposent ces propriétés :

```csharp
service.Temperature = 0.7f;        // Aléatoire [0, 2]. Plus bas = plus déterministe
service.TopP = 1.0f;               // Seuil d'échantillonnage nucleus
service.MaxTokens = 1024;          // Tokens de sortie maximum
service.FrequencyPenalty = 0.0f;   // Pénalise les tokens répétés
service.PresencePenalty = 0.0f;    // Pénalise les tokens déjà présents
service.MaxMessageCount = 20;      // Taille de la fenêtre de conversation (obsolète — supprimé en v7.0)
```

> **Obsolète :** `MaxMessageCount` (la fenêtre glissante basée sur le nombre de messages) est obsolète et sera supprimé en v7.0 — la gestion du contexte devient exclusivement basée sur les tokens via `ConversationPolicy`. Jusqu'à sa suppression, la fenêtre garantit de ne jamais écarter le message utilisateur le plus récent, afin que les exécutions d'outils agentiques ne puissent pas perdre la requête sur laquelle elles travaillent.

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
var chat1 = service.AddNewChat();

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
