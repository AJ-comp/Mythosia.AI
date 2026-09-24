# RAG（检索增强生成）

只需完整答案和停止按钮时，将 `cancellationToken` 传给 `GetCompletionAsync`。进度事件或受支持的中途追加指令使用 Run。参阅[取消回答](completions.md#completion-cancellation)。

使用检索上下文回答时，也向 `RagEnabledService.GetCompletionAsync` 传入 `cancellationToken`。同一令牌贯穿检索、`LlmQueryRewriter`、`LlmReranker` 和内部完成调用；检索中取消会阻止后续模型调用。`RagPipeline.QueryAndGenerateAsync` 也传递令牌。各组件必须配合取消，已完成的检索或工具操作不会回滚。

RAG 通过在查询时检索相关文本片段，让模型基于你自己的文档来回答问题。

需要逐段显示基于检索结果的答案并允许停止生成时，可以使用 `RagEnabledService.StartRunAsync`。检索在 Run 之前执行，追加指令不会自动触发重新检索。示例和适用范围见 [Run 使用指南](execution-api-transition.md)。


通过 `IAIService` 引用时，使用 `Mythosia.AI.Extensions` 的 `GetLastProcessing()`。它读取可选的 `IAIProcessingInfoService`；不支持诊断时返回空列表。`IAIService` 不增加必需成员。RAG 中 `RagEnabledService.WithSpeed(...)` 配置检索后的下一次回答，`LastProcessing` 描述该回答；内部查询改写保持分离。Run 结果提供相同的 `Processing` 记录。 [WithSpeed](request-building.md#inference-speed)

## 安装

```bash
dotnet add package Mythosia.AI.Rag
```

## 快速上手

在任何 `IAIService` 上使用 `.WithRag()` 即可通过流式 API 启用 RAG：

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("退款政策是什么？");
```

文档会被自动分割、嵌入并存储。查询时，最相关的文本片段会被检索并注入到提示词中。

如果文档索引已由提供商管理，请比较[托管文件搜索与 RAG](reasoning-and-search.md)的适用场景。RAG 检索引用与提供商返回的来源引用分别保存。

可选的 `Mythosia.AI.Rag.Search.Pixie` 预览版可比较本地神经网络稀疏搜索与现有搜索。它保留现有稠密嵌入服务，将 PIXIE 索引放在内存中，不迁移持久化存储，也不自动替换默认搜索。 [PIXIE 配置与比较指南（英文）](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## 结合附件和自己的文档回答问题

要结合手册解释产品照片，请将包含问题和图片的 `Message` 传给 `RagEnabledService.GetCompletionAsync(Message)` 或 `StartRunAsync(Message)`。两种方式都会在发往内部 AI 服务的请求中保留非文本附件。检索依据消息文本，不会自动为附件本身建立索引或生成嵌入。所选提供商和模型必须支持该附件类型。检索到的上下文只加入待发送的请求，不会覆盖原始 `Message`，也不会用检索内容替换对话历史中的用户文本。

如果回答既需要手册，也需要实时库存，请将 RAG 与已注册的工具结合使用。在 `GetCompletionAsync` 的工具调用过程中，检索上下文始终保留在最初的输入上，后续每个工具结果都会原样发送给模型。对话历史保留用户的原始输入。

<a id="retrieval-modes"></a>

## 选择文档检索方式

商品代码适合关键词搜索，而与文档措辞不同的问题需要语义搜索。选择检索器后只执行所需处理，关键词搜索不再先生成问题嵌入。

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` 跳过问题嵌入。文档写入仍为现有向量存储分块并生成嵌入；这不是纯文本建索引API。若延迟初始化在首次提问时注册文档，仍会调用文档嵌入服务。

参见[检索模式与存储支持](rag-hybrid-search.md)和[自定义检索器](rag-pipeline.md#custom-retriever)。

## 添加文档

支持多种来源类型：

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // 本地文件
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("也可以直接添加文本内容。")            // 原始字符串
)
```

`AddUrl` 会在读取文本前验证并解开支持的 HTTP 压缩，拒绝不完整、不支持或多层压缩的响应。请参阅 [URL 解压与取消](rag-pipeline.md#url-documents)。

<a id="document-identity"></a>

### 区分同名文件

两家公司可能各自提供一个 `docs/faq.txt`。这两份文档都应保留在索引中，而重新注册同一文件时应继续使用同一文档标识：

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

在默认的 RAG 存储流程中，文档 ID 会在记录发送到向量存储之前生成，随后替换具有相同 `document_id` 的记录。本库的 PostgreSQL（pgvector）存储使用该 ID，不会自行检查原始文件路径。此前，目录注册会为 `company-a/docs/faq.txt` 和 `company-b/docs/faq.txt` 都发送 `faq.txt`，因此第二份文档替换了第一份。此次修复在生成 ID 时保留完整路径；PostgreSQL 数据库结构不变。 存储示例中的 `full_path` 过滤条件使用调用方提供的元数据，不会自动生成唯一的文档或记录 ID。

内置的 `PlainTextDocumentLoader` 和 `DirectoryDocumentLoader` 使用经 `Path.GetFullPath` 规范化的文件绝对路径作为 `Source` 和自动文档 ID。因此，不同目录中的文件具有不同 ID。相对路径、绝对路径和含 `./` 的路径，只要解析为大小写也相同的绝对路径，就会使用同一 ID。使用相对路径时，请保持工作目录一致。移动文件、通过符号链接或硬链接访问，以及大小写不同的路径，不保证保留同一 ID。

`AddText(..., id: ...)`、显式指定的 `RagDocument.Id` 和自定义加载器的 `Source` 规则保持不变，无需更改调用 API。这些内置加载器的 `Source` 现在为绝对路径，因此默认引用也可能显示绝对路径。界面可使用 `filename` 元数据，或默认目录加载器提供的 `relative_path`。带配置回调的目录注册重载不会自动添加 `relative_path`。

**迁移现有索引：** 旧的相对路径 ID 不会自动删除或迁移。建议将所有文档重新索引到新集合，验证后再切换应用程序。如果复用原集合，只删除已确认归属的旧文档 ID，再重新索引其源文件。不要仅按文件名批量删除，否则可能影响其他目录中的同名文档。

为使更新和删除只作用于目标文档，`document_id` 是管道保留键。持久化前，每条记录都会使用实际的 `RagDocument.Id`，即使输入元数据指定了其他值。输入文档和分割器提供的元数据字典本身不会被修改；自定义持久化回调也会收到规范化后的记录。应用自身的标识请使用其他键。

这不会自动修复已用错误 `document_id` 保存的记录。请用可信原文重建新集合，或确认受影响记录的归属后，仅清理这些记录并重新索引。仅以正确 ID 重新注册，无法可靠地找到保存在其他 ID 下的旧记录。

用相对路径和绝对路径注册同一文件时，应更新同一文档；不同文件夹中的同名文件则应保持独立。`WordDocumentLoader`、`ExcelDocumentLoader`、`PowerPointDocumentLoader` 和 `PdfDocumentLoader` 现在与内置 TXT 加载器一样，将 `DoclingDocument.Source` 设置为规范化的绝对文件路径。RAG 据此生成自动文档 ID，显式 ID 仍由调用方管理。默认来源引用可能显示绝对路径。

[让同一文件的标识保持稳定](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### 将文档更新为空时同时清除旧搜索内容

清空已废止的退款说明并重新索引同一文档后，旧说明不应继续出现在回答中。在默认 RAG 存储流程中，如果分块正常完成且结果为 0 块，就会将匹配该 `document_id` 的现有记录替换为空集合。不会请求嵌入，也不会修改其他文档 ID 的数据。这适用于分块结果为 0 的空文档或仅含空白的文档，也适用于正常返回 0 块的自定义分块器。

对于已经配置好的 `RagPipeline` 实例 `pipeline`，请沿用已存储文档的 ID：

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

以后可以用相同 ID 重新索引非空内容。加载器没有返回任何文档，或者某文档从后续文件列表中消失，并不表示要求删除：此时并未提供需要替换的文档 ID。

加载、解析或分块过程中发生异常，或在调用存储之前检测到取消时，会保留该文档的现有记录。加载器和解析器必须通过异常报告失败；仅凭正常返回的 0 块结果，无法区分失败和有意清空。存储开始后的失败或取消能否回滚取决于存储实现；PostgreSQL 的替换操作使用事务。批量索引按文档处理，不会回滚此前已完成的文档。

**自定义存储：**提供 `onDocumentEmbedded` 时，存储仍由该回调负责。结果为 0 块时不会调用回调，也不会访问默认存储。应用程序必须使用已知文档 ID 在自己的存储中显式删除，或使用 `DeleteDocumentAsync` 删除管道存储中的记录。

## 自定义嵌入提供商

默认情况下，RAG 使用内置的本地嵌入提供商。如需使用专用嵌入模型：

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## 自定义向量存储

默认使用内存存储。生产环境请接入持久化向量存储：

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = connectionString,
    Dimension = 1536
});

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .AddDocument("large-corpus.txt")
    );
```

## 查询选项

按查询微调检索行为：

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,          // 检索的文本片段数量
        MinScore = 0.7     // 最低相似度分数
    }
};

var response = await service.GetCompletionAsync("你的问题", options: options);
```

## 后续步骤

- [混合搜索](rag-hybrid-search.md) — 语义搜索与关键词搜索结合
- [查询重写](rag-query-rewriting.md) — 基于对话上下文优化查询
- [重新排序](rag-reranking.md) — 进一步提升搜索结果准确度
- [管线自定义](rag-pipeline.md) — 精细控制 RAG 流程
- [智能体 RAG](rag-agentic.md) — AI 自行判断何时搜索什么
- [向量存储](vectordb-overview.md) — 持久化存储配置
- [文本分割器](text-splitters.md) — 自定义文档分割方式

Perplexity: [为自有文档索引使用向量 / 仅搜索，不生成答案](perplexity.md).
