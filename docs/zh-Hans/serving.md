# 管理运行中的模型服务器

模型选择界面或运维工具需要在发送提示词前了解服务器健康状况、可用模型及加载状态。Serving 包为 Ollama、llama.cpp 和 vLLM 提供统一查询接口，并让运行时专属操作保持显式。

可以用它们填充模型选择列表、显示服务器是否可达、在运行时支持的情况下管理模型驻留，或读取引擎指标。更换运行时时，应用的通用查询代码可以保持不变。

这些客户端连接已有 HTTP 服务器。引擎安装与托管、GPU 租赁、聊天及嵌入生成由其他组件负责。聊天继续通过合适的 AI 服务进行，例如 vLLM 使用 `QwenService`；RAG 嵌入提供者保持独立。发现过程不会自动加载模型。尚未实现 SGLang。

## 选择包

| 包 | 版本 | 用途 |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | 为应用代码或自定义管理适配器提供通用契约。无包依赖。 |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | 查询 Ollama、下载模型以及显式预加载或卸载模型。 |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | 查询 llama.cpp、读取指标，以及在 Router 模式下管理模型。 |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | 通过通用 API 或现有 vLLM 专属 API 查询 vLLM 并读取指标。 |

这四个包均面向 .NET Standard 2.1。安装所需的适配器即可自动引入抽象契约包。适配器依赖通用契约和 Newtonsoft.Json，独立于核心 AI 与 RAG 包。

## 在不改变服务器状态的情况下查询

安装所用运行时的具体包。以下示例使用 Ollama；其他服务器请选择对应命名空间中的 `VllmServer` 或 `LlamaCppServer`。发现过程只使用只读请求，不发送加载、生成或下载命令。

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

端点应为服务器根地址，可包含反向代理的路径前缀。API 密钥可选，以 Bearer 凭据随每次请求发送。客户端不会更改 `HttpClient.DefaultRequestHeaders`，也不会释放传入的 `HttpClient`；请根据应用生命周期复用和释放它。在此 Ollama 示例中，超时也适用于流式响应正文，因此应为模型下载预留足够时间。

## 通用与可选契约

| 契约 | 用途 |
| --- | --- |
| `IModelServer` | 服务器信息、健康状况、模型及已观察到的能力。 |
| `IModelLifecycle` | 显式加载与卸载命令，可选。 |
| `IModelDownloader` | 带进度的显式下载，可选。 |
| `IModelMetricsProvider` | 保留标签的指标样本，可选。 |

实现接口表示客户端具有该操作；`ServingCapabilities` 表示可从已连接端点确认的支持情况。`Supported` 不保证对每个模型都有权限或都能成功。`Unsupported` 表示在观察到的模式或端点上不可用。`Unknown` 表示证据不足，包括认证或连接失败，不能将其当成不支持。

`InstallationState` 和 `LoadState` 是不同的观察结果。`Unknown` 既不表示不存在，也不表示已卸载。缺失的 `SizeBytes`、`MemoryBytes`、`ContextLength` 保留为 `null`，而非零。管理端点健康不代表某个模型已经可以推理。

## 运行时差异

| 操作 | Ollama | llama.cpp 单模型 | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| 信息、健康状况与模型列表 | 支持 | 支持 | 支持 | 支持 |
| 显式加载 / 卸载 | 支持，使用空生成请求 | 不支持 | 确认 Router 身份后支持 | 此客户端不支持 |
| 模型下载 | 支持，带流式进度 | 不支持 | 显式操作；需要下载端点和 SSE 事件 | 此客户端不支持 |
| 指标 | 未实现 | 启用后可读取服务器指标 | 具体类的模型专属重载；模型必须已加载 | 可用时读取服务器指标 |

此表说明客户端提供的操作，不保证每个服务器版本、权限配置或模型都支持这些操作。请检查连接端点的能力并处理操作失败。

**Ollama：** `/api/tags` 提供已注册模型，`/api/ps` 提供当前运行实例。远程模型可以在没有本地权重的情况下注册；若无本地运行实例，其加载状态仍未知。预加载使用空 `/api/generate` 请求和服务器默认 keep-alive。不会将仅用于嵌入的模型自动转到其他端点。卸载使用 `keep_alive: 0`，不会删除文件。未实现指标功能。

