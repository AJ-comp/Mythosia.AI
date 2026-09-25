# Query-Umschreibung

> 📍 **Fragen & Antworten Pipeline:** **`Query-Umschreibung`** → Filtering → Embedding (bei Bedarf) → [Retrieval](rag-hybrid-search.md) → [Re-Ranking](rag-reranking.md) → Kontextaufbau

Die Anfragephase `Embedding` hängt nun vom Retriever ab; Stichwortsuche meldet sie nicht. Eigene Retriever können passende Phasen über `request.ProgressAsync` melden. Dokument-Embeddings bleiben unverändert.

## Warum Query-Umschreibung?

In einem Mehrturngespräch verwenden Nutzer natürlich Pronomen und kurze Referenzen:

> Nutzer: „Erkläre mir die Rückgaberichtlinie."
> Nutzer: „Was sind Ausnahmen **davon**?"

Wenn „Was sind Ausnahmen davon?" so an den Vektorspeicher gesendet wird, hat das Embedding keine Ahnung, worauf sich „davon" bezieht. Die Suche liefert irrelevante Ergebnisse, und die Antwort leidet darunter.

**Query-Umschreibung** löst diese Referenzen vor dem Retrieval auf und erweitert „davon" zu „Ausnahmen von der Rückgaberichtlinie", damit das Embedding die vollständige Absicht erfasst. Außerdem implementiert sie ein **Such-Gate** — wenn die Abfrage kein Retrieval benötigt (z. B. „Danke!"), wird die Vektorsuche komplett übersprungen, was Latenz und Kosten spart.

## Konfiguration

Ein `LlmQueryRewriter` nutzt den KI-Service selbst, um die Abfrage vor der Einbettung umzuschreiben:

```csharp
.WithRag(rag => rag
    .WithQueryRewriter(250)          // Nutzt denselben KI-Service
    .AddDocument("doku.txt")
)
```

Der Rewriter untersucht den Gesprächskontext und produziert eine eigenständige Suchanfrage, die der Vektorspeicher ohne Verlaufswissen versteht.

## Mehrturngespräch-RAG

Beim direkten Abfragen des `RagStore` den Gesprächsverlauf mitgeben, damit der Rewriter Referenzen auflösen kann:

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("Was ist die Rückgaberichtlinie?", "Artikel können innerhalb von 30 Tagen zurückgegeben werden."),
    new ConversationTurn("Was gilt für digitale Produkte?", "Digitale Produkte sind von der Rückgabe ausgeschlossen.")
};

var result = await store.QueryAsync(
    query: "Gibt es dazu Ausnahmen?",
    conversationHistory: history
);
```

Der Rewriter sieht den vollständigen Verlauf und schreibt „Gibt es dazu Ausnahmen?" in etwas wie „Ausnahmen von der Nicht-Rückgabe-Regel für digitale Produkte" um, was deutlich bessere Retrieval-Ergebnisse liefert.

<a id="runtime-query-rewriter"></a>

## Umschreibung während laufender Abfragen ändern

> Damit Änderungen zur Laufzeit bestehende `WithRag(store)`-Wrapper erreichen, ist `Mythosia.AI.Rag` 8.1.1 oder neuer erforderlich. [Patch-Hinweise](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v811).

Mit `store.SetQueryRewriter(rewriter)` können Sie einen Rewriter hinzufügen oder ersetzen, ohne den Index neu aufzubauen; `store.SetQueryRewriter(null)` deaktiviert die Umschreibung und die Ableitung von Suchbegriffen. Sowohl die Überladung von `RagStore.QueryAsync` mit `conversationHistory` als auch bereits über `service.WithRag(store)` verbundene Wrapper wählen für jede Anfrage den aktuellen Rewriter des Stores aus. Während eine Fortschrittsmeldung oder Umschreibung aussteht, behält die Anfrage ihre ausgewählte Instanz. Hinzufügen, Ersetzen oder Entfernen wirkt sich auf nachfolgende Anfragen aus, auch bei Suche, Antwortgenerierung, Streaming und Runs über diese Wrapper.

```csharp
var rag = service.WithRag(store);
store.SetQueryRewriter(rewriter);
var rewritten = await rag.RetrieveAsync("Welche Ausnahmen gelten für die Rückerstattungsrichtlinie?");

store.SetQueryRewriter(null);
var original = await rag.RetrieveAsync("Welche Ausnahmen gelten für die Rückerstattungsrichtlinie?");
```

Der mit `WithQueryRewriter()` aktivierte Standard-`LlmQueryRewriter` wird bei der verzögerten Initialisierung einmal erstellt; nach dem Entfernen wird er bei der nächsten Anfrage nicht neu angelegt. Store-Überladungen ohne `conversationHistory` umgehen die Umschreibung weiterhin. Das gilt auch für Agentic RAG, bei dem der Agent die Suchanfrage formuliert.

## Wie das Such-Gate funktioniert

Nicht jede Nutzernachricht benötigt eine Dokumentensuche. Der Rewriter klassifiziert die Abfrage und gibt bei solchen Nachrichten ein leeres Ergebnis zurück:

- „Danke!"
- „Verstanden, das hilft sehr."
- „Kannst du zusammenfassen, was du gerade gesagt hast?"

Wenn das Gate auslöst, wird die gesamte Retrieval-Pipeline übersprungen — kein Embedding, keine Vektorsuche, kein Re-Ranking — und das LLM antwortet direkt aus dem Gesprächskontext.
