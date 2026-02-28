# Mythosia.AI Loaders ガイド

## 概要

Loaders はファイルソースを `RagDocument` に変換し、RAG パイプラインで使用します。通常は
ローダーパッケージをインストールして `RagBuilder` で取り込めば十分です。独自の取り込みが
必要な場合のみ `IDocumentLoader` を実装してください。

## パッケージ

- **Mythosia.AI.Loaders.Abstractions** — `IDocumentLoader`, `RagDocument`, `ParsedDocument`
- **Mythosia.AI.Loaders.Office** — Word/Excel/PowerPoint (.docx/.xlsx/.pptx) OpenXml ベース
- **Mythosia.AI.Loaders.Pdf** — PDF ローダー（PdfPig パーサー）

## インストール

```bash
dotnet add package Mythosia.AI.Loaders.Office
dotnet add package Mythosia.AI.Loaders.Pdf
```

## クイックスタート

```csharp
using Mythosia.AI.Rag;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("docs/manual.pdf")
        .AddDocument("docs/plan.docx")
        .AddDocuments("./knowledge-base")
    );
```

`AddDocument(...)` と `AddDocuments(...)` は拡張子に応じて自動的にローダーを選択します:

- `.docx` -> `WordDocumentLoader`
- `.xlsx` -> `ExcelDocumentLoader`
- `.pptx` -> `PowerPointDocumentLoader`
- `.pdf` -> `PdfDocumentLoader`
- それ以外 -> `PlainTextDocumentLoader`

## 拡張子ルーティング

ファイル種別ごとにローダーやスプリッターを変えたい場合に使用します。

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Loaders;
using Mythosia.AI.Loaders.Pdf;
using Mythosia.AI.Loaders.Office.Word;
using Mythosia.AI.Rag.Splitters;

var service = new ClaudeService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocuments("./docs", src => src
            .WithExtension(".pdf")
            .WithLoader(new PdfDocumentLoader())
            .WithTextSplitter(new CharacterTextSplitter(800, 80))
        )
        .AddDocuments("./docs", src => src
            .WithExtension(".docx")
            .WithLoader(new WordDocumentLoader())
            .WithTextSplitter(new TokenTextSplitter(600, 60))
        )
        .AddDocuments("./docs", src => src
            .WithTextSplitter(new CharacterTextSplitter(400, 40))
        )
    );
```

## Office ローダー

対応フォーマット: **Word (.docx)**, **Excel (.xlsx)**, **PowerPoint (.pptx)**。

```csharp
using Mythosia.AI.Loaders.Office;
using Mythosia.AI.Loaders.Office.Word;

var options = new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
};

var loader = new WordDocumentLoader(options: options);

.WithRag(rag => rag.AddDocuments(loader, "docs/report.docx"));
```

`OfficeParserOptions` はすべての OpenXml パーサーに適用されます:

- `IncludeMetadata`
- `NormalizeWhitespace`
- `IncludeSheetNames` (Excel)
- `IncludeSlideNumbers` (PowerPoint)

## PDF ローダー

```csharp
using Mythosia.AI.Loaders.Pdf;

var options = new PdfParserOptions
{
    IncludeMetadata = true,
    IncludePageNumbers = false,
    NormalizeWhitespace = true
};

var loader = new PdfDocumentLoader(options: options);

.WithRag(rag => rag.AddDocuments(loader, "docs/manual.pdf"));
```

### カスタムパーサー

既定のパーサーを置き換える場合は、`IDocumentParser` を実装し、`CanParse` が PDF を処理できるようにして渡します:

```csharp
using Mythosia.AI.Loaders.Pdf;

var loader = new PdfDocumentLoader(parser: new MyPdfParser());
```

## カスタムローダー

```csharp
using Mythosia.AI.Loaders;

public class MarkdownDocumentLoader : IDocumentLoader
{
    public Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken ct = default)
    {
        var text = File.ReadAllText(source);
        return Task.FromResult<IReadOnlyList<RagDocument>>(new[]
        {
            new RagDocument
            {
                Id = Path.GetFileName(source),
                Content = text,
                Source = source,
                Metadata = { ["type"] = "markdown" }
            }
        });
    }
}
```

ビルダー登録:

```csharp
.WithRag(rag => rag.AddDocuments(new MarkdownDocumentLoader(), "docs/intro.md"))
```

## メタデータ参照

よく使うメタデータキー:

- Office: `type=office`, `office_type`, `filename`, `extension`, `parser`
- PDF: `type=pdf`, `filename`, `extension`, `parser`

パーサー固有のメタデータは保持されます。
