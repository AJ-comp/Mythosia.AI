# Laufende KI-Aufgaben mit Run steuern

> Diese APIs benötigen `Mythosia.AI` ab 7.1.0, einschließlich `Mythosia.AI.Abstractions` ab 3.1.0. RAG-Beispiele benötigen `Mythosia.AI.Rag` ab 7.6.0.

## Warum eine laufende Aufgabe steuern?

Ein Bericht kann mehrere Dokumentensuchen, API-Aufrufe und Schreibschritte erfordern. Währenddessen möchte ein Benutzer vielleicht den Fortschritt sehen, die Arbeit stoppen oder eine Anforderung wie „Nur Daten aus diesem Jahr berücksichtigen“ ergänzen. Die Anwendung muss diese Aktionen der bereits laufenden Aufgabe zuordnen können.

Run liefert dafür ein Objekt, das die Anwendung aufbewahren kann. Eine Chatoberfläche kann eingehenden Text anzeigen, Werkzeugaufrufe sichtbar machen, eine Stoppschaltfläche mit dem Abbruch verbinden und bei unterstützten Modellen zusätzliche Anweisungen senden. Alle Aktionen beziehen sich auf dieselbe Ausführung.

| Was die Anwendung benötigt | Passende Verwendung |
| --- | --- |
| Eine fertige Antwort empfangen, ohne laufende Arbeit zu steuern | Weiterhin `GetCompletionAsync` verwenden, einschließlich typisierter und RAG-Überladungen. |
| Text sofort anzeigen und nach Abschluss gesammelt erhalten | Einen Run mit `onText` starten und anschließend `run.Result` abwarten. |
| Werkzeugaktivität anzeigen oder asynchrone Ausgabe abwarten | Ereignisse aus `run.StreamAsync()` lesen. |
| Dem Benutzer das Stoppen der Arbeit ermöglichen | `run.Cancel()` auf dem aufbewahrten Objekt aufrufen. |
| Vor Abschluss eine Anforderung ergänzen | `run.CanSteer` prüfen und bei einem unterstützten Modell `run.SteerAsync(...)` verwenden. |

`StartRunAsync` startet eine Modellaufgabe und gibt einen `AIRun` zurück. Die Aufgabe läuft unabhängig davon weiter, ob ihre Ausgabe gelesen wird. Dasselbe Objekt dient für Streaming, das gesammelte Ergebnis, Abbruch und unterstützte zusätzliche Anweisungen während der Ausführung. `GetCompletionAsync` bleibt einschließlich typisierter und RAG-Überladungen eine öffentliche Hilfs-API für Aufrufer, die das abgeschlossene Ergebnis benötigen.

## Text über einen Callback anzeigen

In einer Chatoberfläche oder Konsole kann der Benutzer eine längere Antwort schon während ihrer Entstehung verfolgen, wenn der erste Text sofort angezeigt wird.

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Lies die Dokumente und schreibe einen Bericht.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

string answer = await run.Result;
```

`onText` ist ein optionaler `Action<string>`-Callback, der vor dem Start registriert wird. Er empfängt Text in der richtigen Reihenfolge und führt keine Werkzeuge aus. Wenn nur das Ergebnis benötigt wird, kann er entfallen. Eine Ausnahme im Callback bricht den Run ab und lässt `Result` fehlschlagen. Übergib keine `async`-Lambda an `onText`: Daraus würde `async void`, dessen Arbeit und Fehler der Run nicht abwarten kann. Verwende für asynchrone Ausgabe den Ereignisstream. Callbacks werden nicht automatisch auf den UI-Thread übertragen.

`Result` verbindet alle Textereignisse des Runs, einschließlich Zwischentext zwischen Werkzeugaufrufen und Text vor einer zusätzlichen Anweisung. Es ist weder eine zweite Modellanfrage noch eine neu formulierte Antwort. Wer das Ergebnis erst nach Abschluss benötigt und die bisherige Ergebnissemantik bevorzugt, kann weiterhin `GetCompletionAsync` verwenden.

## Text-, Werkzeug- und Nutzungsereignisse lesen

Wenn eine Aufgabe Dokumente durchsucht oder eine Geschäfts-API aufruft, erklärt Text allein möglicherweise nicht die Wartezeit. Typisierte Ereignisse ermöglichen es, Werkzeugaktivität neben der Antwort anzuzeigen und die vom Anbieter gelieferten Nutzungsdaten zu erfassen.

```csharp
await using var run = await service.StartRunAsync(
    "Durchsuche die Dokumente und erkläre das Ergebnis.",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[Werkzeug wird aufgerufen]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[Werkzeugergebnis empfangen]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[Token insgesamt: {item.Usage.TotalTokens}]");
            break;
    }
}

