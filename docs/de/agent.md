# Agent (ReAct-Loop)

## Warum ein Agent-Loop?

Mit dem normalen Funktionsaufruf macht das Modell **einen** Funktionsaufruf pro Anfrage, du führst ihn aus, und das Gespräch geht weiter. Viele reale Aufgaben erfordern jedoch **mehrere Schritte**, die das Modell autonom planen und ausführen muss:

- „Recherchiere die 3 wichtigsten KI-Unternehmen und vergleiche ihre Aktienkurse" — erfordert mehrere Web-Suchen und Kursabfragen
- „Finde die relevante Richtlinie, prüfe den Bestellstatus und sag mir, ob ich Anspruch auf eine Rückerstattung habe" — erfordert verschiedene Tools in logischer Reihenfolge
- Das Modell muss eine Suche eventuell **wiederholen oder verfeinern**, falls das erste Ergebnis unzureichend ist

Diesen Orchestrierungs-Loop selbst zu schreiben ist mühsam und fehleranfällig. Der **Agent-Loop** (ReAct-Muster: Reason → Act → Observe → Repeat) übernimmt das automatisch — das Modell entscheidet bei jedem Schritt, was als nächstes zu tun ist, bis es eine endgültige Antwort liefert.

## Grundlegende Verwendung

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// Oder per Erweiterungsmethoden:
service.WithMaxRounds(15).WithTimeout(60);
```

Vordefinierte Policies:

```csharp
service.WithFastPolicy();    // Niedriges Timeout, wenige Runden — schnelle Aufgaben
service.WithComplexPolicy(); // Höheres Timeout, mehr Runden — tiefe Recherche
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

Der Context wird über `AsyncLocal` weitergereicht, sodass gleichzeitige Agent-Läufe auf derselben Service-Instanz sich nicht gegenseitig beeinflussen.

Die vollständige Liste der verfügbaren Eigenschaften findest du in [AIRequestContext](request-contexts.md) (`SystemMessagePrefix`, `SystemMessageSuffix`, `AdditionalMessages`, `RequestMessageOverride`).

> Verfügbar ab Mythosia.AI v6.3.0.

## So funktioniert es

Jeder Schritt:

1. LLM erhält das Ziel + Gesprächsverlauf + Funktionsdefinitionen
2. Ruft das LLM eine Funktion auf → ausführen, Ergebnis dem Verlauf hinzufügen
3. Gibt das LLM eine Textantwort zurück → Loop endet, diese Antwort zurückgeben
4. Erreicht die Schrittzahl `maxSteps` → `AgentMaxStepsExceededException` auslösen
