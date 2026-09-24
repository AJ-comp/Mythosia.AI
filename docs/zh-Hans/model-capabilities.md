# 显示所选模型支持的功能选项

> Grok 4.7: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择、推理与处理速度](providers.md#grok-47)

> GPT-6 Sol/Luna: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择与版本要求](providers.md#gpt-6-sol-luna)

[Claude Opus 5.5](providers.md#claude-opus-55) 的 capability 提供从 `Low` 到 `Max` 的级别，包括 `XHigh`；不支持 `None`、`Minimal` 和 `ThinkingToggle`。`MaxOutputTokens` 为 128000。隐藏显示不表示关闭推理。需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。

聊天界面的推理、搜索、工具和图像选项需要匹配当前连接。每个应用单独维护模型名称列表，会重复库的规则，并在提供方、协议或部署变化时产生分歧。能力快照让界面和执行验证使用相同的模型定义。

此 API 属于Mythosia.AI 8.0.0。快照是库已知支持信息的不可变本地描述，不是对账户或服务器的实时探测。类型位于 `Mythosia.AI.Models.Capabilities`。

对等待时间敏感的请求可选择[处理速度](request-building.md#inference-speed)。`WithSpeed` 保持模型和推理级别，`Processing` 显示供应商实际应用的模式。Fast 是受支持组合上的付费选项。

## Before / After

Before：应用自行维护支持列表。下列列表属于应用代码，不是库 API。

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After：检查已配置的请求，再选择支持的选项。只有最后的完成调用会请求模型；查询能力本身不调用 API。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("解释文档内容。");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` 区分 `Supported`、`Unsupported` 和 `Unknown`。自定义部署或服务器选模等信息不足的情况为 `Unknown`，并不等于不支持。示例仅在确定支持时启用额外推理。未知时由应用决定保留默认值或允许尝试请求等策略。

`request.GetCapabilities()` 读取构建器捕获的模型、提供方选项和配置文件。`service.GetCapabilities()` 检查服务默认设置，不消耗下一次调用的待用选项。两者都不发 HTTP、不调用上下文回调或执行验证器、不改变历史，也不启动任务。返回列表也是只读快照。 服务查询也会查看待下一次调用使用的功能设置，并保留它们供实际请求使用。 查询不会序列化函数默认值或托管工具参数，也不会执行运行用配置准备或预留令牌预算。

能力表示连接可以支持什么，不表示已经开启哪些选项。提供方、API 协议和模式与模型名称同样重要。模型标识反映提供方覆盖设置及 Qwen/Ollama ID 转换；没有选择单一模型时可以为 `null`。 示例 Chat UI 根据当前连接及其实际设置（包括已注册的工具）刷新选项，而不仅依赖模型目录。推理模式或工具可用性可能改变采样支持，因此更改这些设置后应重新查询。未知支持状态会与不支持明确区分。

| API | 含义 |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | 通用 `WithReasoning` 的支持情况和级别。 |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | 提供方原生推理设置和预算候选值。 |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | 流式输出、工具、原生异步工具及运行中追加指示。 |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | 托管搜索、保留缓存的推理变更、图像输入及结构化输出。 |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | 采样设置的支持情况和已知输出令牌上限，未知上限为 null。 |
| `StandardSpeed`, `FastSpeed`, `GetSpeedSupport(...)` | Mythosia.AI 8.1.0 / Abstractions 4.1.0: 处理模式的 Supported/Unsupported/Unknown；账户权限另行确认。 |
| `Provider`, `Model` | 提供方及实际发送的模型标识；未知时可以为 null。 |

`ReasoningLevels` 对应通用 `WithReasoning`；`NativeReasoningLevels` 对应提供方自身设置。`ThinkingBudgetPresets` 是适合 UI 的预算候选值，不是全部允许预算或完整数值范围。`AsyncFunctionCalling` 指提供方原生异步工具执行，不是本地函数返回 `Task` 或并行执行。 `StructuredOutput` 包含通过提示与修复实现的通用类型化输出 API，不保证提供方原生约束解码。两种推理级别列表均使用 `ReasoningLevel`，预算预设为整数。

快照不保证账户权限或服务器已就绪，也不会使错误选项组合变得有效。执行时仍保留原有验证和错误。追加指示前检查实际运行的 `run.CanSteer`；模型支持不代表任务仍在进行。

## 单独查询图像生成

图像生成模型独立于聊天模型。特定模型使用 `service.GetImageCapabilities(imageModel)`，省略参数则检查提供方默认图像模型。聊天请求构建器不选择图像生成模型。使用 `Generation`、`Editing` 和 `Mask` 决定显示哪些图像操作。

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`、`Backgrounds`、`OutputFormats`、`SizeKinds`、`Resolutions` 和 `AspectRatios` 是有类型的只读列表。`MaxImages` 和 `MaxInputImages` 为已知上限，未知则为 null。列表中的值不保证可以任意组合；原有尺寸、格式、质量、蒙版及模型验证仍适用。自定义或未知图像模型保持未知，不被判定为不支持。

Google 的 `Resolutions` 和 `AspectRatios` 取决于所选图像模型，也用于生成和编辑验证。请参阅[模型专属表格](providers.md#google-image-options)，其中包含 Flash-Lite 的保守 1K 策略。不支持的显式值在 HTTP 前拒绝；未知自定义模型保持 `Unknown` 和提供方通用选项验证。

自定义 `AIService` 能提供可靠定义时，可重写 protected `ResolveRequestCapabilities()`，默认返回 `AIModelCapabilities.Unknown`。不能因部署未列入目录就将其判定为不支持。`IAIService` 不增加必需成员；查询方法属于 `AIService` 及其请求构建器。

如果自定义提供程序的配置会改变原生模式标志，请重写 `ApplyCapabilityRequestProfile(AIRequestProfile)`，并仅通过 `SetExecutionSetting(...)` 应用解析支持信息所需的标志。默认钩子不执行任何操作。构建器已捕获公共配置覆盖值；查询不会调用 `ApplyRequestProfile` 或 `ApplyProviderSpecificRequestProfile`。此钩子不得执行验证、回调、序列化、预算预留，或修改服务及调用方拥有的状态。临时设置会在查询结束后恢复，重写方法抛出异常时也一样。

[请求设置](request-building.md) · [提供方与图像选项](providers.md) · [Run 控制](execution-api-transition.md)
