# RAG 管線自訂

<a id="indexing-validation"></a>

## 索引失敗時保護既有文件

自訂分割器或嵌入回應有誤時，不應悄悄用不完整或對應錯誤的內容取代可搜尋文件。管線會在每份文件開始持久化之前進行驗證，使用 `onDocumentEmbedded` 時也一樣。

在嵌入、儲存或持久化回呼之前，若 `RagDocument.Id` 為 null、空字串或僅空白，則擲回 `ArgumentException`。分割結果清單或區塊為 null、`Content` 或 `Metadata` 為 null、區塊 ID 為空白或在同一文件內重複時，擲回 `InvalidOperationException`。重複檢查使用區分大小寫的 `StringComparer.Ordinal`。第一次嵌入呼叫前會複製區塊欄位和中繼資料。

有效的自訂 ID 會原樣保留，不會自動產生、修剪或修復，也不會在整個儲存區中偵測不同文件的自訂區塊 ID 衝突。請像[自訂分割器範例](text-splitters.md)一樣使用目標集合中唯一的 ID。保留鍵 `document_id` 只在用於儲存的複本上正規化，原始中繼資料不變。

無效 ID、分割失敗和無效嵌入批次會保留該文件的現有記錄，不呼叫持久化回呼。文件的所有批次必須通過[嵌入驗證](rag-embedding.md#embedding-validation)後才開始儲存。這不會復原同一次作業中先前已完成的其他文件；儲存開始後的復原取決於儲存區或回呼實作。

這些驗證和回應順序修正不會自動復原已被覆寫的本文或先前與錯誤區塊關聯的已儲存向量，因此受影響的文件需要從原始來源重新建立索引。

<a id="custom-persistence"></a>

## 在持久化回呼中取代整份文件

文件縮短後，若只 upsert 新區塊，舊尾段仍會出現在搜尋結果中。`onDocumentEmbedded` 完全接管預設持久化，因此應使用記錄中已正規化的 `document_id` 取代整份文件。回呼每次會收到一份驗證通過且非空的文件：

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

成功分割後若區塊數為 0，不會呼叫此回呼，也不會存取預設儲存。請使用已知文件 ID 在自己的儲存中明確刪除；`DeleteDocumentAsync` 僅用於管線的儲存。取代的不可分割性及回復取決於所選儲存或回呼實作。

<a id="url-documents"></a>

## 安全讀取 URL 文件

伺服器可能會壓縮文字後再傳輸。`AddUrl` 會在讀取文字前解壓縮 `gzip`、`deflate` 和 Brotli（`br`），並檢查壓縮串流是否完整。即使 HTTP 傳輸成功，只要壓縮資料被截斷、解壓縮出錯或格式自帶的總和檢查碼驗證失敗，就會在嵌入或儲存前中止載入，保留該文件的既有記錄。不支援的 `Content-Encoding` 或多層壓縮編碼也會在嵌入或儲存前遭到拒絕。

若要停止等待緩慢的 URL 文件，請傳入 `cancellationToken` 至 `RagStore.BuildAsync`。此權杖會傳遞至 HTTP 要求、回應本文讀取和解壓縮過程。取消採合作方式，不會復原先前已完成的其他文件寫入。

<a id="custom-retriever"></a>

## 接入不強制嵌入的檢索器

商品代碼適合關鍵字搜尋，而與文件措辭不同的問題需要語意搜尋。選擇檢索器後只執行所需處理，關鍵字搜尋不再先產生問題嵌入。

- 之前：所有檢索策略先收到問題嵌入。
- 之後：選定檢索器準備所需的查詢表示。

搜尋外部索引或使用其他查詢表示時，實作 `IRagRetriever`。`RagRetrievalRequest` 包含 `Query`（完整語意查詢）、可為null的 `TextQuery`（詞法查詢覆寫）、`TopK`、`Filter` 與 `ProgressAsync`。內建檢索器在 `TextQuery` 為null時使用 `Query`，為空字串時略過文字搜尋。自訂檢索器必須準備查詢並套用篩選、結果數量限制與取消要求。

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

透過 `UseRetriever(...)` 或 `RagPipeline.SetRetriever(...)` 註冊。既有 `IRetrievalStrategy` 與 `SetRetrievalStrategy(...)` 保留，相容介面卡仍產生問題嵌入。傳回紀錄應包含重新排序與上下文組裝所需的本文及中繼資料。

```csharp
store.UpdateRetriever(new CatalogRetriever(catalog));
store.UseKeywordSearch();
store.UpdateRetrievalStrategy(new HybridSearchOptions { VectorWeight = 0.7f });
store.UpdateRetriever(null); // UseVectorSearch
```

`UseKeywordSearch()` 略過問題嵌入。文件寫入仍為既有向量儲存區分塊並產生嵌入；這不是純文字建立索引的API。若延遲初始化在首次提問時註冊文件，仍會呼叫文件嵌入服務。

問題的 `Embedding` 階段依檢索器需要執行，關鍵字搜尋不會回報此階段。自訂檢索器可透過 `request.ProgressAsync` 回報實際階段。文件嵌入不變。

## 為什麼需要自訂管線？

預設 RAG 管線開箱即用效果良好，但實際專案往往需要更多控制 — 除錯、提示詞工程、架構設計和檢查。

除了檢索階段進度，如果還要控制答案產生時的顯示和停止，可使用 `RagEnabledService.StartRunAsync` 傳回的 Run。追加指示不會自動重複 RAG 檢索。參見 [Run 使用指南](execution-api-transition.md)。

## 進度追蹤

```csharp
var options = new RagQueryOptions
{
    ProgressAsync = async stage =>
    {
        Console.WriteLine($"[RAG] {stage}");
        // 階段：QueryRewrite, Filtering, Embedding, Retrieval, Reranking, ContextBuild
    }
};

var response = await ragService.GetCompletionAsync("你的問題", options);
```

## 自訂提示詞範本

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        僅根據以下資訊回答問題。
        如果答案不在上下文中，請回答「我不知道。」

        上下文：
        {context}

        問題：{question}
        """)
    .AddDocument("faq.txt")
)
```

## 共享 RagStore

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .UseOpenAIEmbedding(apiKey)
    .AddDocuments("docs/"));

var claudeRag = new AnthropicService(apiKey, http).WithRag(store);
var gptRag    = new OpenAIService(apiKey, http).WithRag(store);
```

## RagStore 直接查詢

```csharp
RagProcessedQuery result = await store.QueryAsync("退款政策是什麼？");

Console.WriteLine($"改寫後的查詢：{result.RewrittenQuery}");

foreach (var ref_ in result.References)
    Console.WriteLine($"[{ref_.Score:F2}] {ref_.Record.Content[..100]}");
```

## 內部運作原理

呼叫 `.WithRag()` 時，會在你的 AIService 外層建立一個 `RagEnabledService` 包裝器。其關鍵機制是 [AIRequestContext](request-contexts.md)。

### 完整流程

```
ragService.GetCompletionAsync("退款政策是什麼？")
    ↓
① RagEnabledService 執行 RAG 管線
   查詢改寫 → 篩選 → 嵌入（按需） → 檢索 → 上下文組裝
    ↓
② TemplateContextBuilder 替換 {context} 和 {question}
    ↓
③ RagEnabledService 建立 AIRequestContext
   RequestMessageOverride = 組裝後的提示詞
    ↓
④ 呼叫 _innerService.GetCompletionAsync(原始訊息, context: context)
    ↓
⑤ AIService.GetLatestMessages() 替換目前請求的最初輸入
   對話歷史：「退款政策是什麼？」（保留原文）
   模型看到的：組裝後的提示詞（RequestMessageOverride）
```

### 為什麼這樣設計？

- **對話歷史保留原始問題** — 後續追問「那個怎麼樣？」才有正確的上下文
- **模型接收組裝後的提示詞** — 包含檢索到的文件和問題
- **AIService 狀態不會被修改** — `AsyncLocal<T>` 提供每個請求的隔離

### 程式碼實作

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

`AIService` 將上下文儲存在 `AsyncLocal` 中。`GetLatestMessages()` 僅對目前邏輯請求的最初輸入套用 `RequestMessageOverride`，保留後續助理的工具呼叫與工具結果。因此，後續模型請求可以同時包含檢索文件和工具結果。完成後恢復先前的上下文。
