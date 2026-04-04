# Text-Splitter

Text-Splitter teilen Dokumente in Abschnitte auf, bevor sie eingebettet werden. Abschnittsgröße und -überlappung beeinflussen die Retrieval-Qualität erheblich.

## Verfügbare Splitter

### CharacterTextSplitter

Teilt nach Zeichenanzahl. Einfach und schnell, kann aber mitten im Satz trennen:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (empfohlener Standard)

Versucht, an semantisch sinnvollen Grenzen in dieser Reihenfolge zu trennen: Absätze → Sätze → Wörter → Zeichen. Produziert zusammenhängendere Abschnitte:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Teilt nach Token-Anzahl statt Zeichenanzahl. Genauer für LLM-Kontextfenster-Budgetierung:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

Verwende diesen, wenn das Embedding-Modell strenge Token-Limits hat.

### MarkdownTextSplitter

Bewahrt die Markdown-Struktur — trennt an Überschriften, Listen und Code-Blöcken, bevor auf Zeichentrennung zurückgegriffen wird:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Ideal für Dokumentationsdateien, README-Dateien und strukturierten Markdown-Inhalt.

## Parameter wählen

| Parameter | Auswirkung |
|-----------|--------|
| `chunkSize` (größer) | Mehr Kontext pro Abschnitt, weniger Abschnitte, günstigere Einbettung |
| `chunkSize` (kleiner) | Höhere Präzision beim Retrieval, mehr Abschnitte, mehr Einbettungen |
| `chunkOverlap` | Verhindert Informationsverlust an Abschnittsgrenzen |

Ein guter Startpunkt: `chunkSize: 500, chunkOverlap: 50`.

## Splitter pro Dokument

Verschiedene Splitter können pro Dokument im `RagBuilder` angewendet werden:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "daten.txt", new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // Standard für den Rest
)
```

## Benutzerdefinierter Splitter

Implementiere `ITextSplitter` für vollständig eigene Trennlogik:

```csharp
public class SatzSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Content = s,
            Index = i,
            DocumentId = document.Id
        }).ToList();
    }
}

// Registrieren:
.WithTextSplitter(new SatzSplitter())
```
