# 混合檢索

商品代碼適合關鍵字搜尋，而與文件措辭不同的問題需要語意搜尋。選擇檢索器後只執行所需處理，關鍵字搜尋不再先產生問題嵌入。

## 內建檢索模式

```csharp
// 語意搜尋（預設）
.UseVectorSearch()

// 不產生問題嵌入的關鍵字搜尋
.UseKeywordSearch()

// 加權混合搜尋
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()` 略過問題嵌入。文件寫入仍為既有向量儲存區分塊並產生嵌入；這不是純文字建立索引的API。若延遲初始化在首次提問時註冊文件，仍會呼叫文件嵌入服務。

## 結合關鍵字與語意結果

`VectorWeight` 是向量權重（0–1），關鍵字權重為 `1 - VectorWeight`。`CandidateMultiplier` 控制每路搜尋的候選數量，`RrfK` 控制加權Reciprocal Rank Fusion的排名平滑。它們與RAG重新排序候選倍數不同。請用實際文件和問題評估參數。

純向量及關鍵字模式保留原生分數。可設定的混合搜尋即使只有一路也使用正規化加權RRF；向量權重為0時不產生問題嵌入。分數不是機率。`WeightedBlend` 未經校準便直接組合檢索與重新排序分數；未經校準的關鍵字搜尋建議使用預設 `RerankerOnly`。

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

## 儲存區支援與相容性

InMemory、PostgreSQL、Qdrant支援新的關鍵字搜尋與可設定的加權RRF。文字評分不同：InMemory使用BM25，PostgreSQL使用設定的全文搜尋或trigram，Qdrant使用稀疏索引。不同引擎的分數不能直接等同。

Pinecone在相容的 `dotproduct` 索引上保留預設設定 `UseHybridSearch()` 的原生混合搜尋。此介面卡不支援關鍵字模式或可設定的加權RRF。其他儲存區也必須實作對應的選用介面。不支援的模式或選項會明確報錯，不會悄悄改用向量搜尋或忽略權重。

既有 InMemory、PostgreSQL 和 Qdrant 介面卡不會安裝神經網路模型或遷移索引。`C#` 與 `C++` 的區別取決於各自分析器。下方的 PIXIE 選項也需要實際驗證識別碼的精確比對效果。

參見[檢索模式與儲存區支援](rag.md#retrieval-modes)及[自訂檢索器](rag-pipeline.md#custom-retriever)。

<a id="pixie-search"></a>

## 使用 PIXIE 比較本機神經網路搜尋

當問題與文件使用不同表達時，學習型稀疏搜尋可以補充相關詞彙。選用套件 `Mythosia.AI.Rag.Search.Pixie` 在本機用 PIXIE 編碼文件和問題，並與既有稠密向量搜尋結合。PIXIE 推論不需要 Python 伺服器或 API 金鑰；選用的稠密嵌入或回答生成服務仍可能使用遠端 API。

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

在此儲存區中，`UseKeywordSearch()` 選擇神經網路稀疏搜尋：略過稠密查詢嵌入服務，但仍會對問題執行 PIXIE。RAG 文件匯入仍建立稠密嵌入。`UseHybridSearch(...)` 透過設定的加權 RRF 合併稀疏向量內積排名與稠密向量餘弦相似度排名。

此預覽版提供記憶體索引 `PixieInMemoryStore`，不會把 PIXIE 接到 PostgreSQL、Qdrant 或 Pinecone。重新啟動或更換模型、設定後需重建索引。所有儲存操作結束前請保持編碼器有效，完成後再釋放。既有搜尋仍為預設方式；請先用相同文件和已標註相關性的問題比較，再決定切換。PIXIE 不保證精確區分 `C#` 與 `C++`，也不保證滿足排除條件。

[PIXIE 設定與比較指南（英文）](../rag-pixie-search.md).
