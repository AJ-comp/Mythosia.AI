# RAG 管道自定义

## 为什么需要自定义管道？

默认 RAG 管道开箱即用效果良好，但实际项目往往需要更多控制：

- **调试** — 哪个阶段慢？改写器是否以意想不到的方式修改了查询？
- **提示词工程** — 默认提示词模板可能不适合你的业务领域的语气或约束
- **架构** — 多个服务共享一个索引，节省内存并保持嵌入一致性
- **检查** — 有时你需要在将检索结果发送给 LLM 之前先查看它们

本章介绍提供这些控制能力的工具。

## 进度追踪

通过每次查询的异步回调追踪当前正在执行的 RAG 阶段：

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // 阶段：QueryRewrite, Embedding, Filtering, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("你的问题", options);
```

这对延迟分析非常有用 — 你可以测量各阶段之间的时间来找到瓶颈。

## 自定义提示词模板

使用 `{context}` 和 `{question}` 占位符控制检索到的上下文如何注入到提示词中：

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        仅根据以下信息回答问题。
        如果答案不在上下文中，请回答"我不知道。"

        上下文：
        {context}

        问题：{question}
        """)
    .AddDocument("faq.txt")
)
```

精心设计的模板可以通过指示模型不要超出提供的上下文，显著减少幻觉。

## 共享 RagStore

构建一次索引，跨多个服务实例复用 — 适用于比较不同提供商或进行 A/B 测试：

```csharp
// 构建一次
RagStore store = await RagBuilder.Create()
    .UseOpenAIEmbedding(apiKey, http)
    .UseQdrantStore(qdrantUrl, qdrantKey)
    .AddDocuments("docs/")
    .BuildAsync();

// 跨服务复用
var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

两个服务共享相同的嵌入和向量索引 — 无需重复存储或计算。

## RagStore 直接查询

独立于 AI 服务查询存储，检查将被检索的内容：

```csharp
RagProcessedQuery result = await store.QueryAsync("退款政策是什么？");

Console.WriteLine($"改写后的查询：{result.RewrittenQuery}");

foreach (var ref_ in result.References)
{
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
}
```

`result.RequestMessageContent` 包含将发送给 LLM 的完整组装提示词。这对调试检索质量非常有用，无需消耗 LLM Token。

## 内部工作原理

调用 `.WithRag()` 时，会在你的 AIService 外层创建一个 `RagEnabledService` 包装器。该包装器自动将 RAG 管道与 LLM 调用连接。其关键机制是 [AIRequestContext](request-contexts.md)。

### 完整流程

```
ragService.GetCompletionAsync("退款政策是什么？")
    ↓
① RagEnabledService 执行 RAG 管道
   查询改写 → 嵌入 → 检索 → 上下文组装
    ↓
② TemplateContextBuilder 替换 {context} 和 {question}
   → "根据以下信息回答。\n[1] 30天内可退货...\n问题：退款政策是什么？"
    ↓
③ RagEnabledService 创建 AIRequestContext
   RequestMessageOverride = 组装后的提示词
    ↓
④ 调用 _innerService.GetCompletionAsync(原始消息, context)
   → AIService 将 context 存储在 AsyncLocal 中
   → 原始问题添加到对话历史
    ↓
⑤ AIService.GetLatestMessages() 替换最后一条消息
   对话历史："退款政策是什么？"（保留原文）
   模型看到的：组装后的提示词（RequestMessageOverride）
```

### 为什么这样设计？

关键点在于**将对话历史与模型输入分离**：

- **对话历史保留原始问题** — 这样后续追问"那个怎么样？"才有正确的上下文
- **模型接收组装后的提示词** — 包含检索到的文档和问题的完整提示词
- **AIService 状态不会被修改** — `AsyncLocal<T>` 提供每个请求的隔离

这就是 [AIRequestContext](request-contexts.md) 文档中描述的 `RequestMessageOverride` 的实际应用。RAG 管道自动利用此机制，你只需调用 `.WithRag()` 即可。

### 代码实现

以下是 `RagEnabledService` 中实现此连接的核心代码：

```csharp
// RagEnabledService.GetCompletionAsync 内部
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
return await _innerService.GetCompletionAsync(
    new Message(ActorRole.User, query),         // ← 原始问题（保存在历史中）
    context: BuildRequestContext(processed));    // ← 组装后的提示词（只有模型看到）

// BuildRequestContext — 创建 AIRequestContext
private static AIRequestContext BuildRequestContext(RagProcessedQuery processed)
{
    return new AIRequestContext
    {
        RequestMessageOverride = new Message(
            ActorRole.User,
            processed.RequestMessageContent)  // ← TemplateContextBuilder 的输出
    };
}
```

`AIService` 将此 context 存储在 `AsyncLocal` 中，`GetLatestMessages()` 用 `RequestMessageOverride` 替换最后一条消息。请求完成后，context 自动恢复，确保不影响后续请求。
