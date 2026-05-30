# Token-Nutzung

Die Token-Nutzung zeigt, wie viele Tokens eine Modellanfrage für Eingabe, Ausgabe, Cache und Reasoning verbraucht hat. In Mythosia.AI bekommst du diese Daten über `TokenUsage` auf Streaming-Events.

Wichtig wird das vor allem, wenn eine Antwort nicht aus genau einem LLM-Aufruf besteht. Eine einfache Antwort hat meist nur einen Round. Ein Agent oder ein Function-Calling-Flow kann zuerst das Modell aufrufen, dann ein Tool ausführen und anschließend mit dem Tool-Ergebnis erneut das Modell aufrufen. Deshalb gibt es zwei Werte, die man sauber unterscheiden sollte.

- `RoundUsage` beschreibt die Nutzung eines einzelnen LLM-Rounds.
- `Completion.Usage` beschreibt die kumulierte Nutzung des gesamten Streams.

> [!NOTE]
> Diese Seite setzt voraus, dass du bereits weißt, was ein **LLM-Round** ist. Kurz: Ein Round = ein Anfrage-Antwort-Austausch zwischen deiner App und dem Modell. Function-Calling-Flows können pro Nutzernachricht mehrere Rounds erzeugen. Eine Schritt-für-Schritt-Erklärung findest du unter [Grundkonzepte — Was ist ein Round?](core-concepts.md#was-ist-ein-round).

## Warum das wichtig ist

Für eine Kontextanzeige in einer Chat-UI ist normalerweise der letzte `RoundUsage.Usage.InputTokens` der passende Wert. Er kommt am nächsten an die Frage heran: "Wie groß wäre die nächste Modelleingabe, wenn diese Unterhaltung jetzt weitergeht?"

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
        UpdateContextTokenMeter(chunk.Usage.InputTokens);

        if (chunk.IsFinalRound)
            MarkTokenMeterAsFinal();

        continue;
    }

    if (chunk.Type == StreamingContentType.Text)
        AppendToChat(chunk.Content);
}
```

Der letzte Modell-Round sieht den neuesten Gesprächszustand, einschließlich Tool-Ergebnissen, die während des Runs hinzugefügt wurden. Deshalb ist der letzte `RoundUsage.Usage.InputTokens` für Chat-UIs der nützlichste Wert.

<a id="how-context-size-changes"></a>

## Wie sich die Kontextgröße ändert

Betrachte die Kontextgröße als Eingabegröße des neuesten Modellaufrufs, nicht als laufende Summe. Ein späterer Round enthält bereits die Gesprächsteile, die aus früheren Rounds erhalten geblieben sind. Wenn du die Eingaben mehrerer Rounds addierst, zählst du denselben Prompt, dieselben Tool-Definitionen und dieselbe Historie doppelt.

Beispiel:

| Schritt | Was vor diesem Modellaufruf hinzukommt | Ungefähre Eingabetokens | UI-Kontextanzeige |
|---|---|---:|---:|
| Round 1 | Systemprompt, Tools, Historie, Nutzerfrage | 20.000 | 20.000 |
| Zwischen den Rounds | Tool-Call-Ausgabe 100 Tokens; Tool-Ergebnis 5.000 Tokens | kein LLM-Aufruf | unverändert |
| Round 2 | Eingabe aus Round 1 + Tool-Call-Nachricht + Tool-Ergebnis | 25.100 + Overhead | 25.100 + Overhead |
| Ausgabe von Round 2 | Das Modell erzeugt 3.000 Tokens und ein weiterer Round ist nötig | kein LLM-Aufruf | unverändert |
| Round 3 | Eingabe aus Round 2 + Ausgabe aus Round 2, plus ggf. neues Tool-Ergebnis | 28.100 + Overhead | 28.100 + Overhead |
| Ausgabe von Round 3 | Das Modell erzeugt eine finale Antwort mit 2.000 Tokens | kein LLM-Aufruf | unverändert |
| Nächste Nutzerfrage | Die vorherige finale Antwort und die neue Nutzerfrage gehören nun zur nächsten Eingabe | etwa 30.100 + neue Frage + Overhead | durch die `InputTokens` des neuen Rounds ersetzt |

Wenn Round 3 der finale Round ist, sollte die Kontextanzeige also ungefähr **28.100 + Overhead** zeigen, nicht 30.100 und nicht die Summe aller Rounds. Die finale Antwort mit 2.000 Tokens wirkt sich erst auf den nächsten Modellaufruf aus, weil sie dann Teil der Gesprächshistorie ist.

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
        Console.WriteLine($"Round {chunk.RoundIndex}: input={latestRound.InputTokens}, total={latestRound.TotalTokens} tokens");
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

## Warum die normalisierten Events verwenden

Provider hängen Usage-Daten an unterschiedliche Stream-Chunks. Besonders knifflig ist Gemini: Usage kann an Text- oder Status-Chunks hängen und manchmal erst nach einem Function-Call-Chunk eintreffen — deshalb liest die Bibliothek den Stream lange genug weiter, um diese Usage einzusammeln, bevor der nächste Round startet. Sie fängt diese provider-spezifischen Unterschiede ab und normalisiert sie in `RoundUsage`- und finale `Completion.Usage`-Events. Parse deshalb in deinem Consumer-Code nicht selbst provider-spezifische Metadata, sondern verwende die normalisierten `RoundUsage`- und `Completion.Usage`-Events.
