# Text-Splitter

Ein Suchtreffer braucht genügend Kontext für die Antwort, ohne das gesamte Dokument als eine Einheit einzubetten. Chunking steuert diesen Kompromiss. Diese Splitter arbeiten lokal mit Regeln und benötigen kein KI-Modell. Wählen Sie nach der Dokumentstruktur und prüfen Sie die Suche mit eigenen Fragen.

## Verfügbare Splitter

### CharacterTextSplitter

Für Klartext, bei dem eine einfache Größenbegrenzung genügt. Der konfigurierte Trenner wird bevorzugt, Sätze können aber geteilt werden. `RagBuilder` verwendet standardmäßig `CharacterTextSplitter(300, 30)`; die Endung `.md` wählt nicht automatisch den Markdown-Splitter.

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter (empfohlener Standard)

Für Fließtext, dessen Absätze und Wörter möglichst zusammenbleiben sollen. Die Standardreihenfolge lautet Leerzeile → Zeilenumbruch → `. ` → Leerzeichen → einzelne Zeichen. Dies sind Textregeln, keine semantische Modellentscheidung; Punkt plus Leerzeichen ist nur eine Annäherung an eine Satzgrenze.

Doppelte Einträge in `Separators` werden in der Reihenfolge ihres ersten Auftretens nur einmal angewendet. Ein mehrfach eingetragener Trenner erzeugt keinen zusätzlichen Durchlauf. Lange Trennerlisten werden ohne tief verschachtelte rekursive Aufrufe verarbeitet.

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

Nur für eine grobe Zählung durch Leerraum getrennter Wörter. Trotz des Namens zählen `MaxTokensPerChunk` und `TokenOverlap` Einheiten nach `TokenSeparators` (standardmäßig Leerzeichen, Tabulatoren und Zeilenumbrüche), keine Modell-Token. Die Ausgabe vereinheitlicht Trenner zu Leerzeichen. Text ohne Leerraum kann eine einzige lange Einheit bleiben; Modell-Tokenlimits werden nicht garantiert.

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

Für Markdown-Dokumentation oder Markdown aus Office/HWP-Loadern, wenn Überschriftenkontext, Tabellenzeilen und Codeblöcke erhalten bleiben sollen. Erkennt ATX-Überschriften (`#`–`######`), Code-Fences und Tabellen. Es ist ein regelbasierter Splitter, kein vollständiger Markdown-Syntaxbaumparser. Der Konstruktor akzeptiert nur `chunkSize`; Markdown hat keinen Overlap-Parameter.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### Qualität der Tabellenaufteilung

Erkannte GFM-Tabellen werden zwischen Zeilen geteilt; Kopf- und Trennzeile werden in jedem Tabellen-Chunk wiederholt. Äußere Pipes sind optional (`Name | Value` wird unterstützt). Spaltenbezeichnungen bleiben erhalten; die Suchqualität hängt weiterhin von Dokumenten, Embeddings und Fragen ab.

Eine fettgedruckte Tabellenzelle gehört zu ihrer Zeile: `**Keine Erstattung**` bei Firma A darf beispielsweise nicht zur Bedingung für Firma B werden. Tabellenzellen und Codeblöcke werden nie zu wiederholten Textlabels. Eine eigenständige Zeile `**Label**` wird nur am Absatz- oder Textblockanfang nach einer Leerzeile oder Strukturgrenze erkannt. Eine umgebrochene fettgedruckte Zeile innerhalb eines Absatzes erzeugt weder einen neuen Absatz noch ein wiederholtes Label. Ein erkanntes Label darf in den Teilstücken dieses Textblocks wiederholt werden; eine Tabelle, ein Codezaun, eine Überschrift oder das nächste erkannte Label beendet den Geltungsbereich.

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

#### Code-Block-Schutz

Mit Backticks oder Tilden eingefasste Blöcke bleiben vollständig erhalten. Die schließende Fence muss dasselbe Zeichen verwenden und mindestens so lang wie die öffnende sein. Eine kürzere Fence im Block beendet ihn nicht. Der vollständige Block darf `ChunkSize` überschreiten.

Die Einrückung des öffnenden Codezauns bleibt zusammen mit dem Code erhalten, damit sich dessen gerenderte Einrückung beim Aufteilen nicht verändert. Die Angaben zum öffnenden Zaun werden einmal pro Block gelesen, statt einen langen Zaun für jede Inhaltszeile erneut zu analysieren.

#### Überschriften-Breadcrumb

`IncludeHeadingBreadcrumb` ist standardmäßig `true`: Jeder Chunk wiederholt seinen Überschriftenpfad, damit der Kontext im Suchtreffer erhalten bleibt. `false` schaltet nur diese Wiederholung aus; ursprüngliche Überschriften bleiben erhalten. Auch Abschnitte, die nur eine Überschrift enthalten, werden ausgegeben.

`MinSplitHeadingLevel` akzeptiert 1–6 und legt fest, welche Überschriftenebenen Abschnitte beginnen; Standard ist 1. Wenn sich eine übergeordnete Überschrift ändert, endet der bisherige untergeordnete Abschnitt, damit sein alter Überschriftenpfad nicht auf den neuen Inhalt übertragen wird.

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## Parameter wählen

