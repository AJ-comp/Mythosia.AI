# 選擇推理強度，並取得附有來源的回答

> 這些 API 需要 `Mythosia.AI` 7.1.0 或更新版本，其中包含 `Mythosia.AI.Abstractions` 3.1.0 或更新版本。RAG 範例需要 `Mythosia.AI.Rag` 7.6.0 或更新版本。

## 為什麼需要這些設定？

不同階段需要不同的協助。起草時可能希望快速取得答案，檢查其假設時則值得投入更多推理。關於今天發生的事情，需要最新資訊；關於產品的問題，需要描述該產品的文件。僅僅提高推理強度，並不會讓模型取得這兩類來源。

使用共用的 Fluent API 表達下一個任務的需求。所選供應商會把支援的選項轉換為其原生 API 請求。應用程式可以繼續使用 `GetCompletionAsync` 取得完整答案，也可以用 `StartRunAsync` 顯示進度並控制同一個任務。

| 任務需要 | 設定方式 |
| --- | --- |
| 快速起草，然後仔細審查 | `WithReasoning(...)` |
| 在保留符合條件的對話快取前綴的同時調整推理強度 | `WithReasoning(..., cache: CachePreservation.Required)` |
| 取得網路上的最新資訊 | `WithWebSearch()` |
| 根據供應商已建立索引的文件回答 | `WithFileSearch(store)` |

範例假設服務已初始化，並使用支援相應功能的模型。請匯入 `Mythosia.AI.Extensions` 和 `Mythosia.AI.Models`；串流事件還需要 `Mythosia.AI.Models.Streaming`。

## 從快速起草轉向仔細審查

可以在擬定大綱時減少推理，然後在同一段對話中檢查複雜細節：

```csharp
string outline = await service
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync("擬定移轉計畫的大綱。");

string review = await service
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync("審查該計畫中的故障情境與復原步驟。");
```

`ReasoningLevel` 表達所要求的等級，並非固定的權杖預算，也不保證回答品質。每個模型接受的等級範圍不同。`Auto` 保留供應商已設定的行為或預設行為，不表示自動替換不支援的等級。對於提供權杖預算而非具名等級的模型，原有的供應商專用預算屬性仍然可用。

在長對話中，修改請求最上層的推理設定可能使可重複使用的提示前綴失效。對於支援的模型，可以要求使用供應商的機制，在保留該前綴的同時更改推理強度：

```csharp
string review = await service
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync("重新檢查上一個回答中的假設。");
```

`Required` 約定的是變更的傳送方式。它**不保證**快取命中、免費權杖或更低延遲；供應商的快取資格、保留時間及計費規則仍然適用。不支援的模型會在傳送請求前擲回 `NotSupportedException`。請使用同一段受追蹤的對話、同一模型和同一端點，不要截斷或重新排列包含這些更新的歷程記錄。要改變這些條件，請開始新對話。在要求保留前綴期間，系統會阻止自動壓縮。

已接受的快取保留推理設定會成為對話的有效設定，直到下一次明確變更。一般的 `WithReasoning(level)` 僅套用至對應的邏輯請求，不會悄悄取代這項持續設定。變更發生在**兩次模型回應之間**，不會改變已在產生的回應之推理強度，也不同於 `run.SteerAsync`；後者用來向支援該功能且仍在執行的任務傳送補充指令。

## 回答需要最新資訊的問題

如果答案需要使用模型訓練資料以外的資訊，可以啟用原生網頁搜尋：

```csharp
string answer = await service
    .WithWebSearch()
    .GetCompletionAsync("搜尋最新的版本發行公告，並註明來源。");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

這項託管工具由供應商執行，不必註冊或執行本機函式處理常式。啟用搜尋代表模型可以使用它；模型也可能判斷某個提示不需要搜尋。只有供應商傳回來源時，才能取得對應引用。

OpenAI 和 Anthropic 也接受 `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`。Google 的整合工具未提供這項允許清單，因此帶有網域限制的請求會被拒絕，而不會改為搜尋整個網路。

## 從供應商已建立索引的文件中回答

如果應用程式已經維護供應商託管的文件索引，可以使用該儲存區為回答提供依據，不必自行實作檢索輪次：

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .WithFileSearch(documents)
    .GetCompletionAsync("搜尋我們的政策文件。取消服務的期限是多久？");

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

對於 Google，請將 `new FileSearchStore("Google", "fileSearchStores/your-existing-store")` 與 Google 服務一起使用。儲存區屬於特定的供應商、帳戶和部署環境，不能將 OpenAI 的儲存區 ID 傳給 Google。使用前，請透過供應商 API 或主控台建立儲存區、上傳文件並建立索引。本 API 僅搜尋現有儲存區，不會上傳本機檔案。

託管檔案搜尋與程式庫的 [RAG 管線](rag.md) 滿足不同的部署需求。索引已由供應商管理時，可以選擇託管搜尋；應用程式需要控制載入器、分割、嵌入、檢索或向量儲存時，應選擇 RAG。`RagEnabledService` 也會將 `WithReasoning`、`WithWebSearch` 和 `WithFileSearch` 轉送至最終回答，其內部查詢改寫不會繼承這些選項。RAG 檢索引用仍在 `RagProcessedQuery` 上，與供應商傳回的 `AICitation` 來源分開儲存。

## 顯示進度並保留來源

在 `StartRunAsync` 之前也可以使用相同選項。文字回呼可用於更新介面，Run 則為完整答案保留來源：

```csharp
await using var run = await service
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        "搜尋近期公告，並比較其中的變更。",
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = await run.Result;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

