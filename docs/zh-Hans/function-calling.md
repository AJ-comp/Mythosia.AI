# 函数调用

> GPT-6 Sol/Luna 是尚未发布的新增功能。参见[模型选择与版本要求](providers.md#gpt-6-sol-luna)。

只需完整答案和停止按钮时，将 `cancellationToken` 传给 `GetCompletionAsync`。进度事件或受支持的中途追加指令使用 Run。参阅[取消回答](completions.md#completion-cancellation)。

如需分离每个请求的设置并派生多个版本，请使用[请求构建器](request-building.md)。先调用`CreateRequest(...)`，再连接`With...`。服务属性和服务上的fluent方法保持原有行为。

## 为什么需要函数调用？

LLM 只能生成文本 — 它无法自行查看天气、查询数据库或调用 API。**没有**函数调用时，你需要手动解析模型的意图：

```csharp
// ❌ 没有函数调用 — 手动解析意图
var reply = await service.GetCompletionAsync("北京今天天气怎么样？");
// reply = "我需要查询天气服务才能回答。"

// 你必须自己判断用户想要查天气、提取"北京"、调用 API
if (reply.Contains("天气"))
{
    var city = ExtractCity(reply); // 脆弱的正则或关键词匹配
    var weather = await weatherApi.GetAsync(city);
    // 还得把天气数据注入后再次请求...
}
```

这种方式脆弱且不可扩展，需要你预判所有可能的用户意图。**有了**函数调用，模型会自行决定**何时**调用你的代码以及传递**什么参数**：

```csharp
// ✅ 有函数调用 — 模型自动处理意图和参数提取
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "获取指定地点的当前天气",
        ("location", "城市和国家", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("北京今天天气怎么样？");
// 模型调用 get_weather("北京, 中国")，获取结果后自然地回答。
```

你定义代码**能做什么**；模型自行判断**何时**以及**如何**使用。

## 快速示例

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "获取指定地点的当前天气",
        ("location", "城市和国家", required: true),
        (string location) => $"{location}的天气：晴，22°C"
    );

var response = await service.GetCompletionAsync("北京今天天气怎么样？");
// 模型调用 get_weather("北京, 中国") 并将结果整合到回答中。
```

## 使用特性定义函数

对于较复杂的函数，使用 `[AiFunction]` 和 `[AiParameter]` 特性：

```csharp
using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "搜索产品目录")]
    public string SearchProducts(
        [AiParameter("搜索关键词", required: true)] string query,
        [AiParameter("最大返回数量")] int limit = 5)
    {
        // ... 你的实现
        return JsonSerializer.Serialize(results);
    }
}
```

然后注册：

```csharp
service.WithFunctions(new ProductFunctions());
```

## 函数调用策略

控制模型何时允许调用函数：

```csharp
using Mythosia.AI.Models.Functions;

// 由模型自行决定（默认）
service.FunctionCallMode = FunctionCallMode.Auto;

// 强制模型始终调用函数
service.ForceFunctionName = "search_products";

// 禁用函数调用
service.FunctionCallMode = FunctionCallMode.None;
```

[Claude Fable 5.1](fable-5-1.md) 的进度更新、单轮指令和 thinking 绑定诊断从 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 开始提供。Mythos 5.1 需要邀请访问，两个模型都拒绝强制工具选择。

## 批量注册类中的函数

一次性注册对象中所有标注了 `[AiFunction]` 的方法：

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // 扫描实例方法上的 [AiFunction]
```

注册静态方法：

```csharp
service.WithStaticFunctions<MyTools>();  // 扫描静态方法上的 [AiFunction]
```

## 异步函数处理器

