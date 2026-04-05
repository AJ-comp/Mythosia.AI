# 重排序與檢索調校

> 📍 **問答檢索管線：** [查詢改寫](rag-query-rewriting.md) → 嵌入 → 過濾 → [檢索](rag-hybrid-search.md) → **`重排序`** → 上下文構建

## 為什麼需要重排序？

向量檢索回傳按嵌入相似度排列的候選結果，但嵌入相似度只是一個**近似值**。**重排序器**用更強大的模型對每個片段重新評分，產生更精確的相關性排序。

## 重排序器選項

### LLM 重排序器

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

### Cohere 重排序器

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM 重排序器

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker("http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## 檢索參數

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 最終回傳的片段數
    .WithRetrievalMultiplier(3)    // 檢索 topK × 3 個候選
    .WithMinScore(0.6)             // 最低相似度閾值
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — 最終進入 LLM 上下文的片段數
- **`RetrievalMultiplier`** — 擴大檢索範圍以便重排序器有更多選擇
- **`MinScore`** — 丟棄低於此閾值的結果

## 最終選擇模式

```csharp
using Mythosia.AI.Rag;

.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)
```

**`RerankerOnly`** 是安全的預設選項。**`WeightedBlend`** 在嵌入品質已高時，讓重排序器作為決勝手段。