`CharacterTextSplitter`, `RecursiveTextSplitter` und `MarkdownTextSplitter` messen UTF-16-Codeeinheiten (`string.Length`), keine Modell-Token oder sichtbaren Schriftzeichen. Surrogatpaare wie Emoji werden nicht geteilt. Bei Größe 1 darf ein Paar mit 2 Einheiten die Grenze überschreiten. Kombinierende Zeichen und vollständige Graphemcluster bleiben nicht garantiert zusammen.

Größen müssen positiv und Overlaps nichtnegativ sein. Ungültige Einstellungen lösen vor der Verarbeitung `ArgumentOutOfRangeException` aus; veränderliche Einstellungen werden beim Teilen erneut geprüft. Ein Overlap größer oder gleich der Größe deaktiviert die Überlappung aus Kompatibilitätsgründen. Bei Character/Recursive ist der Overlap ein Zielwert, angepasst an Trenner, Unicode-Grenzen und freien Platz im nächsten Chunk; `0` bedeutet keine Überlappung. Ein zusätzlicher Chunk nur mit der letzten Überlappung entfällt.

Bei Markdown ist `ChunkSize` das Inhaltsbudget **ohne den wiederholten Überschriftenpfad**. Ein ganzer Codeblock oder Tabellenkopf plus eine vollständige Zeile darf es überschreiten. Normaler Text hält die Größe bis auf die genannte Surrogatpaar-Ausnahme ein.

Wiederholte Überschriften und Tabellenköpfe dürfen kleine Dokumente nicht zu unbegrenzt großem Embedding-Text vergrößern. Markdown begrenzt deshalb die gesamte Ausgabe pro Dokument auf `max(65536, 32 × document.Content.Length)` UTF-16-Einheiten. Gezählt wird die Summe aller fertigen Chunks einschließlich wiederholter Breadcrumbs, Tabellenköpfe und Labels. Die Prüfung erfolgt vor dem Erzeugen übermäßiger Wiederholungen; bei Überschreitung wird `InvalidOperationException` ausgelöst, ohne Inhalte abzuschneiden oder Teilergebnisse zurückzugeben. `ChunkSize` und die Ausnahmen für unteilbare Blöcke gelten innerhalb dieses Gesamtlimits. Im normalen RAG-Indizierungsablauf tritt der Fehler vor Embedding oder Datensatzersetzung auf, sodass der bestehende Dokumentindex unverändert bleibt. Dies ist ein Textausgabelimit, kein Modelltoken- oder Prozessspeicherlimit. Das Budget gilt je `Split`-Aufruf und wächst mit der Eingabelänge; es ist keine feste maximale Dokumentgröße.

Beginnen Sie etwa mit `RecursiveTextSplitter(500, 50)` für Fließtext oder `MarkdownTextSplitter(500)` für Markdown und messen Sie mit repräsentativen Fragen. Größere Chunks enthalten mehr Umfeld; Overlap wiederholt Inhalt und erhöht den Embedding-Aufwand. Beides garantiert keine bessere Suche.

Für strikte Modelllimits zählen Sie jeden fertigen Chunk einschließlich wiederholter Überschriften und Tabellenköpfe mit dem Tokenizer des Zielmodells. Zeichen-/Wortzahlen und sprachabhängige Umrechnungen sind keine sicheren Tokenbudgets. Implementieren Sie bei Bedarf `ITextSplitter` mit diesem Tokenizer.

Die Korrekturen ändern Chunk-Grenzen betroffener Dokumente. Indexieren Sie dieselben Dokument-IDs erneut, um alte Chunks zu ersetzen, und aktualisieren Sie betroffene Embedding-Caches und Bewertungsbaselines. Gespeicherte Chunks werden nicht automatisch umgeschrieben.

## Splitter pro Dokument

Verschiedene Splitter können pro Dokument im `RagBuilder` angewendet werden:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "daten.txt", new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // Standard für den Rest
)
```

## Benutzerdefinierter Splitter

Wenn du ein eigenes Splitter-Modul schreiben und einbinden möchtest, implementiere `ITextSplitter`:

Die Indexierung darf keinen Erfolg melden, während ein Chunk einen anderen überschreibt. Gib jedem Chunk eine nicht leere ID, die innerhalb der Sammlung eindeutig ist, und kopiere die Dokumentmetadaten, damit Firmen- oder Zugriffsfilter erhalten bleiben. Dieses Beispiel kombiniert Dokument-ID und Chunk-Index. Die Pipeline lehnt fehlende IDs und Duplikate innerhalb eines Dokuments ab; sie erzeugt keine Ersatz-IDs. Siehe [Validierung beim Indexieren](rag-pipeline.md#indexing-validation).

```csharp
public class SatzSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split(". ");
        return sentences.Select((s, i) => new RagChunk
        {
            Id = $"{document.Id}_chunk_{i}",
            Content = s,
            Index = i,
            DocumentId = document.Id,
            Metadata = new Dictionary<string, string>(document.Metadata)
        }).ToList();
    }
}

// Registrieren:
.WithTextSplitter(new SatzSplitter())
```
