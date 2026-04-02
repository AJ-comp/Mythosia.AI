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