string answer = await run.Result;
```

`run.StreamAsync()` akzeptiert ein optionales Abbruchtoken für die Beobachtung, aber keinen Prompt. Es beobachtet die bereits von `StartRunAsync` gestartete Aufgabe. Registrierte Funktionshandler werden innerhalb der Bibliothek ausgeführt; führe ein Werkzeug deshalb niemals als Reaktion auf sein Anzeigeereignis erneut aus. Optionen für die Textanzeige deaktivieren die registrierten Werkzeuge des Runs nicht.

Der beim Start registrierte Callback und `run.StreamAsync()` können denselben Run gemeinsam beobachten; der Ereignisstream unterstützt einen Leser. Beispielsweise kann `onText` den Text anzeigen, während der Stream nur Werkzeugereignisse verarbeitet, damit Text nicht doppelt erscheint. Auch mit einem Callback werden höchstens 1.024 ungelesene Ereignisse gepuffert. Ein später gestarteter Leser empfängt die Ereignisse von Anfang an, solange sie in den Puffer passen. Bei Überschreitung des Limits schlägt die Streambeobachtung ausdrücklich fehl, während Callback, Ausführung und `Result` weiterlaufen. Der Stream ist kein unbegrenztes Wiedergabeprotokoll. Zum Abwarten von `Result` muss der Ereignisstream nicht vollständig gelesen werden.

## Asynchrone Ausgabe

Warte bei asynchroner Ausgabe die Operation innerhalb des Lesers ab, statt einen asynchronen `onText`-Callback zu verwenden:

```csharp
using var writer = new StreamWriter("bericht.txt");
await using var run = await service.StartRunAsync(
    "Schreibe einen Bericht.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = await run.Result;
```

## Abbrechen und Ressourcen freigeben

- Das Verlassen von `await foreach` oder das Abbrechen eines nur an `run.StreamAsync(token)` übergebenen Tokens beendet die Beobachtung; die Aufgabe läuft weiter.
- `run.Cancel()`, das an `StartRunAsync` übergebene Token und das Freigeben eines aktiven Runs brechen die Ausführung ab.
- `await using` sorgt dafür, dass `DisposeAsync()` den Produzenten und die Bereinigung beim Anbieter abwartet. Werkzeuge ohne Abbruchunterstützung können noch Zeit zum Abschluss benötigen; die Freigabe macht abgeschlossene Aktionen nicht rückgängig.
- Ein Service erlaubt eine aktive `StartRunAsync`-Aufgabe. Ein überlappender Start wird abgelehnt. Verwende für unabhängige parallele Aufgaben getrennte Services und mische während eines aktiven Runs keine bisherigen Aufrufe hinein oder ändere Serviceeinstellungen.

Der Run erfasst Eingabe und anstehende Richtlinie für diese Anfrage vor der Hintergrundausführung. Integrierte Text-, Bild- und Audioinhalte sowie Medien-Bytearrays werden kopiert. Benutzerdefinierte Unterklassen von `MessageContent` behalten ihre Objektidentität und dürfen bis zum Ende des Runs nicht verändert werden.

Der erfasste Wert von `FunctionCallingPolicy.TimeoutSeconds` setzt eine gemeinsame Frist für die Vorbereitung und sämtliche Modell-/Werkzeugrunden. Bei Ablauf wird `AIServiceException` gemeldet; ein Benutzerabbruch versetzt das Ergebnis in den abgebrochenen Zustand. Die Bereinigung wartet weiterhin auf Handler ohne Abbruchunterstützung.

## Während der Arbeit eine weitere Anweisung senden

Angenommen, ein Benutzer startet einen Projektplan und bemerkt anschließend, dass er in einen Zweiwochenzeitraum passen muss. Mit zusätzlichen Anweisungen kann die Anwendung diese neue Vorgabe senden, während das Modell noch arbeitet. Das ist für Korrekturen und Änderungen des Umfangs während längerer Aufgaben hilfreich. Für eine neue Frage nach Abschluss wird regulär die nächste Anfrage gestartet.

```csharp
await using var run = await service.StartRunAsync(
    "Entwirf einen Projektplan.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// Während des aktiven Runs aus dem UI-Handler für zusätzliche Anweisungen aufrufen.
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("Dieser Run unterstützt keine zusätzlichen Anweisungen.");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = await run.Result;
```

Zusätzliche Anweisungen während einer Antwort werden für GPT-6 Astra über die Responses-WebSocket-Verbindung unterstützt. Andere Anbieter und nicht unterstützte Modelle können normale Runs verwenden, aber `CanSteer` ist false und ein Steuerungsaufruf meldet fehlende Unterstützung, statt stillschweigend eine gewöhnliche nächste Gesprächsrunde zu starten. `CanSteer` garantiert nicht, dass der Run bei einem späteren Aufruf noch aktiv ist.

Astra-Runs öffnen einen eigenen Socket. Der übergebene `HttpClient` und seine Nachrichtenhandler bedienen weiterhin HTTP-Aufrufe und fangen diesen Socket nicht ab. Für eigene Transporte kann `OpenAIService.ConnectRunWebSocketAsync` überschrieben werden.

Ein erfolgreicher Aufruf von `SteerAsync` bedeutet, dass der Server die Eingabe in seine Warteschlange aufgenommen hat; das Modell muss sie noch nicht angewendet haben. Beobachte denselben Run auch während der Fortsetzung oder warte sein Ergebnis ab. Bereits ausgegebener Text und abgeschlossene Aktionen werden nicht rückgängig gemacht. Gestartete Werkzeuge werden nicht allein durch das Senden einer zusätzlichen Anweisung abgebrochen. Die Bibliothek verarbeitet Fortsetzung und Zuordnung der Werkzeugergebnisse auf derselben Verbindung. Siehe OpenAIs [Anleitung für zusätzliche Anweisungen](https://developers.openai.com/api/docs/guides/steering) und [WebSocket-Modus](https://developers.openai.com/api/docs/guides/websocket-mode). Gehe bei einem Verbindungsabbruch nicht davon aus, dass verbindungsbezogene Eingaben in der Warteschlange erhalten bleiben, und sende eine angenommene Anweisung nicht ungeprüft erneut.

## Werkzeugaufgaben und bisherige Agent-Methoden

Fragen wie „Prüfe die Rückerstattungsrichtlinie und den Status dieser Bestellung“ erfordern mehrere Quellen. Registriere Werkzeuge für die Dokumentensuche und Bestellabfrage und überlasse dem Modell die Auswahl der notwendigen Aufrufe. Ein Rundenlimit begrenzt, wie lange es weitere Werkzeuge anfordern kann, bevor es abschließen oder einen Fehler melden muss.

Normale Funktionsaufrufe unterstützen bereits wiederholte Modell-/Werkzeugrunden. `StartRunAsync` verwendet dieselben registrierten Funktionen und Ausführungsrichtlinien. Ein eigener Agent-Modus, Planer oder `WithAgentic`-Schalter ist nicht erforderlich.

`RunAgentAsync` und `RunAgentStreamAsync` bleiben aufrufbar, zeigen jedoch jetzt `[Obsolete]`-Warnungen. Bestehende Signaturen, der Standardwert `maxSteps = 10` und das bisherige Fehlerverhalten beim Schrittlimit bleiben während der Migration erhalten. Verwende für neue Aufrufstellen:

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "Finde die Richtlinie, prüfe die Bestellung und erkläre das Ergebnis.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

Der allgemeine Standardwert von `FunctionCallingPolicy.MaxRounds` ist 20. Gib deshalb 10 an, wenn das bisherige Agent-Limit beibehalten werden soll. `WithMaxRounds` setzt eine Richtlinienüberschreibung für eine Anfrage und ändert `DefaultPolicy` nicht. Konfiguriere sie vor dem Start. Die bisherigen Agent-Methoden kopieren dagegen die aktuelle Standardrichtlinie und wenden ihr aufrufspezifisches `maxSteps` an. Neue Runs verwenden den gemeinsamen Vertrag für Ausführungsfehler und garantieren nicht die bisherige Übersetzung in `AgentMaxStepsExceededException`/`PartialResponse`. Wenn diese Fehlersemantik benötigt wird, behalte den bisherigen Aufruf bei, bis auch seine Ausnahmebehandlung migriert ist.

## RAG, MCP und Paketgrenzen

- `RagEnabledService.StartRunAsync` unterstützt Eingaben als Zeichenfolge oder `Message`, `onText`, anfragespezifische `RagQueryOptions`, `streamOptions` und Abbruch. Die Suche erfolgt vor dem zugrunde liegenden Run. Bild-/Audioinhalte und Metadaten sowie die ursprüngliche Eingabe im Gesprächsverlauf bleiben erhalten; der angereicherte Text wird über den Anfragekontext gesendet. Diese Anreicherung ist an die ursprüngliche Benutzerfrage gebunden, sodass spätere Werkzeugergebnisse und zusätzliche Anweisungen nicht durch den ursprünglichen RAG-Prompt ersetzt werden. Zusätzliche Anweisungen an den zurückgegebenen Run aktualisieren das Modell, führen die RAG-Suche aber nicht automatisch erneut aus.
- `WithAgenticRag` registriert weiterhin ein Suchwerkzeug. Wird es über `StartRunAsync` verwendet, kann das Modell bei Bedarf weitere Suchen anfordern. Die MCP-Registrierung mit `WithMcpServerAsync` bleibt ebenfalls unverändert. Gemeinsam verwendete MCP-Verbindungen werden getrennt von den Runs freigegeben, die sie nutzen.
- `IAIRunService` ist eine optionale Fähigkeit in `Mythosia.AI.Abstractions`; `IAIService` erhält keine neuen Pflichtmitglieder. Ein eigener Service muss `IAIRunService` implementieren, um den Start von RAG-Runs zu unterstützen. Nicht unterstützte Services werden vor Beginn der RAG-Indizierung abgelehnt.
- RAG behält seine Abhängigkeit von Abstractions. Separat paketierte Anbieter behalten ihre öffentlichen Completion-Überschreibungen und zugänglichen Anbieter-Erweiterungspunkte. Diese Änderung verwirft keine API für Vektorspeicher, Dokumentenlader oder Serververwaltung.

Um vor dem Start Reasoning, Web- oder Dateisuche festzulegen und Quellen des Runs anzuzeigen, siehe [Reasoning und Antworten mit Quellen](reasoning-and-search.md).

## Kompatibilität und die nächste Hauptversion

| API | Aktueller Status |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Öffentlich und unterstützt, einschließlich Schnittstellen-, Anbieter- und RAG-Varianten. |
| `StartRunAsync` / `AIRun` | Gemeinsame API für Ausführung und Steuerung. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Obsolete-Warnungen; das bisherige Verhalten bleibt zur Kompatibilität erhalten. |
| Eingaben annehmendes `service.StreamAsync` und RAG-`StreamAsync` | In dieser Minor-Version weiterhin aufrufbar; öffentliche Entfernung für die nächste Hauptversion geplant. |
| `run.StreamAsync()` | Beobachtet die Ausgabe einer bestehenden Aufgabe ohne neue Anfrageeingabe. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | Die bisherige typisierte Streaming-API bleibt bestehen; ihr reines Ausgabe-`Stream()` ist keine alte Anfrage-Methode des Services. |

Die nächste Hauptversion ändert die öffentlichen Streaming-Einstiegspunkte und bewahrt die Ausführung sowie notwendige Anbieter-Erweiterungspunkte. Eine öffentliche Methode private oder protected zu machen, bricht weiterhin Quell- und Binärkompatibilität, selbst wenn ihr Methodenrumpf erhalten bleibt. Hilfen wie Nachrichtenketten, Einzelaufrufe, Zusammenfassungen, Anfrageumschreibung und Neusortierung gelten nicht allein deshalb als veraltet, weil sie bestehende Ausführungsmethoden verwenden.
