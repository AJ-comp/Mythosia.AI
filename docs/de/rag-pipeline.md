# RAG-Pipeline-Anpassung

<a id="indexing-validation"></a>

## Vorhandene Dokumente bei Indexierungsfehlern schützen

Fehlerhafte benutzerdefinierte Splitter oder Embedding-Antworten dürfen ein durchsuchbares Dokument nicht stillschweigend durch unvollständige oder falsch zugeordnete Inhalte ersetzen. Die Pipeline prüft jedes Dokument vor dem Speichern, auch bei Verwendung von `onDocumentEmbedded`.

Vor Embedding, Speicherung oder Speicher-Callback löst eine `RagDocument.Id`, die null, leer oder ausschließlich Whitespace ist, eine `ArgumentException` aus. Ungültige Splitter-Ergebnisse führen zu `InvalidOperationException`: eine null-Liste oder ein null-Chunk, null in `Content` oder `Metadata`, eine leere bzw. reine Whitespace-Chunk-ID oder doppelte Chunk-IDs innerhalb desselben Dokuments. Duplikate werden mit `StringComparer.Ordinal` unter Beachtung der Groß-/Kleinschreibung geprüft. Chunk-Werte und Metadaten werden vor dem ersten Embedding-Aufruf kopiert.

Gültige benutzerdefinierte IDs bleiben unverändert. Es gibt keine automatische Erzeugung, Kürzung von Leerraum oder Reparatur; Kollisionen benutzerdefinierter Chunk-IDs zwischen verschiedenen Dokumenten werden nicht global erkannt. Verwende innerhalb der Zielsammlung eindeutige IDs, wie im [Beispiel für benutzerdefinierte Splitter](text-splitters.md). Der reservierte Schlüssel `document_id` wird nur in der Speicherkopie normalisiert; Quellmetadaten bleiben unverändert.

