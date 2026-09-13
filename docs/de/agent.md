# Agent (ReAct-Loop)

Für die fertige Antwort mit Verbrauch und Quellen liefert `await run.Result` eine `AIRunResult`-Momentaufnahme. Die Zeichenfolge steht in `result.Text`; ein Stream-Leser ist unnötig. Diese API-Änderung gehört zu Mythosia.AI 8.0.0. Die Rückgabetypen von `GetCompletionAsync` und `StructuredStreamRun<T>.Result` bleiben erhalten. [Run-Ergebnis und Migration](execution-api-transition.md#run-result).


Für unabhängige Einstellungen und wiederverwendbare Varianten verwenden Sie den [Anfrage-Builder](request-building.md). Rufen Sie `CreateRequest(...)` vor `With...` auf. Service-Eigenschaften und dessen Fluent-Methoden behalten ihr bisheriges Verhalten.

> Die `CreateRequest`-Beispiele benötigen Mythosia.AI 8.0.0 / Abstractions 4.0.0. Die frühere Version 7.1 mit Run und gemeinsamen Anfrageoptionen enthält den Builder noch nicht. Ältere Pakete können ihre bisherigen Service-Überladungen verwenden.

Die Suche in Dokumenten und das Prüfen einer Bestellung können mehrere Werkzeugaufrufe erfordern. Wie du dabei Fortschritt, Abbruch und unterstützte zusätzliche Anweisungen anbietest, zeigt die [Run-Anleitung](execution-api-transition.md).

## Warum ein Agent-Loop?

Manche Fragen brauchen mehrere Informationsquellen: Das Modell muss ein Werkzeug auswählen, dessen Ergebnis prüfen und gegebenenfalls weitere Werkzeuge aufrufen. Eine gemeinsame Modell-/Werkzeugschleife führt diese Schritte bis zur Antwort aus; ein Rundenlimit begrenzt die Ausführung:

- „Recherchiere die 3 wichtigsten KI-Unternehmen und vergleiche ihre Aktienkurse" — erfordert mehrere Web-Suchen und Kursabfragen
- „Finde die relevante Richtlinie, prüfe den Bestellstatus und sag mir, ob ich Anspruch auf eine Rückerstattung habe" — erfordert verschiedene Tools in logischer Reihenfolge
- Das Modell muss eine Suche eventuell **wiederholen oder verfeinern**, falls das erste Ergebnis unzureichend ist

`GetCompletionAsync` und `StartRunAsync` führen bereits die gemeinsame Modell-/Werkzeugschleife aus. Die bisherigen Agent-Hilfsmethoden ergänzen ein Rundenlimit pro Aufruf und eine eigene Fehlerübersetzung, keinen unabhängigen Planer oder Ausführungsmechanismus.

## Werkzeugaufgaben mit Run starten

```csharp
// Registriere die Funktionen vor dem Start auf dem Service.
await using var run = await service
    .CreateRequest("Finde die Richtlinie, prüfe die Bestellung und erkläre das Ergebnis.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

Lokale Tools können Objekte über `Task<T>` / `ValueTask<T>` zurückgeben und ein injiziertes `CancellationToken` erhalten. `run.Cancel()` oder das Starttoken erreicht kooperative Tools; das Beenden des Stream-Lesers allein nicht. Ausnahmen gelten als Fehler. Bei Abbruch werden wartende Aufrufe übersprungen, und die Bereinigung wartet weiterhin auf gestartete Tools, die das Token ignorieren. Siehe [Ergebnisse, Fehler und Abbruch](function-calling.md#tool-execution-contract).

## Bisherige Agent-API: Kompatibilitätsbeispiele

Die folgenden Beispiele dokumentieren die weiterhin aufrufbaren Methoden `RunAgentAsync` und `RunAgentStreamAsync`. Sie zeigen jetzt `[Obsolete]`-Warnungen; neue Aufrufe können `StartRunAsync` verwenden. Unterschiede bei Rundenlimits und Fehlern stehen in der [Run-Anleitung](execution-api-transition.md).

Funktionen registrieren, dann `RunAgentAsync` mit einem Ziel aufrufen:

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "Sucht im Web nach Informationen",
        ("query", "Suchanfrage", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "Gibt den aktuellen Aktienkurs zurück",
        ("ticker", "Aktien-Ticker-Symbol", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "Wie ist der aktuelle Aktienkurs der 3 wichtigsten KI-Unternehmen?",
    maxSteps: 10
);

Console.WriteLine(result);
```

Das Modell ruft Funktionen nach Bedarf auf, beobachtet die Ergebnisse und entscheidet den nächsten Schritt — bis es eine abschließende Textantwort liefert.

## maxSteps

`maxSteps` begrenzt die Anzahl der LLM→Funktionsaufruf-Runden. Wenn der Agent das Limit erreicht, wird `AgentMaxStepsExceededException` ausgelöst:

```csharp
try
{
    string result = await service.RunAgentAsync("Recherchiere und fasse zusammen...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponse enthält alles, was das Modell bisher produziert hat
    Console.WriteLine($"Frühzeitig gestoppt: {ex.PartialResponse}");
}
```

## FunctionCallingPolicy

Das Verhalten des Agent-Loops pro Runde steuern:

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// RunAgentAsync verwendet DefaultPolicy und das explizite Argument maxSteps.
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "Recherchiere und fasse zusammen...", maxSteps: 15);
```

Vordefinierte Policies:

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // Niedriges Timeout, wenige Runden — schnelle Aufgaben
var fastResult = await service.RunAgentAsync(
    "Recherchiere und fasse zusammen...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // Höheres Timeout, mehr Runden — tiefe Recherche
var complexResult = await service.RunAgentAsync(
    "Recherchiere und fasse zusammen...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## Anforderungskontext pro Aufruf

`RunAgentAsync` und `RunAgentStreamAsync` akzeptieren einen optionalen `AIRequestContext`, sodass du dynamische System-Message-Prefix/Suffix, Referenzdokumente oder eine ersetzte Ziel-Nachricht einspeisen kannst — **begrenzt auf einen einzelnen Agent-Lauf**, ohne die System-Message des Services oder den Gesprächsverlauf zu verändern.

```csharp
string result = await service.RunAgentAsync(
    goal: "Finde die Rückerstattungsrichtlinie und prüfe, ob Bestellung #1234 in Frage kommt.",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Heutiges Datum: {DateTime.UtcNow:yyyy-MM-dd}.\n",
        SystemMessageSuffix = "\nZitiere immer den verwendeten Richtlinienabschnitt."
    });
```

Die Streaming-Variante nimmt denselben Parameter:

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "Recherchiere die Aktienkurse der 3 wichtigsten KI-Unternehmen.",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Zeitzone des Nutzers: {userTz}\n"
    }))
{
    // Inhalt verarbeiten
}
```

`AIRequestContext` wird über `AsyncLocal` weitergegeben. Das macht den veränderlichen Dienst, seinen Gesprächsverlauf und seine Ausführungsrichtlinien nicht nebenläufig sicher. Verwende getrennte Dienstinstanzen für unabhängige gleichzeitige Aufgaben.

Die vollständige Liste der verfügbaren Eigenschaften findest du in [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Verfügbar ab Mythosia.AI v6.3.0.

## So funktioniert es

Jeder Schritt:

1. LLM erhält das Ziel + Gesprächsverlauf + Funktionsdefinitionen
2. Ruft das LLM eine Funktion auf → ausführen, Ergebnis dem Verlauf hinzufügen
3. Gibt das LLM eine Textantwort zurück → Loop endet, diese Antwort zurückgeben
4. Erreicht die Schrittzahl `maxSteps` → `AgentMaxStepsExceededException` auslösen
