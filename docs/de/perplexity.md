# Perplexity: belegte Antworten, Suche und Embeddings

Verwenden Sie Perplexity, wenn Antworten aktuelle Informationen und überprüfbare Quellen benötigen. `PerplexityService` ruft die Agent API auf. Die eigenständige Suche und Embeddings ermöglichen eine Dokumentensuche mit einem selbst gewählten Antwortmodell.

## Zuerst die Aufgabe auswählen

Eine aktuelle Antwort, eine Liste von Webseiten und Vektoren für den eigenen Dokumentenindex sind unterschiedliche Aufgaben. Wählen Sie die zuständige Komponente, statt für jeden Abruf ein Antwortmodell aufzurufen.

| Bedarf | Komponente |
| --- | --- |
| Eine recherchierte Antwort mit Quellen | `PerplexityService` |
| Sortierte Webseiten für ein anderes Modell oder die Oberfläche | `PerplexitySearchClient` |
| Vektoren unabhängiger Abschnitte für gewöhnliches RAG | `PerplexityEmbeddingProvider` |
| Vektoren mit Kontext benachbarter Abschnitte eines Dokuments | `PerplexityContextualizedEmbeddingProvider` |

Installieren Sie `Mythosia.AI`, für Embeddings zusätzlich `Mythosia.AI.Rag`. Übergeben Sie Ihren API-Schlüssel und einen von der Anwendung verwalteten `HttpClient`. Die Beispiele verwenden die Anwendungsvariablen `apiKey`, `httpClient` und `cancellationToken`.

## Mit einem Agent-Preset antworten

Ein Preset kombiniert gepflegte Modelle, Anweisungen, Werkzeuge, Denkaufwand und Budgets. `Fast` eignet sich für kurze Abfragen, `Low` für alltägliche Recherche, `Medium` für mehrstufige Vergleiche und `High` / `XHigh` für vertiefte Aufgaben. `WideResearch` eignet sich für breit angelegte Recherchen; für längere Aufgaben empfiehlt sich die Hintergrundausführung. Presets sind keine Modell-IDs.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "Vergleiche aktuelle Verfahren zum Batterierecycling und nenne die Quellen.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

Verwenden Sie `GetCompletionAsync` für die fertige Antwort, den Service-`StreamAsync` für bestehendes Streaming oder `StartRunAsync` zum Beobachten und Abbrechen. `(await run.Result).Text` verbindet ausgegebenen Antworttext. `run.Citations` und `LastCitations` behalten Quellen auch ohne gelesene Zitatereignisse. Denkereignisse enthalten nur vom Anbieter offengelegte Inhalte und hängen vom Modell ab.

`AIRunResult.RequestedModel` ist das beim Start erfasste einzelne Modell, das ausdrücklich in der Anfrage gesendet wird, einschließlich einer anbieterspezifischen Modellüberschreibung. Es ist `null`, wenn Preset, Profil oder serverseitiges Routing ohne einzelnes explizites Modellfeld auswählen (etwa eine Perplexity-`Models`-Liste). Dies ist unabhängig vom tatsächlichen Antwortmodell in `Model`.

## Recherche und Werkzeuge steuern

`WithPerplexityOptions(...)` setzt dauerhafte Serviceoptionen, die jede logische Anfrage kopiert. Gemeinsame `WithReasoning(...)`- und `WithWebSearch(...)`-Optionen gelten für die nächste logische Anfrage einschließlich lokaler Werkzeugrunden und typisierter Ausgabereparatur. Interne RAG-Umschreibungen übernehmen die Suchoptionen der finalen Antwort nicht.

`UsePreset(...)` wählt ein Preset direkt. Preset/Profil bestimmen ihr Modell selbst; `ModelOverride` überschreibt es ausdrücklich. `DisableWebSearch` entfernt nur das Standardwerkzeug des Adapters, nicht verlässlich die eingebaute Presetsuche. Modellabhängig gelten `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`. `None` sowie ausdrücklicher Denkaufwand bei direktem Sonar werden abgelehnt. Internes `DisableReasoning` verwendet niedrigen verfügbaren Aufwand oder lässt ihn weg, ohne das Denken garantiert abzuschalten.