Ungültige IDs, Splitter-Fehler und ungültige Embedding-Batches lassen die bisherigen Datensätze dieses Dokuments unverändert und rufen den Speicher-Callback nicht auf. Alle Batches des Dokuments müssen die [Embedding-Prüfung](rag-embedding.md#embedding-validation) bestehen, bevor gespeichert wird. Bereits zuvor abgeschlossene Dokumente derselben Operation werden nicht zurückgesetzt. Ein Rollback nach Beginn der Speicherung hängt vom Store oder Callback ab.

Diese Prüfungen und Korrekturen der Antwortreihenfolge stellen bereits überschriebene Inhalte oder zuvor falsch zugeordnete gespeicherte Vektoren nicht automatisch wieder her; indexiere betroffene Dokumente erneut aus ihren Originalquellen.

<a id="custom-persistence"></a>

## Im Persistenz-Callback das gesamte Dokument ersetzen

Wird ein Dokument kürzer, lässt ein Upsert nur der neuen Chunks alte Endstücke durchsuchbar. `onDocumentEmbedded` ersetzt die Standardpersistenz vollständig. Ersetzen Sie daher anhand der normalisierten `document_id` in den Datensätzen das gesamte Dokument. Der Callback erhält jeweils ein geprüftes, nicht leeres Dokument:

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

Eine erfolgreiche Aufteilung mit null Chunks ruft weder den Callback noch den Standardspeicher auf. Löschen Sie die bekannte Dokument-ID ausdrücklich im eigenen Speicher; `DeleteDocumentAsync` richtet sich an den Speicher der Pipeline. Atomarität und Rollback des Ersetzens hängen vom Speicher oder Callback ab.

<a id="url-documents"></a>

## URL-Dokumente sicher lesen

Ein Server kann Textdokumente für die Übertragung komprimieren. `AddUrl` dekomprimiert `gzip`, `deflate` und Brotli (`br`) vor dem Lesen des Textes und prüft, ob der komprimierte Datenstrom vollständig ist. Eine erfolgreiche HTTP-Übertragung allein genügt nicht: Abgeschnittene komprimierte Daten, Dekomprimierungsfehler oder Fehler bei den vom Format bereitgestellten Prüfsummen brechen das Laden vor Embedding und Speicherung ab; die bisherigen Datensätze dieses Dokuments bleiben erhalten. Nicht unterstützte oder mehrschichtige `Content-Encoding`-Werte werden ebenfalls vor Embedding und Speicherung abgewiesen.

Um nicht weiter auf ein langsames URL-Dokument zu warten, übergeben Sie `cancellationToken` an `RagStore.BuildAsync`. Das Token erreicht die HTTP-Anfrage, das Lesen des Antwortinhalts und die Dekomprimierung. Der Abbruch ist kooperativ und macht bereits abgeschlossene Dokumentspeicherungen nicht rückgängig.

<a id="custom-retriever"></a>

## Retriever ohne verpflichtende Embeddings anbinden

Produktcodes profitieren von Stichwortsuche, anders formulierte Fragen von semantischer Suche. Der gewählte Retriever bereitet nur die benötigte Darstellung vor; Stichwortsuche benötigt kein vorheriges Anfrage-Embedding mehr.

- Vorher: Jede Suchstrategie erhielt ein Anfrage-Embedding.
- Nachher: Der ausgewählte Retriever erstellt nur die benötigte Darstellung.

Implementieren Sie `IRagRetriever` für externe Indizes oder andere Anfrage-Darstellungen. `RagRetrievalRequest` enthält `Query` (vollständige semantische Anfrage), optionales `TextQuery` (lexikalische Überschreibung), `TopK`, `Filter` und `ProgressAsync`. Eingebaute Retriever nutzen bei null `Query`; ein leerer Text überspringt den Textzweig. Eigene Retriever verantworten Vorbereitung, Filter, Ergebnislimit und Abbruch.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

Registrieren Sie mit `UseRetriever(...)` oder `RagPipeline.SetRetriever(...)`. `IRetrievalStrategy` und `SetRetrievalStrategy(...)` bleiben über einen Adapter verfügbar, der Anfrage-Embeddings erstellt. Ergebnisse brauchen Inhalt und Metadaten für Reranking und Kontextaufbau.

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` überspringt Anfrage-Embeddings. Beim Einlesen werden Dokumente weiterhin geteilt und für den vorhandenen Vektorspeicher eingebettet; dies ist keine rein textbasierte Indizierungs-API. Bei verzögerter Initialisierung können daher während der ersten Anfrage Dokument-Embeddings anfallen.

Die Anfragephase `Embedding` hängt nun vom Retriever ab; Stichwortsuche meldet sie nicht. Eigene Retriever können passende Phasen über `request.ProgressAsync` melden. Dokument-Embeddings bleiben unverändert.

## Warum die Pipeline anpassen?

Die Standard-RAG-Pipeline funktioniert gut von Anfang an, aber reale Projekte brauchen oft mehr Kontrolle:

- **Debugging** — welche Stufe ist langsam? Ändert der Rewriter die Abfrage auf unerwartete Weise?
- **Prompt-Engineering** — das Standard-Prompt-Template passt möglicherweise nicht zum Ton oder den Einschränkungen deiner Domäne
- **Architektur** — mehrere Services, die einen Index teilen, sparen Speicher und halten Embeddings konsistent
- **Inspektion** — manchmal muss man sehen, was das Retrieval liefert, *bevor* es ans LLM gesendet wird

Dieses Kapitel behandelt die Werkzeuge, die dir diese Kontrolle geben.

Für Fortschrittsanzeige und Abbruch während der Antwort lässt sich die angepasste Pipeline mit einem Run verbinden. Wann die Suche stattfindet und was zusätzliche Anweisungen verändern, beschreibt die [Run-Anleitung](execution-api-transition.md).

## Fortschrittsüberwachung

Verfolge, welche RAG-Stufe gerade ausgeführt wird, über einen asynchronen Callback pro Abfrage:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Stufen: QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("Deine Frage", options);
```

Das ist unschätzbar wertvoll für die Latenzmessung — du kannst die Zeit zwischen Stufen messen, um Engpässe zu finden.

## Benutzerdefiniertes Prompt-Template

Steuere, wie der abgerufene Kontext in den Prompt injiziert wird, mit den Platzhaltern `{context}` und `{question}`:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        Beantworte die Frage ausschließlich auf Basis der folgenden Informationen.
        Wenn die Antwort nicht im Kontext enthalten ist, sage "Ich weiß es nicht."

        Kontext:
        {context}

        Frage: {question}
        """)
    .AddDocument("faq.txt")
)
```

Ein gut formuliertes Template kann Halluzinationen deutlich reduzieren, indem das Modell angewiesen wird, sich auf den bereitgestellten Kontext zu beschränken.

## RagStore teilen

Den Index einmal aufbauen und über mehrere Service-Instanzen wiederverwenden — nützlich für Anbietervergleiche oder A/B-Tests:

```csharp
// Einmal aufbauen
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

// Über Services wiederverwenden
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

Beide Services teilen dieselben Embeddings und den gleichen Vektorindex — keine doppelten Speicher- oder Rechenkosten.

## RagStore direkt abfragen

Den Store unabhängig von einem KI-Service abfragen, um zu inspizieren, was abgerufen würde:

```csharp
RagProcessedQuery result = await store.QueryAsync("Was ist die Rückgaberichtlinie?");

Console.WriteLine($"Umgeschriebene Abfrage: {result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` enthält den vollständig zusammengesetzten Prompt, der ans LLM gesendet würde. Das ist extrem nützlich für das Debugging der Retrieval-Qualität, ohne LLM-Tokens zu verbrauchen.

## Wie es intern funktioniert

Wenn du `.WithRag()` aufrufst, wird im Hintergrund ein `RagEnabledService` erzeugt — ein Wrapper um deinen eigentlichen AIService. Dieser Wrapper verbindet die RAG-Pipeline automatisch mit dem LLM-Aufruf. Das zentrale Bindeglied dabei ist [AIRequestContext](request-contexts.md).

### Der vollständige Ablauf

```
ragService.GetCompletionAsync("Was ist die Rückgaberichtlinie?")
    ↓
① RagEnabledService führt die RAG-Pipeline aus
   Query-Umschreibung → Filtering → Embedding (bei Bedarf) → Retrieval → Kontextaufbau
    ↓
② TemplateContextBuilder ersetzt {context} und {question}
   → "Beantworte anhand folgender Infos.\n[1] Rückgabe innerhalb 30 Tagen...\nFrage: Was ist die Rückgaberichtlinie?"
    ↓
③ RagEnabledService erzeugt einen AIRequestContext
   RequestMessageOverride = zusammengesetzter Prompt
    ↓
④ _innerService.GetCompletionAsync(ursprüngliche Nachricht, context: context)
   → AIService speichert den Context in AsyncLocal
   → Die ursprüngliche Frage wird im Gesprächsverlauf abgelegt
    ↓
⑤ AIService.GetLatestMessages() ersetzt die ursprüngliche Eingabe der aktuellen Anfrage
   Gesprächsverlauf: "Was ist die Rückgaberichtlinie?" (Original bleibt erhalten)
   Was das Modell sieht: zusammengesetzter Prompt (RequestMessageOverride)
```

### Warum dieses Design?

Der Kerngedanke ist die **Trennung von Gesprächsverlauf und Modelleingabe**:

- **Im Gesprächsverlauf bleibt die ursprüngliche Frage** — damit Folgefragen wie „und wie genau?" den richtigen Bezug behalten
- **Das Modell erhält den zusammengesetzten Prompt** — inklusive der gefundenen Dokumente und der Frage
- **Der Zustand des AIService wird nicht verändert** — `AsyncLocal<T>` sorgt für saubere Isolation pro Anfrage

Genau so nutzt die RAG-Pipeline die Eigenschaft `RequestMessageOverride`, die in der [AIRequestContext-Dokumentation](request-contexts.md) beschrieben wird. Weil dieser Mechanismus automatisch greift, reicht ein einfacher `.WithRag()`-Aufruf.

### Ein Blick in den Code

Die entscheidende Stelle im `RagEnabledService`, an der Pipeline und LLM-Aufruf verbunden werden:

```csharp
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
var original = new Message(ActorRole.User, query);
return await _innerService.GetCompletionAsync(
    original,
    context: BuildRequestContext(processed, original),
    cancellationToken: cancellationToken);

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
{
    var requestMessage = original.Clone();
    requestMessage.Content = processed.RequestMessageContent;
    if (original.HasMultimodalContent)
    {
        requestMessage.Contents = new List<MessageContent>
        {
            new TextContent(processed.RequestMessageContent)
        };
        requestMessage.Contents.AddRange(
            original.Contents.Where(content => !(content is TextContent)));
    }
    return new AIRequestContext { RequestMessageOverride = requestMessage };
}
```

`AIService` speichert den Kontext in `AsyncLocal`. `GetLatestMessages()` wendet `RequestMessageOverride` auf die ursprüngliche Eingabe der aktuellen logischen Anfrage an und erhält spätere Tool-Aufrufe des Assistenten sowie Tool-Ergebnisse. So werden gefundene Dokumente und Tool-Ausgaben gemeinsam in folgenden Modellanfragen übermittelt. Nach Abschluss wird der vorherige Kontext wiederhergestellt.
