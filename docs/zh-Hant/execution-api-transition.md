# 使用 Run 控制執行中的 AI 工作

> 這些 API 需要 `Mythosia.AI` 7.1.0 或更新版本，其中包含 `Mythosia.AI.Abstractions` 3.1.0 或更新版本。RAG 範例需要 `Mythosia.AI.Rag` 7.6.0 或更新版本。

## 為什麼需要在執行過程中控制工作？

一份報告可能需要多次文件檢索、API 呼叫和撰寫才能完成。在這個過程中，使用者可能想查看進度、停止工作，或補充「只包含今年的資料」這類要求。應用程式需要把這些操作連結到正在執行的工作。

Run 為工作提供一個可保存在應用程式中的控制代碼。例如，聊天畫面可以顯示陸續收到的文字、提示工具正在執行、把停止按鈕連接到取消操作，並在模型支援時傳送追加指示。這些操作都針對同一次執行。

| 應用程式的需求 | 使用方式 |
| --- | --- |
| 只需要完整答案，不需要控制執行過程 | 繼續使用 `GetCompletionAsync`，包括泛型和 RAG 多載。 |
| 即時顯示文字，並在完成後取得彙整文字 | 透過 `onText` 啟動 Run，然後等待 `run.Result`。 |
| 顯示工具活動，或等待非同步輸出處理 | 讀取 `run.StreamAsync()` 的事件。 |
| 讓使用者停止進行中的工作 | 使用保存的控制代碼呼叫 `run.Cancel()`。 |
| 在工作完成前補充要求 | 檢查 `run.CanSteer`，在支援的模型上呼叫 `run.SteerAsync(...)`。 |

`StartRunAsync` 啟動一個模型工作並傳回 `AIRun`。無論是否觀察輸出，工作都會繼續執行。透過同一個控制代碼可以讀取串流、取得累積結果、取消執行，以及在支援的模型上傳送回合中的追加指示。`GetCompletionAsync`（包括泛型和 RAG 多載）仍是面向完整結果呼叫端的公開便利 API。

## 透過回呼顯示文字

在聊天畫面或主控台中，從第一段文字抵達時就開始顯示，可以讓使用者在長答案產生的過程中逐步閱讀。

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "閱讀文件並撰寫報告。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);

string answer = await run.Result;
```

`onText` 是在工作開始前註冊的選用 `Action<string>`。它依序接收文字，不負責執行工具。只需要結果時可以省略。回呼擲出例外會取消 Run，並使 `Result` 以例外結束。不要向 `onText` 傳入 `async` lambda：它會變成 `async void`，Run 無法等待其工作或捕捉其非同步錯誤。非同步輸出處理應使用事件串流。回呼不會自動切換到 UI 執行緒。

`Result` 是 Run 的所有文字事件依序串接而成的字串，包括工具呼叫之間的中間文字，以及追加指示前已經產生的文字。它不會發起第二次模型要求，也不是重新改寫的答案。如果現有完整結果的傳回語意更合適，可以繼續使用 `GetCompletionAsync`。

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

string answer = await run.Result;
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
string answer = await run.Result;
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

string answer = await run.Result;
```

GPT-6 Astra 透過 Responses WebSocket 連線支援回合中的追加指示。其他提供者及不支援的模型仍可使用一般 Run，但 `CanSteer` 為 `false`，追加指示會明確回報不支援，而不會默默建立一般的下一回合。`CanSteer` 不保證稍後呼叫時 Run 仍處於作用中狀態。

Astra 的 Run 會建立專用 socket。傳入的 `HttpClient` 及其訊息處理常式繼續服務於 HTTP 呼叫，不會攔截此 socket。自訂傳輸可以覆寫 `OpenAIService.ConnectRunWebSocketAsync`。

