# Textvervollständigung

## Einfache Abfrage

Die einfachste Verwendung — eine Nachricht senden, eine Antwort erhalten:

```csharp
var response = await service.GetCompletionAsync("Was ist die Hauptstadt von Frankreich?");
Console.WriteLine(response); // Paris
```

## System-Prompt

Gib dem Modell mit einem System-Prompt eine Rolle oder Anweisungen:

```csharp
service.SystemMessage = "Du bist ein prägnanter Assistent. Antworte in einem Satz.";

var response = await service.GetCompletionAsync("Erkläre Rekursion.");
```

## Mehrere Gesprächsrunden

Nachrichten werden automatisch angehängt. Jeder Aufruf von `GetCompletionAsync` erweitert den Gesprächsverlauf:

```csharp
await service.GetCompletionAsync("Mein Name ist Alice.");
var response = await service.GetCompletionAsync("Wie heiße ich?");
// → "Dein Name ist Alice."
```

Um den Gesprächsverlauf zu löschen:

```csharp
service.ActivateChat.ClearMessages();
```

## Nachrichten manuell aufbauen

Verwende `MessageBuilder`, um Nachrichten explizit zu erstellen:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Fasse diesen Text zusammen: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Bild-Eingabe)

Anbieter mit Vision-Unterstützung akzeptieren Bildinhalte neben Text:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagramm.png");

var message = MessageBuilder.Create().AddText("Was zeigt dieses Diagramm?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Schnellabfrage (Statische API)

Für einmalige Abfragen ohne Service-Instanz nutze die statische Methode `QuickAskAsync`. Der Anbieter wird automatisch anhand des Modellnamens erkannt:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "Was ist die Hauptstadt von Frankreich?",
    model: AIModels.OpenAI.Gpt4oMini  // Standard
);
```

Variante mit Bild:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Beschreibe dieses Bild",
    imagePath: "foto.jpg",
    model: AIModels.OpenAI.Gpt4_1
);
```

## Bild-Hilfsmethoden

Bilder analysieren ohne `MessageBuilder` — der Service liest die Datei und erkennt den MIME-Typ automatisch:

```csharp
// Aus Dateipfad
var response = await service.GetCompletionWithImageAsync(
    "Was zeigt dieses Diagramm?", "diagramm.png");

// Aus URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Beschreibe dieses Foto", "https://example.com/foto.jpg");
```

## Letzte Nachricht wiederholen

Die letzte Assistentenantwort entfernen und die letzte Benutzernachricht erneut senden:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Hilfreich, wenn die vorherige Antwort unbefriedigend war.

## Token-Zählung

Schätze die Token-Nutzung vor dem Senden einer Anfrage. Verfügbar bei **allen Anbietern**:

```csharp
// Tokens für den aktuellen Gesprächsverlauf zählen
uint conversationTokens = await service.GetInputTokenCountAsync();

// Tokens für einen bestimmten Prompt zählen
uint promptTokens = await service.GetInputTokenCountAsync("Dein Prompt hier");
```

OpenAI und die meisten Anbieter nutzen lokale TikToken-Schätzungen. Anthropic und Google rufen ihre nativen Token-Zähl-APIs für exakte Ergebnisse auf.

## Fluent Message Chain

`BeginMessage()` bietet eine Fluent-API zum Aufbauen und Senden von Nachrichten in einer einzigen Kette — inklusive Text, Bilder, Streaming und Policy-Konfiguration:

```csharp
// Text + Bild → Senden
string response = await service.BeginMessage()
    .AddText("Was zeigt dieses Diagramm?")
    .AddImage("diagramm.png")
    .SendAsync();

// Einmalige Abfrage (kein Gesprächsverlauf)
string answer = await service.BeginMessage()
    .AddText("Übersetze dies ins Koreanische")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("Schreib ein Gedicht über den Frühling")
    .StreamAsync(chunk => Console.Write(chunk));

// Mit benutzerdefiniertem Timeout und Policy
string result = await service.BeginMessage()
    .AddText("Analysiere dieses Bild")
    .AddImageUrl("https://example.com/foto.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` unterstützt auch `IAsyncEnumerable`:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("Erzähl mir eine Geschichte").StreamAsync())
    Console.Write(chunk);
```

## Ausgabelänge und Temperatur steuern

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // Niedriger = deterministischer
```
