# Query-Umschreibung

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
    .WithQueryRewriter()             // Nutzt denselben KI-Service
    .WithQueryRewriteMaxTokens(250)  // Token-Budget für das Umschreiben
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

## Wie das Such-Gate funktioniert

Nicht jede Nutzernachricht benötigt eine Dokumentensuche. Der Rewriter klassifiziert die Abfrage und gibt bei solchen Nachrichten ein leeres Ergebnis zurück:

- „Danke!"
- „Verstanden, das hilft sehr."
- „Kannst du zusammenfassen, was du gerade gesagt hast?"

Wenn das Gate auslöst, wird die gesamte Retrieval-Pipeline übersprungen — kein Embedding, keine Vektorsuche, kein Re-Ranking — und das LLM antwortet direkt aus dem Gesprächskontext.
