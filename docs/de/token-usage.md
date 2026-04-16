# Token-Nutzung

Die Token-Nutzung zeigt, wie viele Tokens eine Modellanfrage für Eingabe, Ausgabe, Cache und Reasoning verbraucht hat. In Mythosia.AI bekommst du diese Daten über `TokenUsage` auf Streaming-Events.

Wichtig wird das vor allem, wenn eine Antwort nicht aus genau einem LLM-Aufruf besteht. Eine einfache Antwort hat meist nur einen Round. Ein Agent oder ein Function-Calling-Flow kann zuerst das Modell aufrufen, dann ein Tool ausführen und anschließend mit dem Tool-Ergebnis erneut das Modell aufrufen. Deshalb gibt es zwei Werte, die man sauber unterscheiden sollte.

- `RoundUsage` beschreibt die Nutzung eines einzelnen LLM-Rounds.
- `Completion.Usage` beschreibt die kumulierte Nutzung des gesamten Streams.

## Warum das wichtig ist

Für eine Kontextanzeige in einer Chat-UI ist normalerweise der letzte `RoundUsage.Usage.TotalTokens` der passende Wert. Er kommt am nächsten an die Frage heran: "Wie groß wäre die nächste Modelleingabe, wenn diese Unterhaltung jetzt weitergeht?"

Für Logs, Diagnose und Kostenanalyse nimmst du `Completion.Usage.TotalTokens`. Dieser Wert bleibt über den gesamten Run kumuliert, auch wenn Function Calling oder Agenten mehrere Rounds auslösen.

Für Performance-Tuning helfen die Cache- und Reasoning-Felder. Damit siehst du, ob der Provider Eingaben aus dem Cache wiederverwendet hat oder ob zusätzliche Tokens für internes Reasoning verbraucht wurden.

## Event-Modell

| Event | Bedeutung | Typischer Einsatz |
|---|---|---|
| `StreamingContentType.RoundUsage` | Nutzung des gerade abgeschlossenen LLM-Rounds | Kontextanzeige, Debugging pro Round |
| `StreamingContentType.Completion` | Abschließendes Event mit kumulierter Nutzung | Logs, Diagnose, Kostenberichte |

`RoundUsage.Usage` ist nicht kumuliert. Wenn Round 1 zum Beispiel 10.100 Tokens nutzt und Round 2 14.000 Tokens, kann `Completion.Usage.TotalTokens` am Ende 24.100 sein, während der letzte `RoundUsage.Usage.TotalTokens` weiterhin 14.000 ist.

| Eigenschaft | Bedeutung |
|---|---|
| `RoundIndex` | Einsbasierte Nummer des LLM-Rounds |
| `IsFinalRound` | `true`, wenn dieser Round der letzte LLM-Round im Stream ist |

Token-Nutzung wird ausgegeben, wenn der Provider Usage-Daten zurückliefert. `IncludeMetadata = true` ist dafür nicht erforderlich.

## Kumulierte Nutzung am Ende

Nutze `Completion.Usage`, wenn du den Gesamtverbrauch der Streaming-Anfrage brauchst.

```csharp
await foreach (var chunk in service.StreamAsync("Erkläre Quantencomputing", StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.Text)
        Console.Write(chunk.Content);

    if (chunk.Type == StreamingContentType.Completion && chunk.Usage is not null)
    {
        Console.WriteLine($"Input:  {chunk.Usage.InputTokens}");
        Console.WriteLine($"Output: {chunk.Usage.OutputTokens}");
        Console.WriteLine($"Total:  {chunk.Usage.TotalTokens}");
    }
}
```

Bei einem einzelnen LLM-Round liegt dieser Wert meist nahe an `RoundUsage`. Bei einem Agenten ist es die Summe aller LLM-Rounds.

## Token-Anzeige in der UI

Für eine Kontextgrößenanzeige nimmst du das jeweils letzte `RoundUsage`.

```csharp
await foreach (var chunk in service.StreamAsync(message, StreamOptions.FullOptions))
{
    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        UpdateContextTokenMeter(chunk.Usage.TotalTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

Der letzte Modell-Round sieht den neuesten Gesprächszustand, einschließlich Tool-Ergebnissen, die während des Runs hinzugefügt wurden. Deshalb ist der letzte `RoundUsage.TotalTokens` für Chat-UIs der nützlichste Wert.

## Function Calling und Agenten

Bei Function Calling kann das Modell mehrfach ausgeführt werden. Lies jedes `RoundUsage`, behalte das letzte für die UI und nutze `Completion.Usage` am Ende für den Gesamtwert.

```csharp
TokenUsage? latestRound = null;
TokenUsage? cumulative = null;

await foreach (var chunk in service.StreamAsync(message, StreamOptions.WithFunctions))
{
    if (chunk.Type == StreamingContentType.FunctionCall)
    {
        Console.WriteLine($"Calling function: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.FunctionResult)
    {
        Console.WriteLine($"Function result: {chunk.Content}");
        continue;
    }

    if (chunk.Type == StreamingContentType.RoundUsage && chunk.Usage is not null)
    {
        latestRound = chunk.Usage;
        Console.WriteLine($"Round {chunk.RoundIndex}: {latestRound.TotalTokens} tokens");
        continue;
    }

    if (chunk.Type == StreamingContentType.Completion)
        cumulative = chunk.Usage;
}
```

## Cache- und Reasoning-Felder

Wenn der Provider sie liefert, enthält `TokenUsage` zusätzliche Werte für Cache und Reasoning.

| Eigenschaft | Bedeutung |
|---|---|
| `InputTokens` | Tokens im Prompt oder in der Eingabe |
| `OutputTokens` | Vom Modell erzeugte Tokens |
| `TotalTokens` | Eingabe + Ausgabe im Gültigkeitsbereich des Events |
| `CachedInputTokens` | Eingabetokens, die aus dem Cache kamen |
| `CacheCreationTokens` | Tokens, die neu in den Cache geschrieben wurden |
| `ReasoningTokens` | Tokens für verborgenes internes Reasoning |
| `VisibleOutputTokens` | Ausgabetokens ohne Reasoning |

## Hinweise zu Providern

Provider hängen Usage-Daten an unterschiedliche Stream-Chunks. Mythosia.AI normalisiert das in `RoundUsage` und das finale `Completion.Usage`.

Gemini ist dabei der kniffligste Fall: Usage kann an Text- oder Status-Chunks hängen und manchmal erst nach einem Function-Call-Chunk eintreffen. Die Bibliothek liest den Stream daher lange genug weiter, um diese Usage einzusammeln, bevor der nächste Round startet.

Als Consumer solltest du die normalisierten Events `RoundUsage` und `Completion.Usage` verwenden, statt provider-spezifische Metadata selbst zu parsen.
