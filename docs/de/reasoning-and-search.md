# Reasoning-Aufwand wählen und mit Quellen antworten

> Grok 4.7: Benötigt Mythosia.AI 8.1.0 / Abstractions 4.1.0. [Modellwahl, Reasoning und Verarbeitungsgeschwindigkeit](providers.md#grok-47)

> GPT-6 Sol/Luna sind noch nicht veröffentlicht. Siehe [Modellwahl und Voraussetzungen](providers.md#gpt-6-sol-luna).

[Claude Opus 5.5](providers.md#claude-opus-55) ist ab Mythosia.AI 8.1.0 / Abstractions 4.1.0 verfügbar, mit ständig aktivem Denken, standardmäßig mittlerem Aufwand und verborgener Anzeige. Fordern Sie lesbaren Fortschritt ausdrücklich an; Standardwerte und Modellbindung unterscheiden sich von Fable 5.1.

Für unabhängige Einstellungen und wiederverwendbare Varianten verwenden Sie den [Anfrage-Builder](request-building.md). Rufen Sie `CreateRequest(...)` vor `With...` auf. Service-Eigenschaften und dessen Fluent-Methoden behalten ihr bisheriges Verhalten.

> Diese APIs benötigen `Mythosia.AI` ab 7.1.0, einschließlich `Mythosia.AI.Abstractions` ab 3.1.0. RAG-Beispiele benötigen `Mythosia.AI.Rag` ab 7.6.0.

> Die `CreateRequest`-Beispiele benötigen Mythosia.AI 8.0.0 / Abstractions 4.0.0. Die frühere Version 7.1 mit Run und gemeinsamen Anfrageoptionen enthält den Builder noch nicht. Ältere Pakete können ihre bisherigen Service-Überladungen verwenden.

[Claude Fable 5.1](fable-5-1.md) bietet ab `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 Fortschrittsmeldungen, Anweisungen für einzelne Gesprächsrunden und Thinking-Binding-Diagnosen. Mythos 5.1 erfordert eine Einladung. Beide lehnen erzwungene Tool-Auswahl ab.

Für zeitkritische Anfragen wählen Sie die [Verarbeitungsgeschwindigkeit](request-building.md#inference-speed). `WithSpeed` behält Modell und Denkaufwand bei; `Processing` meldet den tatsächlich verwendeten Modus. Fast ist für unterstützte Kombinationen kostenpflichtig.

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
    .CreateRequest("Skizziere den Migrationsplan.")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("Prüfe den Plan auf Fehlerszenarien und Wiederherstellungsschritte.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash akzeptieren `Low`, `Medium` und `High` über `WithReasoning`; `Minimal`, `None` und `CachePreservation.Required` werden nicht unterstützt. Completion, Streaming, Run, Tool-Aufrufe und native Suche nutzen die bestehenden Pfade samt Googles Kombinationsgrenzen. Siehe das [Google-Konfigurationsbeispiel](providers.md#google-googleaiservice).

`ReasoningLevel` bezeichnet einen gewünschten Aufwand, kein festes Token-Budget und keine Qualitätsgarantie. Jedes Modell unterstützt eine eigene Teilmenge. `Auto` behält das konfigurierte beziehungsweise standardmäßige Verhalten des Anbieters bei; nicht unterstützte Werte werden dadurch nicht automatisch ersetzt. Für Modelle mit Token-Budgets statt benannter Stufen bleiben die anbieterspezifischen Budget-Eigenschaften verfügbar.

In langen Gesprächen kann eine Änderung des übergeordneten Effort-Werts einen wiederverwendbaren Prompt-Präfix ungültig machen. Bei einem unterstützten Modell können Sie den Mechanismus verlangen, der den Aufwand unter Erhalt dieses Präfixes ändert:

```csharp
string review = await service
    .CreateRequest("Prüfe die Annahmen der vorherigen Antwort erneut.")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required` legt fest, wie die Änderung übertragen wird. Es garantiert **keinen** Cache-Treffer, kostenlose Tokens oder geringere Latenz: Eignung, Aufbewahrung und Preise des Anbieter-Caches gelten weiterhin. Nicht unterstützte Modelle lösen vor dem Senden `NotSupportedException` aus. Verwenden Sie dasselbe nachverfolgte Gespräch, Modell und denselben Endpunkt; kürzen oder sortieren Sie ein Gespräch mit solchen Änderungen nicht um. Beginnen Sie ein neues Gespräch, wenn sich diese Bedingungen ändern. Automatische Komprimierung wird blockiert, solange der erhaltene Präfix erforderlich ist.

Der akzeptierte Aufwand mit Cache-Erhalt bleibt im Gespräch aktiv, bis eine weitere ausdrückliche Änderung erfolgt. Gewöhnliches `WithReasoning(level)` gilt für seine logische Anfrage und ersetzt diese dauerhafte Einstellung nicht stillschweigend. Die Änderung erfolgt **zwischen Modellantworten**. Sie ändert nicht den Aufwand einer bereits laufenden Antwort und ist unabhängig von `run.SteerAsync`, das einem unterstützten aktiven Run eine weitere Anweisung sendet.

## Fragen mit aktuellen Informationen beantworten

Aktivieren Sie die native Websuche, wenn die Antwort Informationen außerhalb der Trainingsdaten des Modells nutzen soll:

```csharp
string answer = await service
    .CreateRequest("Suche die neueste Release-Ankündigung und nenne die Quelle.")
    .WithWebSearch()
    .GetCompletionAsync();

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
    .CreateRequest("Durchsuche unsere Richtliniendokumente. Welche Kündigungsfrist gilt?")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Verwenden Sie für Google `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` mit einem Google-Service. Speicher gehören zu ihrem Anbieter, Konto und Deployment; eine OpenAI-Speicher-ID kann nicht an Google übergeben werden. Erstellen Sie den Speicher und laden beziehungsweise indexieren Sie seine Dokumente vorab über die API oder Konsole des Anbieters. Diese API durchsucht nur vorhandene Speicher und lädt keine lokalen Dateien hoch.

`CreateRequest(...).With...` speichert Optionen in einem unabhängigen Builder. Bei Wiederverwendung gelten sie für jede Ausführung und deren Tool-Runden. Die bisherigen `service.WithReasoning`, `service.WithWebSearch` und `service.WithFileSearch` liefern weiterhin den konkreten Servicetyp und verbrauchen Optionen beim nächsten logischen Request. Sie bleiben für `IAIRequestFeatureService` und RAG-Wrapper verfügbar. Keine Variante garantiert gleichzeitige Ausführungen auf einem Service.

## Fortschritt anzeigen und Quellen behalten

Verwenden Sie dieselben Optionen vor `StartRunAsync`. Der Text-Callback kann die Anzeige aktualisieren, während der Run Quellen für die fertige Antwort aufbewahrt:

```csharp
await using var run = await service
    .CreateRequest("Suche die aktuellen Ankündigungen und vergleiche die Änderungen.")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

`run.Citations` bleibt verfügbar, wenn der Stream nicht gelesen wird, nur Text beobachtet wird oder der Ausgabepuffer vollläuft. Es enthält die während des Runs gesammelten Quellenverweise des Anbieters, einschließlich Zwischenantworten. `service.LastCitations` beziehungsweise `GetLastCitations()` über `IAIService` beschreibt die letzte logische Anfrage. Bewahren Sie den Run auf oder kopieren Sie seinen Quellen-Snapshot, wenn Sie mehrere Antworten anzeigen.

Um Quellenereignisse beim Eintreffen zu verarbeiten, verwenden Sie einen einzelnen Ereignisleser:

```csharp
await using var run = await service
    .CreateRequest("Suche und erkläre die neuesten Änderungen.")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\nQuelle: {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

Quellenfelder können `null` sein, wenn der Anbieter keinen Wert liefert. `ResponseId`, `OutputIndex` und `ContentIndex` kennzeichnen die ursprüngliche Antwort und deren Inhaltsteil. `StartIndex` und `EndIndex` behalten die lokalen Offsets und die Indexkonvention des Anbieters bei; sie sind **keine** Offsets im zusammengefügten `(await run.Result).Text`. Platzieren Sie Quellen deshalb nicht durch ungeprüftes Indizieren der gesamten Antwort mit diesen Werten.

## Anbieterunterstützung und Anfrageumfang prüfen

| Integrierter Anbieter | Benannte Reasoning-Stufen | Änderung mit Cache-Erhalt | Websuche | Dateisuche |
| --- | --- | --- | --- | --- |
| OpenAI | Unterstützte Reasoning-Modelle; Stufen sind modellabhängig | GPT-6 Astra / Sol / Luna Standard im Einzelagentenmodus | Unterstützte Responses-Modelle | Unterstützte Responses-Modelle, vorhandene Vektorspeicher |
| Anthropic | Modelle mit nativer Effort-Steuerung | Unterstützte Opus 5 / 5.5 / Fable 5.1 / Mythos 5.1 mit Anbieter-Beta | Unterstützte Claude-Modelle | Kein nativer Speicheradapter; RAG verwenden |
| Google | Gemini-3-Stufen; Gemini 2.5 behält anbieterspezifische Budgets | Nicht unterstützt | Unterstützte Gemini-Textmodelle | Unterstützte Gemini-Textmodelle, vorhandene Dateisuchspeicher |
| xAI | Grok 4.7 / 4.6: `Auto`, `Low`, `Medium`, `High`, `XHigh` | Nicht unterstützt | Kein gemeinsamer Adapter | Kein gemeinsamer Adapter |
| DeepSeek | Flash / V4 Pro: `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max`; Zuordnung zu nativem Low/High/Max | Nicht unterstützt | Kein gemeinsamer Adapter | Kein gemeinsamer Adapter |
| Perplexity | `Auto` oder modellabhängig `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max`; kein expliziter Effort für Sonar | Nicht unterstützt | Agent `web_search` | Kein gemeinsamer Adapter |
| Andere Services | Bestehende anbieterspezifische Einstellungen bleiben verfügbar; gemeinsame Optionen benötigen einen Adapter | Von diesen Adaptern nicht unterstützt | Kein gemeinsamer Adapter | Kein gemeinsamer Adapter |

Der Adapter prüft vor dem Senden die lokal bekannten Einschränkungen für Modell, Stufe, Transport und Kombinationen; weitere modellspezifische Regeln prüft der Anbieter. Insbesondere lassen sich **Google-Websuche und Dateisuche nicht in einer Anfrage kombinieren**. Die Bibliothek entfernt keine Funktion stillschweigend, reduziert keine Aufwandsstufe, ignoriert keine Domain-Einschränkung und wechselt nicht zu einem externen Suchdienst. Native Werkzeuge können bei entsprechender Unterstützung mit registrierten Client-Funktionen zusammenarbeiten; Werkzeugrunden folgen weiterhin der Funktionsrichtlinie und `WithMaxRounds`.

`CreateRequest(...).With...` speichert Optionen in einem unabhängigen Builder. Bei Wiederverwendung gelten sie für jede Ausführung und deren Tool-Runden. Die bisherigen `service.WithReasoning`, `service.WithWebSearch` und `service.WithFileSearch` liefern weiterhin den konkreten Servicetyp und verbrauchen Optionen beim nächsten logischen Request. Sie bleiben für `IAIRequestFeatureService` und RAG-Wrapper verfügbar. Keine Variante garantiert gleichzeitige Ausführungen auf einem Service.

Eigene Implementierungen von `IAIService` bleiben kompatibel. Sie unterstützen diese Funktionen optional über `IAIRequestFeatureService`; auf Implementierungen ohne diese Fähigkeit schlagen die Hilfsmethoden ausdrücklich fehl. Bestehende Completion-, Streaming- und anbieterspezifische Konfigurations-APIs bleiben verfügbar. Hinweise zu Abbruch, Beobachtung und Steering finden Sie unter [Run-Steuerung](execution-api-transition.md).

Anbieterprotokolle: [OpenAI-Reasoning-Änderungen](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [OpenAI-Werkzeuge](https://developers.openai.com/api/docs/guides/tools), [Anthropic-Effort-Änderungen](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Anthropic-Websuche](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Quellenbindung mit Google Search](https://ai.google.dev/gemini-api/docs/google-search), [Google-Dateisuche](https://ai.google.dev/gemini-api/docs/file-search).

Bei Perplexity bestimmt das tatsächlich ausgewählte Modell die unterstützten Effort-Stufen; der Server kann unvereinbare Kombinationen ablehnen. `None` wird nicht unterstützt. Standardsuche und Preset-/Profilwerkzeuge sind dauerhafte Anbietereinstellungen und werden durch gemeinsame Anfrageoptionen nicht ausgeschaltet.

Perplexity: [Perplexity Agent API, Suche und Embeddings](perplexity.md).
