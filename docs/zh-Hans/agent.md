# Agent（ReAct 循环）

## 为什么需要 Agent 循环？

在常规函数调用中，模型每次请求只执行**一次**函数调用，你执行后对话继续。但许多实际任务需要模型自主规划并执行**多个步骤**：

- "调研排名前 3 的 AI 公司并比较它们的股价" — 需要多次网络搜索和股价查询
- "查找相关政策，检查订单状态，然后告诉我是否符合退款条件" — 需要按逻辑顺序串联不同工具
- 模型可能需要在第一次搜索结果不够理想时**重试或优化**搜索

手动编写这种编排逻辑既繁琐又容易出错。**Agent 循环**（ReAct 模式：推理 → 行动 → 观察 → 重复）自动处理这些 — 模型在每一步自行决定下一步该做什么，直到得出最终答案。

## 基本用法

注册函数后，使用目标调用 `RunAgentAsync`：

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "search_web",
        "搜索网络信息",
        ("query", "搜索关键词", required: true),
        query => WebSearch(query)
    )
    .WithFunction(
        "get_stock_price",
        "获取当前股价",
        ("ticker", "股票代码", required: true),
        ticker => FetchPrice(ticker)
    );

string result = await service.RunAgentAsync(
    goal: "排名前 3 的 AI 公司当前股价是多少？",
    maxSteps: 10
);

Console.WriteLine(result);
```

模型会根据需要调用函数、观察结果并决定下一步 — 直到生成最终文本响应。

## maxSteps

`maxSteps` 限制 LLM→函数调用的轮数。如果 Agent 在限制内未完成，将抛出 `AgentMaxStepsExceededException`：

```csharp
try
{
    string result = await service.RunAgentAsync("调研并总结...", maxSteps: 5);
}
catch (AgentMaxStepsExceededException ex)
{
    // ex.PartialResponse 包含模型到目前为止生成的内容
    Console.WriteLine($"提前终止：{ex.PartialResponse}");
}
```

## FunctionCallingPolicy

控制 Agent 循环每轮的行为：

```csharp
service.FunctionCallingPolicy = new FunctionCallingPolicy
{
    MaxRounds = 10,
    TimeoutSeconds = 30
};

// 或使用扩展方法：
service.WithMaxRounds(15).WithTimeout(60);
```

预定义策略：

```csharp
service.WithFastPolicy();    // 低超时，少轮数 — 快速任务
service.WithComplexPolicy(); // 高超时，多轮数 — 深度调研
```

## 工作原理

每一步：

1. LLM 接收目标 + 对话历史 + 函数定义
2. 如果 LLM 调用函数 → 执行函数，将结果追加到历史
3. 如果 LLM 返回文本响应 → 循环结束，返回该响应
4. 如果步数达到 `maxSteps` → 抛出 `AgentMaxStepsExceededException`
