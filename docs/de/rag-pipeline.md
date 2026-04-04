# RAG-Pipeline-Anpassung

## Warum die Pipeline anpassen?

Die Standard-RAG-Pipeline funktioniert gut von Anfang an, aber reale Projekte brauchen oft mehr Kontrolle:

- **Debugging** — welche Stufe ist langsam? Ändert der Rewriter die Abfrage auf unerwartete Weise?
- **Prompt-Engineering** — das Standard-Prompt-Template passt möglicherweise nicht zum Ton oder den Einschränkungen deiner Domäne
- **Architektur** — mehrere Services, die einen Index teilen, sparen Speicher und halten Embeddings konsistent
- **Inspektion** — manchmal muss man sehen, was das Retrieval liefert, *bevor* es ans LLM gesendet wird

Dieses Kapitel behandelt die Werkzeuge, die dir diese Kontrolle geben.

## Fortschrittsüberwachung

Verfolge, welche RAG-Stufe gerade ausgeführt wird, über einen asynchronen Callback pro Abfrage:

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // Stufen: QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
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
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDocuments("docs/")
    .BuildAsync();

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
   Query-Umschreibung → Embedding → Retrieval → Kontextaufbau
    ↓
② TemplateContextBuilder ersetzt {context} und {question}
   → "Beantworte anhand folgender Infos.\n[1] Rückgabe innerhalb 30 Tagen...\nFrage: Was ist die Rückgaberichtlinie?"
    ↓
③ RagEnabledService erzeugt einen AIRequestContext
   RequestMessageOverride = zusammengesetzter Prompt
    ↓
④ _innerService.GetCompletionAsync(ursprüngliche Nachricht, context)
   → AIService speichert den Context in AsyncLocal
   → Die ursprüngliche Frage wird im Gesprächsverlauf abgelegt
    ↓
⑤ AIService.GetLatestMessages() tauscht die letzte Nachricht aus
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
// Innerhalb von RagEnabledService.GetCompletionAsync
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← ursprüngliche Frage (wird im Verlauf gespeichert)
    context: BuildRequestContext(processed));    // ← zusammengesetzter Prompt (nur das Modell sieht ihn)

// BuildRequestContext — erzeugt den AIRequestContext
private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)  // ← Ergebnis des TemplateContextBuilder
    };
}
```

`AIService` speichert diesen Context in `AsyncLocal` und tauscht in `GetLatestMessages()` die letzte Nachricht gegen das `RequestMessageOverride` aus. Nach Abschluss der Anfrage wird der Zustand automatisch zurückgesetzt — die nächste Anfrage bleibt davon vollständig unberührt.
