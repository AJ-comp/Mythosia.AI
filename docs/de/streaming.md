# Streaming

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

Alle reasoning-fähigen Anbieter (OpenAI, Claude, Gemini, Grok, DeepSeek) nutzen dasselbe Muster. Übergib `StreamOptions` mit aktiviertem Reasoning:

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

`StreamingContentType.Reasoning` enthält den internen Gedankengang des Modells, `StreamingContentType.Text` die endgültige Antwort.

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
