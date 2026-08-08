# 再ランキング & 検索チューニング

> 📍 **質問応答パイプライン:** [クエリ書き換え](rag-query-rewriting.md) → 埋め込み → フィルタリング → [検索](rag-hybrid-search.md) → **`再ランキング`** → コンテキスト構築

## 検索結果が完璧でない理由

ベクター検索は埋め込み類似度を基準に候補を取得します。しかし、埋め込み類似度は**近似値**です。たとえるなら、本の表紙だけ見て内容を推測するようなものです。ほとんどの場合は合っていますが、0.82点のチャンクが実際には0.85点のものより関連性が高いケースが起こりえます。

## 再ランキングとは？

**再ランキング（Reranking）**はこの問題を補うステージです。ベクター検索が取得した候補リストを受け取り、より精密なモデルで各チャンクを**元の質問と直接比較**して関連性を再評価します。

たとえるとこのような流れです：

```
① ベクター検索：本棚から関連がありそうな本を15冊すばやく選び出す（速いが大まか）
    ↓
② 再ランキング：15冊を一冊ずつ読んで、本当に関連のある5冊だけを最終選定（遅いが正確）
```

次のような場面で特に効果的です：

- ドキュメントに似たような内容が多い場合（例：FAQ項目）
- ベクター検索の上位結果が「近いけど正確ではない」と感じる場合
- 重要な質問で高精度の回答が必要な場合

## 再ランカーオプション

用途と環境に応じて3種類の再ランカーから選択できます：

### LLM再ランカー

現在使用中のAIサービスを活用して結果を再評価します。別途のサービスなしですぐ使えますが、AI呼び出しが追加されるため応答時間がやや長くなります：

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

### Cohere再ランカー

Cohereの専用Rerank APIを呼び出します。再ランキングに特化したモデルなので、高速かつ正確です：

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM再ランカー

ローカルにホストされたvLLM再ランキングエンドポイントを使用します。データを外部に送れない環境に適しています：

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## 検索パラメーター

検索結果の量と品質を調整するための3つの重要なパラメーターです：

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 最終的にAIに渡すチャンク数
    .WithRetrievalMultiplier(3)    // 再ランキング前に取得する候補の倍数（5 × 3 = 15個）
    .WithScoreThreshold(0.6)       // このスコア未満のチャンクは破棄
    .AddDocument("corpus.txt")
)
```

各パラメーターの役割を詳しく見ると：

- **`TopK`** — 最終的にAIのプロンプトに含まれるチャンク数です
- **`RetrievalMultiplier`** — 再ランカーがより良い結果を選べるよう、広い候補群を提供します。たとえばTopK=5で倍数3なら、まず15個を取得してから再ランキングで上位5個だけを残します
- **`WithScoreThreshold`** — 類似度が低すぎる結果は完全に除外します。TopKより少ない数になっても品質を優先します

## 最終選択モード

再ランカーを使用する際、最終ランキングスコアをどう算出するか2つの方式から選べます：

```csharp
using Mythosia.AI.Rag;

// デフォルト：再ランカーの判断のみを使用
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// ベクター検索スコアと再ランカースコアをブレンド
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)  // 65%検索、35%再ランカー
```

- **`RerankerOnly`** — 安全なデフォルトです。再ランカーの判断が元の検索スコアを完全に置き換えます
- **`WeightedBlend`** — ベクター埋め込みの品質が既に十分高く、再ランカーを補助的な判断ツールとして活用したい場合に適しています
