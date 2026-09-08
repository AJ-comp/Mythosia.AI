# 提供商特有配置架构

> GPT-6 Astra、`AllowAsync`、`StartRunAsync` 和通用推理与搜索 API 从 `Mythosia.AI` 7.1.0 开始提供，共享类型包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

## 原则

应用可以通过[通用推理与搜索 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/zh-Hans/reasoning-and-search.md)表达任务所需的推理深度和托管检索。`AIRequestFeatures` 为每个逻辑请求复制，由提供商适配器校验并转换；提供商专有默认设置仍保留在服务上。保留缓存的变更状态存储在受跟踪的会话中。`AICitation` 独立于流的读取保留来源。自定义服务通过可选的 `IAIRequestFeatureService` 提供支持，不向 `IAIService` 添加必需成员。

| 配置类型 | 位置 | 示例 |
|----------|------|------|
| **通用配置** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 等 |
| **提供商特有** | 各服务类 | ThinkingBudget (Gemini), ReasoningEffort (GPT) 等 |
| **每个函数的执行许可** | `FunctionDefinition` | `AllowAsync`（默认为 `false`） |

`AllowAsync` 是调用方选择的许可，模型和 API 是否支持则由服务在内部判断。`FunctionBuilder.WithAsync()` 和 `[AiFunction("lookup", "查询数据", AllowAsync = true)]` 也可开启同一许可。GPT-6 Astra 通过 Responses 使用此选项；不支持的模型会省略 API 选项并等待同一个处理器的结果，不会修改已设置的许可。

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

## 何时需要控制执行中的任务

耗时较长的任务需要向用户展示进度，也可能需要在执行过程中调整要求。通过 `StartRunAsync` 返回的 `AIRun` 可以控制该任务，而执行中追加指令的支持情况由提供商决定。模型设置仍在服务上配置，并应在启动前完成；发送追加指令前检查 `run.CanSteer`。使用场景、示例、取消和兼容性说明请参阅 [Run 使用指南](../../../../../docs/zh-Hans/execution-api-transition.md)。