| Option | Verwendung |
| --- | --- |
| `Preset` / `ModelOverride` | Recherchekonfiguration wählen oder ihr Modell mit einer Anbieter/Modell-ID ausdrücklich überschreiben. |
| `MaxSteps` | Gehostete Schleife begrenzen; null verwendet die Anbietervorgabe. Getrennt von `WithMaxRounds` für Fortsetzungen lokaler Funktionen. |
| `ReasoningEffort` | Denkaufwand anpassen. `Auto` lässt die Einstellung weg; gültige Stufen hängen vom tatsächlichen Modell ab. |
| `DisableWebSearch` / `Tools` | Standard-Webwerkzeug des Adapters und explizite gehostete Werkzeuge konfigurieren. |
| `Models` | Ein bis fünf Ersatzmodelle nach Priorität angeben. Die Liste ersetzt das Einzelmodell; alle Kandidaten müssen die angeforderten Funktionen unterstützen. |
| `Profile` | Gespeicherte Serverkonfiguration verwenden und optional deren Version festlegen. Nicht mit `Preset` kombinierbar. |
| `ServiceTier` | Standard-, Flex- oder Prioritätsverarbeitung anfordern. Nicht unterstützte Stufen kann der Anbieter ignorieren. |
| `Skills` | Integrierte, Inline- oder bereits hochgeladene eigene Skills übergeben. Eigene Ressourcen gehören zum Perplexity-Konto. |
| `LanguagePreference` / `PromptCacheKey` | Antwortsprache oder Hinweis zur Cache-Zuordnung setzen. Cache-Treffer werden nicht garantiert. |
| `PreviousResponseId` / `Store` | Abgeschlossene Anbieterantwort fortsetzen oder Abrufbarkeit steuern. Bei Fortsetzung `StatelessMode` verwenden und nur den neuen Turn senden. `Store = false` deaktiviert die Speicherung beim Anbieter nicht. |

`PerplexityHostedTool` akzeptiert einen unterstützten `Type` und dokumentierte JSON-kompatible `Parameters`: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox` oder `mcp`. MCP-Server und verwaltete Konnektoren laufen über den Anbieter; Zugangsdaten, Berechtigungen und Kontoressourcen müssen zur Verbindung passen. Anwendungsfunktionen werden über `Functions` / den Funktionsbuilder registriert. Gehostete Schritte und lokale Handler haben unterschiedliche Ausführungsverantwortliche.

Die Fabriken `PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp` und `Connector` erstellen Werkzeuge. MCP-Aufrufe warten nicht auf Freigaben; begrenzen Sie gegebenenfalls `allowedTools`. Connector ist eine Anbietervorschau und verweist auf eine bereits verbundene Integration.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "Lies die Projektdokumentation und vergleiche die relevanten Fähigkeiten.");
```

Werkzeug-, Denk-, Bild- und Schemakompatibilität hängen vom Modell ab. `WithFileSearch` ist kein Perplexity-Vektorspeicheradapter. Sandbox-Dateien, hochgeladene Anhänge und entfernte MCP-Daten sind eigenständige Ressourcen und werden nicht automatisch zu einem gemeinsamen Dateisuchspeicher.

## Quellen, Bilder und strukturierte Antworten

Für JSON-Felder nutzen Sie typisierte Completion oder typisiertes Streaming. Der Adapter sendet ein natives Schema und behält die Reparatur bei. Native Antwortteile und Werkzeug-IDs bleiben für Fortsetzungen erhalten; löschen oder sortieren Sie Protokollverläufe nicht manuell um. Bildinput verwendet `Message` und `ImageContent` mit JPEG/PNG/WebP/GIF-Bytes oder HTTPS-URL, abhängig vom Modell. Dies erzeugt keine Bilder.

