# Laufende KI-Aufgaben mit Run steuern

> GPT-6 Sol/Luna sind noch nicht veröffentlicht. Siehe [Modellwahl und Voraussetzungen](providers.md#gpt-6-sol-luna).

Für eine fertige Antwort mit Stoppschaltfläche übergeben Sie `cancellationToken` an `GetCompletionAsync`. Run dient Fortschrittsereignissen oder unterstützten zusätzlichen Anweisungen. Siehe [Completion-Abbruch](completions.md#completion-cancellation).

Für unabhängige Einstellungen und wiederverwendbare Varianten verwenden Sie den [Anfrage-Builder](request-building.md). Rufen Sie `CreateRequest(...)` vor `With...` auf. Service-Eigenschaften und dessen Fluent-Methoden behalten ihr bisheriges Verhalten.

> Für die fertige Antwort mit Verbrauch und Quellen liefert `await run.Result` eine `AIRunResult`-Momentaufnahme. Die Zeichenfolge steht in `result.Text`; ein Stream-Leser ist unnötig. Diese API-Änderung gehört zu Mythosia.AI 8.0.0. Die Rückgabetypen von `GetCompletionAsync` und `StructuredStreamRun<T>.Result` bleiben erhalten. [Run-Ergebnis und Migration](#run-result).

> Die `CreateRequest`-Beispiele benötigen Mythosia.AI 8.0.0 / Abstractions 4.0.0. Die frühere Version 7.1 mit Run und gemeinsamen Anfrageoptionen enthält den Builder noch nicht. Ältere Pakete können ihre bisherigen Service-Überladungen verwenden.

Für zeitkritische Anfragen wählen Sie die [Verarbeitungsgeschwindigkeit](request-building.md#inference-speed). `WithSpeed` behält Modell und Denkaufwand bei; `Processing` meldet den tatsächlich verwendeten Modus. Fast ist für unterstützte Kombinationen kostenpflichtig.

## Warum eine laufende Aufgabe steuern?

Ein Bericht kann mehrere Dokumentensuchen, API-Aufrufe und Schreibschritte erfordern. Währenddessen möchte ein Benutzer vielleicht den Fortschritt sehen, die Arbeit stoppen oder eine Anforderung wie „Nur Daten aus diesem Jahr berücksichtigen“ ergänzen. Die Anwendung muss diese Aktionen der bereits laufenden Aufgabe zuordnen können.

Run liefert dafür ein Objekt, das die Anwendung aufbewahren kann. Eine Chatoberfläche kann eingehenden Text anzeigen, Werkzeugaufrufe sichtbar machen, eine Stoppschaltfläche mit dem Abbruch verbinden und bei unterstützten Modellen zusätzliche Anweisungen senden. Alle Aktionen beziehen sich auf dieselbe Ausführung.

| Was die Anwendung benötigt | Passende Verwendung |
| --- | --- |
| Fertige Antwort mit optionalem Abbruch erhalten | `GetCompletionAsync(..., cancellationToken: token)` |
| Text sofort anzeigen und nach Abschluss gesammelt erhalten | Einen Run mit `onText` starten und anschließend `run.Result` abwarten. |
| Werkzeugaktivität anzeigen oder asynchrone Ausgabe abwarten | Ereignisse aus `run.StreamAsync()` lesen. |
| Dem Benutzer das Stoppen der Arbeit ermöglichen | `run.Cancel()` auf dem aufbewahrten Objekt aufrufen. |
| Vor Abschluss eine Anforderung ergänzen | `run.CanSteer` prüfen und bei einem unterstützten Modell `run.SteerAsync(...)` verwenden. |

`StartRunAsync` startet eine Modellaufgabe und gibt einen `AIRun` zurück. Die Aufgabe läuft unabhängig davon weiter, ob ihre Ausgabe gelesen wird. Dasselbe Objekt dient für Streaming, das gesammelte Ergebnis, Abbruch und unterstützte zusätzliche Anweisungen während der Ausführung. `GetCompletionAsync` bleibt einschließlich typisierter und RAG-Überladungen eine öffentliche Hilfs-API für Aufrufer, die das abgeschlossene Ergebnis benötigen.

<a id="run-result"></a>

## Antwort, Verbrauch und Quellen gemeinsam erhalten

Auch wenn eine Oberfläche nur die fertige Antwort anzeigt, muss sie möglicherweise Tokenverbrauch und Quellen speichern. Bisher lieferte `run.Result` nur eine Zeichenfolge: Verbrauch musste aus Stream-Ereignissen gesammelt und Quellen separat gelesen werden. `AIRunResult` sammelt diese Informationen auch ohne Stream-Leser.

Before — bisheriger Run-Vertrag

```csharp
string answer = await run.Result;
```

After — Mythosia.AI 8.0.0

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Analysiere die Dokumente.")
    .StartRunAsync();

AIRunResult result = await run.Result;
string answer = result.Text;
int? totalTokens = result.Usage?.TotalTokens;
var citations = result.Citations;
string? requestedModel = result.RequestedModel;
string? actualModel = result.Model;
var finishReason = result.FinishReason;
```

Auch bei sofortiger Textanzeige erhalten Sie dasselbe Ergebnis. Callback, `run.StreamAsync()`, `SteerAsync` und Abbruch behalten ihre bisherigen Aufgaben.

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("Analysiere die Dokumente.")
    .StartRunAsync(onText: text => Console.Write(text));

AIRunResult result = await run.Result;
Console.WriteLine($"\nTokens: {result.Usage?.TotalTokens}");
```

Das fertige Ergebnis ist eine Momentaufnahme. `Usage` und Zitatobjekte werden kopiert; Änderungen daran verändern das gespeicherte Ergebnis nicht. Es bleibt nach der Freigabe verfügbar. Stream-Filter, ein gestoppter Leser oder ein übergelaufener Beobachtungspuffer entfernen keine Ergebnisdaten.

`Usage` summiert die gemeldeten Werte jeder Modellrunde einmal. Rundenereignisse und abschließende Summe werden nicht doppelt gezählt. Ohne Meldung ist der Wert `null`; fehlende Daten werden nicht geschätzt. Separate Hilfsanfragen zur Zusammenfassung fehlen darin, weshalb dies keine vollständige Rechnungs- oder Kontosumme ist. Melden nur manche Runden ihren Verbrauch, umfasst die Summe nur diese Runden und garantiert keine vollständige Erfassung.

Ein ausdrücklich gemeldeter Wert für `TotalTokens` bleibt auch bei unvollständiger Aufschlüsselung der Ein- und Ausgabetokens erhalten; die Rundensumme addiert diese gemeldeten Gesamtwerte.

Tokenzähler bleiben `Int32`. Übersteigt die Summe mehrerer Runden `Int32.MaxValue`, schlägt der Stream oder Run mit `OverflowException` fehl, statt übergelaufene Werte zurückzugeben. Die Bereinigung wird abgeschlossen; auch bei einem Fehler in der abschließenden Aggregation bleibt `run.Result` nicht offen.

Die von `run.StreamAsync()` zurückgegebene Sequenz kann nur einmal durchlaufen werden. Erneutes oder paralleles Durchlaufen derselben Sequenz löst `InvalidOperationException` aus; der ursprüngliche Leser und die Ausführung bleiben voneinander unabhängig.

`Provider` benennt den Adapter; `RequestedModel` ist das beim Start erfasste einzelne Modell, das ausdrücklich in der Anfrage gesendet wird, einschließlich einer anbieterspezifischen Modellüberschreibung. Es ist `null`, wenn Preset, Profil oder serverseitiges Routing ohne einzelnes explizites Modellfeld auswählen (etwa eine Perplexity-`Models`-Liste). Dies ist unabhängig vom tatsächlichen Antwortmodell in `Model`. `Model` ist die tatsächlich in der Antwort der letzten Runde gemeldete Modell-ID oder `null`; die angeforderte ID wird nicht ersatzweise verwendet. `RoundCount` zählt LLM-Runden der Bibliothek, keine einzelnen Tools oder internen Schritte gehosteter Agenten. Ohne Rundenangabe eines eigenen Providers gilt `0`.

`FinishReason` verwendet `AIFinishReason` (`Unknown`, `Stop`, `MaxTokens`, `ToolCalls`, `ContentFilter`, `Other`); `RawFinishReason` bewahrt den ursprünglichen Abschlusswert. Fehlende Angaben ergeben `Unknown`/`null`. Dies gilt für erfolgreiche Ergebnisse. Bestehende Fehler wie ein Rundenlimit lassen `Result` weiterhin fehlschlagen. Ein Benutzerabbruch wirft weiterhin `OperationCanceledException`, statt ein erfolgreiches Ersatzobjekt zu liefern.

`Text` enthält weiterhin sämtliche ausgegebenen Texte in Reihenfolge, einschließlich Zwischenantworten und Text vor einer zusätzlichen Anweisung. Das Ergebnis wartet auf Ausführung und Bereinigung. `Citations` enthält die fertigen Quellen; während der Ausführung bleibt `run.Citations` verfügbar. Zitatpositionen beziehen sich auf ursprüngliche Inhaltsteile, nicht auf den zusammengesetzten Text.

<a id="run-result-migration"></a>

**Migration zur Hauptversion:** `AIRun.Result` wechselt von `Task<string>` zu `Task<AIRunResult>`. Die Zeichenfolge erhalten Sie mit `(await run.Result).Text`. Eigene `AIRun`-Implementierungen müssen ihre Überschreibung anpassen und `AIRunResult` erzeugen; Verbraucher müssen neu kompilieren. Der Ergebnistyp liegt in `Mythosia.AI.Models.Runs`, `TokenUsage` und `AIFinishReason` in `Mythosia.AI.Models.Streaming`. `GetCompletionAsync` behält `Task<string>`, `StructuredStreamRun<T>.Result` behält `Task<T>`. Diese Beispiele benötigen Mythosia.AI 8.0.0, nicht die ursprünglichen Run-Pakete 7.1/3.1.

## Text über einen Callback anzeigen

In einer Chatoberfläche oder Konsole kann der Benutzer eine längere Antwort schon während ihrer Entstehung verfolgen, wenn der erste Text sofort angezeigt wird.

```csharp
await using var run = await service
    .CreateRequest("Lies die Dokumente und schreibe einen Bericht.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

Lokale Tools können Objekte über `Task<T>` / `ValueTask<T>` zurückgeben und ein injiziertes `CancellationToken` erhalten. `run.Cancel()` oder das Starttoken erreicht kooperative Tools; das Beenden des Stream-Lesers allein nicht. Ausnahmen gelten als Fehler. Bei Abbruch werden wartende Aufrufe übersprungen, und die Bereinigung wartet weiterhin auf gestartete Tools, die das Token ignorieren. Siehe [Ergebnisse, Fehler und Abbruch](function-calling.md#tool-execution-contract).

`onText` ist ein optionaler `Action<string>`-Callback, der vor dem Start registriert wird. Er empfängt Text in der richtigen Reihenfolge und führt keine Werkzeuge aus. Wenn nur das Ergebnis benötigt wird, kann er entfallen. Eine Ausnahme im Callback bricht den Run ab und lässt `Result` fehlschlagen. Übergib keine `async`-Lambda an `onText`: Daraus würde `async void`, dessen Arbeit und Fehler der Run nicht abwarten kann. Verwende für asynchrone Ausgabe den Ereignisstream. Callbacks werden nicht automatisch auf den UI-Thread übertragen.

`(await run.Result).Text` verbindet alle Textereignisse des Runs, einschließlich Zwischentext zwischen Werkzeugaufrufen und Text vor einer zusätzlichen Anweisung. Es ist weder eine zweite Modellanfrage noch eine neu formulierte Antwort. Wer das Ergebnis erst nach Abschluss benötigt und die bisherige Ergebnissemantik bevorzugt, kann weiterhin `GetCompletionAsync` verwenden.

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

string answer = (await run.Result).Text;
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
string answer = (await run.Result).Text;
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

string answer = (await run.Result).Text;
```

Zusätzliche Anweisungen während einer Antwort werden für GPT-6 Astra / Sol / Luna über die Responses-WebSocket-Verbindung unterstützt. Andere Anbieter und nicht unterstützte Modelle können normale Runs verwenden, aber `CanSteer` ist false und ein Steuerungsaufruf meldet fehlende Unterstützung, statt stillschweigend eine gewöhnliche nächste Gesprächsrunde zu starten. `CanSteer` garantiert nicht, dass der Run bei einem späteren Aufruf noch aktiv ist.

GPT-6-Runs öffnen einen eigenen Socket. Der übergebene `HttpClient` und seine Nachrichtenhandler bedienen weiterhin HTTP-Aufrufe und fangen diesen Socket nicht ab. Für eigene Transporte kann `OpenAIService.ConnectRunWebSocketAsync` überschrieben werden.

Ein erfolgreicher Aufruf von `SteerAsync` bedeutet, dass der Server die Eingabe in seine Warteschlange aufgenommen hat; das Modell muss sie noch nicht angewendet haben. Beobachte denselben Run auch während der Fortsetzung oder warte sein Ergebnis ab. Bereits ausgegebener Text und abgeschlossene Aktionen werden nicht rückgängig gemacht. Gestartete Werkzeuge werden nicht allein durch das Senden einer zusätzlichen Anweisung abgebrochen. Die Bibliothek verarbeitet Fortsetzung und Zuordnung der Werkzeugergebnisse auf derselben Verbindung. Siehe OpenAIs [Anleitung für zusätzliche Anweisungen](https://developers.openai.com/api/docs/guides/steering) und [WebSocket-Modus](https://developers.openai.com/api/docs/guides/websocket-mode). Gehe bei einem Verbindungsabbruch nicht davon aus, dass verbindungsbezogene Eingaben in der Warteschlange erhalten bleiben, und sende eine angenommene Anweisung nicht ungeprüft erneut.

## Werkzeugaufgaben und bisherige Agent-Methoden

Fragen wie „Prüfe die Rückerstattungsrichtlinie und den Status dieser Bestellung“ erfordern mehrere Quellen. Registriere Werkzeuge für die Dokumentensuche und Bestellabfrage und überlasse dem Modell die Auswahl der notwendigen Aufrufe. Ein Rundenlimit begrenzt, wie lange es weitere Werkzeuge anfordern kann, bevor es abschließen oder einen Fehler melden muss.

Normale Funktionsaufrufe unterstützen bereits wiederholte Modell-/Werkzeugrunden. `StartRunAsync` verwendet dieselben registrierten Funktionen und Ausführungsrichtlinien. Ein eigener Agent-Modus, Planer oder `WithAgentic`-Schalter ist nicht erforderlich.

`RunAgentAsync` und `RunAgentStreamAsync` bleiben aufrufbar, zeigen jedoch jetzt `[Obsolete]`-Warnungen. Bestehende Signaturen, der Standardwert `maxSteps = 10` und das bisherige Fehlerverhalten beim Schrittlimit bleiben während der Migration erhalten. Verwende für neue Aufrufstellen:

```csharp
await using var run = await service
    .CreateRequest("Finde die Richtlinie, prüfe die Bestellung und erkläre das Ergebnis.")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

Der allgemeine Standardwert von `FunctionCallingPolicy.MaxRounds` ist 20. Gib deshalb 10 an, wenn das bisherige Agent-Limit beibehalten werden soll. `WithMaxRounds` setzt eine Richtlinienüberschreibung für eine Anfrage und ändert `DefaultPolicy` nicht. Konfiguriere sie vor dem Start. Die bisherigen Agent-Methoden kopieren dagegen die aktuelle Standardrichtlinie und wenden ihr aufrufspezifisches `maxSteps` an. Neue Runs verwenden den gemeinsamen Vertrag für Ausführungsfehler und garantieren nicht die bisherige Übersetzung in `AgentMaxStepsExceededException`/`PartialResponse`. Wenn diese Fehlersemantik benötigt wird, behalte den bisherigen Aufruf bei, bis auch seine Ausnahmebehandlung migriert ist.

## RAG, MCP und Paketgrenzen

- `RagEnabledService.StartRunAsync` unterstützt Eingaben als Zeichenfolge oder `Message`, `onText`, anfragespezifische `RagQueryOptions`, `streamOptions` und Abbruch. Die Suche erfolgt vor dem zugrunde liegenden Run. Bild-/Audioinhalte und Metadaten sowie die ursprüngliche Eingabe im Gesprächsverlauf bleiben erhalten; der angereicherte Text wird über den Anfragekontext gesendet. Diese Anreicherung ist an die ursprüngliche Benutzerfrage gebunden, sodass spätere Werkzeugergebnisse und zusätzliche Anweisungen nicht durch den ursprünglichen RAG-Prompt ersetzt werden. Zusätzliche Anweisungen an den zurückgegebenen Run aktualisieren das Modell, führen die RAG-Suche aber nicht automatisch erneut aus.
- `WithAgenticRag` registriert weiterhin ein Suchwerkzeug. Wird es über `StartRunAsync` verwendet, kann das Modell bei Bedarf weitere Suchen anfordern. Die MCP-Registrierung mit `WithMcpServerAsync` bleibt ebenfalls unverändert. Gemeinsam verwendete MCP-Verbindungen werden getrennt von den Runs freigegeben, die sie nutzen.
- `IAIRunService` ist eine optionale Fähigkeit in `Mythosia.AI.Abstractions`; `IAIService` erhält keine neuen Pflichtmitglieder. Ein eigener Service muss `IAIRunService` implementieren, um den Start von RAG-Runs zu unterstützen. Nicht unterstützte Services werden vor Beginn der RAG-Indizierung abgelehnt.
- RAG behält seine Abhängigkeit von Abstractions. Separat paketierte Anbieter behalten ihre öffentlichen Completion-Überschreibungen und zugänglichen Anbieter-Erweiterungspunkte. Diese Änderung verwirft keine API für Vektorspeicher, Dokumentenlader oder Serververwaltung.

Um vor dem Start Reasoning, Web- oder Dateisuche festzulegen und Quellen des Runs anzuzeigen, siehe [Reasoning und Antworten mit Quellen](reasoning-and-search.md).

## Umstieg auf Mythosia.AI 8

| API | Aktueller Status |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | Öffentlich und unterstützt, einschließlich Schnittstellen-, Anbieter- und RAG-Varianten. |
| `StartRunAsync` / `AIRun` | Gemeinsame API für Ausführung und Steuerung. |
| `RunAgentAsync` / `RunAgentStreamAsync` | Obsolete-Warnungen; das bisherige Verhalten bleibt zur Kompatibilität erhalten. |
| Eingaben annehmendes `service.StreamAsync` und RAG-`StreamAsync` | Eingabeannehmende Service-/RAG-StreamAsync-Methoden bleiben in v8 öffentlich. Für neue Ausführungssteuerung StartRunAsync nutzen; run.StreamAsync() beobachtet nur einen vorhandenen Run. |
| `run.StreamAsync()` | Beobachtet die Ausgabe einer bestehenden Aufgabe ohne neue Anfrageeingabe. |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | Die bisherige typisierte Streaming-API bleibt bestehen; ihr reines Ausgabe-`Stream()` ist keine alte Anfrage-Methode des Services. |

[Umstieg auf Mythosia.AI 8](v8-migration.md).

Perplexity: [Längere Aufgaben weiterlaufen lassen](perplexity.md).

[Modelloptionen mit gemeinsamen Fähigkeitsdefinitionen aufbauen](model-capabilities.md).
