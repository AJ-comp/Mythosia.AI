# Agent 式 RAG

## 为什么需要 Agent 式 RAG？

在标准 RAG 中，每条用户消息都会触发**一次**检索。系统搜索、构建上下文并生成响应 — 无论如何。这对简单问题效果不错，但在以下场景力不从心：

- 问题需要跨不同主题进行**多次搜索**（如"比较硬件和软件产品的退款政策"）
- 第一次搜索结果**不充分**，系统应该优化后重试
- 某些问题**根本不需要检索**（如"总结一下我们的对话"）
- 回答需要结合**文档检索和实时 API 数据**

Agent 式 RAG 解决了所有这些问题。它不是固定的检索-回答管道，而是由 **Agent 自主决定** — 何时搜索、搜索什么、是否再搜一次、何时调用其他工具 — 所有操作都在 ReAct 循环中完成。

## 快速上手

通过 `WithAgenticRag` 将 `RagStore` 注册为工具，然后使用 `RunAgentAsync`：

```csharp
// 构建一次索引
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// 注册 RAG 为工具并运行 Agent
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("总结退款政策。");
```

当 Agent 需要文档上下文时，会自动调用 `search_documents`，然后从检索到的片段中综合生成最终回答。

## 结合其他工具

Agent 式 RAG 与其他工具组合时效果最佳 — Agent 会为每个子任务选择合适的工具：

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "通过订单 ID 查询订单状态。",
           ("order_id", "要查询的订单 ID。", required: true),
           async id => await orderApi.GetStatusAsync(id));

// Agent 搜索文档获取政策，同时调用 API 获取实时订单数据
var answer = await service.RunAgentAsync(
    "订单 #12345 — 根据当前政策，我是否有资格退款？");
```

在这个例子中，Agent 自主完成：

1. 搜索文档获取退款政策
2. 调用订单 API 获取 #12345 的状态
3. 综合两方面信息生成最终回答

## 自定义工具描述

工具描述决定了 Agent 何时调用 RAG。根据你的业务领域定制描述，以获得更精准的工具选择：

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "搜索内部 HR 政策、产品手册和合规文档。" +
        "当需要公司特定政策或产品信息时调用此工具。");
```

模糊的描述（如"搜索文档"）可能导致 Agent 过于频繁或不够频繁地调用 RAG。请具体说明文档**包含什么类型的信息**。

## 与标准 RAG 的区别

| | 标准 RAG | Agent 式 RAG |
| --- | --- | --- |
| 搜索时机 | 每条消息 | Agent 自行决定 |
| 查询构建 | QueryRewriter | Agent 自身 |
| 搜索次数 | 每轮一次 | 按需一次或多次 |
| 工具组合 | 不适用 | 任意已注册工具 |
| 使用方式 | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **注意：** Agent 式 RAG 中故意绕过了 `QueryRewriter`。Agent 会自行构建独立的搜索查询，单独的改写步骤既多余又可能扭曲 Agent 的意图。

## 如何选择

- **标准 RAG** — 每个问题都基于文档、单一主题、追求最低延迟
- **Agent 式 RAG** — 问题跨越多个主题、需要结合文档和实时数据、或需要迭代检索