Originalantworten bleiben in den Verlaufsmetadaten, doch Folgeanfragen senden nur die zulässigen Eingabeelemente `message`, `function_call` und `function_call_output` erneut; für die Fortsetzung des vollständigen gehosteten Zustands beim Anbieter verwenden Sie `PreviousResponseId`.

Zitate können Webtreffer oder andere Anbieterquellen bezeichnen. Positionen beziehen sich auf einen einzelnen Antwort-/Inhaltsteil, nicht auf das zusammengesetzte Run-Ergebnis. Behalten Sie URL und Titel zur Anzeige und Prüfung; eine Quelle bestätigt nicht automatisch jede erzeugte Aussage.

## Längere Aufgaben weiterlaufen lassen

Hintergrundausführung beim Anbieter lässt Recherchen eine vorübergehende Trennung überstehen oder später per ID abrufen. Ein lokaler `AIRun` steuert die aktuelle Clientausführung; eine Hintergrundantwort hat einen eigenen Serverlebenszyklus. Das Beenden des Streamlesers beendet nur die Beobachtung. Stoppen Sie entfernte Arbeit durch ausdrücklichen Jobabbruch.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "Vergleiche aktuelle Verfahren zum Batterierecycling und nenne die Quellen.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` erfasst Eingaben ohne Verlaufseintrag und lehnt aktive lokale Funktionen sowie `Store = false` ab. `GetResponseAsync` fragt einmal ab, `WaitForCompletionAsync` bis zum Endzustand. Speichern Sie `Id` und `LastSequenceNumber`; verbinden Sie sich über `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` erneut. `CancelAsync` stoppt den Serverjob; ein Abfrage-/Lesetoken beendet nur die Clientoperation. `LastResponse` enthält Text, Status, Nutzung, Zitate und `OutputJson`. Prüfen Sie den Endstatus vor der Antwortverwendung.

Sandbox-Ausgaben lesen Sie mit `ListFilesAsync` und `DownloadFileAsync(fileId)`. Der Service bietet auch `GetAgentResponseAsync`, `GetResponseFilesAsync` und `GetResponseFileContentAsync`. Diese lesen Antwortartefakte und erstellen oder durchsuchen keinen Vektorspeicher.

Nutzen Sie für integrierte Office-Skills den Hintergrundpfad dieser Anleitung: `StartBackgroundAsync`, danach `WaitForCompletionAsync` / `GetResponseAsync` und die Dateimethoden. Interne Werkzeugspuren dieser Antworten lassen sich möglicherweise nicht von gewöhnlichen lokalen Funktionsaufrufen unterscheiden.

Hintergrundübermittlung, Abruf, Abbruch und Stream-Wiederverbindung aktivieren weder `SteerAsync` während einer Antwort noch native asynchrone Clientwerkzeuge. Eine Wiederverbindung beobachtet die bestehende Antwort weiter, ohne die Aufgabe neu abzusenden. Behalten Sie Antwort-ID und Cursor des Anbieters.

## Suchen, ohne eine Antwort zu erzeugen

