# 再ランキング & 検索チューニング

## なぜ再ランキングが必要か？

ベクター検索は埋め込み類似度順に候補を返しますが、埋め込み類似度は**近似値**です。0.82点のチャンクが実際には0.85点のものより関連性が高い場合があります — 埋め込みだけではこの違いを区別できません。

**再ランカー**は初期候補リストを受け取り、各チャンクを元のクエリに対してより強力なモデルでスコアリングし、はるかに正確な関連性順序を生成します。以下の場合に特に有効です:

- コーパスに似たようなチャンクが多い場合（例：FAQ項目）
- ベクター検索の上位結果が「近いが正確ではない」と感じる場合
- 重要なユースケースで高精度の回答が必要な場合

## 再ランカーオプション

### LLM再ランカー

AIサービスを使って結果にスコアを付けます。効果的ですが遅延が増加します:

```csharp
.WithRag(rag => rag
    .UseLlmReranker(aiService)
    .AddDocument("corpus.txt")
)
```

### Cohere再ランカー

Cohere Rerank APIを呼び出します — 高速で正確です:

```csharp
.WithRag(rag => rag
    .UseCohereReranker(cohereApiKey)
    .AddDocument("corpus.txt")
)
```

### vLLM再ランカー

ローカルでホストされたvLLM再ランキングエンドポイントを使用します:

```csharp
.WithRag(rag => rag
    .UseVllmReranker("http://localhost:8000")
    .AddDocument("corpus.txt")
)
```

## 検索パラメーター

最終選択前に検索される候補数とフィルタリング方法を制御します:

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 返される最終チャンク数
    .WithRetrievalMultiplier(3)    // topK × 3の候補を検索（再ランキング用）
    .WithMinScore(0.6)             // 最小類似度スコア
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — LLMコンテキストに含まれる最終チャンク数
- **`RetrievalMultiplier`** — 再ランカーにより広い候補群を提供します。3倍なら15候補を取得し、再ランキング後に上位5つだけが残ります。
- **`MinScore`** — `TopK`より少ないチャンクが残っても、この類似度閾値以下の結果は破棄します

## 最終選択モード

再ランカーを使用する際の最終ランキングスコアの計算方法を選択します:

```csharp
using Mythosia.AI.Rag;

// デフォルト: 再ランカースコアのみを信頼
.WithFinalSelectionMode(RagFinalSelectionMode.RerankerOnly)

// 検索スコアと再ランカースコアをブレンド
.WithFinalSelectionMode(RagFinalSelectionMode.WeightedBlend)
.WithRetrievalWeightBlend(0.65)  // 65%検索、35%再ランカー
```

**`RerankerOnly`**は安全なデフォルトです — 再ランカーの判断が初期検索スコアを完全に置き換えます。

**`WeightedBlend`**は再ランカーの判断を組み込みながら元の検索シグナルを保持します。ベクター埋め込みが既に高品質で、再ランカーを完全な置き換えではなくタイブレーカーとして使いたい場合に有効です。