即使不讀取串流、只觀察文字，或輸出觀察緩衝區已滿，`run.Citations` 仍然可用。它包含執行期間從供應商收集的來源引用，包括中間回應。`service.LastCitations`（透過 `IAIService` 使用時為 `GetLastCitations()`）描述最近一次邏輯請求；顯示多個答案時，請保留對應的 Run，或複製其引用快照。

如需在來源事件抵達時處理它們，請只使用一個事件讀取器：

```csharp
await using var run = await service.WithWebSearch().StartRunAsync(
    "搜尋並解釋最新變更。", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\n來源: {source.Title} {source.Url ?? source.FileId}");
}
string answer = await run.Result;
```

供應商未傳回值的引用欄位可以是 null。`ResponseId`、`OutputIndex` 和 `ContentIndex` 識別原始回應及內容片段。`StartIndex` 和 `EndIndex` 保留供應商在局部內容中的位移及索引規則，**不是**串接後的 `run.Result` 中的位置。不要直接把它們當作完整答案的索引來放置引用。

## 檢查供應商支援範圍與請求作用範圍

| 已整合的供應商 | 具名推理等級 | 保留快取的變更 | 網頁搜尋 | 檔案搜尋 |
| --- | --- | --- | --- | --- |
| OpenAI | 支援的推理模型；等級因模型而異 | GPT-6 Astra Standard，單一代理模式 | 支援的 Responses 模型 | 支援的 Responses 模型及現有向量儲存區 |
| Anthropic | 具備原生 effort 控制的模型 | 支援的 Opus 5 / Fable 5.1 / Mythos 5.1，使用供應商測試版功能 | 支援的 Claude 模型 | 沒有原生儲存區轉接器；請使用 RAG |
| Google | Gemini 3 的等級；Gemini 2.5 保留供應商專用預算 | 不支援 | 支援的 Gemini 文字模型 | 支援的 Gemini 文字模型及現有檔案搜尋儲存區 |
| 其他服務 | 原有供應商專用設定仍然可用；這些共用選項需要轉接器 | 本組轉接器不支援 | 沒有共用轉接器 | 沒有共用轉接器 |

模型、等級、傳輸方式及組合檢查均在傳送請求之前進行。尤其要注意，**Google 網頁搜尋與檔案搜尋不能在同一個請求中合併使用**。程式庫不會悄悄移除功能、降低推理等級、忽略網域限制，或切換至外部搜尋服務。在供應商支援時，原生工具可與註冊的用戶端函式共存；Run 的工具輪次仍遵循函式原則與 `WithMaxRounds`。

Fluent 方法保留服務的具體型別，並複製輸入選項。非 null 的元件會合併至下一次邏輯請求中，涵蓋其工具輪次及結構化輸出修復呼叫，隨後被消耗。搜尋不會自動用於後續無關呼叫；需要時，請再次加入 `WithWebSearch` 或 `WithFileSearch`。已啟動的 Run 會保留擷取的設定。與其他可變服務設定一樣，請勿在請求執行時修改同一服務的設定，或在該服務上啟動重疊請求。

自訂 `IAIService` 實作仍然相容。實作可以透過 `IAIRequestFeatureService` 選擇提供這些功能；在沒有這項能力的實作上呼叫相關輔助方法，會明確擲回例外。現有完成、串流及供應商專用設定 API 仍然可用。取消、觀察和補充指令的用法請參閱 [Run 控制](execution-api-transition.md)。

供應商協定：[OpenAI 推理變更](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation)、[OpenAI 工具](https://developers.openai.com/api/docs/guides/tools)、[Anthropic effort 變更](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation)、[Anthropic 網頁搜尋](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool)、[Google Search 資訊依據](https://ai.google.dev/gemini-api/docs/google-search)、[Google File Search](https://ai.google.dev/gemini-api/docs/file-search)。
