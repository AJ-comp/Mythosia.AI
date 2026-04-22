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

Ein strukturbewusster Splitter, der die Markdown-Hierarchie (H1–H6), Code-Fences und Tabellen versteht und Inhalte in semantisch sinnvolle Einheiten aufteilt:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

Ideal für Dokumentationsdateien, README-Dateien und die Ausgabe von strukturierten Dokument-Loadern wie Office und HWP.

> [!TIP]
> Dokument-Loader für Word, Excel, PowerPoint und HWP konvertieren Dokumente intern in Markdown. Die Verwendung von `MarkdownTextSplitter` mit diesen Dokumenten stellt sicher, dass Tabellen- und Code-Block-Strukturen während des Chunking-Prozesses erhalten bleiben.

#### Qualität der Tabellenaufteilung

`MarkdownTextSplitter` teilt Markdown-Tabellen **zeilenweise** auf. Eine Zeile wird niemals mittendrin getrennt, und jeder resultierende Chunk enthält automatisch die **Kopfzeile und die Trennlinie**:

```
Original-Tabelle:
| Name   | Abt.   | Gehalt   |
|--------|--------|----------|
| Alice  | Entw.  | 50.000 € |
| Bob    | PM     | 48.000 € |
| Carol  | Design | 45.000 € |

→ Chunk 1:
| Name   | Abt.   | Gehalt   |
|--------|--------|----------|
| Alice  | Entw.  | 50.000 € |
| Bob    | PM     | 48.000 € |

→ Chunk 2:
| Name   | Abt.   | Gehalt   |
|--------|--------|----------|
| Carol  | Design | 45.000 € |
```

Jeder Chunk ist eine eigenständige, gültige Tabelle — das garantiert die Qualität von Embedding und Retrieval.

#### Code-Block-Schutz

Code-Fence-Blöcke (`` ``` ``) werden als **atomare Einheiten** behandelt. Ein Code-Block wird niemals aufgeteilt, auch wenn er die Chunk-Größe überschreitet — so bleibt die Code-Semantik erhalten.

#### Überschriften-Breadcrumb

Jedem Chunk wird automatisch der Überschriftenpfad zu seinem Inhalt vorangestellt, was den Kontext für die Vektorsuche bereichert:

```
# Produkthandbuch
## Installationsanleitung
### Windows

(eigentlicher Inhalt dieses Abschnitts)
```

Diese Funktion wird über die Eigenschaft `IncludeHeadingBreadcrumb` gesteuert (Standard: `true`).

## Parameter wählen

| Parameter | Auswirkung |
|-----------|--------|
| `chunkSize` (größer) | Mehr Kontext pro Abschnitt, weniger Abschnitte, günstigere Einbettung |
| `chunkSize` (kleiner) | Höhere Präzision beim Retrieval, mehr Abschnitte, mehr Einbettungen |
| `chunkOverlap` | Verhindert Informationsverlust an Abschnittsgrenzen |

Ein guter Startpunkt: `chunkSize: 500, chunkOverlap: 50`.

## Chunk-Größe und Token-Anzahl (mehrsprachig)

`chunkSize` wird in **Zeichen** gemessen, aber die Limits der Embedding-Modelle gelten für **Token**. Die gleiche Zeichenzahl kann je nach Sprache sehr unterschiedliche Token-Zahlen erzeugen:

| Sprache | 1.000 Zeichen ≈ Token | Empfohlene chunkSize |
|---------|-----------------------|----------------------|
| Englisch | ~250 Token | 500–2.000 |
| Koreanisch / Japanisch / Chinesisch | ~800–1.500 Token | 300–1.000 |

> [!WARNING]
> CJK-Text (Koreanisch, Japanisch, Chinesisch) hat ein deutlich höheres Token-pro-Zeichen-Verhältnis als Englisch. Wenn Chunks das Token-Limit des Embedding-Modells überschreiten (z. B. 2.048 Token), tritt ein Fehler auf. Reduzieren Sie `chunkSize` bei CJK-Dokumenten großzügig.

Beispiel mit einem Embedding-Modell mit 2.048-Token-Limit:

```csharp
// Englische Dokumente: 2000 Zeichen ≈ 500 Token → viel Spielraum
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// Koreanische Dokumente: 1000 Zeichen ≈ 1000 Token → sicherer Bereich
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

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

Wenn du ein eigenes Splitter-Modul schreiben und einbinden möchtest, implementiere `ITextSplitter`:

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
