# 重排序與檢索調校

> 📍 **問答檢索管線：** [查詢改寫](rag-query-rewriting.md) → 過濾 → 嵌入（按需） → [檢索](rag-hybrid-search.md) → **`重排序`** → 上下文構建

問題的 `Embedding` 階段依檢索器需要執行，關鍵字搜尋不會回報此階段。自訂檢索器可透過 `request.ProgressAsync` 回報實際階段。文件嵌入不變。

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

為避免每次評估的問題和文件與先前的評估或服務對話混在一起，`LlmReranker` 會為每次評估傳送不使用歷史紀錄的獨立請求。它既不讀取既有對話歷史，也不向其中新增評估內容。服務的預設設定和呼叫方式保持不變。 共用同一個 AI 服務的重排序器會依序執行評估。

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
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## 檢索參數

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 最終回傳的片段數
    .WithRetrievalMultiplier(3)    // 檢索 topK × 3 個候選
    .WithScoreThreshold(0.6)       // 最低相似度閾值
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — 最終進入 LLM 上下文的片段數
- **`RetrievalMultiplier`** — 擴大檢索範圍以便重排序器有更多選擇
- **`WithScoreThreshold`** — 丟棄低於此閾值的結果

## 最終選擇模式

```csharp
using Mythosia.AI.Rag;

.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)
```

**`RerankerOnly`** 是安全的預設選項。**`WeightedBlend`** 在嵌入品質已高時，讓重排序器作為決勝手段。

純向量及關鍵字模式保留原生分數。可設定的混合搜尋即使只有一路也使用正規化加權RRF；向量權重為0時不產生問題嵌入。分數不是機率。`WeightedBlend` 未經校準便直接組合檢索與重新排序分數；未經校準的關鍵字搜尋建議使用預設 `RerankerOnly`。
