# ドキュメントローダー

ドキュメントローダーはファイルを構造化された`DoclingDocument`オブジェクトに解析し、RAGパイプラインに渡すことができます。

<a id="file-source-identity"></a>

## 登録パスが異なっても同じファイルを更新する

同じファイルを相対パスと絶対パスで登録しても一つの文書を更新し、別フォルダーの同名ファイルは区別する必要があります。`WordDocumentLoader`、`ExcelDocumentLoader`、`PowerPointDocumentLoader`、`PdfDocumentLoader` は、標準 TXT ローダーと同様に `DoclingDocument.Source` を正規化した絶対ファイルパスに設定します。RAG の自動文書 ID はこの値から生成され、明示的な ID は呼び出し側が管理します。既定の出典表示に絶対パスが現れる場合があります。

相対パス ID で保存済みのレコードは自動移行・削除されません。以前の文書 ID を確認して対象ストアからその文書だけを明示的に削除し、再インデックスしてください。または、新しい空のコレクションに全原本をインデックスし、検証後にアプリケーションを切り替えてください。既存コレクションで新しい絶対パス ID だけを登録しても旧レコードは残ります。無関係な文書は削除しないでください。[文書 ID と移行](rag.md#document-identity)を参照してください。

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
var loader = new PdfDocumentLoader(options: new PdfParserOptions
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
var loader = new WordDocumentLoader(options: new OfficeParserOptions
{
    IncludeMetadata = true,
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("document.docx");
```

## Excel (.xlsx)

```csharp
var loader = new ExcelDocumentLoader(options: new OfficeParserOptions
{
    IncludeSheetNames = true,  // 各セクションにシート名を先頭に追加
    NormalizeWhitespace = true
});

var docs = await loader.LoadAsync("spreadsheet.xlsx");
```

## PowerPoint (.pptx)

```csharp
var loader = new PowerPointDocumentLoader(options: new OfficeParserOptions
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
var loader = new PdfDocumentLoader(options: new PdfParserOptions { IncludePageNumbers = true });
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

## 処理パイプラインの概要

ドキュメントはRAG検索可能なチャンクになるまでに3つのステージを経ます。各ステージは異なるパッケージが担当します。

```text
┌─────────────────────────────────────────────────────────────┐
│  1. パース (Documents.Hwp / Documents.Office / Documents.Pdf)
│     .hwp, .pdf, .docx など → DoclingDocument（構造化モデル）
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  2. シリアライズ (Documents.Abstractions)
│     DoclingDocument → Markdown文字列
│     MarkdownSerializerが見出し、テーブル、コードブロックを
│     Markdown構文に変換します。
│     テーブルレンダリングはITableSerializerで交換可能です。
└──────────────────────────┬──────────────────────────────────┘
                           ↓
┌──────────────────────────┴──────────────────────────────────┐
│  3. チャンキング (AI.Rag)
│     Markdown文字列 → 検索可能なチャンクリスト
│     MarkdownTextSplitterが見出しベースでセクションに分割し、
│     段落 → 行 → 単語境界の順でカスケード分割します。
└─────────────────────────────────────────────────────────────┘
```

**ステージ1（パース）** — 各ドキュメントローダー（`HwpDocumentLoader`、`PdfDocumentLoader`など）が元のファイルを読み込み、テキスト、見出し、テーブル、コードブロックなどの要素をツリー構造で持つ`DoclingDocument`に変換します。

**ステージ2（シリアライズ）** — `DoclingDocument.ToMarkdown()`が呼び出されると、内部の`MarkdownSerializer`がツリーを走査してMarkdown文字列を生成します。テーブルレンダリングは`ITableSerializer`で交換可能です。HWPドキュメントはデフォルトで`SemanticTableSerializer`を使用し、フォームスタイルのテーブルを太字グループラベルでレンダリングします。

**ステージ3（チャンキング）** — RAGパイプラインの`MarkdownTextSplitter`がMarkdown文字列を受け取り、検索に適したサイズのチャンクに分割します。見出し（`#`、`##`など）ベースでセクションを構成し、各チャンクにブレッドクラム（親見出しパス）を自動的に含めます。

これら3つのステージが分離されているため、新しいドキュメントローダーの追加やテーブルレンダリング戦略の変更が他のステージに影響を与えません。

## ドキュメントローダーとテキストスプリッターの連携

Markdownの見出し・コードブロック・表の行を維持するには `MarkdownTextSplitter` を使用します。overlap引数はありません。繰り返す見出しは本文予算の外であり、コード全体や表ヘッダーと1行は上限を超える場合があります。厳密なトークン上限は最終チャンクをモデルのトークナイザーで検証してください。[テキストスプリッター](text-splitters.md)を参照してください。

```csharp
var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocuments(new WordDocumentLoader(), "manual.docx", new MarkdownTextSplitter(1000))
        .AddDocuments(new ExcelDocumentLoader(), "data.xlsx", new MarkdownTextSplitter(1000))
    );
```

認識したGFM表は行の間で分割し、各表チャンクにヘッダーと区切り行を繰り返します。外側のパイプを省略した `Name | Value` 形式も扱います。列名を維持する機能であり、検索品質は文書・埋め込み・質問によって変わります。
