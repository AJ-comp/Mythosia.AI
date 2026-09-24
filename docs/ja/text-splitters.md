# テキストスプリッター

検索結果には質問に答えるための文脈が必要ですが、文書全体を一つの単位にすると必要な箇所を選びにくくなります。チャンキングはサイズと文脈のバランスを取ります。以下のスプリッターはAIモデルを使わずローカルの規則で動作します。文書構造に合わせて選び、実際の質問で検索を評価してください。

## 利用可能なスプリッター

### CharacterTextSplitter

単純なサイズ基準でプレーンテキストを分ける場合に使います。指定した区切り文字を優先しますが、文の途中で分割することもあります。`RagBuilder` の既定値は `CharacterTextSplitter(300, 30)` です。拡張子 `.md` だけではMarkdown用スプリッターは自動選択されません。

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter（推奨デフォルト）

段落や単語を可能な限りまとめたい本文に使います。既定の区切り順は空行 → 改行 → `. ` → 空白 → 個々の文字です。モデルによる意味判定ではなく、ピリオドと空白も文境界の近似規則です。

`Separators` の重複項目は、最初に現れた順に一度だけ適用します。同じ区切りを繰り返しても分割処理は増えません。長い区切りリストも再帰呼び出しを深く重ねずに処理します。

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

空白で区切った単語数をおおまかな単位にする場合に使います。名前とは異なり、`MaxTokensPerChunk` と `TokenOverlap` は `TokenSeparators`（既定は空白・タブ・改行）による単位数であり、モデルのトークン数ではありません。出力の区切りは空白に正規化します。空白のない長文は一つの単位になり得るため、モデルのトークン上限は保証できません。

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

### MarkdownTextSplitter

見出しの文脈、表の行、コードブロックを維持したいMarkdown文書やOffice/HWPローダーのMarkdown出力に使います。ATX見出し（`#`–`######`）、コードフェンス、表を認識する規則ベースの処理であり、完全なMarkdown構文木パーサーではありません。コンストラクターの引数は `chunkSize` だけで、Markdownのoverlap引数・設定はありません。

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500))
```

#### テーブル分割品質

認識したGFM表は行の間で分割し、各表チャンクにヘッダーと区切り行を繰り返します。外側のパイプを省略した `Name | Value` 形式も扱います。列名を維持する機能であり、検索品質は文書・埋め込み・質問によって変わります。

太字の表セルはその行の内容です。たとえば A 社の行の `**返金不可**` を B 社の条件として付加してはいけません。表セルやコードブロックを、繰り返す本文ラベルとして扱うことはありません。独立した `**ラベル**` 行は、空行や構造の境界に続く段落またはテキストブロックの先頭でのみ認識します。既存の段落内で改行された太字の行から、新しい段落や繰り返しラベルを作りません。認識したラベルはそのブロックの分割片に繰り返すことができ、表、コードフェンス、見出し、次に認識したラベルで適用範囲が終わります。

```
元のテーブル:
| 名前   | 部署   | 給与      |
|--------|--------|----------|
| 田中   | 開発部 | 500万円  |
| 鈴木   | 企画部 | 480万円  |
| 佐藤   | デザイン | 450万円  |

→ チャンク 1:
| 名前   | 部署   | 給与      |
|--------|--------|----------|
| 田中   | 開発部 | 500万円  |
| 鈴木   | 企画部 | 480万円  |

→ チャンク 2:
| 名前   | 部署   | 給与      |
|--------|--------|----------|
| 佐藤   | デザイン | 450万円  |
```

#### コードブロック保護

バッククォートやチルダのフェンスで囲んだコードは一つのブロックとして維持します。閉じるフェンスは開始と同じ文字で同じ長さ以上である必要があり、内部の短いフェンスでは終了しません。ブロックを維持するため `ChunkSize` を超える場合があります。

開始フェンスのインデントもコードとともに保持するため、分割後もレンダリングされたコードのインデントの意味が変わりません。開始フェンスの情報はブロックごとに一度だけ解析し、長いフェンスを本文の各行で繰り返し読み直す処理を避けます。

#### 見出しブレッドクラム

`IncludeHeadingBreadcrumb` の既定値は `true` です。検索された本文に文脈を残すため、各チャンクに親見出しの経路を繰り返します。`false` は繰り返しだけを無効にし、元の見出しは保持します。見出しだけのセクションも残ります。

`MinSplitHeadingLevel` は1～6でセクションを開始する見出しレベルを指定し、既定値は1です。 親見出しが変わると、それまでの下位セクションを終了し、古い見出しの経路が新しい内容に付かないようにします。

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500)
{
    IncludeHeadingBreadcrumb = false
})
```

