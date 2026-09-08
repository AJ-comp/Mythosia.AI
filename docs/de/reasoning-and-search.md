# Reasoning-Aufwand wählen und mit Quellen antworten

> Diese APIs benötigen `Mythosia.AI` ab 7.1.0, einschließlich `Mythosia.AI.Abstractions` ab 3.1.0. RAG-Beispiele benötigen `Mythosia.AI.Rag` ab 7.6.0.

## Warum diese Optionen verwenden?

Verschiedene Arbeitsschritte brauchen unterschiedliche Unterstützung. Für einen ersten Entwurf genügt vielleicht eine schnelle Antwort; die Prüfung seiner Annahmen rechtfertigt mehr Reasoning. Fragen zu heutigen Ereignissen brauchen aktuelle Informationen, Fragen zu Ihrem Produkt dessen Dokumentation. Mehr Reasoning allein verschafft dem Modell keine dieser Quellen.

Mit der gemeinsamen Fluent API beschreiben Sie, was die nächste Aufgabe benötigt. Der gewählte Anbieter übersetzt unterstützte Optionen in seine native API. Ihre Anwendung kann weiterhin `GetCompletionAsync` für eine fertige Antwort verwenden oder mit `StartRunAsync` den Fortschritt anzeigen und dieselbe Aufgabe steuern.

| Die Aufgabe benötigt | Konfiguration |
| --- | --- |
| Einen schnellen Entwurf und danach eine gründlichere Prüfung | `WithReasoning(...)` |
| Eine Reasoning-Änderung, die einen geeigneten Gesprächspräfix im Cache erhält | `WithReasoning(..., cache: CachePreservation.Required)` |
| Aktuelle Informationen aus dem Web | `WithWebSearch()` |
| Antworten auf Basis bereits beim Anbieter indexierter Dokumente | `WithFileSearch(store)` |

Die Beispiele setzen einen initialisierten Service mit unterstütztem Modell voraus. Importieren Sie `Mythosia.AI.Extensions` und `Mythosia.AI.Models`; Stream-Ereignisse verwenden außerdem `Mythosia.AI.Models.Streaming`.

## Vom schnellen Entwurf zur gründlichen Prüfung

Für eine Gliederung können Sie weniger Reasoning einsetzen und anschließend im selben Gespräch schwierige Details prüfen lassen:

```csharp
string outline = await service
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("Skizziere den Migrationsplan.");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("Prüfe den Plan auf Fehlerszenarien und Wiederherstellungsschritte.");
```

`ReasoningLevel` bezeichnet einen gewünschten Aufwand, kein festes Token-Budget und keine Qualitätsgarantie. Jedes Modell unterstützt eine eigene Teilmenge. `Auto` behält das konfigurierte beziehungsweise standardmäßige Verhalten des Anbieters bei; nicht unterstützte Werte werden dadurch nicht automatisch ersetzt. Für Modelle mit Token-Budgets statt benannter Stufen bleiben die anbieterspezifischen Budget-Eigenschaften verfügbar.

