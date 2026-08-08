# AIRequestContext

## Was ist das?

`AIRequestContext` ermöglicht es, **was das Modell für eine einzelne Anfrage sieht** zu verändern — zusätzliche Anweisungen injizieren, Referenzdokumente hinzufügen oder die Nachricht des Benutzers komplett ersetzen — ohne die System-Nachricht oder den Gesprächsverlauf des Services dauerhaft zu ändern.

## Das Problem, das es löst

Stell dir eine RAG-Pipeline vor, die relevante Dokumente abruft und sie in den Prompt einbinden muss. **Ohne** `AIRequestContext` müsstest du die System-Nachricht direkt ändern:

```csharp
// ❌ Ohne AIRequestContext — System-Nachricht verschmutzen
var originalSystem = service.SystemMessage;

service.SystemMessage = originalSystem +
    $"\n\nBenutze den folgenden Kontext zum Antworten:\n{retrievedDocs}";

var answer = await service.GetCompletionAsync(userQuestion);

// Wiederherstellen — aber dieser Kontext steckt jetzt auch im Gesprächsverlauf
service.SystemMessage = originalSystem;
```

Probleme mit diesem Ansatz:

- Der abgerufene Kontext **leckt in den Gesprächsverlauf** — zukünftige Anfragen sehen ihn noch
- Das Wiederherstellen der System-Nachricht macht die Verlaufsverschmutzung nicht rückgängig
- In einer Multi-User-Web-App verursacht das Mutieren gemeinsamer Zustände Race Conditions

**Mit** `AIRequestContext` ist die Injektion auf genau eine Anfrage beschränkt:

```csharp
// ✅ Mit AIRequestContext — sauber, begrenzt, keine Nebeneffekte
var answer = await service.GetCompletionAsync(userQuestion,
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\n\nBenutze den folgenden Kontext zum Antworten:\n{retrievedDocs}"
    });
```

Die System-Nachricht wird nur für diesen einen Aufruf geändert. Die nächste Anfrage sieht die ursprüngliche System-Nachricht. Kein Aufräumen nötig.

## Verfügbare Eigenschaften

### SystemMessagePrefix

Fügt der System-Nachricht Text vorangestellt für diese Anfrage hinzu:

```csharp
var context = new AIRequestContext
{
    SystemMessagePrefix = "Das heutige Datum ist 2026-03-31.\n"
};

var response = await service.GetCompletionAsync("Welcher Tag ist heute?", context: context);
```

**Wann verwenden:** Dynamische Metadaten injizieren (Datum, Nutzerzeitzone, Sitzungsinfo), die sich pro Anfrage ändern.

### SystemMessageSuffix

Hängt Text an die System-Nachricht für diese Anfrage an:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nAntworte immer auf Koreanisch."
};

var response = await service.GetCompletionAsync("Hallo!", context: context);
```

**Wann verwenden:** Pro-Anfrage-Verhaltensanweisungen, RAG-Kontext oder Sprachpräferenzen hinzufügen.

### AdditionalMessages

Fügt für diese Anfrage zusätzliche Nachrichten ins Gespräch ein — nützlich zum Injizieren von Referenzdokumenten oder Few-Shot-Beispielen:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.Create().AddText("Referenzdokument: Die Rückgaberichtlinie erlaubt Rücksendungen innerhalb von 30 Tagen.").Build()
    }
};

var response = await service.GetCompletionAsync("Habe ich Anspruch auf eine Rückerstattung?", context: context);
```

**Wann verwenden:** Referenzmaterial, Few-Shot-Beispiele oder Hilfskontext, der nicht im Gesprächsverlauf bleiben soll.

### RequestMessageOverride

Ersetzt die Nachricht des Benutzers für diese Anfrage vollständig. Der ursprüngliche Prompt wird ignoriert:

