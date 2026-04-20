# 核心概念

本页收录了在其余文档中被反复引用的基础概念。随着时间推移，这里会逐步增加更多概念。

## 什么是 round？

> [!NOTE]
> **Round** 是你的应用与模型之间一次完整的往返调用——app 发送一个 prompt，模型回复，这次交互就是一个 round。一条普通的聊天消息是 1 个 round。function calling 与 agent 可以为一条用户消息串联多个 round。

### 最简单的情况：1 个 round

对于一条普通聊天消息，整段对话发生在一个 round 内。

```
app  →  "2 加 2 等于多少？"     →  模型
app  ←  "等于 4。"               ←  模型
```

`RoundUsage` 会在这一次调用的 token 被确定时触发一次。`Completion.Usage` 在 stream 结束时触发，由于只有一个 round，它的总数与 RoundUsage 相同。

### 多个 round：function calling

当模型无法独立作答时，round 就会累积。比如用户问 *「现在北京的天气怎么样？」* — 模型无法访问实时天气，因此它必须调用一个工具。

**Round 1 — 模型决定调用工具**

你的 app 把用户消息和已注册工具列表（例如 `GetWeather`）一起发给模型。此时模型看到的对话是：

```
system：你是一个天气 assistant，可以调用 GetWeather(city)。
user：  现在北京的天气怎么样？
```

模型不会直接写最终答案，而是返回一个**工具调用请求**：

```
tool_call: GetWeather(city="Beijing")
```

模型的这一轮结束，round 1 也随之结束。此时 `RoundUsage` 触发，包含 round 1 所消耗的 token。**这时还没有最终用户答案。**

**Round 之间 — 你的 app 执行函数**

这一步**不是**对 LLM 的调用。Mythosia.AI 运行时会调用你注册的 `GetWeather` 实现，并获得 `「15°C，多云」` 的结果。不消耗任何 token。

**Round 2 — 模型写出最终答案**

你的 app 把工具结果追加进对话，然后**第二次**调用模型。模型现在看到：

```
system：     你是一个天气 assistant，可以调用 GetWeather(city)。
user：       现在北京的天气怎么样？
assistant：  [调用了 GetWeather(city="Beijing")]
tool_result：15°C，多云
```

获得所需信息后，模型开始写文本：

```
北京目前 15°C，多云。
```

Round 2 结束。`RoundUsage` 第二次触发 —— 这次只包含 round 2 的 token（由于对话变长了，input 一般会比 round 1 多）。stream 关闭后，`Completion.Usage` 触发一次，其值为 **round 1 + round 2 的总和**。

### 一览表

| 步骤 | 是否调用 LLM？ | 发生了什么 | 事件 |
|---|---|---|---|
| Round 1 | ✅ | 模型决定调用 `GetWeather` | `RoundUsage`（`RoundIndex=1`） |
| Round 之间 | ❌ | App 执行函数，得到 `「15°C，多云」` | `FunctionCall`、`FunctionResult` |
| Round 2 | ✅ | 模型看到结果并写出最终答案 | `RoundUsage`（`RoundIndex=2`、`IsFinalRound=true`） |
| Stream 结束 | — | — | `Completion`（Usage = round 1 + round 2） |

### 工具越多，round 越多

如果模型需要连续调用多个工具，round 会继续累加。对于 *「比较北京和上海的天气」*：

1. **Round 1** — 模型调用 `GetWeather("Beijing")`
2. App 执行 → `「15°C，多云」`
3. **Round 2** — 模型看到结果后又调用 `GetWeather("Shanghai")`
4. App 执行 → `「18°C，晴朗」`
5. **Round 3** — 模型把两个结果整合成最终答案

共 3 个 round，`Completion.Usage` 是三者之和。UI 中的上下文计量器应使用最后一个 round 的 `RoundUsage.TotalTokens`——本例中即 round 3 的值。
