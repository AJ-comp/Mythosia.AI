# Re-Ranking & Retrieval-Tuning

> 📍 **Fragen & Antworten Pipeline:** [Query-Umschreibung](rag-query-rewriting.md) → Filtering → Embedding (bei Bedarf) → [Retrieval](rag-hybrid-search.md) → **`Re-Ranking`** → Kontextaufbau

Die Anfragephase `Embedding` hängt nun vom Retriever ab; Stichwortsuche meldet sie nicht. Eigene Retriever können passende Phasen über `request.ProgressAsync` melden. Dokument-Embeddings bleiben unverändert.

## Warum Re-Ranking?

Die Vektorsuche gibt Kandidaten sortiert nach Embedding-Ähnlichkeit zurück, aber Embedding-Ähnlichkeit ist eine **Annäherung**. Ein Abschnitt mit Bewertung 0,82 kann tatsächlich relevanter sein als einer mit 0,85 — das Embedding konnte sie einfach nicht auseinanderhalten.

Ein **Re-Ranker** nimmt die erste Kandidatenliste und bewertet jeden Abschnitt gegen die ursprüngliche Abfrage mit einem leistungsfähigeren Modell, was eine deutlich genauere Relevanzreihenfolge ergibt. Das ist besonders wertvoll wenn:

- Dein Korpus viele ähnlich aussehende Abschnitte enthält (z. B. FAQ-Einträge)
- Die obersten Ergebnisse der Vektorsuche sich „nah dran, aber nicht ganz richtig" anfühlen
- Du hochpräzise Antworten für kritische Anwendungsfälle brauchst

## Re-Ranker-Optionen

### LLM Reranker

Nutzt deinen KI-Service zum Bewerten der Ergebnisse. Effektiv, aber erhöht die Latenz:

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("korpus.txt")
)
```

Damit Fragen und Dokumente einer Bewertung nicht mit früheren Bewertungen oder dem Gesprächsverlauf des Services vermischt werden, verwendet `LlmReranker` für jede Bewertung eine zustandslose Anfrage. Der Gesprächsverlauf wird weder gelesen noch ergänzt. Die Standardeinstellungen des Services und dein Aufrufcode bleiben unverändert. Bewertungen durch Re-Ranker, die denselben KI-Service gemeinsam nutzen, werden nacheinander verarbeitet.

### Cohere Reranker

Ruft die Cohere Rerank API auf — schnell und präzise:

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("korpus.txt")
)
```

### vLLM Reranker

Nutzt einen lokal gehosteten vLLM Re-Ranking-Endpunkt:

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("korpus.txt")
)
```

## Retrieval-Parameter

Steuere, wie viele Kandidaten abgerufen und wie sie vor der Endauswahl gefiltert werden:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // Endanzahl der zurückgegebenen Abschnitte
    .WithRetrievalMultiplier(3)    // TopK × 3 Kandidaten abrufen (für Re-Ranking)
    .WithScoreThreshold(0.6)       // Minimale Ähnlichkeitsbewertung
    .AddDocument("korpus.txt")
)
```

- **`TopK`** — wie viele Abschnitte im LLM-Kontext landen
- **`RetrievalMultiplier`** — breiter suchen, damit der Re-Ranker mehr zur Auswahl hat. Ein Multiplikator von 3 bedeutet: 15 Kandidaten abrufen, dann die besten 5 nach Re-Ranking behalten.
- **`WithScoreThreshold`** — alles unter diesem Ähnlichkeitsschwellenwert verwerfen, auch wenn weniger als `TopK` Abschnitte übrig bleiben

## Finale Auswahlmodus

Wenn ein Re-Ranker verwendet wird, wähle, wie die finale Rangbewertung berechnet wird:

```csharp
using Mythosia.AI.Rag;

// Standard: nur Re-Ranker-Bewertungen vertrauen
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// Retrieval-Bewertung und Re-Ranker-Bewertung mischen
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)  // 65% Retrieval, 35% Re-Ranker
```

**`RerankerOnly`** ist der sichere Standard — das Urteil des Re-Rankers ersetzt die ursprüngliche Retrieval-Bewertung vollständig.

**`WeightedBlend`** behält das ursprüngliche Retrieval-Signal und bezieht das Re-Ranker-Urteil mit ein. Das kann helfen, wenn deine Vektor-Embeddings bereits hochwertig sind und du möchtest, dass der Re-Ranker eher als Tiebreaker als als vollständige Überschreibung fungiert.

Reine Vektor- und Stichwortmodi behalten native Scores. Konfigurierbare Hybridsuche nutzt auch mit einem Zweig normalisierte gewichtete RRF; Vektorgewicht 0 überspringt Anfrage-Embeddings. Scores sind keine Wahrscheinlichkeiten. `WeightedBlend` mischt Such- und Reranker-Scores ohne Kalibrierung; für unkalibrierte Stichwortscores empfiehlt sich der Standard `RerankerOnly`.
