# Embedding

> 📍 **Fragen & Antworten Pipeline:** [Query-Umschreibung](rag-query-rewriting.md) → [Filtering](rag-filtering.md) → **`Embedding (bei Bedarf)`** → [Retrieval](rag-hybrid-search.md) → [Re-Ranking](rag-reranking.md) → [Kontextaufbau](rag-context-build.md)

Die Anfragephase `Embedding` hängt nun vom Retriever ab; Stichwortsuche meldet sie nicht. Eigene Retriever können passende Phasen über `request.ProgressAsync` melden.

<a id="retrieval-aware-embeddings"></a>

## Dokumentkontext und Suchabsicht erhalten

Ein Abschnitt kann von benachbarten Passagen abhängen; Suchfrage und indiziertes Dokument haben unterschiedliche Aufgaben. RAG 8.2.0 ergänzt kontextuelle Voyage-Embeddings und Gemini Embedding 2 für aus TXT, Markdown und PDF extrahierten Text.

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

`IRetrievalEmbeddingProvider : IEmbeddingProvider` ist optional; bestehende Anbieter bleiben kompatibel. Die Indizierung übergibt ein unveränderliches `EmbeddingDocument(documentId, chunks, title)` mit sämtlichen Abschnitten in Reihenfolge, unabhängig von `EmbeddingBatchSize`. Der Titel stammt aus `RagDocument.Metadata["title"]`. Vektorsuche und Diagnose rufen `GetQueryEmbeddingAsync` auf; bisherige Anbieter behalten `GetEmbeddingsAsync`-Batches und `GetEmbeddingAsync`-Abfragen. Reine Stichwortsuche erzeugt kein Abfrage-Embedding.

Wählen Sie eine Embedding-Konfiguration pro Store:

```csharp
rag.UseVoyageEmbedding(voyageApiKey, httpClient,
    model: "voyage-context-4", dimensions: 1024,
    timeout: TimeSpan.FromSeconds(60));

rag.UseGeminiEmbedding(geminiApiKey, httpClient,
    model: "gemini-embedding-2", dimensions: 1536,
    timeout: TimeSpan.FromSeconds(60), maxConcurrency: 4);
```

### Voyage

