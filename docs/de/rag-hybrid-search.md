# Hybridsuche

Produktcodes profitieren von Stichwortsuche, anders formulierte Fragen von semantischer Suche. Der gewählte Retriever bereitet nur die benötigte Darstellung vor; Stichwortsuche benötigt kein vorheriges Anfrage-Embedding mehr.

## Eingebaute Suchmodi

```csharp
// Semantische Suche (Standard)
.UseVectorSearch()

// Stichwortsuche ohne Anfrage-Embedding
.UseKeywordSearch()

// Gewichtete Hybridsuche
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` überspringt Anfrage-Embeddings. Beim Einlesen werden Dokumente weiterhin geteilt und für den vorhandenen Vektorspeicher eingebettet; dies ist keine rein textbasierte Indizierungs-API. Bei verzögerter Initialisierung können daher während der ersten Anfrage Dokument-Embeddings anfallen.

## Stichwort- und semantische Ergebnisse kombinieren

`VectorWeight` bestimmt das Vektorgewicht (0–1), `1 - VectorWeight` das Stichwortgewicht. `CandidateMultiplier` steuert die Kandidatenmenge je Suchzweig, `RrfK` die Rangglättung der gewichteten Reciprocal Rank Fusion. Diese Werte sind vom Kandidatenmultiplikator des RAG-Rerankers unabhängig. Bewerten Sie sie mit repräsentativen Dokumenten und Fragen.

Reine Vektor- und Stichwortmodi behalten native Scores. Konfigurierbare Hybridsuche nutzt auch mit einem Zweig normalisierte gewichtete RRF; Vektorgewicht 0 überspringt Anfrage-Embeddings. Scores sind keine Wahrscheinlichkeiten. `WeightedBlend` mischt Such- und Reranker-Scores ohne Kalibrierung; für unkalibrierte Stichwortscores empfiehlt sich der Standard `RerankerOnly`.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## Speicherunterstützung und Kompatibilität

InMemory, PostgreSQL und Qdrant unterstützen neue Stichwortsuche und konfigurierbare gewichtete RRF. Textscores unterscheiden sich: InMemory nutzt BM25, PostgreSQL konfigurierte Volltext- oder Trigrammsuche, Qdrant seinen Sparse-Index. Scores verschiedener Engines sind nicht gleichzusetzen.

Pinecone behält den nativen Hybridpfad über `UseHybridSearch()` mit Standardwerten auf einem kompatiblen `dotproduct`-Index. Dieser Adapter unterstützt weder Stichwortmodus noch konfigurierbare gewichtete RRF mit beiden Suchzweigen. Andere Speicher brauchen die entsprechenden optionalen Schnittstellen. Nicht unterstützte Modi oder Optionen führen ausdrücklich zu Fehlern, statt zur Vektorsuche zu wechseln oder Gewichte zu ignorieren.

Die vorhandenen InMemory-, PostgreSQL- und Qdrant-Adapter installieren kein neuronales Modell und migrieren keine Indizes. `C#` und `C++` werden entsprechend dem jeweiligen Analyzer unterschieden. Auch für die folgende PIXIE-Option muss die exakte Identifikatorsuche geprüft werden.

Siehe [Suchmodi und Speicherunterstützung](rag.md#retrieval-modes) und [eigene Retriever](rag-pipeline.md#custom-retriever).

<a id="pixie-search"></a>

## Lokale neuronale Suche mit PIXIE vergleichen

Wenn Frage und Dokument unterschiedlich formuliert sind, kann gelernte Sparse-Suche verwandte Begriffe ergänzen. Das optionale Paket `Mythosia.AI.Rag.Search.Pixie` kodiert Dokumente und Fragen lokal mit PIXIE und kombiniert die Ergebnisse mit vorhandenen dichten Embeddings. PIXIE braucht weder Python-Server noch API-Schlüssel; gewählte Anbieter für dichte Embeddings oder Antworten können weiterhin eine externe API nutzen.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

Bei diesem Speicher wählt `UseKeywordSearch()` neuronale Sparse-Suche. Das dichte Anfrage-Embedding entfällt, die PIXIE-Inferenz für die Frage wird weiterhin ausgeführt. RAG erzeugt beim Indexieren weiterhin dichte Dokument-Embeddings. `UseHybridSearch(...)` vereint Sparse-Skalarprodukt- und dichte Kosinus-Ranglisten mit dem konfigurierten gewichteten RRF.

Diese Vorschau stellt `PixieInMemoryStore` mit einem Index im Arbeitsspeicher bereit. Sie bindet PIXIE nicht an PostgreSQL, Qdrant oder Pinecone an. Nach Neustarts oder Modell-/Konfigurationsänderungen ist neu zu indexieren. Den Encoder während aller Speicherzugriffe aktiv halten und anschließend freigeben. Die bisherige Suche bleibt Standard; vergleichen Sie dieselben Dokumente und bewerteten Fragen vor dem Wechsel. PIXIE garantiert weder exakte `C#`/`C++`-Unterscheidung noch Ausschlussbedingungen.

[PIXIE einrichten und vergleichen (Englisch)](../rag-pixie-search.md).
