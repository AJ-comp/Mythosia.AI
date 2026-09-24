# 重排序与检索调优

> 📍 **问答检索管道：** [查询改写](rag-query-rewriting.md) → 过滤 → 嵌入（按需） → [检索](rag-hybrid-search.md) → **`重排序`** → 上下文构建

问题的 `Embedding` 阶段按检索器需要执行，关键词搜索不会报告该阶段。自定义检索器可通过 `request.ProgressAsync` 报告实际阶段。文档嵌入不变。

## 为什么需要重排序？

向量检索返回按嵌入相似度排列的候选结果，但嵌入相似度只是一个**近似值**。得分 0.82 的片段实际上可能比得分 0.85 的更相关 — 嵌入向量无法区分它们。

**重排序器**接收初始候选列表，用更强大的模型对每个片段与原始查询重新评分，产生更精确的相关性排序。以下场景特别有用：

- 语料库中包含大量相似的片段（如 FAQ 条目）
- 向量检索的头部结果感觉"接近但不完全对"
- 关键场景需要高精度回答

## 重排序器选项

### LLM 重排序器

使用你的 AI 服务对结果评分。效果好但会增加延迟：

```csharp
.WithRag(rag => rag
    .WithReranker(new LlmReranker(aiService))
    .AddDocument("corpus.txt")
)
```

为避免每次评估的问题和文档与之前的评估或服务对话混在一起，`LlmReranker` 会为每次评估发送不使用历史记录的独立请求。它既不读取现有对话历史，也不向其中追加评估内容。服务的默认设置和调用方式保持不变。 共享同一个 AI 服务的重排序器会按顺序执行评估。

### Cohere 重排序器

调用 Cohere Rerank API — 快速且准确：

```csharp
.WithRag(rag => rag
    .WithReranker(new CohereReranker(cohereApiKey))
    .AddDocument("corpus.txt")
)
```

### vLLM 重排序器

使用本地部署的 vLLM 重排序端点：

```csharp
.WithRag(rag => rag
    .WithReranker(new VllmReranker(baseUrl: "http://localhost:8000"))
    .AddDocument("corpus.txt")
)
```

## 检索参数

控制候选数量及最终选择前的过滤方式：

```csharp
.WithRag(rag => rag
    .WithTopK(5)                   // 最终返回的片段数
    .WithRetrievalMultiplier(3)    // 检索 topK × 3 个候选（用于重排序）
    .WithScoreThreshold(0.6)       // 最低相似度阈值
    .AddDocument("corpus.txt")
)
```

- **`TopK`** — 最终进入 LLM 上下文的片段数
- **`RetrievalMultiplier`** — 扩大检索范围以便重排序器有更多选择。乘数为 3 表示获取 15 个候选，然后重排序后保留最佳的 5 个。
- **`WithScoreThreshold`** — 丢弃低于此相似度阈值的结果，即使不足 `TopK` 个

## 最终选择模式

使用重排序器时，选择最终排名分数的计算方式：

```csharp
using Mythosia.AI.Rag;

// 默认：仅信任重排序器的分数
.WithFinalSelectionPolicy(RagFinalSelectionMode.RerankerOnly)

// 融合检索分数和重排序分数
.WithFinalSelectionPolicy(RagFinalSelectionMode.WeightedBlend, retrievalWeight: 0.65)  // 65% 检索，35% 重排序
```

**`RerankerOnly`** 是安全的默认选项 — 重排序器的判断完全替代初始检索分数。

**`WeightedBlend`** 保留原始检索信号的同时融入重排序器的判断。当你的向量嵌入质量已经很高，希望重排序器作为决胜手段而非完全覆盖时，这个选项很有用。

纯向量和关键词模式保留原生分数。可配置混合搜索即使只有一路也使用归一化加权RRF；向量权重为0时不生成问题嵌入。分数不是概率。`WeightedBlend` 不经校准直接组合检索和重排分数；未经校准的关键词搜索建议使用默认 `RerankerOnly`。
