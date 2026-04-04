# Gesprächsverwaltung

## Wie der Gesprächsverlauf funktioniert

Jeder Aufruf von `GetCompletionAsync` oder `StreamAsync` fügt der internen Nachrichtenliste des Services Nachrichten hinzu. Das bedeutet, das Modell hat Kontext aus allen vorherigen Gesprächsrunden.

```csharp
await service.GetCompletionAsync("Meine Lieblingsfarbe ist Blau.");
var reply = await service.GetCompletionAsync("Was ist meine Lieblingsfarbe?");
// → "Deine Lieblingsfarbe ist Blau."
```

Für einen Neustart:

```csharp
service.ClearMessages();
```

## Zusammenfassungs-Policy

### Warum automatische Zusammenfassung?

Jede Nachricht im Gesprächsverlauf wird bei jeder Anfrage ans Modell gesendet. Bei langen Gesprächen entstehen zwei Probleme:

1. **Kosten** — längere Verläufe bedeuten mehr abgerechnete Eingabe-Tokens pro Anfrage
2. **Kontext-Überlauf** — sobald der Verlauf das Kontextfenster des Modells überschreitet (z. B. 128K Tokens bei GPT-4o), schlagen Anfragen komplett fehl

Alte Nachrichten manuell abzuschneiden verliert Kontext, den das Modell noch benötigen könnte. **`SummaryConversationPolicy`** löst das, indem ältere Nachrichten automatisch zu einer kompakten Zusammenfassung verdichtet werden, während aktuelle Nachrichten wortgetreu erhalten bleiben — das Modell behält den Kerninhalt des gesamten Gesprächs ohne die Token-Kosten.

### Auslösung per Nachrichtenanzahl

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,   // Zusammenfassen, wenn Verlauf 20 Nachrichten überschreitet
    keepRecentCount: 5  // Die 5 aktuellsten Nachrichten wortgetreu behalten
);
```

### Auslösung per Token-Anzahl

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,    // Zusammenfassen, wenn Token-Nutzung 3000 überschreitet
    keepRecentTokens: 1000  // Aktuelle Nachrichten bis 1000 Tokens behalten
);
```

### Auslösung durch beides (ODER-Bedingung)

Zusammenfassung auslösen, wenn **entweder** das Token-Limit oder die Nachrichtenanzahl überschritten wird:

```csharp
service.ConversationPolicy = SummaryConversationPolicy.ByBoth(
    triggerTokens: 4000,
    triggerCount: 30,
    keepRecentTokens: 1300,  // optional, Standard: triggerTokens / 3
    keepRecentCount: 7       // optional, Standard: triggerCount / 4
);
```

Nach dem Einrichten läuft die Zusammenfassung automatisch bei `GetCompletionAsync` ab. Keine weiteren Änderungen nötig.

### So funktioniert es

1. Vor jeder Vervollständigung prüft die Policy, ob das Gespräch den konfigurierten Schwellenwert überschreitet.
2. Bei Auslösung werden ältere Nachrichten per statuslosem LLM-Aufruf zu einem knappen Text zusammengefasst.
3. Die Zusammenfassung wird als System-Nachrichten-Präfix eingefügt — das Modell sieht sie als Vorkontext.
4. Aktuelle Nachrichten (gesteuert durch `KeepRecentCount` oder `KeepRecentTokens`) bleiben wortgetreu erhalten.

Bei Token-basierten Auslösern verwendet die Policy automatisch die **tatsächliche Eingabe-Token-Anzahl** aus der letzten Streaming-Antwort statt lokaler Schätzung.

### Streaming

Die Zusammenfassung wird während `StreamAsync` nicht automatisch ausgelöst. Explizit vorher aufrufen:

```csharp
await service.ApplySummaryPolicyIfNeededAsync();

await foreach (var chunk in service.StreamAsync("Lass uns unser Gespräch fortsetzen..."))
    Console.Write(chunk.Content);
```

## Zusammenfassung speichern und wiederherstellen

Die Zusammenfassung sitzungsübergreifend persistieren, damit das Modell nach einem Neustart den Kontext beibehält:

```csharp
// Speichern
string saved = service.ConversationPolicy.CurrentSummary;
// → in Datenbank, Datei etc. speichern

// In einer neuen Sitzung wiederherstellen
service.ConversationPolicy.LoadSummary(saved);
```
