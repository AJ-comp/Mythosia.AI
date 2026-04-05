# ドキュメントローダー

ドキュメントローダーはファイルを構造化された`DoclingDocument`オブジェクトに解析し、RAGパイプラインに渡すことができます。

## インストール

OfficeとPDFローダーは`Mythosia.AI.Rag`に含まれます。単独使用する場合:

```bash
dotnet add package Mythosia.Documents.Office
dotnet add package Mythosia.Documents.Pdf
```

## サポート形式

| ローダー | 形式 | パッケージ |
|---------|------|---------|
| `PdfDocumentLoader` | `.pdf` | `Mythosia.Documents.Pdf` |
| `WordDocumentLoader` | `.docx` | `Mythosia.Documents.Office` |
| `ExcelDocumentLoader` | `.xlsx` | `Mythosia.Documents.Office` |
| `PowerPointDocumentLoader` | `.pptx` | `Mythosia.Documents.Office` |
| `HwpDocumentLoader` | `.hwp` | `Mythosia.Documents.Hwp` |
| `PlainTextDocumentLoader` | `.txt`、`.md`など | `Mythosia.AI.Rag` |

## PDF

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions
{
    Password = "secret",           // 暗号化されたPDF用
    IncludeMetadata = true,        // タイトル、著者を抽出
    IncludePageNumbers = true,     // ページ番号マーカーを追加
    NormalizeWhitespace = true     // 余分な空白を除去
});

var docs = await loader.LoadAsync("report.pdf");
```

## Word (.docx)

```csharp
var loader = new WordDocumentLoader(new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("document.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(new OfficeParserOptions
{
    IncludeSheetNames = true,  // 各セクションにシート名を先頭に追加
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(new OfficeParserOptions
{
    IncludeSlideNumbers = true,  // 各セクションにスライド番号を先頭に追加
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("presentation.pptx");
```

## HWP (.hwp)

韓国語ワープロ形式（HWP）ファイルを解析します。別パッケージとして提供されています：

```bash
dotnet add package Mythosia.Documents.Hwp
```

```csharp
var loader = new HwpDocumentLoader(options: new HwpParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true,
    IncludeSectionHeaders = false
});

var docs = await loader.LoadAsync("report.hwp");
```

HWPローダーはテキスト、テーブル、見出し構造を`DoclingDocument`に変換し、最終的にMarkdown形式で出力します。テーブルはMarkdownテーブル（`| ... |`）として変換されるため、`MarkdownTextSplitter`と併用するとチャンキング時にもテーブル構造が完全に保持されます。

## RAGでの使用

ローダーは`RagBuilder`で`.AddDocument()`を使用する際に自動的に統合されます。手動でロードして結果を追加するには:

```csharp
var loader = new PdfDocumentLoader(new PdfParserOptions { IncludePageNumbers = true });
var docs = await loader.LoadAsync("report.pdf");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("report.pdf")  // 形式を自動検出
        .AddDocument("notes.docx")
    );
```

## DoclingDocument構造

読み込まれた各ファイルは階層的な要素ツリーを持つ`DoclingDocument`になります:

```csharp
var docs = await loader.LoadAsync("report.pdf");
var doc = docs[0];

Console.WriteLine(doc.Title);   // ドキュメントタイトル
Console.WriteLine(doc.Source);  // ファイルパス

foreach (var item in doc.Document)
{
    switch (item)
    {
        case SectionHeaderItem h: Console.WriteLine($"## {h.Text}"); break;
        case TextItem t:          Console.WriteLine(t.Text); break;
        case TableItem table:     /* テーブルセルを処理 */ break;
        case CodeItem code:       Console.WriteLine(code.Text); break;
    }
}
```

**要素タイプ:** `TextItem`、`SectionHeaderItem`、`TitleItem`、`ListItem`、`TableItem`、`CodeItem`、`FormulaItem`、`PictureItem`、`GroupItem`、`RefItem`

## ドキュメントローダーとテキストスプリッターの連携

Word、Excel、PowerPoint、HWPのドキュメントローダーは、内部的に`DoclingDocument`を経由して**Markdown形式**に変換します。この過程でテーブルはMarkdownテーブル（`| ヘッダー |` + `|---|` + `| データ |`）に、見出しやコードブロックもMarkdown構文で出力されます。

そのため、Office/HWPドキュメントには`MarkdownTextSplitter`が最も効果的です：

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000, 100))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000, 100))
    );
```

`MarkdownTextSplitter`はテーブルを行単位で分割し、各チャンクにヘッダーを自動的に含めるため、検索結果でもテーブルデータが完全な形で返されます。詳細は[テキストスプリッター](text-splitters.md)を参照してください。
