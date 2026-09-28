<div align="center">

🌐 [English](../../README.md) · [한국어](../ko/README.md) · [日本語](../ja/README.md) · [Français](../fr/README.md) · [Deutsch](../de/README.md) · [Русский](../ru/README.md) · [Українська](../uk/README.md) · [简体中文](README.md) · [繁體中文](../zh-Hant/README.md) · [Tiếng Việt](../vi/README.md) · [ภาษาไทย](../th/README.md) · [Português](../pt/README.md) · [Español](../es/README.md)

<br>

[![OPEN SOURCE](https://img.shields.io/badge/OPEN%20SOURCE%20·%20.NET%20·%20NUGET-111827?style=flat-square&labelColor=111827&color=111827)](https://github.com/AJ-comp/Mythosia.AI)

<img width="694" height="181" alt="title_60" src="https://github.com/user-attachments/assets/57fd8c63-5b9b-46f6-be30-274354808c0d" />

### 用于构建智能应用的模块化 .NET AI 库

**切换提供商、接入 RAG、加载文档 — 一套统一的 API 全部搞定。**

<br>

[![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg?style=for-the-badge&logo=nuget&label=NuGet&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Downloads](https://img.shields.io/nuget/dt/Mythosia.AI.svg?style=for-the-badge&logo=nuget&color=512BD4)](https://www.nuget.org/packages/Mythosia.AI)
[![Docs](https://img.shields.io/badge/Docs-GitHub%20Pages-0ea5e9?style=for-the-badge&logo=readthedocs&logoColor=white)](https://aj-comp.github.io/Mythosia.AI/)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-6d28d9?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)

<br>

**[📖 快速入门](https://aj-comp.github.io/Mythosia.AI/)** &nbsp;·&nbsp; **[API 参考](https://aj-comp.github.io/Mythosia.AI/api/)** &nbsp;·&nbsp; **[GitHub ↗](https://github.com/AJ-comp/Mythosia.AI)**

<br>

</div>

## 演示 / 测试平台 (Chat UI)

编写集成代码前，先在 Playground 中试用模型和文档搜索。

观看当前 Playground 实际界面的录屏演示，了解如何浏览模型、切换语言，以及查看文档和 RAG 流水线设置。视频包含英文字幕。

https://github.com/user-attachments/assets/4cf90210-b000-41be-8317-a467e93e7504

### 运行示例

在本地运行 **`Mythosia.AI.Samples.ChatUi`**：

```bash
# 在仓库根目录下
dotnet run --project apps/Mythosia.AI.Samples.ChatUi
```

<details>
<summary>Playground 操作与语言</summary>

在接入应用前，可在左侧按模型名称或提供商搜索并调整请求设置，在中央对话，并在右侧 Inspector 中查看处理信息。Stop 可停止等待当前响应；速度选项仅对支持的模型和服务端点启用，Fast 可能产生额外费用。在较窄的屏幕上，Models 和 Inspector 会以抽屉面板打开；本地运行、文档导入和检索流程设置请参阅 [Chat UI 指南](https://github.com/AJ-comp/Mythosia.AI/blob/main/apps/Mythosia.AI.Samples.ChatUi/README.md)。

流水线面板支持 Voyage Context 4、Gemini Embedding 2 和 Perplexity 上下文嵌入的密钥、维度及超时设置。文档页面可查看分块和向量数量并取消索引。保存设置、数据库重连和代码示例均使用所选配置；更换嵌入模型或维度后需重新索引。

使用顶部的语言选择器可在13种界面语言之间切换，并保留输入内容和设置。模型列表默认显示全部7个提供商的折叠分组，可展开分组或搜索模型。

</details>

## 为什么选择 Mythosia.AI？

- **通过统一 API 切换 AI 提供商**，使用聊天、流式输出、工具调用和结构化响应。
- 结合加载器、嵌入、检索和重排序，**根据自己的文档构建答案**。
- **独立保存请求设置**，通过通用 Run API 控制正在进行的任务。
- 从核心库到可选的 RAG 和向量存储集成，**只选择需要的包**。

## 需要安装哪些包？

```
dotnet add package Mythosia.AI                    # 从这里开始（这就够了）
dotnet add package Mythosia.AI.Rag                # 可选：需要 RAG 时安装
dotnet add package Mythosia.VectorDb.Postgres     # 可选：需要生产级向量存储时安装
```

| 步骤 | 包 | 适用场景 |
| :--: | --- | --- |
| **1** | **`Mythosia.AI`** | **从这里开始** — 补全、流式输出、函数调用、结构化输出 (OpenAI / Claude / Gemini / Grok / DeepSeek / Perplexity) |
| **2** | **`Mythosia.AI.Rag`** | 需要 RAG 时 — 文本分割、嵌入、混合搜索、重排序、InMemory 向量存储、文档加载器 (Word / Excel / PowerPoint / PDF) |
| **3** | **`Mythosia.VectorDb.Postgres`** / **`Qdrant`** / **`Pinecone`** | 需要生产级向量存储替代 InMemory 时 — 选择其一 |

准备不同设置时无需改变其他请求：`CreateRequest(...).WithTemperature(...).GetCompletionAsync()` 使用独立且可复用的请求构建器。[请求设置指南](request-building.md)提供前后对比示例、Run、配置档和共享会话限制。

对等待时间敏感的请求可选择[处理速度](request-building.md#inference-speed)。`WithSpeed` 保持模型和推理级别，`Processing` 显示供应商实际应用的模式。Fast 是受支持组合上的付费选项。

## 快速开始

### 基础 AI 补全

```csharp
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
var response = await service.GetCompletionAsync("Hello!");
```

### 流式输出

```csharp
await using var run = await service.StartRunAsync(
    "Tell me a story",
    onText: text => Console.Write(text));
string answer = (await run.Result).Text;
```

### 推理流式输出

OpenAI、Claude、Gemini、Grok 和 DeepSeek Flash 通过相同流式模式返回提供方推理。先在服务或请求中开启推理，再用 `StreamOptions.WithReasoning()` 观察：

```csharp
await using var run = await service.StartRunAsync(
    message, options: new StreamOptions().WithReasoning());
await foreach (var content in run.StreamAsync())
{
    if (content.Type == StreamingContentType.Reasoning)
        Console.Write($"[Think] {content.Content}");
    else if (content.Type == StreamingContentType.Text)
        Console.Write(content.Content);
}
```

### 函数调用

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient)
    .WithFunction(
        "get_weather",
        "Gets the current weather for a location",
        ("location", "The city and country", required: true),
        (string location) => $"The weather in {location} is sunny, 22C"
    );

var response = await service.GetCompletionAsync("What's the weather in Seoul?");
```

一次模型响应返回的调用默认按顺序执行。如果注册的函数相互独立，可以选择限制并发数量的并行处理器执行：

```csharp
using Mythosia.AI.Models.Functions;

service.DefaultPolicy = new FunctionCallingPolicy
{
    ExecutionMode = FunctionExecutionMode.Parallel,
    MaxConcurrency = 3
};
```

普通批次的结果会按提供商原始调用顺序返回给模型。取消操作会跳过尚未开始的调用，并提供对应的取消结果。已开始的工具在支持时接收取消令牌，并等待完成，以保持调用与结果历史一一对应。`FunctionCallingPolicy.TimeoutSeconds` 覆盖整个流式轮次循环，包括响应头和 SSE 正文，不会在工具轮次之间重置。策略超时抛出 `AIServiceException`；调用方取消仍表现为与其令牌关联的 `OperationCanceledException`。

慢速查询进行期间，模型仍可完成有用的独立工作，例如在天气预报返回前介绍一般旅行用品。设置 `FunctionDefinition.AllowAsync = true` 或使用 `FunctionBuilder.WithAsync()`，可让支持的模型在函数执行时继续工作。默认值为 `false`。GPT-6 Astra / Sol / Luna 通过 Responses API 使用此选项；不支持的模型不会发送不受支持的 API 选项，而是等待同一个处理器的结果。此功能与 C# `async` 处理器和并行处理器调度相互独立。示例及请求生命周期行为见[异步工具调用](function-calling.md#async-tool-calling)。

### 图像生成与编辑

通过 OpenAI、Google 和 xAI 共用的可选能力，可以根据文本创建图像草稿或修改现有图像。图像模型独立于所选聊天模型：

```csharp
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Pixels(1024, 1024),
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

生成和编辑方法见[提供商指南](providers.md#image-generation)，重大 API 变更见[类型化图像选项与迁移](providers.md#image-options-migration)。xAI 使用 `ImageOutputFormat.Auto`；请根据 `GeneratedImage.MediaType` 选择输出扩展名。

Google 图像预设因模型而异：Flash 支持 512/1K/2K/4K，Flash-Lite 目前支持 1K，Pro 支持 1K/2K/4K。Flash/Lite 提供 14 种宽高比，Pro 提供 10 种标准宽高比，全部接受 `Auto`。展示选项前请检查 `GetImageCapabilities(model)`；生成和编辑时，显式指定不支持的尺寸或宽高比都会在 HTTP 请求前失败。参阅[模型支持表与 Flash-Lite 文档差异](providers.md#google-image-options)。

### 结构化输出（基础）

```csharp
// 将 LLM 响应直接反序列化为 C# POCO，支持自动修复
var result = await service.GetCompletionAsync<WeatherResponse>(
    "What's the weather in Seoul?");
```

### 结构化输出（列表）

```csharp
// 集合类型直接可用 — 无需包装 DTO
var items = await service.GetCompletionAsync<List<ItemDto>>(
    "Extract all entities from this document...");
```

### 结构化输出（流式）

```csharp
// 实时流式接收文本片段 + 获取最终反序列化对象
var run = service.BeginStream(prompt).As<MyDto>();

await foreach (var chunk in run.Stream())
    Console.Write(chunk);          // 实时 UI

MyDto dto = await run.Result;      // 已解析并自动修复
```

### 对话摘要策略

```csharp
// 当对话变长时自动摘要旧消息
service.ConversationPolicy = SummaryConversationPolicy.ByMessage(
    triggerCount: 20,
    keepRecentCount: 5
);

// 基于 Token 的触发
service.ConversationPolicy = SummaryConversationPolicy.ByToken(
    triggerTokens: 3000,
    keepRecentTokens: 1000
);

// 正常使用即可 — 摘要会自动进行
await service.GetCompletionAsync("Continue our conversation...");

// 流式输出时，在 StreamAsync() 前显式应用摘要策略
await service.ApplySummaryPolicyIfNeededAsync();
await foreach (var chunk in service.StreamAsync("Continue..."))
    Console.Write(chunk.Content);

// 跨会话保存/恢复摘要
string saved = service.ConversationPolicy.CurrentSummary;
policy.LoadSummary(saved);
```

### RAG（检索增强生成）

选择关键词、语义或混合检索，无需强制每次搜索生成问题嵌入。`UseKeywordSearch()` 跳过问题嵌入；`UseRetriever(...)` 连接外部索引；`UseHybridSearch(HybridSearchOptions)` 传递显式权重和候选设置。文档摄取仍会创建向量。参阅[检索模式与存储支持](rag-hybrid-search.md)。

```bash
dotnet add package Mythosia.AI.Rag
```

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .AddDocument("policy.txt")
    );

var response = await service.GetCompletionAsync("What is the refund policy?");
```

要让智能体控制检索，请通过 `WithAgenticRag(...)` 注册存储，并通过 `service.WithMaxRounds(10).StartRunAsync(...)` 启动任务。可等待同一任务的 `run.Result`，或观察 `run.StreamAsync()`。完整示例见 [Mythosia.AI.Rag README](../../src/rag/Mythosia.AI.Rag/README.md)。

#### 保留文档上下文和查询用途

分块的含义可能依赖相邻段落，搜索问题与索引文档的用途也不同。RAG 8.2.0 为从 TXT、Markdown 和 PDF 提取的文本提供 Voyage 上下文嵌入和 Gemini Embedding 2。

```csharp
using Mythosia.AI.Rag;

var store = await RagStore.BuildAsync(rag => rag
    .UseVoyageEmbedding(voyageApiKey, httpClient)
    .AddDocument("policy.pdf"));
var result = await store.QueryAsync("What is the refund period?");
```

[配置和提供者约定](rag-embedding.md#retrieval-aware-embeddings): `UseVoyageEmbedding(...)`, `UseGeminiEmbedding(...)`, `IRetrievalEmbeddingProvider` / `EmbeddingDocument` (RAG 8.2.0, RAG Abstractions 6.4.0).

## 支持的提供商

> Grok 4.7: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择、推理与处理速度](providers.md#grok-47)

> GPT-6 Sol/Luna: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型选择与版本要求](providers.md#gpt-6-sol-luna)

> Claude Opus 5.5: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [配置与迁移](providers.md#claude-opus-55)

| 提供商 | 包 | 模型 |
| --- | --- | --- |
| **OpenAI** | `Mythosia.AI` | GPT-6 Astra / Sol / Luna, GPT-5.6 Sol / Terra / Luna, GPT-5.5 / 5.5 Pro / 5.4 / 5.4 Mini / 5.4 Nano / 5.4 Pro / 5.3 Codex / 5.2 / 5.2 Pro / 5.1, GPT-4.1 / 4.1 Mini, GPT-4o / 4o Mini |
| **Anthropic** | `Mythosia.AI` | Claude Fable 5.1 / 5, Mythos 5.1 / 5 (有限开放), [Opus 5.5](providers.md#claude-opus-55) / 5 / 4.8 / 4.7 / 4.6 / 4.5, Sonnet 5 / 4.6 / 4.5, Haiku 4.5 |
| **Google** | `Mythosia.AI` | Gemini 3.8 Flash, Gemini 3.7 Flash, Gemini 3.6 Flash, Gemini 3.5 Flash/Flash-Lite, Gemini 3.1 Pro Preview/Flash-Lite, Gemini 3 Flash Preview, Gemini 2.5 Pro/Flash/Flash-Lite, Gemini 3.1 Flash Image, Gemini 3.1 Flash-Lite Image, Gemini 3 Pro Image |
| **xAI** | `Mythosia.AI` | Grok 4.7, Grok 4.6, Grok 4.5 (默认), Grok 4.3, Grok 4.20 (推理 / 非推理), Grok Build |
| **DeepSeek** | `Mythosia.AI` | Flash (V4.1 Flash), V4 Pro |
| **Perplexity** | `Mythosia.AI` | Agent API 预设与 `perplexity/sonar` |
| **Alibaba / Qwen** | `Mythosia.AI.Providers.Alibaba` | Qwen Max / Plus / Turbo / Qwen3 / Qwen3.5 系列 |

当回答需要基于最新信息，并让读者能够核对来源时，可以使用 Perplexity。`PerplexityService` 调用 Agent API；独立搜索和嵌入则用于为自行选择的回答模型构建检索能力。 [Perplexity Agent API、搜索与嵌入](perplexity.md).

审查长文档或执行多轮工具任务时，可以通过现有 Google 适配器选择 Gemini 3.7 Flash 或 3.8 Flash。支持从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 开始，服务默认模型仍为 Gemini 3.6 Flash。

需要快速起草再深入审查时，可显式选择 Grok 4.6，并设置 `Low` 至 `XHigh` 的推理强度。支持从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 开始，`XAIService` 默认模型仍为 Grok 4.5。参阅 [Grok 配置](providers.md#xai-xaiservice)。

创建图片草稿或组合参考图时，通过`IImageGenerationService`使用[Grok Imagine Image 2.0](providers.md#grok-imagine-image-20)。保留`OutputFormat = ImageOutputFormat.Auto`，并根据`MediaType`选择扩展名；xAI不能选择输出编码。参见[图片选项类型迁移](providers.md#image-options-migration)。聊天模型保持不变。

快速制作视觉草稿可选 Flare，精细修改可选 Sunburst。[GPT Image 2.5 生成与编辑](providers.md#gpt-image-25)通过现有图像 API 为每个请求指定模型；OpenAI 默认仍为 GPT Image 2。

图表和截图分析、本地函数调用、快速回答后的深入审查可使用 [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash)。推理默认关闭，通过 `WithDeepSeekReasoning(...)` 或请求级 `WithReasoning(...)` 开启。

纯文本任务可选择 `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813)。默认模型 Flash 支持图像，两者均提供 Low/High/Max 推理和相同输出上限。若要通过现有补全、流式、Run 和本地函数 API 使用 Responses，请在创建请求前设置 `UseResponsesApi = true`。默认仍为 `false`，以保留现有应用的 Chat Completions 行为；设置会固定到该请求及后续工具轮次。Responses 重发完整对话和原始推理历史，不依赖服务器保存的响应 ID。

使用 `DeepSeekImageFileContent`，可在 Flash 的 Chat Completions 或 Responses 中为多次提问复用已上传图像；仅支持文本的 V4 Pro 会拒绝图像。 请参阅[图像上传、复用与限制](providers.md#deepseek-deepseekservice)。 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。

> Claude Fable 5 和 Claude Mythos 5 要求保留数据 30 天，不适用于零数据保留安排。它们的自适应推理始终开启；调用方请求关闭推理时，Mythosia 使用低推理强度并省略推理摘要。Mythos 5 仅限获批的 Project Glasswing 客户使用。

## 指南与迁移

TXT 与 Markdown 应按文档结构选择[规则分割器](text-splitters.md)。实现会检查大小、重叠和 Unicode 边界，并保留 Markdown 标题、代码块和表格行。字符或单词数量不等于模型 token 上限。 表格条件和代码缩进的含义会保留；Markdown 上下文重复过量时会明确抛出异常并停止。

为避免索引看似成功却覆盖分块或关联错误向量，[索引验证](rag-pipeline.md#indexing-validation)会在持久化前拒绝无效 ID 和嵌入批次。自定义分割器必须提供唯一 ID 并继承文档元数据。

稳定的[文件标识](document-loaders.md#file-source-identity)、[问题向量验证](rag-embedding.md#query-embedding-validation)及[按文档持久化与 URL 取消](rag-pipeline.md#custom-persistence)可防止重复注册、无效搜索和旧分块残留。

可选的 `Mythosia.AI.Rag.Search.Pixie` 预览版可比较本地神经网络稀疏搜索与现有搜索。它保留现有稠密嵌入服务，将 PIXIE 索引放在内存中，不迁移持久化存储，也不自动替换默认搜索。 [PIXIE 配置与比较指南（英文）](../rag-pixie-search.md).

[检索评估基础设施](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Rag.Evaluation/README.md)支持可复用的数据集、搜索适配器、运行报告持久化和回归检查。可扩展同一个评估器，用于新的搜索方法和自己的文档集合。

独立管理请求设置，停止进行中的任务，并同时获取答案、用量和来源。[v8 升级指南](v8-migration.md)整理了六项架构变更、迁移示例和验证范围。

> 本文档对应的包版本: [Mythosia.AI 8.1.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v810), [Abstractions 4.1.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v410), [Alibaba 3.0.1](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v301), [RAG 8.2.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v820), [PostgreSQL 10.8.1](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081), [MCP 0.1.1-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v011-preview), [Serving.Abstractions 1.0.0](../../src/serving/Mythosia.AI.Serving.Abstractions/RELEASE_NOTES.md#v100), [Serving.Ollama 1.0.0](../../src/serving/Mythosia.AI.Serving.Ollama/RELEASE_NOTES.md#v100), [Serving.LlamaCpp 1.0.0](../../src/serving/Mythosia.AI.Serving.LlamaCpp/RELEASE_NOTES.md#v100), [Serving.Vllm 1.1.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v110). 其余检索、文档和向量包的版本见[此前补丁版本表](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811)及[此前联合发布](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v810)。

> [RAG 8.1.1 / PostgreSQL 10.8.1 补丁](https://github.com/AJ-comp/Mythosia.AI/blob/main/RELEASE_NOTES.md#v811)：现有 RAG 包装器会采用运行时更改的改写器，PostgreSQL 混合检索也会应用配置的向量搜索参数。核心包 `Mythosia.AI` 仍为 8.1.0。

---

## 架构

<a href="../assets/architecture.svg">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="../assets/architecture-dark.svg">
    <img src="../assets/architecture.svg" alt="Mythosia.AI 架构：核心 AI、RAG 编排、文档加载器、向量存储、共享契约、MCP 集成及独立的 Ollama、llama.cpp 和 vLLM 管理。" width="1600">
  </picture>
</a>

### 包依赖关系详情

箭头表示直接包引用。共享包出现在多个视图中；Serving 客户端共享管理契约，并独立于核心 AI。

#### 核心 AI 与扩展

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Extensions["提供商与工具扩展"]
        Alibaba["Mythosia.AI.<br/>Providers.Alibaba"]:::extension
        Mcp["Mythosia.AI.Mcp"]:::extension
    end
    AI["Mythosia.AI"]:::core
    AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
    subgraph Independent["独立的服务器管理"]
        ServingAbs["Mythosia.AI.Serving.<br/>Abstractions"]:::contract
        OllamaServing["Mythosia.AI.<br/>Serving.Ollama"]:::extension
        LlamaCppServing["Mythosia.AI.<br/>Serving.LlamaCpp"]:::extension
        VllmServing["Mythosia.AI.<br/>Serving.Vllm"]:::extension
        OllamaServing --> ServingAbs
        LlamaCppServing --> ServingAbs
        VllmServing --> ServingAbs
    end
    Alibaba --> AI
    Mcp --> AI
    AI --> AIAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### RAG 与文档加载

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    Rag["Mythosia.AI.Rag"]:::rag
    subgraph Contracts["AI 与 RAG 契约"]
        AIAbs["Mythosia.AI.<br/>Abstractions"]:::contract
        RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    end
    InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
    subgraph Documents["文档加载"]
        Office["Mythosia.Documents.<br/>Office"]:::documents
        Pdf["Mythosia.Documents.<br/>Pdf"]:::documents
        LoaderAbs["Mythosia.Documents.<br/>Abstractions"]:::contract
        Office --> LoaderAbs
        Pdf --> LoaderAbs
    end
    Rag --> AIAbs
    Rag --> RagAbs
    Rag --> InMem
    Rag --> Office
    Rag --> Pdf
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

#### 向量存储与搜索

```mermaid
%%{init: {"theme":"base","look":"classic","htmlLabels":false,"themeVariables":{"fontFamily":"Arial, sans-serif","fontSize":"18px","primaryTextColor":"#172c46","lineColor":"#64748b","clusterBkg":"#f8fafc","clusterBorder":"#cbd5e1"},"flowchart":{"htmlLabels":false,"curve":"linear","nodeSpacing":20,"rankSpacing":36,"padding":16,"wrappingWidth":300},"fontFamily":"Arial, sans-serif","fontSize":18}}%%
flowchart LR
    subgraph Stores["向量存储"]
        InMem["Mythosia.VectorDb.<br/>InMemory"]:::store
        Pg["Mythosia.VectorDb.<br/>Postgres"]:::store
        Qd["Mythosia.VectorDb.<br/>Qdrant"]:::store
        Pine["Mythosia.VectorDb.<br/>Pinecone"]:::store
    end
    subgraph Search["可选神经网络搜索"]
        Pixie["Mythosia.AI.Rag.<br/>Search.Pixie"]:::rag
    end
    RagAbs["Mythosia.AI.Rag.<br/>Abstractions"]:::contract
    VdbAbs["Mythosia.VectorDb.<br/>Abstractions"]:::contract
    InMem --> RagAbs
    InMem --> VdbAbs
    RagAbs --> VdbAbs
    Pg --> VdbAbs
    Qd --> VdbAbs
    Pine --> VdbAbs
    Pixie --> VdbAbs
    classDef core fill:#eff6ff,stroke:#93b4de,color:#172c46,stroke-width:1.5px
    classDef rag fill:#eef8f5,stroke:#83b4a4,color:#164638,stroke-width:1.5px
    classDef extension fill:#f5f0fc,stroke:#b9a4d4,color:#403054,stroke-width:1.5px
    classDef documents fill:#fff8e9,stroke:#d4b879,color:#61491d,stroke-width:1.5px
    classDef store fill:#edf7fb,stroke:#8dbaca,color:#194758,stroke-width:1.5px
    classDef contract fill:#f8fafc,stroke:#a7b2c2,color:#334155,stroke-width:1.5px
```

## 包列表

### 核心

| 包 | NuGet | 描述 |
| --- | --- | --- |
| [Mythosia.AI](../../src/core/Mythosia.AI/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.svg)](https://www.nuget.org/packages/Mythosia.AI) | 核心库 — 内置提供商、流式输出、函数调用及多模态支持 |
| [Mythosia.AI.Abstractions](../../src/core/Mythosia.AI.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Abstractions) | `IAIService` 接口和共享模型 — 面向库的轻量契约包 |
| [Mythosia.AI.Providers.Alibaba](../../src/core/Mythosia.AI.Providers.Alibaba/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Providers.Alibaba.svg)](https://www.nuget.org/packages/Mythosia.AI.Providers.Alibaba) | 基于 `Mythosia.AI` 的 Alibaba / Qwen 提供商包 |

### RAG

| 包 | NuGet | 描述 |
| --- | --- | --- |
| [Mythosia.AI.Rag](../../src/rag/Mythosia.AI.Rag/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag) | 通过 `.WithRag()` API 为 IAIService 提供 Fluent RAG 扩展 |
| [Mythosia.AI.Rag.Abstractions](../../src/rag/Mythosia.AI.Rag.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Rag.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Rag.Abstractions) | RAG 管道组件的接口和模型 |

### 文档加载器

| 包 | NuGet | 描述 |
| --- | --- | --- |
| [Mythosia.Documents.Abstractions](../../src/loaders/Mythosia.Documents.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.Documents.Abstractions) | 文档加载器接口和模型 (`IDocumentLoader`, `DoclingDocument`) |
| [Mythosia.Documents.Office](../../src/loaders/Mythosia.Documents.Office/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Office.svg)](https://www.nuget.org/packages/Mythosia.Documents.Office) | Word / Excel / PowerPoint 的 OpenXml 解析器 |
| [Mythosia.Documents.Pdf](../../src/loaders/Mythosia.Documents.Pdf/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.Documents.Pdf.svg)](https://www.nuget.org/packages/Mythosia.Documents.Pdf) | 基于 PdfPig 的 PDF 解析器 |

### 向量存储

> **选择一个或多个** — 均实现 Abstractions 包中的 `IVectorStore`。

| 包 | NuGet | 描述 |
| --- | --- | --- |
| [Mythosia.VectorDb.Abstractions](../../src/vectordb/Mythosia.VectorDb.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Abstractions) | `IVectorStore` · `VectorRecord` · `VectorFilter` 契约 |
| [Mythosia.VectorDb.InMemory](../../src/vectordb/Mythosia.VectorDb.InMemory/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.InMemory.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.InMemory) | 内存存储 — 零基础设施，非常适合原型开发 |
| [Mythosia.VectorDb.Pinecone](../../src/vectordb/Mythosia.VectorDb.Pinecone/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Pinecone.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Pinecone) | Pinecone HTTP API — 托管向量数据库的索引/命名空间/作用域隔离 |
| [Mythosia.VectorDb.Postgres](../../src/vectordb/Mythosia.VectorDb.Postgres/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Postgres.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Postgres) | PostgreSQL + pgvector — HNSW / IVFFlat 索引，可用于生产环境 |
| [Mythosia.VectorDb.Qdrant](../../src/vectordb/Mythosia.VectorDb.Qdrant/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.VectorDb.Qdrant.svg)](https://www.nuget.org/packages/Mythosia.VectorDb.Qdrant) | Qdrant gRPC 客户端 — Cosine / Euclidean / Dot，自动配置 |

### Serving — 控制平面

通过统一的管理 API，为运行中的 Ollama、llama.cpp 和 vLLM 构建模型选择器和服务器状态页面。`IModelServer` 查询健康状态、模型和可用功能；查询不会加载或下载模型。这些客户端连接现有服务器，不托管运行时，也不发送聊天请求。

可选的 `IModelLifecycle`、`IModelDownloader` 和 `IModelMetricsProvider` 在功能可用时提供显式管理操作。请检查所连接服务器的功能：`Unknown` 表示证据不足，不等于 `Unsupported`；`Supported` 也不保证每个模型都能操作成功。无法确定的安装和加载状态仍保留为未知。

已通过实际服务器验证的配置包括 Ollama **0.34.4**（`qwen2.5:0.5b`）、llama.cpp **b11146** 的 Router 与单模型模式（Qwen2.5 0.5B、Q4_K_M），以及 vLLM **0.30.0**（小型 Qwen 模型）。结果仅适用于这些已验证配置。操作覆盖范围和运行时限制请参阅[服务器管理指南](serving.md)。

| 包 | NuGet | 描述 |
| --- | --- | --- |
| [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Abstractions.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Abstractions) | 通用管理契约与不可变的服务器、模型及能力快照。 |
| [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Ollama.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Ollama) | Ollama 模型列表、健康状况、显式预加载/卸载及流式下载。 |
| [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.LlamaCpp.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.LlamaCpp) | llama.cpp 查询、路由确认后的生命周期/下载及无自动加载的指标。 |
| [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) | [![NuGet](https://img.shields.io/nuget/v/Mythosia.AI.Serving.Vllm.svg)](https://www.nuget.org/packages/Mythosia.AI.Serving.Vllm) | vLLM 模型卡、健康状况、版本及带标签指标；保留现有具体 API。 |

## 仓库结构

```text
src/
  core/
    Mythosia.AI/                        # 核心 AI 服务库
    Mythosia.AI.Abstractions/           # IAIService 接口和共享模型
    Mythosia.AI.Providers.Alibaba/      # Alibaba / Qwen 提供商包
  loaders/
    Mythosia.Documents.Abstractions/    # 文档加载器契约 (IDocumentLoader, DoclingDocument)
    Mythosia.Documents.Office/          # Office 文档加载器 (Word/Excel/PowerPoint)
    Mythosia.Documents.Pdf/             # PDF 文档加载器
  rag/
    Mythosia.AI.Rag/                    # RAG Fluent API 和管道
    Mythosia.AI.Rag.Abstractions/       # RAG 接口和模型 (RagDocument)
  serving/
    Mythosia.AI.Serving.Abstractions/  # 通用模型服务器管理契约
    Mythosia.AI.Serving.Ollama/        # Ollama 管理与显式下载
    Mythosia.AI.Serving.LlamaCpp/      # llama.cpp 单模型/路由管理
    Mythosia.AI.Serving.Vllm/          # vLLM 管理与指标
  vectordb/
    Mythosia.VectorDb.Abstractions/     # 向量存储契约
    Mythosia.VectorDb.InMemory/         # 内存向量存储
    Mythosia.VectorDb.Pinecone/         # Pinecone 向量存储
    Mythosia.VectorDb.Postgres/         # PostgreSQL + pgvector 存储
    Mythosia.VectorDb.Qdrant/           # Qdrant 向量存储
apps/                                   # 示例应用
tests/                                  # 单元/集成测试项目
```

## 安装

```bash
dotnet add package Mythosia.AI
```

如需对流进行高级 LINQ 操作：

```bash
dotnet add package System.Linq.Async
```

## 文档

需要先快速起草再深入审查，或根据最新信息和已托管文档回答时，请参阅[推理与带来源的搜索](reasoning-and-search.md)。

- **[📖 完整文档站点](https://aj-comp.github.io/Mythosia.AI/)** — 由 DocFX 生成，涵盖全部功能、RAG 流水线、向量存储及 API 参考
- [基础使用指南](getting-started.md)
- [Mythosia.AI README](../../src/core/Mythosia.AI/README.md)  包含函数调用、流式输出和模型配置的完整 API 参考
- [Mythosia.AI.Rag README](../../src/rag/Mythosia.AI.Rag/README.md)  RAG 管道使用方法和自定义实现
- [加载器指南](document-loaders.md)
- [发布说明](../../src/core/Mythosia.AI/RELEASE_NOTES.md)

## 使用真实提供商验证处理速度

在仓库根目录运行：

```powershell
./build/test-inference-speed-live.ps1
```

这套付费测试使用现有测试 Key Vault 配置和合成提示词，针对 Anthropic Opus 5.5、OpenAI GPT-6 Astra、Gemini 3.8 Flash 和 Grok 4.6，组合 ProviderDefault/Standard/Fast 与补全/Run 路径，共验证 24 个用例。账户访问错误、缺少已应用模式报告或服务器降级均不算 Fast 验证成功；所有用例必须无跳过地通过。报告保存到 `artifacts/test-results/inference-speed-live`。只有在构建当前 Release 测试后才能使用 `-NoBuild`。此命令说明如何运行测试，并不代表当前账户已通过验证。

## 许可证

本项目基于 [MIT 许可证](https://github.com/AJ-comp/Mythosia.AI/blob/main/LICENSE) 发布。

## 前身

本项目最初是 [Mythosia](https://github.com/AJ-comp/Mythosia) 的一部分。

[用共享支持定义构建模型功能选项](model-capabilities.md).
