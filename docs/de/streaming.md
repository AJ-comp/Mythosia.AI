# Streaming

Für die fertige Antwort mit Verbrauch und Quellen liefert `await run.Result` eine `AIRunResult`-Momentaufnahme. Die Zeichenfolge steht in `result.Text`; ein Stream-Leser ist unnötig. Diese API-Änderung gehört zu Mythosia.AI 8.0.0. Die Rückgabetypen von `GetCompletionAsync` und `StructuredStreamRun<T>.Result` bleiben erhalten. [Run-Ergebnis und Migration](execution-api-transition.md#run-result).


Für unabhängige Einstellungen und wiederverwendbare Varianten verwenden Sie den [Anfrage-Builder](request-building.md). Rufen Sie `CreateRequest(...)` vor `With...` auf. Service-Eigenschaften und dessen Fluent-Methoden behalten ihr bisheriges Verhalten.

Text sofort anzuzeigen macht längere Antworten schon während ihrer Entstehung lesbar. Mit `StartRunAsync` kannst du denselben Auftrag zusätzlich abbrechen und bei unterstützten Modellen neue Anweisungen senden; siehe [Run-Anleitung](execution-api-transition.md).

```csharp
await using var run = await service.StartRunAsync(
    "Fasse das Dokument zusammen.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}

string answer = (await run.Result).Text;
```

## Beispiele für die bisherigen Kompatibilitätsmethoden

Eingabeannehmende Service-/RAG-StreamAsync-Methoden bleiben in v8 öffentlich. Für neue Ausführungssteuerung StartRunAsync nutzen; run.StreamAsync() beobachtet nur einen vorhandenen Run.

## Einfaches Streaming

Verwende `StreamAsync`, um Tokens während der Generierung zu empfangen:

```csharp
await foreach (var token in service.StreamAsync("Erzähl mir eine Geschichte"))
{
    Console.Write(token);
}
```

## Streaming mit Content-Typ

`StreamAsync` kann `StreamingContent`-Objekte zurückgeben, die sowohl den Text als auch seinen Typ enthalten:

```csharp
await foreach (var content in service.StreamAsync("Erkläre Quantencomputing", StreamOptions.Default))
{
    Console.Write(content.Content);
}
```

## Reasoning-Streaming

OpenAI, Claude, Gemini, Grok und DeepSeek Flash liefern Reasoning-Inhalte des Anbieters im selben Streaming-Muster. Aktiviere Reasoning am Service oder für die Anfrage und beobachte es mit `StreamOptions.WithReasoning()`:

```csharp
using Mythosia.AI.Models.Streaming;

await foreach (var content in service.StreamAsync("Löse: 2x + 5 = 13", new StreamOptions().WithReasoning()))
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Gedanken] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

Gemini 3.7/3.8 Flash nutzen die bestehenden Streaming- und Run-Ereignisse. `StreamingContentType.Reasoning` enthält vom Anbieter bereitgestellte Zusammenfassungen oder Fortschrittsmeldungen, sofern vorhanden, nicht garantiert den gesamten internen Gedankengang. `StreamOptions.WithReasoning()` wählt diese Ausgabe; `WithReasoning(ReasoningLevel...)` am Service steuert den Aufwand.

Grok 4.6 kann über diese Ereignisse ebenfalls optionale Reasoning-Zusammenfassungen des Anbieters liefern. Die Streamoption wählt die sichtbare Ausgabe; `WithReasoning(ReasoningLevel...)` bestimmt den Aufwand einer Aufgabe. Fehlende Zusammenfassungen bedeuten nicht, dass Reasoning deaktiviert ist. Siehe [Grok-Konfiguration](providers.md#xai-xaiservice).

DeepSeek Flash gibt nach Aktivierung von Reasoning `reasoning_content` über dieselben Ereignisse aus. `StreamOptions.WithReasoning()` steuert die Beobachtung; `WithDeepSeekReasoning(...)` oder `WithReasoning(...)` am Service steuert Reasoning. Siehe [DeepSeek](providers.md#deepseek-deepseekservice).

## Streaming mit strukturierter Ausgabe

Text in Echtzeit streamen und am Ende ein deserialisiertes Objekt erhalten:

```csharp
var run = service.BeginStream(prompt).As<MyDto>();

// Tokens beim Eintreffen an die UI streamen
await foreach (var chunk in run.Stream())
    Console.Write(chunk);

// Vollständig geparsertes Ergebnis nach Abschluss
MyDto result = await run.Result;
```

## Token-Nutzung

Nach Abschluss des Streamings enthält das `Completion`-Ereignis ein `TokenUsage`-Objekt mit detaillierten Nutzungsmetriken:

```csharp
await foreach (var content in service.StreamAsync("Erkläre Quantencomputing", StreamOptions.Default))
{
    if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);

    if (content.Type == StreamingContentType.Completion && content.Usage != null)
    {
        Console.WriteLine($"\nEingabe-Tokens:  {content.Usage.InputTokens}");
        Console.WriteLine($"Ausgabe-Tokens: {content.Usage.OutputTokens}");
        Console.WriteLine($"Gesamt-Tokens:  {content.Usage.TotalTokens}");
    }
}
```

### TokenUsage-Eigenschaften

| Eigenschaft | Beschreibung |
|---|---|
| `InputTokens` | Tokens in der Eingabe/dem Prompt |
| `OutputTokens` | Tokens in der Ausgabe/Vervollständigung |
| `TotalTokens` | Eingabe + Ausgabe |
| `CachedInputTokens` | Aus Cache bediente Tokens (geringere Kosten) |
| `CacheCreationTokens` | In Cache geschriebene Tokens (Anthropic) |
| `ReasoningTokens` | Tokens für internes Reasoning verwendet |
| `CacheHitRatio` | Cache-Trefferquote (0,0–1,0) |
| `VisibleOutputTokens` | Ausgabe-Tokens ohne Reasoning |

### Cache-Effizienz prüfen

```csharp
if (content.Usage?.HasCacheActivity == true)
{
    Console.WriteLine($"Cache-Trefferquote: {content.Usage.CacheHitRatio:P1}");
    Console.WriteLine($"Nicht gecachte Eingabe: {content.Usage.NonCachedInputTokens}");
}
```

## StreamOptions-Voreinstellungen

`StreamOptions` bietet Voreinstellungen und einen Fluent-Builder zur Steuerung der Stream-Ausgabe:

```csharp
// Vollständig — Metadaten, Funktionsaufrufe, Reasoning
await foreach (var c in service.StreamAsync("prompt", StreamOptions.FullOptions))
    Console.Write(c.Content);

