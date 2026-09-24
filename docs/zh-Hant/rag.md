# RAG（檢索增強生成）

只需完整答案和停止按鈕時，將 `cancellationToken` 傳給 `GetCompletionAsync`。進度事件或支援的中途追加指令使用 Run。參閱[取消回答](completions.md#completion-cancellation)。

使用檢索內容回答時，也向 `RagEnabledService.GetCompletionAsync` 傳入 `cancellationToken`。同一權杖貫穿檢索、`LlmQueryRewriter`、`LlmReranker` 和內部完成呼叫；檢索中取消會阻止後續模型呼叫。`RagPipeline.QueryAndGenerateAsync` 也傳遞權杖。各元件必須配合取消，已完成的檢索或工具操作不會回復。

RAG 透過在查詢時檢索相關文字片段，讓模型基於你自己的文件來回答問題。

需要逐段顯示根據檢索結果的答案並允許停止產生時，可以使用 `RagEnabledService.StartRunAsync`。檢索在 Run 之前執行，追加指示不會自動觸發重新檢索。範例和適用範圍見 [Run 使用指南](execution-api-transition.md)。


透過 `IAIService` 參照時，使用 `Mythosia.AI.Extensions` 的 `GetLastProcessing()`。它讀取選用的 `IAIProcessingInfoService`；不支援診斷時回傳空清單。`IAIService` 不增加必要成員。RAG 中 `RagEnabledService.WithSpeed(...)` 設定檢索後的下一次回答，`LastProcessing` 描述該回答；內部查詢改寫保持分離。Run 結果提供相同的 `Processing` 記錄。 [WithSpeed](request-building.md#inference-speed)

## 安裝

```bash
dotnet add package Mythosia.AI.Rag
```

## 快速上手

在任何 `IAIService` 上使用 `.WithRag()` 即可透過流式 API 啟用 RAG：

```csharp
using Mythosia.AI.Rag;

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("退款政策是什麼？");
```

文件會被自動分割、嵌入並儲存。查詢時，最相關的文字片段會被檢索並注入到提示詞中。

如果文件索引已由供應商管理，請比較[託管檔案搜尋與 RAG](reasoning-and-search.md)的適用情境。RAG 檢索引用與供應商傳回的來源引用分別儲存。

選用的 `Mythosia.AI.Rag.Search.Pixie` 預覽版可比較本機神經網路稀疏搜尋與既有搜尋。它保留既有稠密嵌入服務，將 PIXIE 索引放在記憶體中，不遷移持久化儲存區，也不自動取代預設搜尋。 [PIXIE 設定與比較指南（英文）](rag-hybrid-search.md#pixie-search).

<a id="rag-message-attachments"></a>

## 結合附件與自己的文件回答問題

若要結合手冊說明產品照片，請將包含問題與圖片的 `Message` 傳給 `RagEnabledService.GetCompletionAsync(Message)` 或 `StartRunAsync(Message)`。兩種方式都會在傳送至內部 AI 服務的請求中保留非文字附件。檢索依據訊息文字，不會自動為附件本身建立索引或產生嵌入。所選供應商與模型必須支援該附件類型。檢索到的上下文只加入待傳送的請求，不會覆寫原始 `Message`，也不會以檢索內容取代對話歷史中的使用者文字。

如果回答同時需要手冊與即時庫存，請將 RAG 與已註冊的工具搭配使用。在 `GetCompletionAsync` 的工具呼叫過程中，檢索上下文會保留在最初的輸入上，後續每個工具結果都會原樣傳送給模型。對話歷史保留使用者的原始輸入。

<a id="retrieval-modes"></a>

## 選擇文件檢索方式

商品代碼適合關鍵字搜尋，而與文件措辭不同的問題需要語意搜尋。選擇檢索器後只執行所需處理，關鍵字搜尋不再先產生問題嵌入。

```csharp
RagStore store = await RagStore.BuildAsync(rag => rag
    .AddDocument("manual.txt")
    .UseKeywordSearch());

RagProcessedQuery result = await store.QueryAsync("refund policy");
```

`UseKeywordSearch()` 略過問題嵌入。文件寫入仍為既有向量儲存區分塊並產生嵌入；這不是純文字建立索引的API。若延遲初始化在首次提問時註冊文件，仍會呼叫文件嵌入服務。

參見[檢索模式與儲存區支援](rag-hybrid-search.md)及[自訂檢索器](rag-pipeline.md#custom-retriever)。

## 新增文件

支援多種來源類型：

```csharp
.WithRag(rag => rag
    .AddDocument("readme.txt")                    // 本機檔案
    .AddUrl("https://example.com/doc.txt")        // URL
    .AddText("也可以直接加入文字內容。")            // 原始字串
)
```

`AddUrl` 會在讀取文字前驗證並解開支援的 HTTP 壓縮，拒絕不完整、不支援或多層壓縮的回應。請參閱 [URL 解壓縮與取消](rag-pipeline.md#url-documents)。

<a id="document-identity"></a>

### 區分同名檔案

兩家公司可能各自提供一份 `docs/faq.txt`。這兩份文件都應保留在索引中，而重新註冊同一檔案時應繼續使用同一文件識別碼：

```csharp
var store = await RagStore.BuildAsync(rag => rag
    .AddDocuments("company-a/docs")
    .AddDocuments("company-b/docs"));
```

在預設的 RAG 儲存流程中，文件 ID 會在記錄傳送至向量儲存之前產生，隨後取代具有相同 `document_id` 的記錄。本程式庫的 PostgreSQL（pgvector）儲存使用該 ID，不會自行檢查原始檔案路徑。先前，目錄註冊會為 `company-a/docs/faq.txt` 與 `company-b/docs/faq.txt` 都傳送 `faq.txt`，因此第二份文件取代了第一份。此次修正於產生 ID 時保留完整路徑；PostgreSQL 資料庫結構不變。 儲存範例中的 `full_path` 篩選條件使用呼叫端提供的中繼資料，不會自動產生唯一的文件或記錄 ID。

內建的 `PlainTextDocumentLoader` 與 `DirectoryDocumentLoader` 使用經 `Path.GetFullPath` 正規化的檔案絕對路徑作為 `Source` 與自動文件 ID。因此，不同目錄中的檔案具有不同 ID。相對路徑、絕對路徑及含 `./` 的路徑，只要解析為大小寫也相同的絕對路徑，就會使用同一 ID。使用相對路徑時，請保持工作目錄一致。移動檔案、透過符號連結或硬連結存取，以及大小寫不同的路徑，不保證保留同一 ID。

`AddText(..., id: ...)`、明確指定的 `RagDocument.Id` 與自訂載入器的 `Source` 規則保持不變，無須更改呼叫 API。這些內建載入器的 `Source` 現在為絕對路徑，因此預設引用也可能顯示絕對路徑。畫面可使用 `filename` 中繼資料，或預設目錄載入器提供的 `relative_path`。接受設定回呼的目錄註冊多載不會自動加入 `relative_path`。

**移轉現有索引：** 舊的相對路徑 ID 不會自動刪除或移轉。建議將所有文件重新建立索引至新集合，驗證後再切換應用程式。若重用原集合，僅刪除已確認歸屬的舊文件 ID，再重新索引其來源檔案。不要僅按檔名大量刪除，否則可能影響其他目錄中的同名文件。

為使更新與刪除只作用於目標文件，`document_id` 是管線保留鍵。持久化前，每筆紀錄都會使用實際的 `RagDocument.Id`，即使輸入中繼資料指定了其他值。輸入文件與分割器提供的中繼資料字典本身不會被修改；自訂持久化回呼也會收到正規化後的紀錄。應用程式自身的識別碼請使用其他鍵。

這不會自動修復已用錯誤 `document_id` 儲存的紀錄。請用可信原文重建新集合，或確認受影響紀錄的歸屬後，僅清理這些紀錄並重新索引。僅以正確 ID 重新註冊，無法可靠地找到儲存在其他 ID 下的舊紀錄。

使用相對路徑和絕對路徑註冊同一檔案時，應更新同一份文件；不同資料夾中的同名檔案則應保持獨立。`WordDocumentLoader`、`ExcelDocumentLoader`、`PowerPointDocumentLoader` 和 `PdfDocumentLoader` 現在與內建 TXT 載入器一樣，將 `DoclingDocument.Source` 設為正規化的絕對檔案路徑。RAG 據此產生自動文件 ID，明確指定的 ID 仍由呼叫端管理。預設來源引用可能顯示絕對路徑。

[讓同一檔案的識別保持穩定](document-loaders.md#file-source-identity).

<a id="empty-document-updates"></a>

### 將文件更新為空白時一併清除舊搜尋內容

清空已廢止的退款說明並重新索引同一份文件後，舊說明不應繼續出現在回答中。在預設 RAG 儲存流程中，如果分塊正常完成且結果為 0 塊，就會將符合該 `document_id` 的現有記錄替換為空集合。不會請求嵌入，也不會修改其他文件 ID 的資料。這適用於分塊結果為 0 的空文件或僅含空白的文件，也適用於正常傳回 0 塊的自訂分塊器。

對於已設定完成的 `RagPipeline` 執行個體 `pipeline`，請沿用已儲存文件的 ID：

```csharp
await pipeline.IndexDocumentAsync(
    new RagDocument { Id = "refund-policy", Content = "" },
    cancellationToken);
```

之後可以使用相同 ID 重新索引非空內容。載入器沒有傳回任何文件，或某文件從後續檔案清單中消失，並不表示要求刪除：此時並未提供需要替換的文件 ID。

載入、剖析或分塊過程中發生例外，或在呼叫儲存之前偵測到取消時，會保留該文件的現有記錄。載入器和剖析器必須透過例外回報失敗；僅憑正常傳回的 0 塊結果，無法區分失敗和刻意清空。儲存開始後的失敗或取消能否復原取決於儲存實作；PostgreSQL 的替換作業使用交易。批次索引逐份文件處理，不會復原先前已完成的文件。

**自訂儲存：**提供 `onDocumentEmbedded` 時，儲存仍由該回呼負責。結果為 0 塊時不會呼叫回呼，也不會存取預設儲存。應用程式必須使用已知文件 ID 在自己的儲存中明確刪除，或使用 `DeleteDocumentAsync` 刪除管線儲存中的記錄。

## 自訂嵌入供應商

預設情況下，RAG 使用內建的本機嵌入供應商。如需使用專用嵌入模型：

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(apiKey, http, "text-embedding-3-small");

var service = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseEmbedding(embedder)
        .AddDocument("knowledge-base.txt")
    );
```

## 自訂向量儲存

預設使用記憶體儲存。正式環境請接入持久化向量儲存：

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

## 查詢選項

按查詢微調檢索行為：

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};

var response = await service.GetCompletionAsync("你的問題", options: options);
```

## 後續步驟

- [混合搜尋](rag-hybrid-search.md) — 語義搜尋與關鍵字搜尋結合
- [查詢重寫](rag-query-rewriting.md) — 基於對話上下文優化查詢
- [重新排序](rag-reranking.md) — 進一步提升搜尋結果準確度
- [管線自訂](rag-pipeline.md) — 精細控制 RAG 流程
- [智能體 RAG](rag-agentic.md) — AI 自行判斷何時搜尋什麼
- [向量儲存](vectordb-overview.md) — 持久化儲存配置
- [文字分割器](text-splitters.md) — 自訂文件分割方式

Perplexity: [為自有文件索引使用向量 / 僅搜尋，不產生答案](perplexity.md).
