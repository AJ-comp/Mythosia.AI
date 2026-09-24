# RAG (Retrieval-Augmented Generation)

Für eine fertige Antwort mit Stoppschaltfläche übergeben Sie `cancellationToken` an `GetCompletionAsync`. Run dient Fortschrittsereignissen oder unterstützten zusätzlichen Anweisungen. Siehe [Completion-Abbruch](completions.md#completion-cancellation).

Übergeben Sie auch bei Antworten mit Suchkontext `cancellationToken` an `RagEnabledService.GetCompletionAsync`. Dasselbe Token erreicht Suche, `LlmQueryRewriter`, `LlmReranker` und die innere Completion; ein Abbruch während der Suche verhindert den späteren Modellaufruf. Auch `RagPipeline.QueryAndGenerateAsync` reicht das Token weiter. Komponenten müssen kooperieren; abgeschlossene Such- oder Toolaktionen werden nicht rückgängig gemacht.

RAG ermöglicht es dem Modell, Fragen auf Basis deiner eigenen Dokumente zu beantworten, indem zur Abfragezeit relevante Abschnitte abgerufen werden.

Soll der Benutzer eine Antwort auf Basis seiner Dokumente während der Entstehung verfolgen oder abbrechen können, kann die RAG-Suche mit einem Run kombiniert werden. Ablauf und Grenzen zusätzlicher Anweisungen erklärt die [Run-Anleitung](execution-api-transition.md).


Bei einer `IAIService`-Referenz verwenden Sie `GetLastProcessing()` aus `Mythosia.AI.Extensions`. Es liest das optionale `IAIProcessingInfoService` und liefert ohne Diagnoseunterstützung eine leere Liste. `IAIService` erhält keine Pflichtmitglieder. In RAG gilt `RagEnabledService.WithSpeed(...)` für die nächste Antwort nach der Suche, und `LastProcessing` beschreibt diese Antwort. Interne Suchanfragen-Umschreibung bleibt getrennt; Run-Ergebnisse liefern dieselben `Processing`-Einträge. [WithSpeed](request-building.md#inference-speed)

## Installation

```bash
dotnet add package Mythosia.AI.Rag
```

## Schnellstart

Verwende `.WithRag()` auf einem beliebigen `IAIService`, um RAG mit einer Fluent-API zu aktivieren:

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("handbuch.txt")
        .AddDocument("richtlinie.txt")
    );

var response = await service.GetCompletionAsync("Was ist die Rückgaberichtlinie?");
```

Die Dokumente werden automatisch aufgeteilt, eingebettet und gespeichert. Bei der Abfrage werden die relevantesten Abschnitte abgerufen und in den Prompt injiziert.

Mit der optionalen Vorschau `Mythosia.AI.Rag.Search.Pixie` lässt sich lokale neuronale Sparse-Suche mit der bisherigen Suche vergleichen. Der Anbieter dichter Embeddings bleibt erhalten; PIXIE nutzt einen Index im Arbeitsspeicher. Persistente Speicher werden nicht migriert und die Standardsuche wird nicht ersetzt. [PIXIE einrichten und vergleichen (Englisch)](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## Anhänge anhand eigener Dokumente erklären

Um ein Produktfoto anhand Ihres Handbuchs zu erklären, übergeben Sie eine `Message` mit Frage und Bild an `RagEnabledService.GetCompletionAsync(Message)` oder `StartRunAsync(Message)`. Beide erhalten nicht textuelle Anhänge in der Anfrage an den inneren KI-Dienst. Die Suche verwendet den Nachrichtentext; Anhänge selbst werden nicht automatisch indiziert oder eingebettet. Anbieter und Modell müssen den Anhangstyp unterstützen. Der gefundene Kontext wird nur der ausgehenden Anfrage hinzugefügt: Er überschreibt weder die ursprüngliche `Message` noch den Benutzertext im Gesprächsverlauf.

Wenn eine Antwort sowohl ein Handbuch als auch den aktuellen Lagerbestand benötigt, kombinieren Sie RAG mit Ihren registrierten Tools. Während der Tool-Aufrufe von `GetCompletionAsync` bleibt der Suchkontext an der ursprünglichen Eingabe erhalten; jedes spätere Tool-Ergebnis wird unverändert an das Modell gesendet. Der Gesprächsverlauf behält die ursprüngliche Benutzereingabe.

<a id="retrieval-modes"></a>

## Die passende Dokumentsuche wählen

Produktcodes profitieren von Stichwortsuche, anders formulierte Fragen von semantischer Suche. Der gewählte Retriever bereitet nur die benötigte Darstellung vor; Stichwortsuche benötigt kein vorheriges Anfrage-Embedding mehr.

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` überspringt Anfrage-Embeddings. Beim Einlesen werden Dokumente weiterhin geteilt und für den vorhandenen Vektorspeicher eingebettet; dies ist keine rein textbasierte Indizierungs-API. Bei verzögerter Initialisierung können daher während der ersten Anfrage Dokument-Embeddings anfallen.

Siehe [Suchmodi und Speicherunterstützung](rag-hybrid-search.md) und [eigene Retriever](rag-pipeline.md#custom-retriever).

## Dokumente hinzufügen

Mehrere Quellentypen werden unterstützt:

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // lokale Datei
    .AddUrl("https://example.com/dok.txt")        // URL
    .AddText("Inline-Inhalt kann hier rein.")      // direkte Zeichenkette
)
```

`AddUrl` prüft und dekomprimiert unterstützte HTTP-Komprimierungen vor dem Lesen des Textes und weist unvollständige, nicht unterstützte oder mehrschichtige Kodierungen ab. Siehe [URL-Dekomprimierung und Abbruch](rag-pipeline.md#url-documents).

<a id="document-identity"></a>

### Dateien mit gleichem Namen unterscheiden

Zwei Unternehmen können jeweils eine `docs/faq.txt` bereitstellen. Beide Dokumente sollen im Index bleiben, während die erneute Registrierung derselben Datei ihre Identität beibehält:

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

Im standardmäßigen RAG-Speicherablauf wird die Dokument-ID vor dem Senden an den Vektorspeicher erzeugt. Anschließend werden Datensätze mit derselben `document_id` ersetzt. Unser PostgreSQL-Speicher (pgvector) verwendet diese ID; er prüft den ursprünglichen Dateipfad nicht selbst. Zuvor wurde bei der Verzeichnisregistrierung sowohl für `company-a/docs/faq.txt` als auch für `company-b/docs/faq.txt` die ID `faq.txt` gesendet. Dadurch ersetzte das zweite Dokument das erste. Die Korrektur behält beim Erzeugen der ID den vollständigen Pfad bei; das PostgreSQL-Schema bleibt unverändert. Ein `full_path`-Filter in einem Speicherbeispiel verwendet vom Aufrufer bereitgestellte Metadaten; er erzeugt nicht automatisch eindeutige Dokument- oder Datensatz-IDs.

Die integrierten `PlainTextDocumentLoader` und `DirectoryDocumentLoader` verwenden den mit `Path.GetFullPath` normalisierten absoluten Dateipfad als `Source` und automatische Dokument-ID. Dateien in verschiedenen Verzeichnissen erhalten dadurch unterschiedliche IDs. Relative, absolute und `./`-Pfade verwenden dieselbe ID, wenn sie einschließlich Groß-/Kleinschreibung denselben absoluten Pfad ergeben. Halten Sie bei relativen Pfaden das Arbeitsverzeichnis konstant. Beim Verschieben von Dateien sowie bei symbolischen Links, Hardlinks oder abweichender Groß-/Kleinschreibung ist dieselbe ID nicht garantiert.

`AddText(..., id: ...)`, eine ausdrücklich gesetzte `RagDocument.Id` und die `Source`-Regeln eigener Loader bleiben unverändert. Die Aufruf-API muss nicht geändert werden. Da `Source` dieser integrierten Loader nun absolut ist, können auch Standardquellenangaben absolute Pfade anzeigen. Verwenden Sie für die Anzeige `filename` oder die Metadaten `relative_path` des Standardverzeichnisloaders. Die konfigurierbare Verzeichnisüberladung ergänzt `relative_path` nicht automatisch.

**Bestehende Indizes:** Alte relative IDs werden nicht automatisch gelöscht oder migriert. Indizieren Sie möglichst alle Dokumente in einer neuen Collection, prüfen Sie diese und stellen Sie dann die Anwendung um. Bei Wiederverwendung einer Collection löschen Sie nur alte Dokument-IDs, deren Zuordnung Sie geprüft haben, und indizieren anschließend deren Quelldateien neu. Löschen Sie nicht pauschal nach Dateinamen: Andere Verzeichnisse können gleichnamige Dokumente enthalten.

Damit Aktualisierungen und Löschungen auf das richtige Dokument beschränkt bleiben, ist `document_id` für die Pipeline reserviert. Vor dem Speichern erhält jeder Datensatz die tatsächliche `RagDocument.Id`, selbst wenn die Eingabemetadaten einen anderen Wert enthalten. Die Metadaten-Wörterbücher des Eingabedokuments und des Splitters werden nicht verändert; auch benutzerdefinierte Speicher-Callbacks erhalten normalisierte Datensätze. Verwenden Sie für anwendungseigene IDs einen anderen Schlüssel.

Bereits mit einer falschen `document_id` gespeicherte Datensätze werden dadurch nicht repariert. Erstellen Sie aus vertrauenswürdigen Quelldokumenten eine neue Collection, oder prüfen Sie die Zuordnung und bereinigen Sie nur betroffene Datensätze vor der Neuindizierung. Allein die Neuindizierung unter der richtigen ID findet alte Datensätze unter einer anderen ID nicht zuverlässig.

Dieselbe Datei muss über relative und absolute Pfade dasselbe Dokument aktualisieren; gleichnamige Dateien in verschiedenen Ordnern müssen getrennt bleiben. `WordDocumentLoader`, `ExcelDocumentLoader`, `PowerPointDocumentLoader` und `PdfDocumentLoader` setzen `DoclingDocument.Source` wie die integrierten TXT-Loader auf den normalisierten absoluten Dateipfad. RAG leitet daraus automatische Dokument-IDs ab; explizite IDs bleiben unter Kontrolle des Aufrufers. Standardquellenangaben können deshalb absolute Pfade zeigen.

[Eine stabile Identität für jede Datei behalten](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### Ein Dokument leeren, ohne alte Suchtreffer zu behalten

Wenn Sie eine veraltete Erstattungsrichtlinie leeren und erneut indizieren, darf ihr alter Text nicht weiter in Antworten erscheinen. Bei der standardmäßigen RAG-Speicherung ersetzt eine erfolgreiche Aufteilung mit null Chunks die Datensätze der entsprechenden `document_id` durch eine leere Menge. Es werden keine Embeddings angefordert; andere Dokument-IDs bleiben unverändert. Das gilt für leere Dokumente oder reine Leerzeichen, wenn der Splitter null Chunks liefert, ebenso wie für benutzerdefinierte Splitter mit einem erfolgreichen leeren Ergebnis.

Verwenden Sie bei einer bereits konfigurierten `RagPipeline` namens `pipeline` dieselbe gespeicherte Dokument-ID:

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

Später können Sie unter derselben ID wieder Inhalt indizieren. Ein Loader, der keine Dokumente zurückgibt, oder ein Dokument, das in einer späteren Dateiliste fehlt, ist keine Löschanweisung: Es wurde keine Dokument-ID zum Ersetzen übergeben.

Ausnahmen beim Laden, Parsen oder Aufteilen sowie vor dem Speicheraufruf erkannter Abbruch lassen die gespeicherten Datensätze dieses Dokuments unverändert. Loader und Parser müssen Fehler als Ausnahmen melden; ein erfolgreiches Ergebnis mit null Chunks lässt sich nicht von absichtlichem Leeren unterscheiden. Nach Beginn der Speicherung hängt ein Rollback bei Fehlern oder Abbruch vom Speicher ab; PostgreSQL verwendet für das Ersetzen eine Transaktion. Stapel werden dokumentweise verarbeitet und setzen bereits abgeschlossene Dokumente nicht zurück.

**Eigene Speicherung:** Mit `onDocumentEmbedded` bleibt die Speicherung Aufgabe des Callbacks. Bei null Chunks wird weder der Callback aufgerufen noch auf den Standardspeicher zugegriffen. Die Anwendung muss die bekannte Dokument-ID im eigenen Speicher explizit löschen oder `DeleteDocumentAsync` für den Speicher der Pipeline verwenden.

## Benutzerdefinierter Embedding-Anbieter

Standardmäßig nutzt RAG den integrierten lokalen Embedding-Anbieter. Für ein dediziertes Embedding-Modell:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("wissensdatenbank.txt")
    );
```

## Benutzerdefinierter Vektorspeicher

Standardmäßig wird ein In-Memory-Speicher verwendet. Für den Produktivbetrieb binde einen persistenten Vektorspeicher ein:

```csharp
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("grosser-korpus.txt")
    );
```

## Abfrageoptionen

Das Retrieval-Verhalten pro Abfrage feinjustieren:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,              // Anzahl der abzurufenden Abschnitte
        MinScore = 0.7         // Minimale Ähnlichkeitsbewertung
    }
};

var response = await service.GetCompletionAsync("Deine Frage", options: options);
```

Wenn Ihr Index bereits beim Modellanbieter liegt, vergleichen Sie RAG mit der [nativen Dateisuche und den gemeinsamen Reasoning-Optionen](reasoning-and-search.md).

## Nächste Schritte

- [Hybridsuche](rag-hybrid-search.md) — semantische und Stichwortsuche kombinieren
- [Query-Rewriting](rag-query-rewriting.md) — Abfragen mit Gesprächskontext optimieren
- [Re-Ranking](rag-reranking.md) — Suchergebnis-Genauigkeit weiter verbessern
- [Pipeline-Anpassung](rag-pipeline.md) — feingranulare Steuerung des RAG-Prozesses
- [Agentisches RAG](rag-agentic.md) — AI entscheidet selbst, wann und was gesucht wird
- [Vektorspeicher](vectordb-overview.md) — persistente Speicher einrichten
- [Text-Splitter](text-splitters.md) — Anpassen der Dokument-Segmentierung

Perplexity: [Vektoren im eigenen Dokumentenindex nutzen / Suchen, ohne eine Antwort zu erzeugen](perplexity.md).
