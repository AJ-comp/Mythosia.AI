# 使用 Run 控制執行中的 AI 工作

> GPT-6 Sol/Luna 是尚未發布的新增功能。參見[模型選擇與版本需求](providers.md#gpt-6-sol-luna)。

只需完整答案和停止按鈕時，將 `cancellationToken` 傳給 `GetCompletionAsync`。進度事件或支援的中途追加指令使用 Run。參閱[取消回答](completions.md#completion-cancellation)。

若要分離每個請求的設定並衍生多個版本，請使用[請求建構器](request-building.md)。先呼叫`CreateRequest(...)`，再串接`With...`。服務屬性與服務上的fluent方法維持原有行為。

> 需要同時取得完整答案、用量與來源時，使用 `await run.Result` 傳回的 `AIRunResult`；字串位於 `result.Text`，不必讀取串流。這是Mythosia.AI 8.0.0 的 API 變更；`GetCompletionAsync` 與 `StructuredStreamRun<T>.Result` 的傳回型別保持不變。 [Run 結果與移轉](#run-result).

> `CreateRequest`範例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和共通請求功能的舊7.1版本不包含建構器；舊套件可繼續使用原有服務多載。

對等待時間敏感的請求可選擇[處理速度](request-building.md#inference-speed)。`WithSpeed` 保持模型和推理層級，`Processing` 顯示供應商實際套用的模式。Fast 是受支援組合上的付費選項。

## 為什麼需要在執行過程中控制工作？

一份報告可能需要多次文件檢索、API 呼叫和撰寫才能完成。在這個過程中，使用者可能想查看進度、停止工作，或補充「只包含今年的資料」這類要求。應用程式需要把這些操作連結到正在執行的工作。

Run 為工作提供一個可保存在應用程式中的控制代碼。例如，聊天畫面可以顯示陸續收到的文字、提示工具正在執行、把停止按鈕連接到取消操作，並在模型支援時傳送追加指示。這些操作都針對同一次執行。

| 應用程式的需求 | 使用方式 |
| --- | --- |
| 接收完整答案，並可選擇取消 | `GetCompletionAsync(..., cancellationToken: token)` |
| 即時顯示文字，並在完成後取得彙整文字 | 透過 `onText` 啟動 Run，然後等待 `run.Result`。 |
| 顯示工具活動，或等待非同步輸出處理 | 讀取 `run.StreamAsync()` 的事件。 |
| 讓使用者停止進行中的工作 | 使用保存的控制代碼呼叫 `run.Cancel()`。 |
| 在工作完成前補充要求 | 檢查 `run.CanSteer`，在支援的模型上呼叫 `run.SteerAsync(...)`。 |

`StartRunAsync` 啟動一個模型工作並傳回 `AIRun`。無論是否觀察輸出，工作都會繼續執行。透過同一個控制代碼可以讀取串流、取得累積結果、取消執行，以及在支援的模型上傳送回合中的追加指示。`GetCompletionAsync`（包括泛型和 RAG 多載）仍是面向完整結果呼叫端的公開便利 API。

<a id="run-result"></a>

## 一次取得答案、用量與來源

即使畫面只顯示完整答案，也可能需要儲存權杖用量與來源。以前 `run.Result` 只傳回字串，用量需自行收集串流事件，來源則從 Run 另外取得。`AIRunResult` 會彙整這些資訊，不必讀取串流。

Before — 舊 Run 契約

```csharp
string answer = await run.Result;
```

After — Mythosia.AI 8.0.0

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("分析這些文件。")
    .StartRunAsync();

AIRunResult result = await run.Result;
string answer = result.Text;
int? totalTokens = result.Usage?.TotalTokens;
var citations = result.Citations;
string? requestedModel = result.RequestedModel;
string? actualModel = result.Model;
var finishReason = result.FinishReason;
```

即時顯示文字時也使用同一結果。回呼、`run.StreamAsync()`、`SteerAsync` 與取消操作的角色不變。

```csharp
using Mythosia.AI.Models.Runs;

await using var run = await service
    .CreateRequest("分析這些文件。")
    .StartRunAsync(onText: text => Console.Write(text));

AIRunResult result = await run.Result;
Console.WriteLine($"\nTokens: {result.Usage?.TotalTokens}");
```

完成結果是快照。`Usage` 與引用物件以複本提供，修改傳回的物件不會影響已儲存結果。釋放 Run 後仍可讀取。串流篩選、停止觀察或觀察緩衝區溢位不會遺失結果資料。

`Usage` 將程式庫各模型回合回報的用量各加總一次，不會重複計算回合事件和最終合計。完全沒有回報時為 `null`，不估算缺少的資訊。不包括獨立輔助摘要要求，因此不是完整帳單或帳戶用量。 如果只有部分回合回報用量，就只加總這些回合，不保證包含所有回合的用量。

即使輸入、輸出明細不完整，也會保留提供者明確回報的 `TotalTokens`；跨輪彙總會累加這些已回報的總數。

Token 計數仍使用 `Int32`。跨輪總和超過 `Int32.MaxValue` 時，串流或 Run 會以 `OverflowException` 失敗，而不會傳回溢位後的錯誤值。清理仍會完成；即使最終彙總失敗，`run.Result` 也不會一直處於等待狀態。

`run.StreamAsync()` 傳回的序列只能列舉一次。重複或並行列舉同一序列會擲回 `InvalidOperationException`；原有讀取與執行仍相互獨立。

`Provider` 為配接器名稱，`RequestedModel` 是啟動時擷取、實際要求中傳送的單一明確模型，包含提供者的模型覆寫設定。若透過預設組合、設定檔或伺服器模型路由選擇，未傳送單一模型欄位，則為 `null`（例如 Perplexity 的 `Models` 清單）。這與實際回應模型 `Model` 相互獨立。 `Model` 是提供者在最後一回合回應中回報的實際模型 ID，缺少時為 `null`，不以要求模型代替。`RoundCount` 為程式庫的 LLM 回合數，不是個別工具呼叫或託管代理的內部步驟數。未回報回合數的自訂提供者得到 `0`。

`FinishReason` 使用 `AIFinishReason`（`Unknown`、`Stop`、`MaxTokens`、`ToolCalls`、`ContentFilter`、`Other`）；`RawFinishReason` 保留提供者原始結束值。缺少時為 `Unknown`/`null`。這些欄位只描述成功結果。回合超限等既有錯誤仍使 `Result` 失敗；使用者取消仍擲回 `OperationCanceledException`，不會轉成成功的取消結果。

`Text` 保留既有語義：依序串接所有輸出文字，包括工具回合的中間內容及追加指示前的文字。結果等待執行與清理完成。`Citations` 為完成後的來源快照，執行中仍可讀取 `run.Citations`。引用位移保留原始內容部分的座標，不是串接文字中的位置。

<a id="run-result-migration"></a>

**主要版本移轉：** `AIRun.Result` 從 `Task<string>` 改為 `Task<AIRunResult>`。只需字串時讀取 `(await run.Result).Text`。自訂 `AIRun` 必須更新覆寫並建立 `AIRunResult`，使用端須重新建置。結果型別位於 `Mythosia.AI.Models.Runs`；`TokenUsage` 與 `AIFinishReason` 位於 `Mythosia.AI.Models.Streaming`。`GetCompletionAsync` 仍傳回 `Task<string>`，`StructuredStreamRun<T>.Result` 仍傳回 `Task<T>`。這些範例需要Mythosia.AI 8.0.0，不適用於最初的 7.1/3.1 Run 套件。

## 透過回呼顯示文字

在聊天畫面或主控台中，從第一段文字抵達時就開始顯示，可以讓使用者在長答案產生的過程中逐步閱讀。

```csharp
await using var run = await service
    .CreateRequest("閱讀文件並撰寫報告。")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

本機工具可透過`Task<T>` / `ValueTask<T>`回傳物件，並接收程式庫注入的`CancellationToken`。`run.Cancel()`或啟動權杖的取消會傳遞給配合取消的工具，僅停止串流讀取則不會。例外會記錄為失敗；取消時略過排隊呼叫，清理仍會等待已啟動且忽略權杖的工具。請參閱[結果、錯誤與取消](function-calling.md#tool-execution-contract)。

`onText` 是在工作開始前註冊的選用 `Action<string>`。它依序接收文字，不負責執行工具。只需要結果時可以省略。回呼擲出例外會取消 Run，並使 `Result` 以例外結束。不要向 `onText` 傳入 `async` lambda：它會變成 `async void`，Run 無法等待其工作或捕捉其非同步錯誤。非同步輸出處理應使用事件串流。回呼不會自動切換到 UI 執行緒。

`(await run.Result).Text` 是 Run 的所有文字事件依序串接而成的字串，包括工具呼叫之間的中間文字，以及追加指示前已經產生的文字。它不會發起第二次模型要求，也不是重新改寫的答案。如果現有完整結果的傳回語意更合適，可以繼續使用 `GetCompletionAsync`。

## 讀取文字、工具和用量事件

工作檢索文件或呼叫業務 API 時，僅有文字可能無法解釋等待的原因。透過帶有型別的事件，可以同時顯示工具活動和答案，並記錄提供者傳回的用量資訊。

```csharp
await using var run = await service.StartRunAsync(
    "檢索文件並解釋結果。",
    cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    switch (item.Type)
    {
        case StreamingContentType.Text:
            Console.Write(item.Content);
            break;
        case StreamingContentType.FunctionCall:
            Console.WriteLine("\n[正在呼叫工具]");
            break;
        case StreamingContentType.FunctionResult:
            Console.WriteLine("\n[已收到工具結果]");
            break;
        case StreamingContentType.Completion when item.Usage != null:
            Console.WriteLine($"\n[總 token 數：{item.Usage.TotalTokens}]");
            break;
    }
}

string answer = (await run.Result).Text;
```

`run.StreamAsync()` 接收選用的觀察取消權杖，不接收提示詞。它觀察的是 `StartRunAsync` 已經啟動的工作。註冊的函式處理常式由程式庫內部執行，不要因收到顯示事件而再次執行同一個工具。文字顯示選項也不會停用 Run 中註冊的工具。

啟動時的回呼和 `run.StreamAsync()` 可以同時觀察同一個 Run，事件串流支援一個讀取者。例如，以 `onText` 顯示文字，並只在串流中處理工具事件，可以避免重複顯示。即使設定了回呼，也最多緩衝 1,024 個未讀事件。只要未超出容量，較晚開始讀取時仍可從頭取得緩衝事件；超出上限後，串流觀察會明確失敗，但回呼、工作執行和 `Result` 會繼續。不要把串流當作無限重播記錄。等待 `Result` 不需要先讀完事件串流。

搜尋網頁或文件的 Run 也可以顯示回答來源。[推理與搜尋指南](reasoning-and-search.md)提供 `WithWebSearch`、`WithFileSearch`、`run.Citations` 和引用事件的範例。

## 非同步處理輸出

需要非同步處理輸出時，應在讀取迴圈中等待操作，而不是使用非同步的 `onText` 回呼：

```csharp
using var writer = new StreamWriter("report.txt");
await using var run = await service.StartRunAsync(
    "撰寫報告。", cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text && item.Content is string text)
        await writer.WriteAsync(text);
}
string answer = (await run.Result).Text;
```

## 取消和釋放資源

- 跳出 `await foreach`，或取消僅傳給 `run.StreamAsync(token)` 的權杖，只會停止觀察，工作繼續執行。
- `run.Cancel()`、傳給 `StartRunAsync` 的權杖，以及釋放仍在執行的 Run，都會取消執行。
- `await using` 確保 `DisposeAsync()` 等待產生工作和提供者清理完成。不支援取消的工具可能需要一段時間才能結束；釋放資源不會復原已完成的操作。
- 同一個服務只允許一個作用中的 `StartRunAsync` 工作，重疊啟動會被拒絕。獨立的並行工作應使用不同服務；Run 作用中時不要混用舊呼叫或修改服務設定。

Run 在背景執行前擷取輸入和待套用的單次要求原則。內建文字、影像、音訊內容與媒體位元組陣列會被複製。自訂 `MessageContent` 子類別保留原執行個體，因此在 Run 結束前不得修改。

擷取的 `FunctionCallingPolicy.TimeoutSeconds` 為 Run 準備階段及所有模型/工具回合設定同一個總期限。逾時會回報 `AIServiceException`；使用者取消會使結果進入取消狀態。清理仍會等待不支援取消的處理常式結束。

## 在工作執行中追加指示

假設使用者開始產生專案計畫後，才發現必須把工期控制在兩週內。追加指示（steering）允許應用程式在模型仍在工作時提交這個新要求，適合較長工作中發現的修正和範圍調整。工作結束後的新問題，應像平常一樣啟動下一次要求。

```csharp
await using var run = await service.StartRunAsync(
    "草擬專案計畫。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

// 在 Run 作用中時，從 UI 的追加指示處理常式呼叫此函式。
async Task SendUpdateAsync(string instruction)
{
    if (!run.CanSteer)
        throw new NotSupportedException("此 Run 不支援追加指示。");

    await run.SteerAsync(instruction, cancellationToken);
}

string answer = (await run.Result).Text;
```

GPT-6 Astra / Sol / Luna 透過 Responses WebSocket 連線支援回合中的追加指示。其他提供者及不支援的模型仍可使用一般 Run，但 `CanSteer` 為 `false`，追加指示會明確回報不支援，而不會默默建立一般的下一回合。`CanSteer` 不保證稍後呼叫時 Run 仍處於作用中狀態。

GPT-6 的 Run 會建立專用 socket。傳入的 `HttpClient` 及其訊息處理常式繼續服務於 HTTP 呼叫，不會攔截此 socket。自訂傳輸可以覆寫 `OpenAIService.ConnectRunWebSocketAsync`。

`SteerAsync` 成功表示伺服器已將輸入接受到佇列中，不表示模型已經套用該指示。繼續透過同一個 Run 觀察後續執行或等待結果。已經傳送的文字和已完成的操作不會復原，也不會僅因提交了追加指示而取消已啟動的工具。程式庫在同一連線上處理接續執行和工具結果關聯。參見 OpenAI 的[回合中追加指示指南](https://developers.openai.com/api/docs/guides/steering)和 [WebSocket 模式](https://developers.openai.com/api/docs/guides/websocket-mode)。佇列中的輸入屬於目前連線，不應假設中斷後仍會保留；不要盲目重送已被接受的指示。

## 使用工具的工作與舊 Agent 方法

「檢查退款政策和這個訂單的狀態」這類問題需要多個資訊來源。註冊文件檢索和訂單查詢工具後，由模型選擇必要的呼叫。回合限制約束模型在必須結束或回報錯誤前，能夠繼續要求工具的範圍。

一般函式呼叫已經支援多回合模型/工具互動。`StartRunAsync` 使用相同的已註冊函式和執行原則，不需要獨立的 Agent 模式、規劃器或 `WithAgentic` 開關。

`RunAgentAsync` 和 `RunAgentStreamAsync` 仍可呼叫，但現在帶有 `[Obsolete]` 警告。移轉期間保留現有簽章、預設 `maxSteps = 10` 和舊有的步數超限錯誤行為。新呼叫請使用：

```csharp
await using var run = await service
    .CreateRequest("查找政策、檢查訂單並說明結果。")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

一般 `FunctionCallingPolicy.MaxRounds` 預設值為 20；如果需要保留舊 Agent 上限，請明確指定 10。`WithMaxRounds` 設定單次要求的原則覆寫，不修改 `DefaultPolicy`，應在工作開始前設定。舊 Agent 方法則複製目前預設原則，再套用該次呼叫的 `maxSteps`。新 Run 使用共通執行錯誤契約，不保證轉換為舊的 `AgentMaxStepsExceededException`/`PartialResponse`。如果依賴這項契約，應先移轉例外處理，再取代舊呼叫。

## RAG、MCP 和套件邊界

- `RagEnabledService.StartRunAsync` 支援字串或 `Message` 輸入、`onText`、每次查詢的 `RagQueryOptions`、`streamOptions` 和取消。它在底層 Run 啟動前執行檢索，保留影像/音訊內容及中繼資料，將原始輸入保留在對話歷程中，並透過要求內容傳送增強文字。增強內容綁定原始使用者問題，因此不會用原來的 RAG 提示詞覆蓋後續工具結果或追加指示。向傳回的 Run 追加指示會更新模型的工作，但不會自動重新執行 RAG 檢索。
- `WithAgenticRag` 繼續註冊檢索工具。透過 `StartRunAsync` 使用該工具時，模型可視需要發起後續檢索。`WithMcpServerAsync` 的 MCP 註冊方式也不變。共用 MCP 連線與使用它的 Run 應分別釋放。
- `IAIRunService` 是 `Mythosia.AI.Abstractions` 中的選用能力介面，`IAIService` 沒有新增必要成員。自訂服務必須實作 `IAIRunService` 才能支援從 RAG 啟動 Run；不支援的服務會在 RAG 索引開始前被拒絕。
- RAG 保持對 Abstractions 的相依性，獨立封裝的提供者保留公開的 completion 覆寫方法和可存取的提供者擴充點。此變更不會淘汰向量存放區、文件載入器或伺服器管理 API。

## 遷移至 Mythosia.AI 8

| API | 目前狀態 |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | 繼續公開並受支援，包括介面、提供者和 RAG 變體。 |
| `StartRunAsync` / `AIRun` | 共通的執行與控制 API。 |
| `RunAgentAsync` / `RunAgentStreamAsync` | 帶有淘汰警告；為相容性保留現有行為。 |
| 接收輸入的 `service.StreamAsync` 和 RAG `StreamAsync` | 接收輸入的服務與 RAG StreamAsync 在 v8 仍公開。新的執行控制使用 StartRunAsync；run.StreamAsync() 只觀察已啟動的 run。 |
| `run.StreamAsync()` | 觀察現有工作的輸出，不接收要求輸入。 |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | 保留現有泛型串流 API；其僅輸出的 `Stream()` 不是舊服務要求方法。 |

[遷移至 Mythosia.AI 8](v8-migration.md).

Perplexity: [讓長時間工作持續執行](perplexity.md).

[用共用支援定義建立模型功能選項](model-capabilities.md).