## パラメーターの選択

`CharacterTextSplitter`、`RecursiveTextSplitter`、`MarkdownTextSplitter` のサイズはUTF-16コード単位（`string.Length`）です。モデルのトークン数や見た目の文字数ではありません。絵文字などのサロゲートペアは途中で切りません。サイズ1ではペアの2単位が例外的に上限を超えます。結合文字や書記素クラスタ全体の保持は保証しません。

サイズは正、overlapは0以上である必要があります。不正な値は処理前に `ArgumentOutOfRangeException` となり、変更可能な設定も分割時に再検証します。overlapがサイズ以上なら互換動作として重複を無効にします。Character/Recursiveのoverlapは区切り・Unicode境界と次のチャンクの空きに合わせる目標値です。`0` は重複なしを意味し、末尾の重複部分だけの追加チャンクは出力しません。

Markdownの `ChunkSize` は**繰り返す親見出しの経路を除く本文の予算**です。コードブロック全体や表ヘッダーと1行は予算を超えることがあります。通常の本文は上記サロゲートペアの例外を除き設定サイズを守ります。

見出しや表ヘッダーの反復で小さな文書が過大な埋め込み入力にならないよう、Markdown には文書全体の出力予算があります。上限は `max(65536, 32 × document.Content.Length)` UTF-16 単位で、繰り返す見出しパス、表ヘッダー、ラベルを含む全最終チャンクの長さを合計します。過剰な反復出力を作る前に確認し、超過する場合は `InvalidOperationException` を送出します。内容を切り捨てたり、一部の結果だけを返したりはしません。`ChunkSize` と不可分ブロックの例外もこの全体上限内で適用されます。既定の RAG インデックス処理では埋め込みや保存レコードの置換前に分割が失敗するため、既存の文書インデックスは維持されます。これは出力文字列の上限であり、モデルのトークン数やプロセスメモリの上限ではありません。 予算は `Split` 呼び出しごとに適用され、入力の長さに応じて増えるため、文書サイズの固定上限ではありません。

本文には `RecursiveTextSplitter(500, 50)`、Markdownには `MarkdownTextSplitter(500)` から始め、代表的な質問で評価してください。大きなチャンクは周囲の文脈を増やし、overlapは重複内容と埋め込み処理を増やします。検索改善は保証されません。

厳密なトークン上限には、繰り返した見出しや表ヘッダーを含む最終チャンクを対象モデルのトークナイザーで数える必要があります。文字・単語数や言語別換算は安全な上限ではありません。必要ならそのトークナイザーで `ITextSplitter` を実装してください。

今回の修正により対象文書のチャンク境界が変わります。同じ文書IDで再インデックスして古いチャンクを置き換え、関連する埋め込みキャッシュと評価ベースラインも更新してください。保存済みチャンクは自動更新されません。

## ドキュメントごとのスプリッター

`RagBuilder`でドキュメントごとに異なるスプリッターを適用できます:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // 残りのデフォルト
)
```

## カスタムスプリッター

カスタムな分割モジュールを作成して連携したい場合は、`ITextSplitter`を実装してください:

インデックス作成が成功したように見えても、前のチャンクが上書きされてはいけません。各チャンクにコレクション内で一意の空白ではない ID を付け、会社やアクセス権のフィルターを保つために文書のメタデータをコピーしてください。この例では文書 ID とチャンク番号を組み合わせます。パイプラインは ID の欠落と同一文書内の重複を拒否し、代わりの ID は生成しません。[インデックス検証](rag-pipeline.md#indexing-validation)を参照してください。

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split("。");
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

// 登録:
.WithTextSplitter(new SentenceSplitter())
```
