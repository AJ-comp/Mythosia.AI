# 查询改写

> 📍 **问答检索管道：** **`查询改写`** → 过滤 → 嵌入（按需） → [检索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → 上下文构建

问题的 `Embedding` 阶段按检索器需要执行，关键词搜索不会报告该阶段。自定义检索器可通过 `request.ProgressAsync` 报告实际阶段。文档嵌入不变。

## 为什么需要查询改写？

在多轮对话中，用户经常使用代词和简短引用：

> 用户："介绍一下退款政策。"
> 用户："**它**有哪些例外情况？"

如果将"它有哪些例外情况？"原样发送到向量存储，嵌入向量无法理解"它"指的是什么，检索结果会不相关，回答质量也会下降。

**查询改写**在检索前解析这些引用，将"它"展开为"退款政策的例外情况"，使嵌入向量能捕捉完整意图。它还实现了**搜索门控** — 如果查询不需要检索（如"谢谢！"），则跳过向量搜索，节省延迟和成本。

## 配置

`LlmQueryRewriter` 使用 AI 服务本身在嵌入前改写查询：

```csharp
.WithRag(rag => rag
    .WithQueryRewriter(250)          // 使用相同的 AI 服务
    .AddDocument("docs.txt")
)
```

改写器检查对话上下文，生成一个向量存储无需历史就能理解的独立搜索查询。

## 多轮 RAG

直接查询 `RagStore` 时，传入对话历史以便改写器解析引用：

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("退款政策是什么？", "30 天内可以退货。"),
    new ConversationTurn("数字产品呢？", "数字产品不可退款。")
};

var result = await store.QueryAsync(
    query: "有没有例外情况？",
    conversationHistory: history
);
```

改写器看到完整历史后，会将"有没有例外情况？"改写为类似"数字产品不可退款政策的例外情况"的查询，显著提升检索效果。

<a id="runtime-query-rewriter"></a>

## 在处理查询时更改改写设置

要在不重建索引的情况下暂时关闭改写或更换实现，请使用 `store.SetQueryRewriter(null)` 或 `store.SetQueryRewriter(rewriter)`。通过接收 `conversationHistory` 的重载直接调用 `RagStore.QueryAsync` 时，会在查询开始时保存所选改写器。即使在等待进度通知或改写期间关闭或更换设置，该查询仍使用同一实例，后续查询才使用新设置。此行为适用于直接查询存储，不会更新 `RagEnabledService` 包装器已经保存的改写器。

## 搜索门控的工作方式

并非每条用户消息都需要文档搜索。改写器会对查询进行分类，对以下类型的消息返回空改写结果：

- "谢谢！"
- "了解了，很有帮助。"
- "你能总结一下刚才说的吗？"

当门控触发时，整个检索管道被跳过 — 不做嵌入、不做向量搜索、不做重排序 — LLM 直接从对话上下文回答。
