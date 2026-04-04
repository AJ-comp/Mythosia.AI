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

[AiFunction("search_products", "搜索产品目录")]
public static string SearchProducts(
    [AiParameter("搜索关键词", required: true)] string query,
    [AiParameter("最大返回数量")] int limit = 5)
{
    // ... 你的实现
    return JsonSerializer.Serialize(results);
}
```

然后注册：

```csharp
service.AddFunction(SearchProducts);
```

## 函数调用策略

控制模型何时允许调用函数：

```csharp
using Mythosia.AI.Models.Functions;

// 由模型自行决定（默认）
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// 强制模型始终调用函数
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// 禁用函数调用
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
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

var fn = FunctionBuilder
    .Create("get_stock_price", "返回当前股票价格")
    .AddParameter("ticker", "股票代码", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