`VoyageContextualizedEmbeddingProvider` verwendet standardmäßig `voyage-context-4` und 1024 Dimensionen (256, 512, 1024 oder 2048). Das gesamte Dokument wird als geordnete Gruppe mit `input_type=document` gesendet, eine einzelne Suchfrage mit `input_type=query`. Automatisches Aufteilen ist deaktiviert. Ein Dokument darf höchstens 16.000 Abschnitte enthalten; Tokenlimits prüft der Dienst. Generische Methoden lassen `input_type` weg und behandeln bis zu 1.000 Texte als unabhängige Einzelgruppen. Dokument-ID und Titel werden nicht gesendet. [Voyage API](https://docs.voyageai.com/docs/contextualized-chunk-embeddings).

Generische Batches prüfen beim Einlesen auf Abbruch. Sobald mehr als 1.000 Texte vorliegen, endet das Einlesen und der Batch wird ohne HTTP-Anfrage abgelehnt. Dokumentgruppen bleiben vollständig erhalten.

### Gemini

`GeminiEmbeddingProvider` verwendet standardmäßig `gemini-embedding-2`, 1536 Dimensionen (128–3072) und `maxConcurrency=4`. Jeder Abschnitt erhält über eine eigene HTTP-Anfrage einen Vektor. Sucheingaben verwenden `title: {title} | text: {text}` (fehlender Titel: `none`) oder `task: search result | query: {query}`. Präfixe gelten nur für HTTP-Eingaben; generische Methoden senden unveränderten Text. `embedContentConfig.autoTruncate=false` weist zu lange Eingaben zurück, statt sie zu kürzen. [Gemini API](https://ai.google.dev/gemini-api/docs/embeddings).

Beide Anbieter erhalten den gespeicherten Quelltext und prüfen Anzahl, Dimensionen und endliche Vektorwerte. Der übergebene `HttpClient` gehört weiterhin dem Aufrufer; seine Einstellungen bleiben unverändert. Voyage stellt Dokument- und Abschnittsreihenfolge anhand geprüfter Indizes wieder her. Fehler enthalten weder Schlüssel noch entfernte Nutzdaten; Abbruch wird weitergegeben, Zeitüberschreitungen erzeugen `TimeoutException`. Voyage setzt `timeout` pro Anfrage um, Gemini für den gesamten Vorgang einschließlich Wartezeiten auf Parallelität; das Clientlimit gilt zusätzlich. Eingaben werden nicht still aufgeteilt oder gekürzt. Fehler vor dem Speichern erhalten das bisherige Dokument; danach hängt Atomarität vom Store oder Callback ab. Nach Änderungen an Modell, Dimensionen oder Suchformat Dokumente neu indizieren und den Store auf denselben Vektorraum einstellen.

### Den tatsächlichen Dienst prüfen

Live-Tests senden synthetischen TXT-, Markdown- und PDF-Text und verursachen API-Kosten. Setzen Sie `MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE=1`, konfigurieren Sie Zugangsdaten und wählen Sie `All`, `Voyage` oder `Gemini`. Übersprungene oder nicht eindeutige Fälle führen zum Fehlschlag; Offline-Tests bestätigen keine Dienstverfügbarkeit.

```powershell
$env:MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE = "1"
pwsh -NoProfile -File build/test-retrieval-embedding-live.ps1 -Provider All
```

[Den tatsächlichen Dienst prüfen](https://github.com/AJ-comp/Mythosia.AI/blob/main/build/RELEASE.md#retrieval-embedding-live-validation).

## Was ist Embedding?

Embedding wandelt Text in **numerische Vektoren** (Zahlenarrays) um, die die Bedeutung erfassen. In diesem Vektorraum landen **Texte mit ähnlicher Bedeutung nah beieinander**.

Stellen Sie sich vor, Sie platzieren Städte auf einer Karte: geographisch nahe Städte liegen auch auf der Karte nah beieinander. Genauso erzeugen „Wie kündige ich mein Abo?" und „Ich möchte meine Mitgliedschaft beenden" ähnliche Vektoren — obwohl sie völlig andere Wörter verwenden.

Im RAG-Pipeline geschieht Embedding an zwei Stellen:

1. **Dokumentenindexierung** — jeder Chunk wird vektorisiert und gespeichert
2. **Query-Zeit** — die Benutzerfrage wird vektorisiert für den Ähnlichkeitsvergleich

## Integrierte Anbieter

Wählen Sie einen Embedding-Anbieter passend zu Dokumentensprache, Betriebsumgebung und Suchanforderungen.

### Perplexity

`PerplexityContextualizedEmbeddingProvider` implementiert jetzt `IRetrievalEmbeddingProvider` und lässt sich mit `.UseEmbedding(contextual)` registrieren. Die bisherige öffentliche Gruppenmethode `GetDocumentEmbeddingsAsync` und Binärmethoden bleiben erhalten. Die neue Einzeldokumentmethode implementiert das Interface explizit, sodass bestehende Aufrufe unverändert bleiben. RAG erhält Dokumentgrenzen und nutzt für Suchfragen dasselbe Kontextmodell und dieselben Dimensionen.

Perplexity akzeptiert bei Float- und Binärbatches bis zu 512 unabhängige Texte oder 512 Kontextdokumente mit insgesamt 16.000 Abschnitten. Beim Einlesen wird auf Abbruch geprüft; bei Überschreitung einer Grenze endet das Einlesen und der Batch wird vor einer HTTP-Anfrage abgelehnt. Dokumentgruppen und Reihenfolge bleiben erhalten. Details stehen im [Perplexity-Leitfaden](perplexity.md).

[Perplexity Agent API, Suche und Embeddings](perplexity.md).

### OpenAI

```csharp
var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Builder-Kurzform:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

<a id="openai-dimensions"></a>

`text-embedding-ada-002` hat eine feste Größe von **1536 Dimensionen**. Der Anbieter lässt das nicht unterstützte Feld `dimensions` bei Einzel- und Batchanfragen weg. Eine andere konfigurierte Größe führt vor jedem API-Aufruf zu einer `ArgumentOutOfRangeException`. Für `text-embedding-3-small` und `text-embedding-3-large` enthalten Anfragen weiterhin den konfigurierten Wert für `dimensions`.

### Ollama (lokal)

Embeddings lokal ausführen mit [Ollama](https://ollama.com/):

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

<a id="ollama-dimensions"></a>

Dokument- und Anfragevektoren müssen dasselbe Modell und dieselbe Dimensionszahl verwenden. `OllamaEmbeddingProvider` sendet die konfigurierten `dimensions` an `/api/embed` und prüft die Länge jedes zurückgegebenen Vektors. Der Provider verwendet weiterhin standardmäßig `qwen3-embedding:4b` mit **1024 angeforderten Dimensionen**; die native Modellausgabe hat 2560 Dimensionen. Ollama-Server und Modell müssen die angeforderte Größe unterstützen. Nicht unterstützte Anfragen oder Antworten, die diese Einstellung ignorieren, schlagen fehl, statt `Dimensions` stillschweigend zu ändern oder Vektoren lokal anzupassen.

Bei einem Modell- oder Dimensionswechsel erstellen Sie die Dokument-Embeddings mit denselben Einstellungen wie die Anfragevektoren neu und passen den Vektorspeicher an. Vorhandene Vektoren werden nicht automatisch umgewandelt.

### vLLM (selbst gehostet)

Für Teams mit eigenem [vLLM](https://docs.vllm.ai/)-Server:

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local (ohne API)

Leichtgewichtiger Anbieter basierend auf Feature-Hashing. Kein API-Schlüssel oder externer Dienst erforderlich — allerdings ist die Embedding-Qualität deutlich schlechter als bei neuronalen Modellen und wird daher **nicht für den produktiven Einsatz empfohlen**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **Tipp:** Verwenden Sie stattdessen `OpenAIEmbeddingProvider` mit dem Modell `text-embedding-3-small`. Es ist extrem günstig — nahezu kostenlos — und liefert deutlich bessere Ergebnisse.

## Batch-Verarbeitung

`EmbeddingBatchSize` steuert flache Batches bisheriger `IEmbeddingProvider`-Implementierungen. `IRetrievalEmbeddingProvider` erhält das gesamte Dokument und verwaltet HTTP-Batches selbst; ein kleinerer Wert teilt ein Voyage-Dokument nicht in separate Kontextgruppen.

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // Standard: 100 Chunks pro API-Aufruf
pipeline.Options = options;
```

`EmbeddingBatchSize` muss positiv sein. Die Pipeline prüft und erfasst den Wert zu Beginn jedes Dokument-Indizierungsaufrufs, noch vor Embedding oder Datensatzersetzung. Das verhindert leere Batch-Schleifen und übersprungene Chunks, wenn sich die Einstellung während eines asynchronen Aufrufs ändert. Spätere Aufrufe können den neuen Wert verwenden.

<a id="embedding-validation"></a>

## Vektoren den richtigen Chunks zuordnen

Auch eine erfolgreiche HTTP-Antwort kann fehlende Vektoren oder eine falsche Reihenfolge enthalten. Dadurch würde Text mit der Bedeutung eines anderen Chunks verknüpft. Ein benutzerdefinierter `IEmbeddingProvider` muss pro Eingabe genau ein nicht-null `float[]` in Eingabereihenfolge liefern und einen positiven Wert für `Dimensions` angeben. Jeder Vektor muss genau so viele endliche Elemente enthalten, ohne `NaN` oder Unendlich.

Beim Indexieren lehnt die Pipeline ungültige Dimensionen, Antwortanzahlen oder Vektoren mit `InvalidOperationException` vor Speicherung oder `onDocumentEmbedded` ab. Sie kopiert jeden akzeptierten Vektor vor der nächsten Batch-Anfrage, damit die spätere Wiederverwendung eines Provider-Puffers frühere Chunks nicht verändert. Zurückgegebene Daten müssen während des Lesens stabil bleiben; gleichzeitige Änderungen während Prüfung oder Kopieren werden nicht unterstützt. Bei einem Validierungsfehler bleiben die bisherigen Datensätze dieses Dokuments erhalten.

`OpenAIEmbeddingProvider` verlangt für jedes Antwortobjekt einen gültigen, eindeutigen `index` und stellt die Eingabereihenfolge wieder her. `VllmEmbeddingProvider` folgt derselben Regel, wenn Indizes vorhanden sind; aus Kompatibilitätsgründen akzeptiert er auch Antworten, in denen alle Objekte `index` weglassen, in Antwortreihenfolge. Teilweise fehlende, doppelte oder außerhalb des Bereichs liegende Indizes werden abgelehnt. Benutzerdefinierte Provider oder Antworten ohne Indizes müssen selbst die richtige Reihenfolge sicherstellen; Strukturprüfungen prüfen nicht die Bedeutung eines Vektors.

<a id="query-embedding-validation"></a>

## Den Fragevektor vor der Suche schützen

Ein wiederverwendeter Provider-Puffer darf eine Frage während wartender Fortschrittsmeldungen oder Suche nicht verändern. Die integrierte dichte Suche einschließlich des `IRetrievalStrategy`-Adapters verlangt positive `Dimensions`, einen nicht-null Vektor mit genau dieser Länge und endliche Werte. Ungültige Ausgaben lösen vor der Suche `InvalidOperationException` aus. Der gültige Vektor wird unmittelbar nach der Rückgabe vor weiteren Meldungen oder Suchaufrufen kopiert. Während des Lesens muss der Provider seine Daten stabil halten; eigene `IRagRetriever` übernehmen Vorbereitung und Prüfung selbst.

`OllamaEmbeddingProvider` prüft auch bei direkten Einzel- und Batch-Aufrufen Antwortstruktur, exakte Vektoranzahl, Dimensionen und endliche Werte. Fehlerhaftes JSON oder ungültige Vektoren führen zu `InvalidOperationException` statt unvollständiger Ergebnisse. Der übergebene `HttpClient` bleibt Eigentum des Aufrufers; das Freigeben einzelner HTTP-Anfragen und Antworten gibt ihn nicht frei.

## Dimensionen

| Anbieter | Modell | Standard-Dimensionen |
| --- | --- | --- |
| Voyage | voyage-context-4 | 1024 |
| Gemini | gemini-embedding-2 | 1536 |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 angefordert (nativ: 2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (Feature-Hashing) | 1024 |

## Eigener Embedding-Anbieter

Implementieren Sie `IEmbeddingProvider` für andere Dienste:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // Hier Ihre API aufrufen
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // Batch-Aufruf
    }
}
```

## Interner Ablauf

```
Benutzerfrage (string) → GetQueryEmbeddingAsync() / GetEmbeddingAsync() → Query-Vektor (float[])
```

Dieser Vektor wird an die nächste Stufe ([Filtering](rag-filtering.md)) und dann an das [Retrieval](rag-hybrid-search.md) weitergegeben.

## Nächste Schritte

- [Filtering](rag-filtering.md) — Chunks eingrenzen
- [Hybridsuche](rag-hybrid-search.md) — Vektor- und Stichwortsuche kombinieren
- [Pipeline-Anpassung](rag-pipeline.md) — Embedding-Anbieter serviceübergreifend teilen
