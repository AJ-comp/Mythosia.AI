# Textvervollständigung

Für unabhängige Einstellungen und wiederverwendbare Varianten verwenden Sie den [Anfrage-Builder](request-building.md). Rufen Sie `CreateRequest(...)` vor `With...` auf. Service-Eigenschaften und dessen Fluent-Methoden behalten ihr bisheriges Verhalten.

Wenn die Anwendung nur die fertige Antwort benötigt, ist `GetCompletionAsync` weiterhin passend. Für Fortschrittsanzeige, Abbruch und unterstützte zusätzliche Anweisungen während der Arbeit hilft die [Run-Anleitung](execution-api-transition.md).

<a id="completion-cancellation"></a>

## Eine nicht mehr benötigte Antwort abbrechen

Wenn Benutzer ein Fenster schließen, auf Stopp klicken oder die Wartezeit der Anwendung abläuft, wird die Antwort möglicherweise nicht mehr benötigt. Mit einem `CancellationToken` beenden Sie die Kommunikation und Arbeit auf Clientseite und vermeiden weitere Tool- und Modellaufrufe. Für die fertige Antwort bleibt `GetCompletionAsync` geeignet; ein Abbruch allein erfordert keinen Run.

### Before: kein Abbruchsignal des Aufrufers

```csharp
string answer = await service.CreateRequest("Fasse dieses Dokument zusammen.")
    .GetCompletionAsync();
```

### After: Benutzerabbruch oder Abbruch nach 30 Sekunden

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Fasse dieses Dokument zusammen.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Abgebrochen.");
}
```

Bewahren Sie die Tokenquelle während des Aufrufs auf und verbinden Sie Stopp oder das Schließen mit `cancellation.Cancel()`. Das Beispiel plant zusätzlich einen Abbruch nach 30 Sekunden. Nach der Bereinigung erhält der Aufrufer eine `OperationCanceledException`. Eine Frist mit `CancellationTokenSource` zählt ebenfalls als Aufruferabbruch; `FunctionCallingPolicy.TimeoutSeconds` behält das bisherige Timeout-Fehlerverhalten.

String- und `Message`-Überladungen des Dienstes, typisierte Antworten, Request-Builder und `MessageChain.SendAsync` / `SendOnceAsync` akzeptieren das Token. Bisherige Aufrufe ohne Token funktionieren weiter. Alternative Einstiegspunkte:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Fasse dieses Dokument zusammen.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Gib Titel und Autor als JSON zurück.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Fasse dieses Dokument zusammen.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Übersetze diesen Satz.")
    .SendOnceAsync(cancellationToken: token);
```

Das Token erreicht Vorbereitung, HTTP-Senden und -Lesen, kooperative lokale Tools und spätere Modellrunden. Nach erkanntem Abbruch werden wartende Tools und weitere Runden übersprungen. Die Bereinigung erhält die Zuordnung protokollierter Toolaufrufe und Ergebnisse; gestartete Tools, die das Token ignorieren, können sie verzögern. Abgeschlossene Aktionen und der Gesprächsverlauf werden nicht zurückgesetzt. Siehe [Toolvertrag](function-calling.md#tool-execution-contract).

Der Abbruch von Berechnung oder Abrechnung beim Anbieter ist nicht garantiert. OpenAI beschreibt das Trennen der Verbindung für gewöhnliche Responses; Google nennt ausdrücklich einen reinen Clientabbruch mit weiterhin berechnetem Verbrauch. [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) · [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). Ein Hintergrundauftrag braucht ein ausdrückliches `CancelAsync()`; ein Abbruch von `WaitForCompletionAsync(cancellationToken: ...)` beendet nur das Warten. Gewöhnliche Completion wird nicht in Hintergrundausführung umgewandelt. Siehe [Perplexity](perplexity.md).

<a id="completion-cancellation-migration"></a>

Diese Ergänzung gehört zu Mythosia.AI 8.0.0. Aufrufe ohne Token und bisherige positionelle profile/context-Argumente bleiben auf Quelltextebene gültig; Verbraucher müssen neu gebaut werden. Eigene `IAIService`-Implementierungen müssen beiden Completion-Signaturen abschließend `CancellationToken cancellationToken = default` hinzufügen und weiterreichen. Eigene `AIService`-Provider behalten das Override `GetCompletionAsync(Message)` und reichen das geschützte `RequestCancellationToken` an den Transport weiter. Builder und Run allein benötigten diese Schnittstellenänderung nicht. Unterklassen mit Overrides geänderter public-virtual-Überladungen für String/profile/context-Completion, Bildhelfer oder `RunAgentAsync` müssen ebenfalls das neue `CancellationToken` anhängen und weiterreichen; nur das Provider-Override mit einem einzelnen `Message` behält seine Signatur. Methodengruppen-Delegates für geänderte Signaturen benötigen gegebenenfalls ein explizites Lambda, das das Token übergibt oder weglässt.

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

Für Diagramme, Screenshots, lokale Tool-Aufrufe oder gründliche Prüfung nach einer schnellen Antwort nutze [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). Reasoning bleibt standardmäßig aus; aktiviere es mit `WithDeepSeekReasoning(...)` oder `WithReasoning(...)` je Anfrage.

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

Perplexity: [Mit einem Agent-Preset antworten / Quellen, Bilder und strukturierte Antworten](perplexity.md).
