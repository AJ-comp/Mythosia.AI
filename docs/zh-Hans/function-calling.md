# 函数调用

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

Mythosia 在 GPT-6 Astra 的 Responses API 中发送 `async: true`。对于不支持的模型和 API，会省略该字段并等待同一个处理器的结果，不会修改用户设置的 `AllowAsync` 值。提供商还必须将实际调用标为异步（`FunctionCall.IsAsync`）；开启许可不代表一定异步执行。

`WithFunctionAsync` 用于注册 .NET 异步处理器，`FunctionExecutionMode.Parallel` 控制本地处理器调度，两者都不会自动开启此选项。`AllowAsync` 允许模型在函数结果返回前继续工作。 `FunctionExecutionMode` 仍控制普通调用。允许的异步任务即使在 `Sequential` 模式下也可重叠执行，并在独立任务池内共享 `MaxConcurrency` 上限。

运行中的工具任务由 `GetCompletionAsync`、接收输入的旧 `StreamAsync`，或 `StartRunAsync` 返回的 `AIRun` 管理；完成后使用原始调用 ID 发送结果。成功的最终返回或 Run 完成会等待待处理结果完成处理。Run 独立于输出观察继续运行，但工具任务并不是脱离 Run 存续的公开后台会话。

使用异步工具时，`GetCompletionAsync` 在请求结束后按顺序汇总中间的独立说明与最终文本。旧 `StreamAsync` 和 `run.StreamAsync()` 都在各轮文本到达时通知读取者；`run.Result` 同样是 Run 中所有文本的连接结果。

处理器不接收取消令牌。因此，在取消、超时、错误或提前结束接收输入的旧流时，清理仍会等待已启动的处理器完成。仅停止读取 `run.StreamAsync()` 不会停止执行；取消 Run 应使用 `run.Cancel()`、启动时的令牌或释放 Run。此集成适用于已注册的函数处理器。底层协议见[官方 API 指南](https://developers.openai.com/api/docs/guides/async-tool-calling)。

流式调用在确认函数调用完整且到达有效响应边界后才启动处理器，随后可在异步任务执行期间继续下一轮模型请求。未完成的调用事件不会触发执行。如果模型没有发起新调用但仍有任务未完成，Mythosia 会等待结果后再继续。为避免未完成的调用从历史中丢失，存在待处理调用时会禁用上下文超限后的自动摘要和重试。