所有 `WithFunction` 重载都有对应的 `WithFunctionAsync` 版本，接受 `Func<..., Task<string>>`：

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "从外部 API 获取数据",
    ("url", "要请求的 URL", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

支持 0 到 3 个参数，与同步版本一致。

## 临时禁用函数

在不移除注册的情况下对单个请求禁用函数调用：

```csharp
// 扩展方法 — 返回禁用函数后的结果
string answer = await service.AskWithoutFunctionsAsync("直接回答即可");

// 或切换属性
service.WithoutFunctions();  // 设置 FunctionsDisabled = true
```

## 使用 FunctionBuilder

以编程方式构建函数定义：

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("返回当前股票价格")
    .AddParameter("ticker", "string", "股票代码", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

<a id="tool-execution-contract"></a>

## 异步工具直接返回对象，并响应取消

读取文件或数据库的工具通常在异步I/O结束后返回对象。用户点击停止时，取消也应传递给仍在执行的操作。同步函数早已支持直接返回对象；本次更新让异步返回保持一致，并将异常正确记录为失败。

Before：异步函数以前需要自行序列化结果。直接返回`Task<FileResult>`会丢失对象，只传回`"Success"`。

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "读取文本文件")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After：直接返回对象，并把库注入的取消令牌传给I/O操作。应用无需实现新的结果包装类型或适配器。

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "读取文本文件")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

`[AiFunction]`注册支持普通对象、`Task<T>`和`ValueTask<T>`，非字符串值会转换为JSON。`string`、`Task<string>`和`ValueTask<string>`保持原始文本，不额外添加JSON引号。无返回值的`Task`和`ValueTask`也会等待完成。原有同步对象返回继续可用。 null返回值会变为`"Done"`；无返回值的`Task` / `ValueTask`完成后传递`"Success"`。

即使返回类型声明为`Task`或`object`，实际返回的`Task<T>`也会等待完成并按相同规则转换。作为`object`返回的`ValueTask<T>`和无返回值的`ValueTask`也会等待完成，每个`ValueTask`只消费一次。

`CancellationToken`参数由库注入，不出现在面向模型的参数架构中。通过服务或请求构建器的`WithFunctions(...)`、`WithStaticFunctions<T>()`注册即可。

注册时会拒绝`async void`工具方法。请返回`Task`或`ValueTask`，以便执行器等待完成、观察错误并完成取消后的清理。

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("读取report.txt并总结。")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`、取消传给`StartRunAsync`的令牌，或释放活动run，都会传递给支持取消的本地工具。函数必须使用该令牌；无法强制终止忽略令牌的代码。尚未开始的调用会跳过并记录取消结果，已经开始的函数仍会等待，以保持调用与结果成对。执行取消后run保持取消状态，不会再启动下一轮模型请求。

Run启动失败或释放MCP连接时，即使取消回调抛出异常，也会继续尝试清理会话或传输连接。原始错误与清理错误都会保留，必要时通过`AggregateException`一起传递。 并发异步等待`McpConnection.DisposeAsync()`的调用会等待同一次清理完成。先关闭传输连接，让依赖连接关闭的读取结束，再等待读取循环退出。

为避免关闭期间迟到的工具调用一直等待，连接开始释放后，新的 `InitializeAsync`、`RefreshToolsAsync` 和 `CallToolAsync` 操作会以 `ObjectDisposedException` 被拒绝。如果响应的请求 ID 匹配但正文格式无效，则跳过该响应并保留请求的等待登记；后续有效响应、调用方取消或连接清理仍可结束该调用。 如果服务器关闭流或传输读取失败导致读取已结束，新操作会以 `McpException` 失败，而不是等待无法到达的响应；请创建新连接后继续。

实际失败时直接抛出异常。执行器将其记录为`FunctionCallResult.IsError = true`，不会误当成正常的`"Error: ..."`字符串。函数有意返回的字符串仍是正常结果。取消的工具结果同时设置`IsCancelled = true`和`IsError = true`。

编程注册时，使用接收两个参数的`WithHandler`重载：

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("读取文本文件")
    .AddParameter("path", "string", "文件路径", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

现有单参数字符串处理器继续支持。直接构造定义时，可将`Func<Dictionary<string, object>, CancellationToken, Task<string>>`赋给`HandlerWithCancellation`。设置`Handler`或`HandlerWithCancellation`都会替换同一个处理器，不会注册两次执行。此低层API仍返回字符串；对象自动序列化由方法注册负责。

这是本地.NET函数的返回值与取消处理，不依赖提供商原生的`AllowAsync`功能。仅停止`run.StreamAsync(token)`读取只会停止观察，run继续执行。参见[Run指南](execution-api-transition.md)和[提供商协议](https://developers.openai.com/api/docs/guides/async-tool-calling)。

## 模型异步工具调用

天气查询较慢时，模型仍可先介绍不依赖天气结果的通用旅行用品。模型原生异步工具调用用于在这种等待期间继续独立工作；依赖查询结果的判断仍应等结果返回后再进行。

GPT-6 Astra 支持和异步工具调用从 `Mythosia.AI` 7.1.0 开始提供，共享类型包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

`FunctionDefinition.AllowAsync` 默认为 `false`。只有在函数执行期间允许模型继续其他工作时，才将其设为 `true`，或调用 `FunctionBuilder.WithAsync()`。使用 `WithAsync(false)` 可关闭该选项。同一份函数定义和处理器可以在多个提供商之间复用。

使用特性注册时，也可以通过 `[AiFunction("lookup", "查询数据", AllowAsync = true)]` 指定同一许可。

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("返回首尔的示例天气")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "查询首尔的示例天气。等待时请介绍三件旅行必备物品。");
```

