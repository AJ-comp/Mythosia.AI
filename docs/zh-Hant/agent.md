# Agent（ReAct 迴圈）

需要同時取得完整答案、用量與來源時，使用 `await run.Result` 傳回的 `AIRunResult`；字串位於 `result.Text`，不必讀取串流。這是Mythosia.AI 8.0.0 的 API 變更；`GetCompletionAsync` 與 `StructuredStreamRun<T>.Result` 的傳回型別保持不變。 [Run 結果與移轉](execution-api-transition.md#run-result).


若要分離每個請求的設定並衍生多個版本，請使用[請求建構器](request-building.md)。先呼叫`CreateRequest(...)`，再串接`With...`。服務屬性與服務上的fluent方法維持原有行為。

> `CreateRequest`範例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和共通請求功能的舊7.1版本不包含建構器；舊套件可繼續使用原有服務多載。

## 為什麼需要 Agent 迴圈？

一般函式呼叫也能將模型單次回應中的**多個函式依序組成批次執行**，並繼續後續工具回合。Agent API 將此機制封裝為具有明確**步驟上限**的目標導向 ReAct 迴圈，把每個批次的結果傳回模型，直到模型產生最終答案：

- 「調研排名前 3 的 AI 公司並比較它們的股價」— 需要多次網路搜尋和股價查詢
- 「查找相關政策，檢查訂單狀態，然後告訴我是否符合退款條件」— 需要按邏輯順序串聯不同工具

`GetCompletionAsync`與`StartRunAsync`已執行共用的模型與工具循環。舊版Agent輔助方法增加的是每次呼叫的輪數限制與專用例外轉換，並不是獨立的規劃器或執行引擎。

## 使用 Run 顯示工具工作的進度

如果需要讓使用者查看多工具工作的進度，或在中途停止工作，可以使用 `StartRunAsync`。追加指示支援和結果取得方式見 [Run 使用指南](execution-api-transition.md)。

```csharp
// 在工作開始前向 service 註冊函式。
await using var run = await service
    .CreateRequest("查找政策、檢查訂單並說明結果。")
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

本機工具可透過`Task<T>` / `ValueTask<T>`回傳物件，並接收程式庫注入的`CancellationToken`。`run.Cancel()`或啟動權杖的取消會傳遞給配合取消的工具，僅停止串流讀取則不會。例外會記錄為失敗；取消時略過排隊呼叫，清理仍會等待已啟動且忽略權杖的工具。請參閱[結果、錯誤與取消](function-calling.md#tool-execution-contract)。

## 舊API相容範例

下文的 `RunAgentAsync` 和 `RunAgentStreamAsync` 是帶 `[Obsolete]` 警告的相容 API。新程式碼可使用上面的 Run；明確指定 `WithMaxRounds(10)` 可保留原來的 10 回合限制。如果依賴舊方法的專用例外處理，請先查看移轉指南。

註冊函式後，使用目標呼叫 `RunAgentAsync`：

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "搜尋網路資訊",
        ("query", "搜尋關鍵字", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "取得目前股價",
        ("ticker", "股票代碼", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "排名前 3 的 AI 公司目前股價是多少？",
    maxSteps: 10
);

Console.WriteLine(result);
```

## maxSteps

`maxSteps` 限制 LLM→函式呼叫的輪數。如果 Agent 在限制內未完成，將擲出 `AgentMaxStepsExceededException`：

```csharp
try
{
    string result = await service.RunAgentAsync("調研並摘要...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    Console.WriteLine($"提前終止：{ex.PartialResponse}");
}
```

## FunctionCallingPolicy

控制 Agent 迴圈每輪的行為：

```csharp
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// 舊版RunAgentAsync使用DefaultPolicy與明確的maxSteps參數。
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "調研並摘要...", maxSteps: 15);
```

預定義策略：

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // 低逾時，少輪數 — 快速任務
var fastResult = await service.RunAgentAsync(
    "調研並摘要...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // 高逾時，多輪數 — 深度調研
var complexResult = await service.RunAgentAsync(
    "調研並摘要...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## 每次呼叫的請求內容

`RunAgentAsync` 和 `RunAgentStreamAsync` 接受可選的 `AIRequestContext`，可在**單次 Agent 執行範圍內**注入動態的 system message prefix/suffix、參考文件，或完全替換目標訊息 — 不會修改服務的 system message 或對話歷史。

```csharp
string result = await service.RunAgentAsync(
    goal: "查找退款政策，並判斷訂單 #1234 是否符合條件。",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"今天的日期是 {DateTime.UtcNow:yyyy-MM-dd}。\n",
        SystemMessageSuffix = "\n始終引用你參考的政策條款。"
    });
```

串流版本接受相同的參數：

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "調研排名前 3 的 AI 公司的股價。",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"使用者時區：{userTz}\n"
    }))
{
    // 處理內容
}
```

`AIRequestContext`透過`AsyncLocal`傳播，但這不代表服務的對話歷史與執行策略可安全地並行修改。獨立的並行工作應使用不同的服務執行個體。

完整的可用屬性清單請參閱 [AIRequestContext](request-contexts.md)（`SystemMessagePrefix`、`SystemMessageSuffix`、`AdditionalMessages`、`RequestMessageOverride`）。

> 自 Mythosia.AI v6.3.0 起可用。

## 運作原理

每一步：

1. LLM 接收目標 + 對話歷史 + 函式定義
2. 若 LLM 呼叫函式 → 執行函式，將結果追加到歷史
3. 若 LLM 回傳文字回應 → 迴圈結束，回傳該回應
4. 若步數達到 `maxSteps` → 擲出 `AgentMaxStepsExceededException`