In langen Gesprächen kann eine Änderung des übergeordneten Effort-Werts einen wiederverwendbaren Prompt-Präfix ungültig machen. Bei einem unterstützten Modell können Sie den Mechanismus verlangen, der den Aufwand unter Erhalt dieses Präfixes ändert:

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("Prüfe die Annahmen der vorherigen Antwort erneut.");
```

`Required` legt fest, wie die Änderung übertragen wird. Es garantiert **keinen** Cache-Treffer, kostenlose Tokens oder geringere Latenz: Eignung, Aufbewahrung und Preise des Anbieter-Caches gelten weiterhin. Nicht unterstützte Modelle lösen vor dem Senden `NotSupportedException` aus. Verwenden Sie dasselbe nachverfolgte Gespräch, Modell und denselben Endpunkt; kürzen oder sortieren Sie ein Gespräch mit solchen Änderungen nicht um. Beginnen Sie ein neues Gespräch, wenn sich diese Bedingungen ändern. Automatische Komprimierung wird blockiert, solange der erhaltene Präfix erforderlich ist.

Der akzeptierte Aufwand mit Cache-Erhalt bleibt im Gespräch aktiv, bis eine weitere ausdrückliche Änderung erfolgt. Gewöhnliches `WithReasoning(level)` gilt für seine logische Anfrage und ersetzt diese dauerhafte Einstellung nicht stillschweigend. Die Änderung erfolgt **zwischen Modellantworten**. Sie ändert nicht den Aufwand einer bereits laufenden Antwort und ist unabhängig von `run.SteerAsync`, das einem unterstützten aktiven Run eine weitere Anweisung sendet.

## Fragen mit aktuellen Informationen beantworten

Aktivieren Sie die native Websuche, wenn die Antwort Informationen außerhalb der Trainingsdaten des Modells nutzen soll:

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("Suche die neueste Release-Ankündigung und nenne die Quelle.");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

Der Anbieter führt dieses gehostete Werkzeug aus. Sie müssen keinen lokalen Funktionshandler registrieren oder ausführen. Die Aktivierung stellt die Suche dem Modell bereit; bei einer bestimmten Eingabe kann es entscheiden, dass keine Suche nötig ist. Quellenverweise sind verfügbar, wenn der Anbieter sie zurückgibt.

OpenAI und Anthropic akzeptieren außerdem `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`. Google bietet diese Positivliste im integrierten Werkzeug nicht an. Eine so eingeschränkte Anfrage wird daher abgelehnt, statt das gesamte Web zu durchsuchen.

## Aus bereits beim Anbieter indexierten Dokumenten antworten

Wenn Ihre Anwendung bereits einen Dokumentindex beim Anbieter führt, können Sie Antworten auf diesen Speicher stützen, ohne einen eigenen Retrieval-Durchlauf zu implementieren:

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .WithFileSearch(documents)
    .GetCompletionAsync("Durchsuche unsere Richtliniendokumente. Welche Kündigungsfrist gilt?");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Verwenden Sie für Google `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` mit einem Google-Service. Speicher gehören zu ihrem Anbieter, Konto und Deployment; eine OpenAI-Speicher-ID kann nicht an Google übergeben werden. Erstellen Sie den Speicher und laden beziehungsweise indexieren Sie seine Dokumente vorab über die API oder Konsole des Anbieters. Diese API durchsucht nur vorhandene Speicher und lädt keine lokalen Dateien hoch.

Gehostete Dateisuche und die [RAG-Pipeline](rag.md) der Bibliothek passen zu unterschiedlichen Ausgangslagen. Wählen Sie die gehostete Suche, wenn der Anbieter Ihren Index bereits verwaltet. Wählen Sie RAG, wenn Ihre Anwendung Loader, Aufteilung, Embeddings, Retrieval oder Vektorspeicher kontrollieren muss. `RagEnabledService` reicht auch `WithReasoning`, `WithWebSearch` und `WithFileSearch` an die abschließende Antwort weiter; seine interne Query-Umschreibung erbt diese Optionen nicht. RAG-Retrieval-Referenzen bleiben auf `RagProcessedQuery`, getrennt von den `AICitation`-Quellen des Anbieters.

## Fortschritt anzeigen und Quellen behalten

Verwenden Sie dieselben Optionen vor `StartRunAsync`. Der Text-Callback kann die Anzeige aktualisieren, während der Run Quellen für die fertige Antwort aufbewahrt:

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "Suche die aktuellen Ankündigungen und vergleiche die Änderungen.",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` bleibt verfügbar, wenn der Stream nicht gelesen wird, nur Text beobachtet wird oder der Ausgabepuffer vollläuft. Es enthält die während des Runs gesammelten Quellenverweise des Anbieters, einschließlich Zwischenantworten. `service.LastCitations` beziehungsweise `GetLastCitations()` über `IAIService` beschreibt die letzte logische Anfrage. Bewahren Sie den Run auf oder kopieren Sie seinen Quellen-Snapshot, wenn Sie mehrere Antworten anzeigen.