```csharp
var context = new AIRequestContext
{
    RequestMessageOverride = MessageBuilder
        .User($"Beantworte die Frage basierend auf folgendem Kontext.\n\nKontext: {docs}\n\nFrage: {userQuery}")
        .Build()
};

await service.GetCompletionAsync(userQuery, context: context);
```

**Wann verwenden:** Wenn eine Middleware-Schicht (RAG, Query-Umschreibung) den Prompt vor der Übermittlung ans Modell vollständig reformulieren muss, während die ursprüngliche Nutzereingabe im Gesprächsverlauf bleibt.

> **💡 Hinweis:** Bei Verwendung von `.WithRag()` nutzt die RAG-Pipeline diese Eigenschaft automatisch. Den vollständigen Ablauf findest du unter [Pipeline-Anpassung — Wie es intern funktioniert](rag-pipeline.md#wie-es-intern-funktioniert).

## Vorher vs. Nachher

### Szenario: RAG mit Datumseinspeisung und abgerufenem Kontext

**Ohne AIRequestContext:**

```csharp
// ❌ Unübersichtlich, zustandsbehaftet, fehleranfällig
var origSys = service.SystemMessage;
service.SystemMessage = origSys
    + $"\nHeute: {DateTime.Now:yyyy-MM-dd}"
    + $"\n\nKontext:\n{retrievedChunks}";

var fewShotIndex = service.ActivateChat.Messages.Count;
service.ActivateChat.Messages.Add(MessageBuilder.Create().AddText(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.ActivateChat.Messages.RemoveAt(fewShotIndex); // Few-Shot-Beispiel entfernen
```

**Mit AIRequestContext:**

```csharp
// ✅ Sauber, statuslos, keine Nebeneffekte
var answer = await service.GetCompletionAsync(userQuery,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"Heute: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nKontext:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText(fewShotExample).Build()
        }
    });
```

## Kombinieren mit AIRequestProfile

Beide können zusammen übergeben werden für maximale Kontrolle über eine einzelne Anfrage:

```csharp
var response = await service.GetCompletionAsync(
    prompt,
    profile: new AIRequestProfile { Temperature = 0.1f, Stateless = true },
    context: new AIRequestContext
    {
        SystemMessageSuffix = $"\nKontext:\n{docs}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.Create().AddText("Beispiel: ...").Build()
        }
    }
);
```

Weitere Details zu Generierungsparametern unter [AIRequestProfile](request-profiles.md).

## Automatische Injektion mit `SystemMessageProvider`

### Welches Problem löst es

Eine typische Chat-App hat mehrere LLM-Entry-Points, die alle dieselbe Baseline benötigen — heutiges Datum, aktiver Ordner, Session-Info. **Ohne** `SystemMessageProvider` muss jede einzelne Aufrufstelle daran denken, diesen Context zu bauen und zu übergeben:

```csharp
// ❌ Ohne SystemMessageProvider — jeder Entry Point muss an die Injektion denken
var today = $"Today is {DateTime.UtcNow:yyyy-MM-dd}.";

// 1. Haupt-Chat-Antwort
var answer = await service.GetCompletionAsync(userMessage,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 2. Titelgenerator (später hinzugefügt)
var title = await service.GetCompletionAsync("Summarize as a title: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 3. Summarizer (noch später hinzugefügt)
var summary = await service.GetCompletionAsync("Summarize: " + conversation,
    context: new AIRequestContext { SystemMessageSuffix = today });

// 4. Agent-Aufruf — leicht zu vergessen! Der Compiler warnt nicht
var agentResult = await service.RunAgentAsync(goal);  // ← Datum fehlt, stiller Bug
```

Probleme dieses Ansatzes:

- Derselbe Context-Build-Snippet ist an jeder Aufrufstelle **dupliziert**
- Neue Entry Points (der `RunAgentAsync` oben) werden **leicht übersehen** — keine Compile-Time-Prüfung
- Jedes neue Feature, das einen LLM-Aufruf hinzufügt, muss sich an die Konvention erinnern
- Tests müssen das Context-Setup an jeder Aufrufstelle replizieren

Mit `SystemMessageProvider` registrierst du die Baseline **einmal**, und jeder ausgehende Aufruf holt sie automatisch ab:

```csharp
// ✅ Mit SystemMessageProvider — einmal registrieren, überall angewendet
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}."
});

// All diese erhalten automatisch die Baseline — kein Per-Call-Boilerplate
var answer      = await service.GetCompletionAsync(userMessage);
var title       = await service.GetCompletionAsync("Summarize as a title: " + conversation);
var summary     = await service.GetCompletionAsync("Summarize: " + conversation);
var agentResult = await service.RunAgentAsync(goal);  // ← bekommt auch die Baseline

// Streaming-Entry-Points genauso — gleiche Baseline, kein Per-Call-Boilerplate
await foreach (var chunk in service.StreamAsync(userMessage)) { /* ... */ }
await foreach (var token in service.RunAgentStreamAsync(goal)) { /* ... */ }
```

### Wie es funktioniert

Registriere den Callback einmal über den `WithSystemMessageProvider` fluent Helper. Jeder ausgehende Aufruf (`GetCompletionAsync`, `StreamAsync`, `RunAgentAsync`, `RunAgentStreamAsync`) ruft ihn automatisch auf, um einen Basis-Context zu erstellen:

```csharp
// Typischerweise bei Service-Konstruktion / DI-Setup
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix =
        $"Today is {DateTime.UtcNow:yyyy-MM-dd}.\n" +
        $"Current folder: {_uiContext.CurrentFolder}"
});

var answer = await service.GetCompletionAsync(userQuery);
await foreach (var chunk in service.StreamAsync(msg, options)) { /* ... */ }
var agentResult = await service.RunAgentAsync(goal);
```

### Async-Overload für IO-gestützte Provider

Wenn der Basis-Context aus einer Datenbank, einem Cache oder einem HTTP-Aufruf stammt, verwende den Async-Overload, damit der Provider nicht mit `.Result` / `.GetAwaiter().GetResult()` blockieren muss. Die Overload-Auflösung wählt anhand der Lambda-Arity automatisch den richtigen — kein Argument für sync, ein `CancellationToken` für async:

```csharp
service.WithSystemMessageProvider(async ct =>
{
    var prefs = await _db.UserPreferences.FirstOrDefaultAsync(ct);
    return new AIRequestContext
    {
        SystemMessageSuffix = $"User language: {prefs?.Language ?? "en"}"
    };
});
```

Nicht-Streaming-Pfade (`GetCompletionAsync`, `RunAgentAsync`) unterstützen bewusst keine Cancellation — ihre Signaturen akzeptieren keinen `CancellationToken`, und an den Provider wird immer `CancellationToken.None` übergeben. Wenn dein Provider Cancellation benötigt (z. B. eine langlaufende DB-Abfrage), verwende die Streaming-Pfade (`StreamAsync`, `RunAgentStreamAsync`), die das Token des Aufrufers bis zum Provider-Callback durchreichen.

### Merging mit einem expliziten per-call-Context

Wenn ein Aufruf einen registrierten Provider **und** auch einen expliziten `AIRequestContext` übergibt, werden beide feldweise zusammengeführt:

| Feld | Merge-Regel |
|---|---|
| `SystemMessagePrefix` | expliziter Wert gewinnt, wenn non-null, sonst Provider |
| `SystemMessageSuffix` | expliziter Wert gewinnt, wenn non-null, sonst Provider |
| `RequestMessageOverride` | expliziter Wert gewinnt, wenn non-null, sonst Provider |
| `AdditionalMessages` | verkettet (Provider zuerst, dann explizit) |

Begründung: der häufige Fall ist „Provider liefert einen Baseline, ein spezifischer Aufruf will ein Skalarfeld ersetzen oder zusätzliche Nachrichten anhängen" — Feld-Override hält die Semantik vorhersagbar ohne überraschende Verkettung.

### Per-Call-Invocation

Der Provider wird **einmal pro Request** aufgerufen, so dass Rückgabewerte den aktuellen Zustand (Zeitstempel, Session etc.) widerspiegeln können. `null` zurückzugeben ist ein No-Op — identisch zum Nicht-Setzen von `SystemMessageProvider` für diesen Aufruf.

### Zusammenfassung: Wann dieses Werkzeug einsetzen — die Schnittmenge dreier Bedingungen

Tritt man einen Schritt von den obigen Beispielen und Merge-Regeln zurück, ist `SystemMessageProvider` das dedizierte Werkzeug, wenn **drei Bedingungen gleichzeitig zutreffen**:

1. **Eine Baseline muss bei jedem LLM-Aufruf vorhanden sein** — man möchte sich nicht an jedem Einstiegspunkt an die manuelle Injektion erinnern
2. **Der Wert muss zum Aufrufzeitpunkt dynamisch ausgewertet werden** — aktuelle Zeit, aktiver Ordner, angemeldeter Benutzer und weitere Werte, die beim Start nicht fixiert werden können
3. **Der permanente Zustand (`SystemMessage`, Konversationsverlauf) darf nicht kontaminiert werden** — der Wert darf nicht in nachfolgende Aufrufe durchsickern

Fehlt eine der drei Bedingungen, ist ein einfacheres Werkzeug die richtige Wahl:

| Situation | Richtiges Werkzeug | Grund |
|---|---|---|
| Baseline ist über die gesamte Session **fixiert (unveränderlich)** | `service.SystemMessage = "..."` | Einmalige Zuweisung reicht, kein Provider nötig |
| **Nur ein einzelner Aufruf** braucht Sonderbehandlung | `AIRequestContext` explizit an der Aufrufstelle übergeben | Keine gemeinsame Baseline — eine einmalige Injektion |
| Gemeinsam + dynamisch + kontaminationsfrei **(alle drei)** | **`SystemMessageProvider`** | Das dedizierte Werkzeug für diese Dreier-Schnittmenge |

#### Warum dies nicht im Widerspruch zum „Einmaligkeits"-Prinzip von `AIRequestContext` steht

Die Essenz von `AIRequestContext` ist nicht „nur einmal verwendet", sondern **„kontaminiert niemals den permanenten Zustand"**. `SystemMessageProvider` ist eine Factory, die den Callback **bei jeder Anfrage erneut ausführt** und dabei **einen brandneuen, auf diese Anfrage beschränkten `AIRequestContext` erzeugt**. Der resultierende Kontext ist weiterhin per-request scoped, der Wert sickert nie in den Konversationsverlauf durch, und beim nächsten Aufruf wird der Callback erneut ausgeführt, um den **dann aktuellen** Wert widerzuspiegeln. Der Provider verletzt also nicht das Designprinzip von `AIRequestContext` — er **automatisiert es lediglich**.

Konkret: Die untenstehende Registrierung verändert `service.SystemMessage` und `service.ActivateChat.Messages` **nicht**:

```csharp
service.WithSystemMessageProvider(() => new AIRequestContext
{
    SystemMessageSuffix = $"Today is {DateTime.UtcNow:yyyy-MM-dd}"
});
```

- Sobald Mitternacht überschritten ist, spiegelt die Provider-Neuausführung beim nächsten Aufruf automatisch das **neue Datum** wider (nicht statisch)
- Eine Woche später zeigt der Konversationsverlauf in vergangenen Anfragen kein eingebettetes „Today is ..."
- Auch wenn ein gemeinsamer Service in einer Mehrbenutzer-Umgebung verwendet wird, erzeugt jeder Aufruf seinen eigenen unabhängigen Kontext

> Verfügbar in Mythosia.AI v6.3.0+.
