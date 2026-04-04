# Agent（ReAct 迴圈）

## 為什麼需要 Agent 迴圈？

在一般函式呼叫中，模型每次請求只執行**一次**函式呼叫。但許多實際任務需要模型自主規劃並執行**多個步驟**：

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
service.FunctionCallingPolicy = new FunctionCallingPolicy
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

## 運作原理

每一步：

1. LLM 接收目標 + 對話歷史 + 函式定義
2. 若 LLM 呼叫函式 → 執行函式，將結果追加到歷史
3. 若 LLM 回傳文字回應 → 迴圈結束，回傳該回應
4. 若步數達到 `maxSteps` → 擲出 `AgentMaxStepsExceededException`