`PerplexitySearchClient` liefert Webseiten für eigene Sortierung, Oberflächen oder andere LLMs. Er ruft kein Antwortmodell auf und ändert den Verlauf von `PerplexityService` nicht.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "Verfahren zum Batterierecycling",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` nimmt einzelne oder mehrere Abfragen an. Optionen umfassen Web/Personensuche, Land, Domains, Sprachen, Veröffentlichungs-/Änderungsdatum und Aktualität. Wählen Sie `ContentSize` oder explizite `MaxTokens` / `MaxTokensPerPage`, nicht beides. Ergebnisse enthalten Rang, Titel, URL, Auszug und Anbieterdaten. Rang ist die Rückgabereihenfolge, kein Relevanzwert.

`ContentSize` wird nur bei der Web-Suche unterstützt. Lassen Sie es bei People weg; der Client weist diese Kombination vor dem Senden zurück.

## Vektoren im eigenen Dokumentenindex nutzen

Standard-Embeddings behandeln Abschnitte unabhängig und implementieren `IEmbeddingProvider` für den bestehenden RAG-Builder. Kontextuelle Embeddings behalten Reihenfolge und Dokumentgruppen benachbarter Abschnitte bei. Ihre getrennte API verhindert, dass unabhängige Dokumente zu einer flachen Eingabe werden.

| Modellkonstante | Anbieter-ID | Standarddimensionen |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("Rückgaben sind innerhalb von 30 Tagen möglich.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("Wie lange kann ich einen Kauf zurückgeben?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "Rückgaben sind innerhalb von 30 Tagen möglich.", "Bewahren Sie den Beleg für eine Erstattung auf." },
    new[] { "Der Standardversand dauert drei Tage.", "Expressversand ist an Werktagen verfügbar." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "Wie lange kann ich einen Kauf zurückgeben?", cancellationToken);
```

Nutzen Sie dasselbe Modell, dieselbe Dimension und Kodierung für Dokumente und Abfragen. `GetQueryEmbeddingAsync` sendet eine Abfrage als eigenes Dokument an dasselbe Kontextmodell. Ergebnisse behalten Dokument- und Abschnittsreihenfolge, ohne automatische Anbindung an den flachen RAG-Builder.

Float-APIs dekodieren Base64-Signed-int8-Vektoren und normalisieren sie für Ähnlichkeitsberechnungen. Explizite Binär-APIs liefern gepackte Bits mit Hamming-Distanz, niemals stillschweigend Floatkoordinaten. Volle Dimensionen sind 1024 für 0.6B und 2560 für 4B; reduzierte Dimensionen folgen Anbietergrenzen. Batch-, Längen-, Gesamt-Token- und Kontolimits gelten weiterhin.

Binärmethoden: `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync` sowie kontextuell `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`. `PerplexityBinaryEmbedding` bietet `Dimensions`, eine Kopie über `ToArray()` und `HammingDistance`; kleiner bedeutet ähnlicher. Binärdimensionen müssen durch acht teilbar sein. Maximal 512 Standardtexte beziehungsweise 512 Dokumente mit 16.000 Kontextabschnitten. 32K Tokens pro Text/Dokument und 120K insgesamt prüft der Anbieter.

## Bestehenden Sonar-Code migrieren

Diese Version entfernt den alten Sonar-Adapter vor der angekündigten Endpunktabschaltung am 27. September 2026. `PerplexityService` verwendet `/v1/agent`; `AIModels.Perplexity.Sonar` bedeutet nun `perplexity/sonar`. Alte Sonar-Suchhelfer und Antworttypen entfallen. Nutzen Sie gemeinsame Completion/Run/Zitate, Agent-Presets und `PerplexitySearchClient` für unabhängige Suche.

Empfohlen ist Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`. Gleiche Texte, Kosten oder Modellreaktionen werden nicht garantiert. Dynamische Presets ändern sich mit dem Anbieter; bei Bedarf Modell oder Profilversion ausdrücklich festlegen.

Native Steuerung, native asynchrone Clientwerkzeuge und `CachePreservation.Required` werden nicht unterstützt. Router/Gateway-APIs gehören nicht zu dieser Integration. Verfügbarkeit hängt von Anbieter, Modell und Konto ab; diese Anleitung behauptet keine bezahlte Liveprüfung jeder Kombination.

Profile, eigene Skills und Connectoren benötigen bereits registrierte Kontoressourcen. Ihre Anfrageformen sind durch Unit-Tests abgedeckt; erfolgreiche Live-Aufrufe mit diesen Ressourcen wurden nicht geprüft.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