**llama.cpp：** 生命周期或下载命令执行前，必须通过 `/props` 明确确认路由模式。单模型模式不支持这些命令，并保留观察到的休眠状态。路由下载先订阅 `/models/sse`，再提交 `POST /models`，只有目标模型的 `download_finished` 事件才代表成功。仅有 SSE 可用仍不能确认下载能力。服务器级指标用于单模型模式；路由指标需要具体类的 `GetMetricsAsync(modelId, token)` 重载，该重载发送 `autoload=false`，避免查询自动加载模型。

**vLLM：** 保留服务别名和可选的 `root` 字段，但通用安装与加载状态均保持未知。模型和指标根据实际响应检查；不支持生命周期及下载。具体客户端继续提供现有 `VllmServer` 方法和 DTO，通用健康、模型及指标方法通过显式接口提供。


## 显式执行管理操作

下载和驻留状态更改会消耗网络、磁盘或设备内存。应用需要时再执行。以下代码继续前面的 Ollama 示例，下载一个小模型并短暂加载以观察状态。请使用服务器的精确模型 ID，包括 Ollama 标签或 llama.cpp 量化标签。

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

示例使用专门的测试模型，并在结束后卸载。生产应用应自行决定何时释放模型；不要卸载仍被其他请求使用的模型。清理操作有独立的截止时间，服务器不可用时可能失败。

进度描述单个文件或阶段。可空字节计数不代表零，也不是整个模型的百分比。加载调用成功仅表示命令已获确认，不表示就绪或无限期驻留；需要就绪状态时，应在有限等待时间内观察 `LoadState`。llama.cpp Router 下载协议和版本限制请参阅具体包指南。明确请求的操作在核实服务器配置后，即使支持状态为 `Unknown` 也可尝试，但能力发现本身绝不会发起该操作。

## 取消与错误

为查询和命令传递取消令牌。取消会停止此客户端的 HTTP 工作和等待，不保证远程取消、回滚或清除已下载层。请按操作时长配置传入的 `HttpClient`；客户端不接管其所有权。

比较模型或引擎时保留指标标签。缺失指标不是零，数值可能包含 `NaN` 或无穷大。`ServingException` 是通用错误类型；通用管理错误不包含原始响应正文或凭据。现有 vLLM 专属调用保留旧版错误详情。

`GetHealthAsync` 将端点失败归类为健康状态，但仍会传播调用方取消。其他操作可能抛出 `ServingException`，已知不支持的 llama.cpp 模式可能抛出 `NotSupportedException`。超时或请求失败都不能证明远程操作已回滚。下载方法仅在运行时报告完成后才成功返回：Ollama 需要终止成功消息及其后的 EOF；llama.cpp Router 需要匹配的 `download_finished` 事件。

## 已验证的范围

离线测试涵盖受控的成功响应、格式错误响应、错误和取消。另行开展的真实服务器检查使用一块 NVIDIA A40、小型公开 Qwen 模型及以下引擎构建：

| 运行时 | 测试模型 | 已验证的管理操作 |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | 发现、全新下载、加载/卸载、经过敏感信息清理的模型缺失错误、预先取消，以及部分下载进度后的取消。 |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | 发现、下载事件、加载/卸载、无自动加载的模型专属指标、错误及下载取消。 |
| llama.cpp b11146，单模型 | 相同的 GGUF 模型 | 发现、服务器指标、取消，以及显式拒绝 Router 生命周期命令。 |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | 发现、服务器指标及预先取消。 |

简短的原生 HTTP 推理请求也在四种配置中返回了生成文本。这些请求验证的是引擎运行情况，而非 AI 服务聊天适配器、模型质量、吞吐量或与所有引擎构建的兼容性。以上配置是实际测试配置，不是最低支持版本。下载取消检查使用了其他较大的测试模型，未断言远程回滚。第一次 Ollama 下载失败；重试和删除模型后的全新下载均通过，但未确定首次失败的准确原因。

请使用[需显式启用的真实服务器验证指南](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md)检查部署的端点。指南区分了已纳入仓库的管理测试运行器，以及验证期间额外使用的推理和取消探测。详细执行报告不纳入公开文档。

## 各包指南

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — 通用管理契约与不可变的服务器、模型及能力快照。
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Ollama 模型列表、健康状况、显式预加载/卸载及流式下载。
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — llama.cpp 查询、路由确认后的生命周期/下载及无自动加载的指标。
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — vLLM 模型卡、健康状况、版本及带标签指标；保留现有具体 API。
