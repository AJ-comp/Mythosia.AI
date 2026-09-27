# 嵌入

> 📍 **问答检索管道：** [查询改写](rag-query-rewriting.md) → [过滤](rag-filtering.md) → **`嵌入（按需）`** → [检索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → [上下文构建](rag-context-build.md)

问题的 `Embedding` 阶段按检索器需要执行，关键词搜索不会报告该阶段。自定义检索器可通过 `request.ProgressAsync` 报告实际阶段。

<a id="retrieval-aware-embeddings"></a>

## 保留文档上下文和查询用途

分块的含义可能依赖相邻段落，搜索问题与索引文档的用途也不同。RAG 8.2.0 为从 TXT、Markdown 和 PDF 提取的文本提供 Voyage 上下文嵌入和 Gemini Embedding 2。

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

`IRetrievalEmbeddingProvider : IEmbeddingProvider` 是可选能力，现有提供者继续工作。索引将所有有序分块放入不可变的 `EmbeddingDocument(documentId, chunks, title)`，不受 `EmbeddingBatchSize` 限制。标题取自 `RagDocument.Metadata["title"]`。向量检索和诊断调用 `GetQueryEmbeddingAsync`；原有提供者继续使用 `GetEmbeddingsAsync` 批次和 `GetEmbeddingAsync` 查询。纯关键词检索不生成查询嵌入。

为每个存储选择一种嵌入配置：

```csharp
rag.UseVoyageEmbedding(voyageApiKey, httpClient,
    model: "voyage-context-4", dimensions: 1024,
    timeout: TimeSpan.FromSeconds(60));

rag.UseGeminiEmbedding(geminiApiKey, httpClient,
    model: "gemini-embedding-2", dimensions: 1536,
    timeout: TimeSpan.FromSeconds(60), maxConcurrency: 4);
```

### Voyage

`VoyageContextualizedEmbeddingProvider` 默认使用 `voyage-context-4`、1024 维，可选 256、512、1024 或 2048 维。整篇文档作为一个有序组以 `input_type=document` 发送，查询单独成组并使用 `input_type=query`。自动分块关闭。每篇文档最多 16,000 个分块，令牌上限由服务端检查。通用方法省略 `input_type`，最多将 1,000 段文本分别作为独立的单分块组处理。文档 ID 和标题不发送。 [Voyage API](https://docs.voyageai.com/docs/contextualized-chunk-embeddings).

通用批处理在读取输入时检查取消请求；文本一旦超过 1,000 段，就停止读取并拒绝该批次，不发送 HTTP 请求。文档分组保持完整。

### Gemini

`GeminiEmbeddingProvider` 默认使用 `gemini-embedding-2`、1536 维（128–3072）及 `maxConcurrency=4`。每个分块通过独立 HTTP 请求得到一个向量。检索输入格式为 `title: {title} | text: {text}`（缺少标题时用 `none`）或 `task: search result | query: {query}`。前缀仅用于 HTTP 输入，通用方法发送原文。`embedContentConfig.autoTruncate=false` 会拒绝过长输入，而非静默截断。 [Gemini API](https://ai.google.dev/gemini-api/docs/embeddings).

两者保留存储的原文，验证向量数量、维度及有限值。传入的 `HttpClient` 仍由调用方拥有，配置不变。Voyage 根据经过验证的响应索引恢复文档和分块顺序。错误不包含密钥或远端响应内容；取消向下传递，超时抛出 `TimeoutException`。Voyage 的 `timeout` 按请求生效，Gemini 则覆盖整个操作及并发等待；客户端超时也仍然生效。不支持的输入不会被静默拆分或截断。持久化前失败会保留旧文档；开始写入后的原子性取决于存储或回调实现。更换模型、维度或检索格式后，应重新索引文档并将存储配置为相同向量空间。

### 验证真实服务

实时测试会发送合成的 TXT、Markdown 和 PDF 文本，并产生 API 费用。设置 `MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE=1` 和凭据，然后选择 `All`、`Voyage` 或 `Gemini`。执行器拒绝跳过或无法判定的案例；离线测试不代表服务当前可用。

```powershell
$env:MYTHOSIA_RETRIEVAL_EMBEDDING_LIVE = "1"
pwsh -NoProfile -File build/test-retrieval-embedding-live.ps1 -Provider All
```

[验证真实服务](https://github.com/AJ-comp/Mythosia.AI/blob/main/build/RELEASE.md#retrieval-embedding-live-validation).

## 什么是嵌入？

嵌入是将文本转换为**数值向量**（数字数组）的过程，向量能够捕捉文本的语义。在这个向量空间中，**语义相似的文本会彼此靠近**。

想象在地图上标注城市：地理位置相近的城市在地图上也会靠在一起。同样，"怎么取消订阅？"和"我想结束会员资格"虽然用词完全不同，但因为语义相近，会生成相似的向量。

在 RAG 管道中，嵌入在两个环节使用：

1. **文档索引时** — 每个文本块被向量化并存入向量存储
2. **查询时** — 用户的问题被向量化，用于相似度搜索

## 内置嵌入提供者

根据文档语言、部署环境和检索需求选择嵌入提供者。

### Perplexity

`PerplexityContextualizedEmbeddingProvider` 现在实现 `IRetrievalEmbeddingProvider`，可用 `.UseEmbedding(contextual)` 注册。现有公开分组方法 `GetDocumentEmbeddingsAsync` 和二进制方法保留。新增的单文档方法采用显式接口实现，保持原有调用兼容。RAG 保留文档边界，查询使用相同上下文模型和维度。

Perplexity 浮点和二进制批次最多接受 512 段独立文本，或 512 个上下文文档、合计 16,000 个分块。读取输入时检查取消，超出限制便停止读取，并在发送 HTTP 请求前拒绝。文档分组和顺序保持不变。详见 [Perplexity 指南](perplexity.md)。

[Perplexity Agent API、搜索与嵌入](perplexity.md).

### OpenAI

```csharp
var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",
    dimensions: 1536
);
```

Builder 简写：

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

<a id="openai-dimensions"></a>

`text-embedding-ada-002` 固定使用 **1536 维**。提供程序在单条和批量请求中省略该模型不支持的 `dimensions` 字段；如果配置其他维数，会在调用 API 前抛出 `ArgumentOutOfRangeException`。`text-embedding-3-small` 和 `text-embedding-3-large` 仍会在请求中发送配置的 `dimensions`。

### Ollama（本地）

通过 [Ollama](https://ollama.com/) 在本地运行嵌入：

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",
    dimensions: 1024,
    baseUrl: "http://localhost:11434"
);
```

<a id="ollama-dimensions"></a>

文档和查询向量必须使用相同的模型与维度。`OllamaEmbeddingProvider` 将配置的 `dimensions` 发送到 `/api/embed`，并验证返回的每个向量是否具有该长度。提供程序仍默认使用 `qwen3-embedding:4b` 并**请求 1024 维**；模型的原生输出为 2560 维。Ollama 服务器和所选模型必须支持请求的维度。不支持的请求或忽略设置的响应会失败，不会静默更改 `Dimensions` 或在本地调整向量长度。

更改模型或维度后，请使用与查询相同的设置重新生成文档嵌入，并相应配置向量存储。已有向量不会自动转换。

### vLLM（自托管）

适合运行自有 [vLLM](https://docs.vllm.ai/) 服务器的团队：

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B",
    dimensions: 1024,
    baseUrl: "http://localhost:8002"
);
```

### Local（无需 API）

基于特征哈希的轻量提供者，无需 API 密钥或外部服务。但嵌入质量远低于神经网络模型，**不建议在生产环境中使用**。

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **提示：** 建议改用 `OpenAIEmbeddingProvider` 的 `text-embedding-3-small` 模型。费用极低，几乎免费，效果远优于本地方案。

## 批处理

`EmbeddingBatchSize` 控制原有 `IEmbeddingProvider` 实现的平面批次。`IRetrievalEmbeddingProvider` 接收整篇文档并自行管理 HTTP 批次；调小该值不会把 Voyage 文档分成多个上下文组。

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // 默认：每次 API 调用 100 个块
pipeline.Options = options;
```

`EmbeddingBatchSize` 必须为正数。管道在每次文档索引调用开始时，在嵌入或替换存储记录之前验证并固定本次调用使用的值，以防止空批次循环，以及等待响应期间修改设置导致跳过块。后续索引调用可以使用新设置。

<a id="embedding-validation"></a>

## 让每个向量始终对应正确的分块

HTTP 响应成功仍可能缺少向量或顺序错误，导致文本与另一个分块的含义配对。自定义 `IEmbeddingProvider` 必须按输入顺序，为每个输入返回一个非 null 的 `float[]`，并提供正数 `Dimensions`。每个向量的长度必须等于该维度，所有元素必须是有限值（不能有 `NaN` 或无穷大）。

文档索引期间，管线会在保存或调用 `onDocumentEmbedded` 之前，以 `InvalidOperationException` 拒绝无效维度、响应数量或向量。每个向量都会在请求下一批次前复制，因此提供程序在后续批次复用缓冲区不会改变之前的分块。调用方读取期间应保持返回数据稳定；不支持在验证或复制期间并发修改。验证失败时，该文档的现有记录保持不变。

`OpenAIEmbeddingProvider` 要求每个响应项都有有效且唯一的 `index`，并恢复输入顺序。`VllmEmbeddingProvider` 在包含索引时采用同样规则；为保持兼容性，也接受所有项均省略 `index` 的响应，并使用响应顺序。部分缺失、重复或越界的索引均被拒绝。自定义提供程序或无索引响应的顺序仍由提供程序负责；结构检查无法验证向量的实际含义。

<a id="query-embedding-validation"></a>

## 搜索前保护问题向量

等待进度通知或搜索时，提供程序复用缓冲区不应让问题向量变成另一个问题。内置密集向量检索（包括 `IRetrievalStrategy` 适配器）要求正数 `Dimensions`、长度完全匹配的非 null 向量及有限值。无效结果会在搜索前抛出 `InvalidOperationException`。有效向量在返回后立即复制，早于后续进度通知和搜索。提供程序应在读取期间保持数据稳定；自定义 `IRagRetriever` 负责自己的问题准备与验证。

直接调用 `OllamaEmbeddingProvider` 的单条或批量方法时，也会验证响应结构、准确向量数量、维度及有限值。错误的 JSON 或向量会抛出 `InvalidOperationException`，不会静默返回不完整结果。传入的 `HttpClient` 仍归调用方所有；释放各 HTTP 请求和响应不会释放该客户端。

## 向量维度

| 提供者 | 模型 | 默认维度 |
| --- | --- | --- |
| Voyage | voyage-context-4 | 1024 |
| Gemini | gemini-embedding-2 | 1536 |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 请求 1024（原生：2560） |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | （特征哈希） | 1024 |

## 自定义提供者

实现 `IEmbeddingProvider` 接口：

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // 调用你的 API
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // 批量调用
    }
}
```

## 内部机制

```
用户问题 (string) → GetQueryEmbeddingAsync() / GetEmbeddingAsync() → 查询向量 (float[])
```

该向量传递到下一步（[过滤](rag-filtering.md)），然后进入[检索](rag-hybrid-search.md)。

## 后续步骤

- [过滤](rag-filtering.md) — 缩小搜索范围
- [混合检索](rag-hybrid-search.md) — 结合向量搜索与关键词搜索
- [管道自定义](rag-pipeline.md) — 跨服务共享嵌入提供者