Mythosia 在 GPT-6 Astra / Sol / Luna 的 Responses API 中发送 `async: true`。对于不支持的模型和 API，会省略该字段并等待同一个处理器的结果，不会修改用户设置的 `AllowAsync` 值。提供商还必须将实际调用标为异步（`FunctionCall.IsAsync`）；开启许可不代表一定异步执行。

`WithFunctionAsync` 用于注册 .NET 异步处理器，`FunctionExecutionMode.Parallel` 控制本地处理器调度，两者都不会自动开启此选项。`AllowAsync` 允许模型在函数结果返回前继续工作。 `FunctionExecutionMode` 仍控制普通调用。允许的异步任务即使在 `Sequential` 模式下也可重叠执行，并在独立任务池内共享 `MaxConcurrency` 上限。

运行中的工具任务由 `GetCompletionAsync`、接收输入的旧 `StreamAsync`，或 `StartRunAsync` 返回的 `AIRun` 管理；完成后使用原始调用 ID 发送结果。成功的最终返回或 Run 完成会等待待处理结果完成处理。Run 独立于输出观察继续运行，但工具任务并不是脱离 Run 存续的公开后台会话。

使用异步工具时，`GetCompletionAsync` 在请求结束后按顺序汇总中间的独立说明与最终文本。旧 `StreamAsync` 和 `run.StreamAsync()` 都在各轮文本到达时通知读取者；`(await run.Result).Text` 同样是 Run 中所有文本的连接结果。

本地工具可以通过`Task<T>` / `ValueTask<T>`返回对象，并接收库注入的`CancellationToken`。`run.Cancel()`或启动令牌的取消会传递给配合取消的工具，仅停止流读取则不会。异常会记录为失败；取消时跳过排队调用，清理仍会等待已启动且忽略令牌的工具。参见[结果、错误与取消](function-calling.md#tool-execution-contract)。

流式调用在确认函数调用完整且到达有效响应边界后才启动处理器，随后可在异步任务执行期间继续下一轮模型请求。未完成的调用事件不会触发执行。如果模型没有发起新调用但仍有任务未完成，Mythosia 会等待结果后再继续。为避免未完成的调用从历史中丢失，存在待处理调用时会禁用上下文超限后的自动摘要和重试。

Perplexity: [控制研究和工具](perplexity.md).
