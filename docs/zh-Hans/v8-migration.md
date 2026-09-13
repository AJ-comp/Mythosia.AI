# 迁移到 Mythosia.AI 8

当请求需要独立设置、用户需要停止进行中的任务，或应用需要一起保存答案、用量和来源时，可以使用本次版本。它将以下六项架构改进、提供商与模型更新，以及三轮对抗验证中的修复合并为一次主版本升级。

只升级应用使用的包，并重新编译使用方。Mythosia.AI 会自动引入对应的 Abstractions 依赖。下表列出已发布基线与本次应配套使用的兼容版本。

| 包 | 已发布基线 | 发布目标 |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` 在本次从 `1.0.0-preview` 转为稳定版 `1.0.0`。它保留现有的模型、健康状态、服务器版本和指标 API，是不依赖核心 AI 包的独立包。

## 从需求选择变更

| 需求 | 变更与迁移 |
| --- | --- |
| 发送前发现图片选项拼写错误 | 用 `ImageQuality`、`ImageBackground`、`ImageOutputFormat` 和 `ImageSize.Pixels(...)` / `ImageSize.Preset(...)` 替换字符串。不同提供商仍有不同支持范围。 |
| 准备多个请求时互不影响设置 | 从 `CreateRequest(...)` 开始，保存每次 `With...` 返回的新构建器。服务级 setter 仍会修改共享默认值。 |
| 异步工具直接返回应用数据 | 通过特性注册的方法可用 `Task<T>` / `ValueTask<T>` 返回对象并接收注入的 `CancellationToken`。异常记为失败，原有字符串处理器继续支持。 |
| 用户取消时结束等待 | 向补全、Run 和支持的 RAG 入口传递 `cancellationToken`。它停止本地工作和配合取消的工具，不保证远端停止或撤销已完成的外部操作。 |
| 一起保存答案、用量和来源 | `AIRun.Result` 返回 `Task<AIRunResult>`。需要字符串时使用 `(await run.Result).Text`；不读取流也会收集结果。 |
| 显示适合所选模型的控件 | 调用 `request.GetCapabilities()` 或服务、图片能力查询。`Supported`、`Unsupported`、`Unknown` 表示库的本地信息，不是实时账户权限探测。 |

## 更新调用方与自定义提供商

图片选项类型、`AIRun.Result` 和添加取消令牌的签名属于破坏性变更。自定义 `IAIService` 和受影响公开重载的 override 必须添加并传递令牌；提供商的 `GetCompletionAsync(Message)` override 保留签名并传递 `RequestCancellationToken`。自定义 `AIRun` 必须返回 `AIRunResult`。GetCompletionAsync 的字符串返回、类型化补全与 `StructuredStreamRun<T>.Result` 的类型化返回保持不变。接收输入的服务和 RAG StreamAsync 在 v8 中仍公开。RunAgentAsync 和 RunAgentStreamAsync 保留兼容行为与 obsolete 警告。新的进度、取消和支持模型的追加指令流程使用 Run。

## 一个请求，同时获取结果与可选进度

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

OpenAI 使用像素，Google 和 xAI 使用 `ImageSize.Preset(...)` 指定图片大小。只有提供商支持显式格式时才改变 Auto，保存格式以返回的 `GeneratedImage.MediaType` 为准。

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

注册工具可像下面这样返回应用对象。低层 `HandlerWithCancellation` 仍返回 `Task<string>`，不需要新的对象包装器。

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## 提供商更新与验证范围

本次还包括已准备的 Fable 5.1、Gemini 3.7/3.8 Flash、Grok 4.6、DeepSeek Flash、Perplexity Agent 集成以及 OpenAI、Google、xAI 共用的图片生成和编辑。已移除的模型常量与 Perplexity 端点变更可能要求修改调用代码；具体范围见提供商指南和包发布说明。

用 `PerplexityAgentOptions` 配置研究。Profile、Custom Skill 和 Connector 的实证测试已准备，但需要已注册账户资源才能运行。MCP 保持 preview；释放开始后调用抛出 `ObjectDisposedException`，读取循环已停止时新调用抛出 `McpException`，避免无限等待。

三轮对抗验证加强了请求复制、工具返回、取消与清理、令牌计算、响应校验和 MCP 生命周期。第三轮新增 43 个回归案例，全部 2,703 项测试通过；文档覆盖 13 种语言。本轮未调用真实提供商 API，单元测试通过并不代表实测了所有依赖账户资源的集成。

## 详细指南

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
