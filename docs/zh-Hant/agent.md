# Agent（ReAct 迴圈）

## 為什麼需要 Agent 迴圈？

一般函式呼叫也能將模型單次回應中的**多個函式依序組成批次執行**，並繼續後續工具回合。Agent API 將此機制封裝為具有明確**步驟上限**的目標導向 ReAct 迴圈，把每個批次的結果傳回模型，直到模型產生最終答案：

- 「調研排名前 3 的 AI 公司並比較它們的股價」— 需要多次網路搜尋和股價查詢
- 「查找相關政策，檢查訂單狀態，然後告訴我是否符合退款條件」— 需要按邏輯順序串聯不同工具

**Agent 迴圈**（ReAct 模式：推理 → 行動 → 觀察 → 重複）自動處理這些 — 模型在每一步自行決定下一步該做什麼，直到得出最終答案。

## 基本用法

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
    MaxRounds = 10,
    TimeoutSeconds = 30
};

service.WithMaxRounds(15).WithTimeout(60);
```

預定義策略：

```csharp
service.WithFastPolicy();    // 低逾時，少輪數 — 快速任務
service.WithComplexPolicy(); // 高逾時，多輪數 — 深度調研
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

內容透過 `AsyncLocal` 傳遞，因此同一服務實例上並行執行的多個 agent 呼叫不會互相干擾。

完整的可用屬性清單請參閱 [AIRequestContext](request-contexts.md)（`SystemMessagePrefix`、`SystemMessageSuffix`、`AdditionalMessages`、`RequestMessageOverride`）。

> 自 Mythosia.AI v6.3.0 起可用。

## 運作原理

每一步：

1. LLM 接收目標 + 對話歷史 + 函式定義
2. 若 LLM 呼叫函式 → 執行函式，將結果追加到歷史
3. 若 LLM 回傳文字回應 → 迴圈結束，回傳該回應
4. 若步數達到 `maxSteps` → 擲出 `AgentMaxStepsExceededException`
