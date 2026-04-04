# 提供商特有配置架构

## 原则

| 配置类型 | 位置 | 示例 |
|----------|------|------|
| **通用配置** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **提供商特有** | 各服务类 | ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |

## 当前实现: 服务级别

提供商特有配置作为各服务类的属性进行管理。

```csharp
// 通用配置 → ChatBlock
geminiService.ActivateChat.Temperature = 0.7f;
geminiService.ActivateChat.MaxTokens = 4096;

// 提供商特有配置 → 服务
geminiService.ThinkingBudget = 1024;
```

### 优点
- ChatBlock对提供商完全无关（干净的分离）
- 符合OOP原则（服务管理自己的特有配置）
- 一个服务实例一套特有配置 → 简单结构

### 缺点
- 一个服务内的多个ChatBlock共享相同的特有配置

## 需要迁移到ChatBlock级别的情况

如果未来出现 **每个ChatBlock需要独立维护特有配置的需求**，通过在ChatBlock内添加延迟初始化的配置类进行迁移。

```csharp
// 示例（当前未实现）
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// 使用
chatBlock.Gemini.ThinkingBudget = 1024;
```

### 需要此方式的场景
- 一个服务实例中ChatBlock A和B需要使用不同的ThinkingBudget
- 实际上这种情况非常罕见，因此目前维持服务级别

## 决策日志

- **2026-02-12**: 最初以Option B（ChatBlock级别）实现后，回滚到服务级别。判断特有配置放在服务中更自然。