`SteerAsync` 成功表示伺服器已將輸入接受到佇列中，不表示模型已經套用該指示。繼續透過同一個 Run 觀察後續執行或等待結果。已經傳送的文字和已完成的操作不會復原，也不會僅因提交了追加指示而取消已啟動的工具。程式庫在同一連線上處理接續執行和工具結果關聯。參見 OpenAI 的[回合中追加指示指南](https://developers.openai.com/api/docs/guides/steering)和 [WebSocket 模式](https://developers.openai.com/api/docs/guides/websocket-mode)。佇列中的輸入屬於目前連線，不應假設中斷後仍會保留；不要盲目重送已被接受的指示。

## 使用工具的工作與舊 Agent 方法

「檢查退款政策和這個訂單的狀態」這類問題需要多個資訊來源。註冊文件檢索和訂單查詢工具後，由模型選擇必要的呼叫。回合限制約束模型在必須結束或回報錯誤前，能夠繼續要求工具的範圍。

一般函式呼叫已經支援多回合模型/工具互動。`StartRunAsync` 使用相同的已註冊函式和執行原則，不需要獨立的 Agent 模式、規劃器或 `WithAgentic` 開關。

`RunAgentAsync` 和 `RunAgentStreamAsync` 仍可呼叫，但現在帶有 `[Obsolete]` 警告。移轉期間保留現有簽章、預設 `maxSteps = 10` 和舊有的步數超限錯誤行為。新呼叫請使用：

```csharp
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "查找政策、檢查訂單並說明結果。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

一般 `FunctionCallingPolicy.MaxRounds` 預設值為 20；如果需要保留舊 Agent 上限，請明確指定 10。`WithMaxRounds` 設定單次要求的原則覆寫，不修改 `DefaultPolicy`，應在工作開始前設定。舊 Agent 方法則複製目前預設原則，再套用該次呼叫的 `maxSteps`。新 Run 使用共通執行錯誤契約，不保證轉換為舊的 `AgentMaxStepsExceededException`/`PartialResponse`。如果依賴這項契約，應先移轉例外處理，再取代舊呼叫。

## RAG、MCP 和套件邊界

- `RagEnabledService.StartRunAsync` 支援字串或 `Message` 輸入、`onText`、每次查詢的 `RagQueryOptions`、`streamOptions` 和取消。它在底層 Run 啟動前執行檢索，保留影像/音訊內容及中繼資料，將原始輸入保留在對話歷程中，並透過要求內容傳送增強文字。增強內容綁定原始使用者問題，因此不會用原來的 RAG 提示詞覆蓋後續工具結果或追加指示。向傳回的 Run 追加指示會更新模型的工作，但不會自動重新執行 RAG 檢索。
- `WithAgenticRag` 繼續註冊檢索工具。透過 `StartRunAsync` 使用該工具時，模型可視需要發起後續檢索。`WithMcpServerAsync` 的 MCP 註冊方式也不變。共用 MCP 連線與使用它的 Run 應分別釋放。
- `IAIRunService` 是 `Mythosia.AI.Abstractions` 中的選用能力介面，`IAIService` 沒有新增必要成員。自訂服務必須實作 `IAIRunService` 才能支援從 RAG 啟動 Run；不支援的服務會在 RAG 索引開始前被拒絕。
- RAG 保持對 Abstractions 的相依性，獨立封裝的提供者保留公開的 completion 覆寫方法和可存取的提供者擴充點。此變更不會淘汰向量存放區、文件載入器或伺服器管理 API。

## 相容性與下一個主要版本

| API | 目前狀態 |
| --- | --- |
| `GetCompletionAsync` / `GetCompletionAsync<T>` | 繼續公開並受支援，包括介面、提供者和 RAG 變體。 |
| `StartRunAsync` / `AIRun` | 共通的執行與控制 API。 |
| `RunAgentAsync` / `RunAgentStreamAsync` | 帶有淘汰警告；為相容性保留現有行為。 |
| 接收輸入的 `service.StreamAsync` 和 RAG `StreamAsync` | 本次次要版本更新中仍可呼叫，計畫在下一個主要版本移出公開 API。 |
| `run.StreamAsync()` | 觀察現有工作的輸出，不接收要求輸入。 |
| `BeginStream(...).As<T>()` / `StructuredStreamRun<T>` | 保留現有泛型串流 API；其僅輸出的 `Stream()` 不是舊服務要求方法。 |

下一個主要版本將調整公開的串流入口，同時保留執行實作和必要的提供者掛鉤。即使保留方法主體，把公開方法改為 private 或 protected 仍會破壞呼叫端的原始碼和二進位相容性。訊息鏈、單次呼叫、摘要、查詢改寫和重新排序等輔助功能，不會僅僅因為使用了現有執行方法而被淘汰。