Um Quellenereignisse beim Eintreffen zu verarbeiten, verwenden Sie einen einzelnen Ereignisleser:

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "Suche und erkläre die neuesten Änderungen.", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nQuelle: {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

Quellenfelder können `null` sein, wenn der Anbieter keinen Wert liefert. `ResponseId`, `OutputIndex` und `ContentIndex` kennzeichnen die ursprüngliche Antwort und deren Inhaltsteil. `StartIndex` und `EndIndex` behalten die lokalen Offsets und die Indexkonvention des Anbieters bei; sie sind **keine** Offsets im zusammengefügten `run.Result`. Platzieren Sie Quellen deshalb nicht durch ungeprüftes Indizieren der gesamten Antwort mit diesen Werten.

## Anbieterunterstützung und Anfrageumfang prüfen

| Integrierter Anbieter | Benannte Reasoning-Stufen | Änderung mit Cache-Erhalt | Websuche | Dateisuche |
| --- | --- | --- | --- | --- |
| OpenAI | Unterstützte Reasoning-Modelle; Stufen sind modellabhängig | GPT-6 Astra Standard im Einzelagentenmodus | Unterstützte Responses-Modelle | Unterstützte Responses-Modelle, vorhandene Vektorspeicher |
| Anthropic | Modelle mit nativer Effort-Steuerung | Unterstützte Opus 5 / Fable 5.1 / Mythos 5.1 mit Anbieter-Beta | Unterstützte Claude-Modelle | Kein nativer Speicheradapter; RAG verwenden |
| Google | Gemini-3-Stufen; Gemini 2.5 behält anbieterspezifische Budgets | Nicht unterstützt | Unterstützte Gemini-Textmodelle | Unterstützte Gemini-Textmodelle, vorhandene Dateisuchspeicher |
| Andere Services | Bestehende anbieterspezifische Einstellungen bleiben verfügbar; gemeinsame Optionen benötigen einen Adapter | Von diesen Adaptern nicht unterstützt | Kein gemeinsamer Adapter | Kein gemeinsamer Adapter |

Modell, Stufe, Transport und Kombinationen werden vor dem Senden geprüft. Insbesondere lassen sich **Google-Websuche und Dateisuche nicht in einer Anfrage kombinieren**. Die Bibliothek entfernt keine Funktion stillschweigend, reduziert keine Aufwandsstufe, ignoriert keine Domain-Einschränkung und wechselt nicht zu einem externen Suchdienst. Native Werkzeuge können bei entsprechender Unterstützung mit registrierten Client-Funktionen zusammenarbeiten; Werkzeugrunden folgen weiterhin der Funktionsrichtlinie und `WithMaxRounds`.

Fluent-Methoden behalten den konkreten Service-Typ bei und kopieren ihre Eingabeoptionen. Nicht-null-Komponenten werden für die nächste logische Anfrage zusammengeführt, einschließlich ihrer Werkzeugrunden und Reparaturaufrufe für strukturierte Ausgaben, und danach verbraucht. Für spätere unabhängige Aufrufe bleibt die Suche nicht aktiv; setzen Sie bei Bedarf erneut `WithWebSearch` oder `WithFileSearch`. Ein gestarteter Run behält seine erfassten Einstellungen. Wie bei anderer veränderlicher Service-Konfiguration dürfen Sie während einer laufenden Anfrage weder Einstellungen ändern noch überlappende Anfragen auf demselben Service starten.

Eigene Implementierungen von `IAIService` bleiben kompatibel. Sie unterstützen diese Funktionen optional über `IAIRequestFeatureService`; auf Implementierungen ohne diese Fähigkeit schlagen die Hilfsmethoden ausdrücklich fehl. Bestehende Completion-, Streaming- und anbieterspezifische Konfigurations-APIs bleiben verfügbar. Hinweise zu Abbruch, Beobachtung und Steering finden Sie unter [Run-Steuerung](execution-api-transition.md).

Anbieterprotokolle: [OpenAI-Reasoning-Änderungen](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [OpenAI-Werkzeuge](https://developers.openai.com/api/docs/guides/tools), [Anthropic-Effort-Änderungen](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Anthropic-Websuche](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Quellenbindung mit Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Google-Dateisuche](https://ai.google.dev/gemini-api/docs/file-search).
