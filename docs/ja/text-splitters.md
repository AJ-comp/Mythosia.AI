# テキストスプリッター

テキストスプリッターは埋め込み前にドキュメントをチャンクに分割します。チャンクサイズとオーバーラップは検索品質に大きく影響します。

## 利用可能なスプリッター

### CharacterTextSplitter

文字数で分割します。シンプルで高速ですが、文の途中で切れることがあります:

```csharp
.WithTextSplitter(new CharacterTextSplitter(500, 50))
```

### RecursiveTextSplitter（推奨デフォルト）

段落 → 文 → 単語 → 文字の順に意味のある境界で分割を試みます。より一貫したチャンクを生成します:

```csharp
.WithTextSplitter(new RecursiveTextSplitter(500, 50))
```

### TokenTextSplitter

文字数ではなくトークン数で分割します。LLMコンテキストウィンドウの予算管理に正確です:

```csharp
.WithTextSplitter(new TokenTextSplitter(256, 32))
```

埋め込みモデルに厳格なトークン制限がある場合に使用します。

### MarkdownTextSplitter

Markdownの構造を理解し保持するスプリッターです。見出し階層（H1–H6）、コードフェンス、テーブルなどの構造を認識し、意味のある単位で分割します：

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

ドキュメントファイル、README、およびOffice/HWPなど構造化ドキュメントローダーの出力に最適です。

> [!TIP]
> Word、Excel、PowerPoint、HWPなどのドキュメントローダーは内部的にドキュメントをMarkdownに変換します。これらのドキュメントに`MarkdownTextSplitter`を使用すると、テーブルやコードブロックの構造がチャンキング過程でも完全に保持されます。

#### テーブル分割品質

`MarkdownTextSplitter`はMarkdownテーブルを**行単位**で分割します。行の途中で切れることは絶対になく、分割された各チャンクには**ヘッダー行と区切り線が自動的に含まれます**：

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

各チャンクが独立した有効なテーブルとなり、埋め込みと検索品質が保証されます。

#### コードブロック保護

コードフェンス（`` ``` ``）で囲まれたブロックは**アトミック（原子的）な単位**として扱われます。コードブロックはチャンクサイズを超えても絶対に中途で分割されず、コードの意味が損なわれません。

#### 見出しブレッドクラム

各チャンクには、そのコンテンツが属する見出しパスが自動的に先頭に付加されます。これによりベクトル検索時のコンテキストが豊かになります：

```
# 製品マニュアル
## インストールガイド
### Windows

（このセクションの実際のコンテンツ）
```

この機能は`IncludeHeadingBreadcrumb`プロパティ（デフォルト: `true`）で制御します。

## パラメーターの選択

| パラメーター | 効果 |
|------------|------|
| `chunkSize`（大きく） | チャンクごとのコンテキストが多い、チャンク数が少ない、埋め込みコストが安い |
| `chunkSize`（小さく） | より高精度な検索、チャンク数が多い、埋め込みが多い |
| `chunkOverlap` | チャンク境界での情報損失を防ぐ |

一般的な開始点: `chunkSize: 500, chunkOverlap: 50`。

## チャンクサイズとトークン数（多言語参考）

`chunkSize`は**文字数**基準ですが、埋め込みモデルの制限は**トークン数**基準です。言語によって同じ文字数でもトークン数が大きく異なります：

| 言語 | 1,000文字 ≈ トークン数 | 推奨 chunkSize |
|------|---------------------|----------------|
| 英語 | ~250 トークン | 500–2,000 |
| 日本語 / 韓国語 / 中国語 | ~800–1,500 トークン | 300–1,000 |

> [!WARNING]
> 日本語、韓国語、中国語などのCJKテキストは、英語よりも文字あたりのトークン比率がはるかに高いです。埋め込みモデルのトークン制限（例: 2,048トークン）を超えるとエラーが発生します。CJKドキュメントを扱う際は`chunkSize`を十分に小さく設定してください。

例えば、トークン制限が2,048の埋め込みモデルを使用する場合：

```csharp
// 英語ドキュメント: 2000文字 ≈ 500 トークン → 余裕あり
.WithTextSplitter(new MarkdownTextSplitter(2000, 200))

// 日本語ドキュメント: 1000文字 ≈ 1000 トークン → 安全範囲
.WithTextSplitter(new MarkdownTextSplitter(1000, 200))
```

## ドキュメントごとのスプリッター

`RagBuilder`でドキュメントごとに異なるスプリッターを適用できます:

```csharp
.WithRag(rag => rag
    .AddDocuments(new PlainTextDocumentLoader(), "readme.md", new MarkdownTextSplitter(600, 60))
    .AddDocuments(new PlainTextDocumentLoader(), "data.txt",  new RecursiveTextSplitter(300, 30))
    .WithTextSplitter(new RecursiveTextSplitter(500, 50))  // 残りのデフォルト
)
```

## カスタムスプリッター

カスタムな分割モジュールを作成して連携したい場合は、`ITextSplitter`を実装してください:

```csharp
public class SentenceSplitter : ITextSplitter
{
    public IReadOnlyList<RagChunk> Split(RagDocument document)
    {
        var sentences = document.Content.Split("。");
        return sentences.Select((s, i) => new RagChunk
        {
            Content = s,
            Index = i,
            DocumentId = document.Id
        }).ToList();
    }
}

// 登録:
.WithTextSplitter(new SentenceSplitter())
```
