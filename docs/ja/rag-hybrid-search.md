# ハイブリッド検索

## なぜハイブリッド検索が必要か？

純粋なベクター検索はセマンティックな意味の把握に優れています — 「サブスクリプションの解約」が「メンバーシップの終了」と単語が異なっていてもマッチします。しかし、ユーザーがそのまま入力した**正確な用語** — 製品名、エラーコード、ポリシー識別子など — を見逃す可能性があります。

BM25キーワード検索はこれらのケースを完璧に処理しますが、セマンティックな理解には弱いです。**ハイブリッド検索は両方を組み合わせ**、セマンティックな理解と正確なキーワードマッチングを同時に提供します。

## 設定

1つのメソッド呼び出しで密なベクター検索とBM25キーワード検索を組み合わせます:

```csharp
.WithRag(rag => rag
    .UseHybridRetrieval(vectorWeight: 0.6f)  // 60%ベクター、40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight`の範囲は0.0（純粋なBM25）から1.0（純粋なベクター）です。ほとんどの場合**0.5〜0.7**程度が適切です。

## シナリオ別推奨ウェイト

| シナリオ | 推奨ウェイト |
| --- | --- |
| 自然言語による一般的なQ&A | 0.7–0.8（ベクター寄り） |
| 特定用語が多い技術文書 | 0.4–0.5（バランス型） |
| コード/エラーコード検索 | 0.2–0.3（BM25寄り） |

## 例

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridRetrieval(vectorWeight: 0.5f)
        .AddDocument("product-catalog.txt")
        .AddDocument("error-codes.txt")
    );

// "ERR-4012"はBM25で、セマンティックコンテキストはベクターでマッチ
var answer = await service.GetCompletionAsync("ERR-4012の解決方法は？");
```
