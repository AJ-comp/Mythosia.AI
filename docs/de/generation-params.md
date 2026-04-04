# Generierungsparameter

## Gemeinsame Eigenschaften

Alle KI-Service-Instanzen stellen diese Eigenschaften bereit:

```csharp
service.Temperature = 0.7f;        // Zufälligkeit [0, 2]. Niedriger = deterministischer
service.TopP = 1.0f;               // Schwellenwert für Nucleus-Sampling
service.MaxTokens = 1024;          // Maximale Ausgabe-Tokens
service.FrequencyPenalty = 0.0f;   // Wiederholte Tokens bestrafen
service.PresencePenalty = 0.0f;    // Bereits vorhandene Tokens bestrafen
service.MaxMessageCount = 20;      // Größe des Gesprächsfensters
```

## Fluent-Erweiterungsmethoden

Diese geben `this` zurück, was Verkettung ermöglicht:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithSystemMessage("Du bist ein hilfreicher Assistent.")
    .WithTemperature(0.3f)
    .WithMaxTokens(2048)
    .WithStatelessMode(true);
```

| Methode | Beschreibung |
|--------|-------------|
| `.WithSystemMessage(string)` | System-Prompt setzen |
| `.WithTemperature(float)` | Begrenzt auf [0, 2] |
| `.WithMaxTokens(uint)` | Maximale Ausgabe-Tokens |
| `.WithStatelessMode(bool)` | Gesprächsverlauf deaktivieren |

## Statusloser Modus

Wenn aktiviert, ist jede Anfrage unabhängig — kein Gesprächsverlauf wird gesendet oder gespeichert:

```csharp
service.StatelessMode = true;

// Äquivalent:
var service = new OpenAIService(apiKey, http).WithStatelessMode(true);
```

Nützlich für einmalige Abfragen, bei denen du keinen Verlauf-Overhead möchtest.

## Einmalige Abfragen

Diese Erweiterungsmethoden führen eine einzelne Abfrage durch, ohne den Gesprächsverlauf zu nutzen oder zu beeinflussen:

```csharp
// Text-Prompt
string response = await service.AskOnceAsync("Was ist 2+2?");

// Nachricht (multimodal)
string response = await service.AskOnceAsync(message);

// Bild aus Dateipfad
string response = await service.AskOnceWithImageAsync("Beschreibe das", "foto.jpg");
```

## Modell wechseln

Modell mitten in einer Sitzung wechseln, während der Gesprächsverlauf erhalten bleibt:

```csharp
service.ChangeModel(AIModels.OpenAI.Gpt4_1);

// Oder per Erweiterungsmethode — löscht den Verlauf und startet neu:
service.StartNewConversation(AIModels.Anthropic.ClaudeSonnet4_6);
```

## Mehrere Gespräche verwalten

Eine einzige Service-Instanz kann mehrere unabhängige Gesprächsthreads führen:

```csharp
// Neues Gesprächsblock starten
var chat1 = service.AddNewChat();

// Zu einem anderen Block wechseln
service.SetActivateChat(chat2Id);

// Alle Blöcke abrufen
var allChats = service.ChatRequests;
```

## Gesprächsstatus abrufen

Letzte Assistentenantwort oder eine kurze Zusammenfassung der aktuellen Sitzung abrufen:

```csharp
// Letzte Assistentennachricht (oder null, wenn keine vorhanden)
string? lastReply = service.GetLastAssistantResponse();

// Textzusammenfassung des aktuellen Service-Status
string info = service.GetConversationSummary();
// → Model: gpt-4o-mini
// → Messages: 12
// → Stateless Mode: False
// → System: Du bist ein hilfreicher Assistent.
```

## Service-Konfiguration kopieren

Alle Einstellungen einer anderen Service-Instanz klonen (ohne Gesprächsverlauf):

```csharp
var newService = new AnthropicService(apiKey, http);
newService.CopyFrom(existingService);
```
