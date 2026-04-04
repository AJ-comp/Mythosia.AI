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

Markdownの構造を保持します — 文字分割にフォールバックする前にヘッダー、リスト、コードブロックで分割します:

```csharp
.WithTextSplitter(new MarkdownTextSplitter(500, 50))
```

ドキュメントファイル、READMEファイル、構造化Markdownコンテンツに最適です。

## パラメーターの選択

| パラメーター | 効果 |
|------------|------|
| `chunkSize`（大きく） | チャンクごとのコンテキストが多い、チャンク数が少ない、埋め込みコストが安い |
| `chunkSize`（小さく） | より高精度な検索、チャンク数が多い、埋め込みが多い |
| `chunkOverlap` | チャンク境界での情報損失を防ぐ |

一般的な開始点: `chunkSize: 500, chunkOverlap: 50`。

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

完全なカスタム分割ロジックのために`ITextSplitter`を実装します:

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
