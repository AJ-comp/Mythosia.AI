# Perplexity：附來源的回答、搜尋與嵌入

當回答需要根據最新資訊，並讓讀者能夠核對來源時，可以使用 Perplexity。`PerplexityService` 呼叫 Agent API；獨立搜尋與嵌入則用於替自行選擇的回答模型建立檢索能力。

## 先選擇要完成的工作

產生最新答案、取得網頁清單與為自有文件索引建立向量，是不同的工作。請選擇負責該工作的元件，而不是每次檢索都呼叫回答模型。

| 需求 | 元件 |
| --- | --- |
| 附來源的研究答案 | `PerplexityService` |
| 供其他模型或介面使用的網頁清單 | `PerplexitySearchClient` |
| 一般 RAG 中獨立段落的向量 | `PerplexityEmbeddingProvider` |
| 考慮同一文件相鄰區塊關係的向量 | `PerplexityContextualizedEmbeddingProvider` |

安裝 `Mythosia.AI`；嵌入範例還需要 `Mythosia.AI.Rag`。提供 API 金鑰及由應用程式管理的 `HttpClient`。範例中的 `apiKey`、`httpClient`、`cancellationToken` 來自應用程式。

## 使用 Agent 預設回答

預設組合了模型、指示、工具、推理投入與預算。簡單查詢用 `Fast`，日常研究用 `Low`，多步比較用 `Medium`，深入研究用 `High` / `XHigh`。`WideResearch` 用於廣泛調查；預期耗時較長的工作建議使用背景執行。這些值是預設而非模型 ID。

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "比較最新的電池回收方法，並註明來源。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

只需答案時使用 `GetCompletionAsync`，既有串流程式使用服務的 `StreamAsync`，需要觀察或取消執行時使用 `StartRunAsync`。`(await run.Result).Text` 累積輸出的回答文字。不讀取引用事件也會在 `run.Citations` 與 `LastCitations` 保留來源。推理事件只包含提供者公開的內容，並取決於所選模型。

`AIRunResult.RequestedModel` 是啟動時擷取、實際要求中傳送的單一明確模型，包含提供者的模型覆寫設定。若透過預設組合、設定檔或伺服器模型路由選擇，未傳送單一模型欄位，則為 `null`（例如 Perplexity 的 `Models` 清單）。這與實際回應模型 `Model` 相互獨立。

## 控制研究與工具

`WithPerplexityOptions(...)` 設定服務的持續組態，每個邏輯請求都會擷取副本。共用 `WithReasoning(...)` 與 `WithWebSearch(...)` 作用於下一個邏輯請求，包括用戶端工具回合與型別輸出修復。內部 RAG 查詢改寫不會繼承最終答案的搜尋設定。

可用 `UsePreset(...)` 快速選擇預設。預設/組態自行選擇模型，`ModelOverride` 可明確取代。`DisableWebSearch` 只移除配接器預設工具，不保證關閉預設內建搜尋。依模型可用 `Minimal`、`Low`、`Medium`、`High`、`XHigh`、`Max`；`None` 與直接 Sonar 的明確推理設定會被拒絕。內部 `DisableReasoning` 使用較低的可用層級或省略設定，不保證完全關閉推理。

| 設定 | 用途 |
| --- | --- |
| `Preset` / `ModelOverride` | 選擇研究組態，或透過 provider/model ID 明確覆寫模型。 |
| `MaxSteps` | 限制提供者的託管迴圈；0 使用提供者預設值。它與限制用戶端函式後續請求的 `WithMaxRounds` 不同。 |
| `ReasoningEffort` | 調整推理投入。`Auto` 省略覆寫值，支援的層級由實際模型決定。 |
| `DisableWebSearch` / `Tools` | 控制配接器預設網頁工具與明確選擇的託管工具。 |
| `Models` | 按優先順序指定 1～5 個備用模型，覆寫單一模型設定。所有候選模型都必須相容於請求的功能。 |
| `Profile` | 使用伺服器儲存的組態，可固定版本；不能與 `Preset` 同時使用。 |
| `ServiceTier` | 請求預設、flex 或 priority 處理。提供者可能忽略模型不支援的服務層級。 |
| `Skills` | 提供內建、行內或已上傳的自訂技能。自訂資源屬於 Perplexity 帳戶。 |
| `LanguagePreference` / `PromptCacheKey` | 設定回答語言或快取路由提示；提示不保證快取命中。 |
| `PreviousResponseId` / `Store` | 接續已完成的提供者回應，或控制可查詢性。接續時使用 `StatelessMode`，只傳送新一回合輸入。`Store = false` 不會關閉提供者的持久儲存。 |

