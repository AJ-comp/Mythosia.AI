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
    new AIRequestContext
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

var response = await service.GetCompletionAsync("Welcher Tag ist heute?", context);
```

**Wann verwenden:** Dynamische Metadaten injizieren (Datum, Nutzerzeitzone, Sitzungsinfo), die sich pro Anfrage ändern.

### SystemMessageSuffix

Hängt Text an die System-Nachricht für diese Anfrage an:

```csharp
var context = new AIRequestContext
{
    SystemMessageSuffix = "\nAntworte immer auf Koreanisch."
};

var response = await service.GetCompletionAsync("Hallo!", context);
```

**Wann verwenden:** Pro-Anfrage-Verhaltensanweisungen, RAG-Kontext oder Sprachpräferenzen hinzufügen.

### AdditionalMessages

Fügt für diese Anfrage zusätzliche Nachrichten ins Gespräch ein — nützlich zum Injizieren von Referenzdokumenten oder Few-Shot-Beispielen:

```csharp
var context = new AIRequestContext
{
    AdditionalMessages = new List<Message>
    {
        MessageBuilder.User("Referenzdokument: Die Rückgaberichtlinie erlaubt Rücksendungen innerhalb von 30 Tagen.").Build()
    }
};

var response = await service.GetCompletionAsync("Habe ich Anspruch auf eine Rückerstattung?", context);
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

await service.GetCompletionAsync(userQuery, context);
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

service.Messages.Add(MessageBuilder.User(fewShotExample).Build());

var answer = await service.GetCompletionAsync(userQuery);

service.SystemMessage = origSys;
service.Messages.RemoveAt(service.Messages.Count - 2); // Few-Shot-Beispiel entfernen
```

**Mit AIRequestContext:**

```csharp
// ✅ Sauber, statuslos, keine Nebeneffekte
var answer = await service.GetCompletionAsync(userQuery,
    new AIRequestContext
    {
        SystemMessagePrefix = $"Heute: {DateTime.Now:yyyy-MM-dd}\n",
        SystemMessageSuffix = $"\n\nKontext:\n{retrievedChunks}",
        AdditionalMessages = new List<Message>
        {
            MessageBuilder.User(fewShotExample).Build()
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
            MessageBuilder.User("Beispiel: ...").Build()
        }
    }
);
```

Weitere Details zu Generierungsparametern unter [AIRequestProfile](request-profiles.md).
