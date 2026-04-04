# Function Calling (FC) 回退: FC ON → FC OFF

## 核心问题

当FC ON的对话历史通过FC OFF（非函数）API路径发送时，所有提供商都会因两个问题产生 `400 Bad Request` 错误:

1. **`Role = Function` 在FC OFF中无效** — Claude、OpenAI、Gemini在Function Calling未启用时都拒绝 `"function"` role。仅接受 `User` 和 `Assistant` role。

2. **`Assistant` 的content为空** — FC ON中AI调用函数时，assistant消息的content为空，实际调用信息在metadata中。FC OFF中，空的assistant content会触发验证错误（尤其是Claude）。

## 解决方案

FC OFF时，在发送前进行如下转换:

| 消息 | 问题 | 处理 |
|------|------|------|
| `Function` role（结果） | `"function"` role被拒绝 | 将role改为 `User`，将函数结果写入content |
| `Assistant`（函数调用） | content为空 | 将调用的函数信息写入content |

在 `GetLatestMessagesWithFunctionFallback()` 中处理，ChatBlock中的原始消息不会被修改。

### 转换示例

```text
[FC ON — 保存在ChatBlock中的历史]
  User: "告诉我首尔的天气"
  Assistant: (空content, metadata: function_call=get_weather)       ← 问题: 空content
  Function: "Seoul: 15°C, Clear"                                    ← 问题: 无效的role
  Assistant: "首尔的天气是15°C，晴天。"

[FC OFF发送时的转换结果]
  User: "告诉我首尔的天气"
  Assistant: "[Called get_weather({"city":"Seoul"})]"                ← 用调用信息填充
  User: "[Function get_weather returned: Seoul: 15°C, Clear]"      ← role改为User
  Assistant: "首尔的天气是15°C，晴天。"
```

## 实现

```csharp
// AIService.cs
internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
{
    foreach (var message in GetLatestMessages())
    {
        // 空content的Assistant（函数调用） → 将调用信息写入content
        if (message.Role == ActorRole.Assistant &&
            message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)
                ?.ToString() == "function_call")
        {
            var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
            var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
            yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
            continue;
        }

        // Function role → 改为User role，保持结果作为content
        if (message.Role == ActorRole.Function)
        {
            var funcName = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "function";
            yield return new Message(ActorRole.User, $"[Function {funcName} returned: {message.Content}]");
            continue;
        }

        yield return message;
    }
}
```

适用于各服务的非函数 `BuildRequestBody()`:

- `AnthropicService.Parsing.cs`
- `OpenAIService.Parsing.cs` (`BuildNewApiBody()`, `BuildLegacyApiBody()`)
- `GoogleAIService.Parsing.cs`

## 相关

与 **MaxTokens自动封顶**（`GetEffectiveMaxTokens()`）配合工作 — 参见 `RELEASE_NOTES.md` v4.0.1。
