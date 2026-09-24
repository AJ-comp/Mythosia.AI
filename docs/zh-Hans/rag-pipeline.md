# RAG 管道自定义

<a id="indexing-validation"></a>

## 索引失败时保护已有文档

自定义分割器或嵌入响应有误时，不应悄悄用不完整或匹配错误的内容替换可搜索文档。管线会在每份文档开始持久化之前进行验证，使用 `onDocumentEmbedded` 时也一样。

在嵌入、保存或持久化回调之前，若 `RagDocument.Id` 为 null、空字符串或仅空白，则抛出 `ArgumentException`。分割结果列表或分块为 null、`Content` 或 `Metadata` 为 null、分块 ID 为空白或在同一文档内重复时，抛出 `InvalidOperationException`。重复检查使用区分大小写的 `StringComparer.Ordinal`。第一次嵌入调用前会复制分块字段和元数据。

有效的自定义 ID 会原样保留，不会自动生成、去除首尾空白或修复，也不会在整个存储中检测不同文档的自定义分块 ID 冲突。请像[自定义分割器示例](text-splitters.md)一样使用目标集合中唯一的 ID。保留键 `document_id` 只在用于存储的副本上规范化，原始元数据不变。

无效 ID、分割失败和无效嵌入批次会保留该文档的现有记录，不调用持久化回调。文档的所有批次必须通过[嵌入验证](rag-embedding.md#embedding-validation)后才开始保存。此机制不会回滚同一次操作中先前已完成的其他文档；保存开始后的回滚取决于存储或回调实现。

这些验证和响应顺序修正不会自动恢复已被覆盖的正文或此前与错误分块关联的已存储向量，因此受影响的文档需要从原始来源重新建立索引。

<a id="custom-persistence"></a>

## 在持久化回调中替换整个文档

文档缩短后，如果只 upsert 新分块，旧的尾部仍可被搜索。`onDocumentEmbedded` 完全接管默认持久化，因此应使用记录中已规范化的 `document_id` 替换整个文档。回调每次收到一份验证通过且非空的文档：

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var store = await RagStore.BuildAsync(config => config
    .AddDocuments("./docs/")
    .UseOpenAIEmbedding(apiKey)
    .UseStore(vectorStore),
    onDocumentEmbedded: async records =>
    {
        var documentId = records[0].Metadata["document_id"];
        await vectorStore.ReplaceByFilterAsync(
            new VectorFilter().Where("document_id", documentId), records, cancellationToken);
    },
    cancellationToken: cancellationToken);
```

成功分割后若分块数为 0，不会调用此回调，也不会访问默认存储。请用已知文档 ID 在自己的存储中显式删除；`DeleteDocumentAsync` 仅用于管线的存储。替换的原子性及回滚取决于所选存储或回调实现。

<a id="url-documents"></a>

## 安全读取 URL 文档

服务器可能会压缩文本后再传输。`AddUrl` 会在读取文本前解压 `gzip`、`deflate` 和 Brotli（`br`），并检查压缩流是否完整。即使 HTTP 传输成功，只要压缩数据被截断、解压出错或格式自带的校验和验证失败，就会在嵌入或保存前中止加载，保留该文档的已有记录。不支持的 `Content-Encoding` 或多层压缩编码也会在嵌入或保存前被拒绝。

如需停止等待缓慢的 URL 文档，请向 `RagStore.BuildAsync` 传入 `cancellationToken`。该令牌会传递到 HTTP 请求、响应正文读取和解压过程。取消是协作式的，不会撤销此前已完成的其他文档写入。

<a id="custom-retriever"></a>

## 接入不强制嵌入的检索器

商品代码适合关键词搜索，而与文档措辞不同的问题需要语义搜索。选择检索器后只执行所需处理，关键词搜索不再先生成问题嵌入。

- 之前：所有检索策略先收到问题嵌入。
- 之后：选定检索器准备所需的查询表示。

搜索外部索引或使用其他查询表示时，实现 `IRagRetriever`。`RagRetrievalRequest` 包含 `Query`（完整语义查询）、可空 `TextQuery`（词法查询覆盖）、`TopK`、`Filter` 和 `ProgressAsync`。内置检索器在 `TextQuery` 为null时使用 `Query`，为空字符串时跳过文本搜索。自定义检索器必须准备查询并执行过滤、结果数量限制与取消请求。

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class CatalogRetriever : IRagRetriever
{
    private readonly ITextSearchStore _catalog;
    public CatalogRetriever(ITextSearchStore catalog) => _catalog = catalog;

    public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(
        RagRetrievalRequest request,
        CancellationToken cancellationToken = default)
        => _catalog.TextSearchAsync(
            request.TextQuery ?? request.Query,
            request.TopK,
            request.Filter,
            cancellationToken);
}
```

```csharp
// catalog: an existing ITextSearchStore
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag.UseRetriever(new CatalogRetriever(catalog)));
```

通过 `UseRetriever(...)` 或 `RagPipeline.SetRetriever(...)` 注册。现有 `IRetrievalStrategy` 和 `SetRetrievalStrategy(...)` 保留，兼容适配器仍生成问题嵌入。返回记录应包含重排与上下文构建所需的正文和元数据。

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` 跳过问题嵌入。文档写入仍为现有向量存储分块并生成嵌入；这不是纯文本建索引API。若延迟初始化在首次提问时注册文档，仍会调用文档嵌入服务。

问题的 `Embedding` 阶段按检索器需要执行，关键词搜索不会报告该阶段。自定义检索器可通过 `request.ProgressAsync` 报告实际阶段。文档嵌入不变。

## 为什么需要自定义管道？

默认 RAG 管道开箱即用效果良好，但实际项目往往需要更多控制：

- **调试** — 哪个阶段慢？改写器是否以意想不到的方式修改了查询？
- **提示词工程** — 默认提示词模板可能不适合你的业务领域的语气或约束
- **架构** — 多个服务共享一个索引，节省内存并保持嵌入一致性
- **检查** — 有时你需要在将检索结果发送给 LLM 之前先查看它们

本章介绍提供这些控制能力的工具。

除检索阶段进度外，如果还要控制答案生成时的显示和停止，可使用 `RagEnabledService.StartRunAsync` 返回的 Run。追加指令不会自动重复 RAG 检索。参见 [Run 使用指南](execution-api-transition.md)。

## 进度追踪

通过每次查询的异步回调追踪当前正在执行的 RAG 阶段：

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // 阶段：QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
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
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

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
   查询改写 → 过滤 → 嵌入（按需） → 检索 → 上下文组装
    ↓
② TemplateContextBuilder 替换 {context} 和 {question}
   → "根据以下信息回答。\n[1] 30天内可退货...\n问题：退款政策是什么？"
    ↓
③ RagEnabledService 创建 AIRequestContext
   RequestMessageOverride = 组装后的提示词
    ↓
④ 调用 _innerService.GetCompletionAsync(原始消息, context: context)
   → AIService 将 context 存储在 AsyncLocal 中
   → 原始问题添加到对话历史
    ↓
⑤ AIService.GetLatestMessages() 替换当前请求的最初输入
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
var processed = await RewriteAndProcessAsync(query, options, cancellationToken);
var original = new Message(ActorRole.User, query);
return await _innerService.GetCompletionAsync(
    original,
    context: BuildRequestContext(processed, original),
    cancellationToken: cancellationToken);

private static AIRequestContext BuildRequestContext(RagProcessedQuery processed, Message original)
{
    var requestMessage = original.Clone();
    requestMessage.Content = processed.RequestMessageContent;
    if (original.HasMultimodalContent)
    {
        requestMessage.Contents = new List<MessageContent>
        {
            new TextContent(processed.RequestMessageContent)
        };
        requestMessage.Contents.AddRange(
            original.Contents.Where(content => !(content is TextContent)));
    }
    return new AIRequestContext { RequestMessageOverride = requestMessage };
}
```

`AIService` 将上下文存储在 `AsyncLocal` 中。`GetLatestMessages()` 仅对当前逻辑请求的最初输入应用 `RequestMessageOverride`，保留后续助手的工具调用和工具结果。因此，后续模型请求可以同时携带检索文档和工具结果。完成后恢复之前的上下文。