`PerplexityHostedTool` 接受支援的 `Type` 與文件定義的 JSON 相容 `Parameters`：`web_search`、`fetch_url`、`finance_search`、`people_search`、`sandbox`、`mcp`。MCP 伺服器與託管連接器透過提供者執行，認證資訊、權限與帳戶資源須符合目標連線。應用程式函式仍透過 `Functions` / 函式建構器註冊。託管步驟與本機處理常式由不同主體執行。

`PerplexityHostedTools.WebSearch`、`FetchUrl`、`Sandbox`、`FinanceSearch`、`PeopleSearch`、`Mcp`、`Connector` 可建立工具組態。MCP 不會暫停等候核准，需要時用 `allowedTools` 限制。Connector 是提供者預覽功能，參照已連接的整合。

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "閱讀專案文件並比較相關能力。");
```

工具、推理、影像與結構描述的相容性由所選模型決定。共用 `WithFileSearch` 不是 Perplexity 向量儲存配接器。沙箱產生檔案、上傳附件與遠端 MCP 資料是不同資源，不會自動成為共用檔案搜尋儲存。

## 來源、影像與結構化答案

應用程式需要 JSON 欄位時，使用型別 completion 或型別串流。配接器傳送原生結構描述，並保留既有修復流程。原生回應項目與工具識別碼會保留供後續請求使用，請勿任意刪除或重排通訊協定歷程。影像透過 `Message` 與 `ImageContent` 輸入 JPEG/PNG/WebP/GIF 位元組或 HTTPS URL，實際支援取決於模型；這不是影像產生請求。

原始回應記錄保存在歷史中繼資料，但後續請求只重送允許的 `message`、`function_call` 與 `function_call_output` 輸入項目；若需延續供應商端完整的託管執行狀態，請使用 `PreviousResponseId`。

引用可能指向網頁結果或其他提供者來源。位移屬於單一提供者回應的內容部分，而不是 Run 累積結果。保留 URL 與標題供顯示和核對；傳回來源本身並不證明每項產生的主張都正確。

## 讓長時間工作持續執行

研究需要在用戶端暫時斷線後繼續，或稍後憑 ID 查詢時，使用提供者背景執行。本機 `AIRun` 控制目前用戶端執行，背景回應有獨立的伺服器生命週期。停止讀取串流只會停止觀察。要停止遠端工作，必須明確取消提供者工作。

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "比較最新的電池回收方法，並註明來源。", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync` 擷取輸入但不新增歷程，並拒絕啟用的本機函式或 `Store = false`。`GetResponseAsync` 查詢一次；`WaitForCompletionAsync` 輪詢到終止狀態。保存 `Id` 與 `LastSequenceNumber`，用 `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)` 重連。`CancelAsync` 取消遠端工作；取消查詢/讀取權杖只停止該用戶端操作。`LastResponse` 包含文字、狀態、用量、引用與 `OutputJson`，使用答案前請檢查終止狀態。

用控制代碼的 `ListFilesAsync` 與 `DownloadFileAsync(fileId)` 讀取沙箱輸出。服務也提供 `GetAgentResponseAsync`、`GetResponseFilesAsync`、`GetResponseFileContentAsync`。它們讀取回應產物，不建立或搜尋向量儲存。

