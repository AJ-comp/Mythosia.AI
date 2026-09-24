# 嵌入

> 📍 **问答检索管道：** [查询改写](rag-query-rewriting.md) → [过滤](rag-filtering.md) → **`嵌入（按需）`** → [检索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → [上下文构建](rag-context-build.md)

问题的 `Embedding` 阶段按检索器需要执行，关键词搜索不会报告该阶段。自定义检索器可通过 `request.ProgressAsync` 报告实际阶段。文档嵌入不变。

## 什么是嵌入？

嵌入是将文本转换为**数值向量**（数字数组）的过程，向量能够捕捉文本的语义。在这个向量空间中，**语义相似的文本会彼此靠近**。

想象在地图上标注城市：地理位置相近的城市在地图上也会靠在一起。同样，"怎么取消订阅？"和"我想结束会员资格"虽然用词完全不同，但因为语义相近，会生成相似的向量。

在 RAG 管道中，嵌入在两个环节使用：

1. **文档索引时** — 每个文本块被向量化并存入向量存储
2. **查询时** — 用户的问题被向量化，用于相似度搜索

本页重点介绍查询时的嵌入（步骤 2）。

## 内置嵌入提供者

根据文档语言、部署环境和检索需求选择嵌入提供者。

### Perplexity

标准嵌入独立处理各段落，并实现 `IEmbeddingProvider`，因此可接入现有构建器。上下文嵌入保留相邻分块顺序和文档分组；为防止把无关文档展平成单一输入，使用单独的 API。

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

索引时按批次处理文本块：

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
用户问题 (string) → EmbeddingProvider.GetEmbeddingAsync() → 查询向量 (float[])
```

该向量传递到下一步（[过滤](rag-filtering.md)），然后进入[检索](rag-hybrid-search.md)。

## 后续步骤

- [过滤](rag-filtering.md) — 缩小搜索范围
- [混合检索](rag-hybrid-search.md) — 结合向量搜索与关键词搜索
- [管道自定义](rag-pipeline.md) — 跨服务共享嵌入提供者
