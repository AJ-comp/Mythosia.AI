# 嵌入

> 📍 **问答检索管道：** [查询改写](rag-query-rewriting.md) → **`嵌入`** → [过滤](rag-filtering.md) → [检索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → [上下文构建](rag-context-build.md)

## 什么是嵌入？

嵌入是将文本转换为**数值向量**（数字数组）的过程，向量能够捕捉文本的语义。在这个向量空间中，**语义相似的文本会彼此靠近**。

想象在地图上标注城市：地理位置相近的城市在地图上也会靠在一起。同样，"怎么取消订阅？"和"我想结束会员资格"虽然用词完全不同，但因为语义相近，会生成相似的向量。

在 RAG 管道中，嵌入在两个环节使用：

1. **文档索引时** — 每个文本块被向量化并存入向量存储
2. **查询时** — 用户的问题被向量化，用于相似度搜索

本页重点介绍查询时的嵌入（步骤 2）。

## 内置嵌入提供者

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

基于特征哈希的轻量提供者，无需 API 密钥，适合**原型开发**：

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

## 批处理

索引时按批次处理文本块：

```csharp
var options = new RagPipelineOptions
{
    EmbeddingBatchSize = 100   // 默认：每次 API 调用 100 个块
};
```

## 向量维度

| 提供者 | 模型 | 默认维度 |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 1024 |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 |
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