內建 Office 技能請使用本指南的背景路徑：先呼叫 `StartBackgroundAsync`，再使用 `WaitForCompletionAsync` / `GetResponseAsync` 與檔案方法。此類回應的內部工具記錄可能無法與一般本機函式呼叫區分。

背景提交、查詢、取消與串流重連不會啟用執行中的 `SteerAsync` 或原生非同步用戶端工具。重連繼續觀察既有回應，不會重新提交原工作。請保存提供者回應 ID 與游標。

## 僅搜尋，不產生答案

需要自行排序、在介面顯示或交給其他 LLM 的網頁時，使用 `PerplexitySearchClient`。它不呼叫回答模型，也不修改 `PerplexityService` 的對話歷程。

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "電池回收方法",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync` 接受單一或多個查詢，可設定網頁/人物搜尋、國家、網域、語言、發布/更新日期範圍與新近程度。`ContentSize` 與明確的 `MaxTokens` / `MaxTokensPerPage` 擇一。結果包含順序、標題、URL、摘要與提供者日期；順序是傳回位置，而非相關性分數。

`ContentSize` 僅適用於 Web 搜尋。People 搜尋必須省略；用戶端會在傳送前拒絕此組合。

## 為自有文件索引使用向量

標準嵌入獨立處理各段落並實作 `IEmbeddingProvider`，因此可接入既有建構器。情境嵌入保留相鄰區塊順序與文件分組，並使用獨立 API，避免將無關文件攤平成單一輸入。

| 模型常數 | 提供者 ID | 預設維度 |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("購買後 30 天內可以退貨。", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("購買的商品可以在多久內退貨？");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "購買後 30 天內可以退貨。", "申請退款時請保留收據。" },
    new[] { "標準配送需要三天。", "工作日可使用快速配送。" }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "購買的商品可以在多久內退貨？", cancellationToken);
```

索引文件與查詢應使用相同模型、維度與編碼。`GetQueryEmbeddingAsync` 將單一查詢當成獨立文件，傳給同一情境模型。情境結果保留文件與區塊順序，不會自動接入接收平面輸入的 RAG 建構器。

float API 解碼提供者的 base64 signed-int8 向量，並為向量相似度正規化。明確的 binary API 傳回壓縮位元並使用漢明距離，不會把二進位靜默當成浮點座標。0.6B 的完整維度為 1024，4B 為 2560；選用降維遵守提供者限制。批次、文件長度、總權杖與帳戶速率限制仍適用。

二進位方法包括 `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync`，以及情境的 `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`。`PerplexityBinaryEmbedding` 提供 `Dimensions`、傳回副本的 `ToArray()` 與越小越相似的 `HammingDistance`。二進位維度須為8的倍數。標準批次最多512個文字，情境批次最多512份文件、16,000個區塊。每文字/文件32K及合計120K權杖由提供者檢查。

## 遷移既有 Sonar 程式碼

此版本在提供者宣布的 2026 年 9 月 27 日端點停止服務前，主動移除舊 Sonar 配接器。`PerplexityService` 改為呼叫 `/v1/agent`，`AIModels.Perplexity.Sonar` 改為 `perplexity/sonar`。舊 Sonar 專用搜尋方法與回應型別已移除。請使用共用 completion/Run/引用、Agent 預設，以及獨立檢索用的 `PerplexitySearchClient`。

建議映射為 Sonar → `Fast`、Sonar Pro → `Low`、Sonar Reasoning Pro → `Medium`、Sonar Deep Research → `High`。這是工作流程遷移，不保證文字、費用或模型行為相同。動態預設可能隨提供者更新而改變，需要固定時請指定模型或含版本的組態。

此配接器不支援原生 steering、原生非同步用戶端工具與 `CachePreservation.Required`。Router/Gateway API 不在本次整合範圍。可用組合取決於提供者、模型與帳戶，本指南不宣稱所有組合都已通過付費實際呼叫測試。

Profile、custom skill 和 connector 需要帳戶中事先註冊的資源。請求結構已通過單元測試，但尚未驗證使用這些資源的實際 API 成功呼叫。

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
