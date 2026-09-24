# 混合检索

商品代码适合关键词搜索，而与文档措辞不同的问题需要语义搜索。选择检索器后只执行所需处理，关键词搜索不再先生成问题嵌入。

## 内置检索模式

```csharp
// 语义搜索（默认）
.UseVectorSearch()

// 不生成问题嵌入的关键词搜索
.UseKeywordSearch()

// 加权混合搜索
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` 跳过问题嵌入。文档写入仍为现有向量存储分块并生成嵌入；这不是纯文本建索引API。若延迟初始化在首次提问时注册文档，仍会调用文档嵌入服务。

## 结合关键词和语义结果

`VectorWeight` 是向量权重（0–1），关键词权重为 `1 - VectorWeight`。`CandidateMultiplier` 控制每路搜索的候选数量，`RrfK` 控制加权Reciprocal Rank Fusion的排名平滑。它们与RAG重排候选倍数不同。请用实际文档和问题评估参数。

纯向量和关键词模式保留原生分数。可配置混合搜索即使只有一路也使用归一化加权RRF；向量权重为0时不生成问题嵌入。分数不是概率。`WeightedBlend` 不经校准直接组合检索和重排分数；未经校准的关键词搜索建议使用默认 `RerankerOnly`。

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## 存储支持与兼容性

InMemory、PostgreSQL、Qdrant支持新的关键词搜索与可配置加权RRF。文本评分不同：InMemory使用BM25，PostgreSQL使用配置的全文检索或trigram，Qdrant使用稀疏索引。不同引擎的分数不能直接等同。

Pinecone在兼容的 `dotproduct` 索引上保留默认配置 `UseHybridSearch()` 的原生混合搜索。此适配器不支持关键词模式或两路混合的可配置加权RRF。其他存储也必须实现相应的可选接口。不支持的模式或选项会明确报错，而不会悄悄改用向量搜索或忽略权重。

现有 InMemory、PostgreSQL 和 Qdrant 适配器不会安装神经网络模型或迁移索引。`C#` 与 `C++` 的区别取决于各自分析器。下面的 PIXIE 选项也需要实际验证标识符的精确匹配效果。

参见[检索模式与存储支持](rag.md#retrieval-modes)和[自定义检索器](rag-pipeline.md#custom-retriever)。

<a id="pixie-search"></a>

## 使用 PIXIE 比较本地神经网络搜索

当问题与文档使用不同表达时，学习型稀疏搜索可以补充相关词汇。可选包 `Mythosia.AI.Rag.Search.Pixie` 在本地用 PIXIE 编码文档和问题，并与现有稠密向量搜索结合。PIXIE 推理无需 Python 服务器或 API 密钥；所选的稠密嵌入或回答生成服务仍可能使用远程 API。

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

在此存储中，`UseKeywordSearch()` 选择神经网络稀疏搜索：跳过稠密查询嵌入服务，但仍会对问题运行 PIXIE。RAG 文档导入仍创建稠密嵌入。`UseHybridSearch(...)` 通过配置的加权 RRF 合并稀疏向量内积排名和稠密向量余弦相似度排名。

此预览版提供内存索引 `PixieInMemoryStore`，不会把 PIXIE 接入 PostgreSQL、Qdrant 或 Pinecone。重启或更换模型、配置后需要重新建立索引。所有存储操作结束前请保持编码器有效，完成后再释放。现有搜索仍为默认方式；请先使用相同文档和已标注相关性的问题比较，再决定切换。PIXIE 不保证精确区分 `C#` 与 `C++`，也不保证满足排除条件。

[PIXIE 配置与比较指南（英文）](../rag-pixie-search.md).
