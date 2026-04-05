# Hybridsuche

> 📍 **Fragen & Antworten Pipeline:** [Query-Umschreibung](rag-query-rewriting.md) → Embedding → Filtering → **`Retrieval`** → [Re-Ranking](rag-reranking.md) → Kontextaufbau

## Warum Hybridsuche?

Reine Vektorsuche ist gut darin, semantische Bedeutung zu erfassen — „Abonnement kündigen" passt zu „Mitgliedschaft beenden", obwohl sie keine Wörter teilen. Bei **exakten Begriffen** wie Produktnamen, Fehlercodes oder Richtlinienkennzeichen, die Nutzer wörtlich eintippen, kann sie aber versagen.

BM25-Schlüsselwortsuche behandelt diese Fälle hervorragend, scheitert aber am semantischen Verständnis. **Hybridsuche kombiniert beides** und bietet das Beste aus beiden Welten: semantisches Verständnis plus präzise Schlüsselwortsuche.

## Konfiguration

Dense-Vektorsuche mit BM25-Schlüsselwortsuche mit einem einzigen Methodenaufruf kombinieren:

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% Vektor, 40% BM25
    .AddDocument("wissensdatenbank.txt")
)
```

`vectorWeight` reicht von 0,0 (reines BM25) bis 1,0 (reine Vektorsuche). Ein Wert von **0,5–0,7** funktioniert in den meisten Fällen gut.

## Wann was verwenden

| Szenario | Empfohlenes Gewicht |
| --- | --- |
| Allgemeine Fragen in natürlicher Sprache | 0,7–0,8 (mehr Vektor) |
| Technische Doku mit spezifischen Begriffen | 0,4–0,5 (ausgewogen) |
| Code- oder Fehlercode-Suche | 0,2–0,3 (mehr BM25) |

## Beispiel

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("produktkatalog.txt")
        .AddDocument("fehlercodes.txt")
    );

// "ERR-4012" wird von BM25 gefunden; semantischer Kontext von der Vektorsuche
var answer = await service.GetCompletionAsync("Wie behebe ich ERR-4012?");
```