// Minimal — nur Text, keine Metadaten
await foreach (var c in service.StreamAsync("prompt", StreamOptions.Minimal))
    Console.Write(c.Content);

// Für Funktionsaufruf-Szenarien
await foreach (var c in service.StreamAsync("prompt", StreamOptions.WithFunctions))
{ /* Text, FunctionCall, FunctionResult, Completion behandeln */ }
```

Fluent-Builder für individuelle Kombinationen:

```csharp
var options = new StreamOptions()
    .WithReasoning()       // Gedankengang einbeziehen
    .WithMetadata()        // Modellinfo in Completion einbeziehen
    .WithFunctionCalls();  // Funktionsaufruf während des Streams aktivieren
```

Behandeln Sie angezeigte Chunks als vorläufig, bis `run.Result` erfolgreich abgeschlossen ist. Der gemeinsame OpenAI-kompatible Streaming-Pfad und der DeepSeek-Streaming-Pfad weisen neue Text-, Reasoning- oder Tool-Daten nach einem expliziten Abschluss sowie einen geänderten Abschlussgrund zurück: `run.Result` löst eine Ausnahme aus, die fehlgeschlagene Runde wird nicht im Gesprächsverlauf gespeichert und ihre Tools werden nicht ausgeführt. Diese Fehlerbehandlung macht frühere Runden oder bereits extern ausgeführte Aktionen nicht rückgängig. Das letzte Delta darf im ersten Abschlussereignis enthalten sein; ein nachfolgendes Ereignis ausschließlich mit Nutzungsdaten bleibt zulässig.

## Statusloses Streaming (StreamOnceAsync)

Eine Antwort streamen, ohne den Gesprächsverlauf zu beeinflussen — das Streaming-Äquivalent von `AskOnceAsync`:

```csharp
await foreach (var chunk in service.StreamOnceAsync("Übersetze das ins Französische"))
    Console.Write(chunk);
```

Akzeptiert auch eine `Message` für multimodale Eingaben:

```csharp
var message = MessageBuilder.Create().AddText("Beschreibe das").AddImage("foto.jpg").Build();

await foreach (var chunk in service.StreamOnceAsync(message))
    Console.Write(chunk);
```

## Gesprächszusammenfassung vor dem Streaming

Die automatische Zusammenfassungs-Policy wird während des Streamings nicht ausgelöst. Rufe `ApplySummaryPolicyIfNeededAsync` explizit vor `StreamAsync` auf:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Lass uns unser Gespräch fortsetzen...", StreamOptions.Default))
    Console.Write(chunk.Content);
```

Perplexity: [Längere Aufgaben weiterlaufen lassen / Zitate können Webtreffer oder andere Anbieterquellen bezeichnen. Positionen beziehen sich auf einen einzelnen Antwort-/Inhaltsteil, nicht auf das zusammengesetzte Run-Ergebnis. Behalten Sie URL und Titel zur Anzeige und Prüfung; eine Quelle bestätigt nicht automatisch jede erzeugte Aussage.](perplexity.md).
