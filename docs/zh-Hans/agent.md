# Agent（ReAct 循环）

## 为什么需要 Agent 循环？

常规函数调用也可以把模型一次响应中的**多个函数按有序批次执行**，并继续后续工具轮次。Agent API 将这一机制封装成带有明确**步骤上限**的目标导向 ReAct 循环，把每个批次的结果返回给模型，直到模型生成最终答案：

- "调研排名前 3 的 AI 公司并比较它们的股价" — 需要多次网络搜索和股价查询
- "查找相关政策，检查订单状态，然后告诉我是否符合退款条件" — 需要按逻辑顺序串联不同工具
- 模型可能需要在第一次搜索结果不够理想时**重试或优化**搜索

`GetCompletionAsync`和`StartRunAsync`已经执行共用的模型与工具循环。旧版Agent辅助方法增加的是每次调用的轮数限制和专用异常转换，并不是独立的规划器或执行引擎。

## 使用 Run 显示工具任务的进度

如果需要让用户查看多工具任务的进度，或在中途停止任务，可以使用 `StartRunAsync`。追加指令支持和结果获取方式见 [Run 使用指南](execution-api-transition.md)。

```csharp
// 在任务开始前向 service 注册函数。
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "查找政策、检查订单并说明结果。",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = await run.Result;
```

## 旧API兼容示例

下文的 `RunAgentAsync` 和 `RunAgentStreamAsync` 是带 `[Obsolete]` 警告的兼容 API。新代码可使用上面的 Run；明确指定 `WithMaxRounds(10)` 可保留原来的 10 轮限制。如果依赖旧方法的专用异常处理，请先查看迁移指南。

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
service.DefaultPolicy = new FunctionCallingPolicy
{
    TimeoutSeconds = 30
};

// 旧版RunAgentAsync使用DefaultPolicy和显式maxSteps参数。
service.DefaultPolicy.TimeoutSeconds = 60;
var policyResult = await service.RunAgentAsync(
    "调研并总结...", maxSteps: 15);
```

预定义策略：

```csharp
service.DefaultPolicy = FunctionCallingPolicy.Fast;    // 低超时，少轮数 — 快速任务
var fastResult = await service.RunAgentAsync(
    "调研并总结...", maxSteps: service.DefaultPolicy.MaxRounds);
service.DefaultPolicy = FunctionCallingPolicy.Complex; // 高超时，多轮数 — 深度调研
var complexResult = await service.RunAgentAsync(
    "调研并总结...", maxSteps: service.DefaultPolicy.MaxRounds);
```

## 每次调用的请求上下文

`RunAgentAsync` 和 `RunAgentStreamAsync` 接受可选的 `AIRequestContext`，可在**单次 Agent 运行范围内**注入动态的 system message prefix/suffix、参考文档，或完全替换目标消息 — 不会修改服务的 system message 或对话历史。

```csharp
string result = await service.RunAgentAsync(
    goal: "查找退款政策，并判断订单 #1234 是否符合条件。",
    maxSteps: 10,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"今天的日期是 {DateTime.UtcNow:yyyy-MM-dd}。\n",
        SystemMessageSuffix = "\n始终引用你参考的政策条款。"
    });
```

流式版本接受相同的参数：

```csharp
await foreach (var content in service.RunAgentStreamAsync(
    goal: "调研排名前 3 的 AI 公司的股价。",
    maxSteps: 10,
    options: StreamOptions.WithFunctions,
    context: new AIRequestContext
    {
        SystemMessagePrefix = $"用户时区：{userTz}\n"
    }))
{
    // 处理内容
}
```

`AIRequestContext`通过`AsyncLocal`传播，但这不意味着服务的对话历史和执行策略可以安全地并发修改。独立的并发任务应使用不同的服务实例。

完整的可用属性列表请参阅 [AIRequestContext](request-contexts.md)（`SystemMessagePrefix`、`SystemMessageSuffix`、`AdditionalMessages`、`RequestMessageOverride`）。

> 自 Mythosia.AI v6.3.0 起可用。

## 工作原理

每一步：

1. LLM 接收目标 + 对话历史 + 函数定义
2. 如果 LLM 调用函数 → 执行函数，将结果追加到历史
3. 如果 LLM 返回文本响应 → 循环结束，返回该响应
4. 如果步数达到 `maxSteps` → 抛出 `AgentMaxStepsExceededException`
