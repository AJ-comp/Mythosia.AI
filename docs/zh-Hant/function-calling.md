# 函式呼叫

## 為什麼需要函式呼叫？

LLM 只能生成文字 — 它無法自行查看天氣、查詢資料庫或呼叫 API。**沒有**函式呼叫時，你需要手動解析模型的意圖：

```csharp
// ❌ 沒有函式呼叫 — 手動解析意圖
var reply = await service.GetCompletionAsync("台北今天天氣怎麼樣？");
// reply = "我需要查詢天氣服務才能回答。"

// 你必須自己判斷使用者想查天氣、提取「台北」、呼叫 API
if (reply.Contains("天氣"))
{
    var city = ExtractCity(reply); // 脆弱的正規表示式或關鍵字比對
    var weather = await weatherApi.GetAsync(city);
}
```

**有了**函式呼叫，模型會自行決定**何時**呼叫你的程式碼以及傳遞**什麼參數**：

```csharp
// ✅ 有函式呼叫 — 模型自動處理意圖和參數擷取
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "取得指定地點的目前天氣",
        ("location", "城市和國家", required: true),
        (string location) => weatherApi.Get(location)
    );

var response = await service.GetCompletionAsync("台北今天天氣怎麼樣？");
```

你定義程式碼**能做什麼**；模型自行判斷**何時**以及**如何**使用。

## 快速範例

```csharp
var service = new OpenAIService(apiKey, http)
    .WithFunction(
        "get_weather",
        "取得指定地點的目前天氣",
        ("location", "城市和國家", required: true),
        (string location) => $"{location}的天氣：晴，22°C"
    );

var response = await service.GetCompletionAsync("台北今天天氣怎麼樣？");
```

## 使用特性定義函式

對於較複雜的函式，使用 `[AiFunction]` 和 `[AiParameter]` 特性：

```csharp
using Mythosia.AI.Attributes;

[AiFunction("search_products", "搜尋產品目錄")]
public static string SearchProducts(
    [AiParameter("搜尋關鍵字", required: true)] string query,
    [AiParameter("最大回傳數量")] int limit = 5)
{
    return JsonSerializer.Serialize(results);
}
```

然後註冊：

```csharp
service.AddFunction(SearchProducts);
```

## 函式呼叫策略

控制模型何時允許呼叫函式：

```csharp
using Mythosia.AI.Models.Functions;

// 由模型自行決定（預設）
service.FunctionCallingPolicy = FunctionCallingPolicy.Auto;

// 強制模型始終呼叫函式
service.FunctionCallingPolicy = FunctionCallingPolicy.Required;

// 停用函式呼叫
service.FunctionCallingPolicy = FunctionCallingPolicy.None;
```

## 批次註冊類別中的函式

一次性註冊物件中所有標注了 `[AiFunction]` 的方法：

```csharp
var tools = new MyTools();
service.WithFunctions(tools);  // 掃描實體方法上的 [AiFunction]
```

註冊靜態方法：

```csharp
service.WithStaticFunctions<MyTools>();  // 掃描靜態方法上的 [AiFunction]
```

## 非同步函式處理器

所有 `WithFunction` 多載都有對應的 `WithFunctionAsync` 版本，接受 `Func<..., Task<string>>`：

```csharp
service.WithFunctionAsync<string>(
    "fetch_data",
    "從外部 API 擷取資料",
    ("url", "要請求的 URL", required: true),
    async (string url) =>
    {
        var result = await httpClient.GetStringAsync(url);
        return result;
    }
);
```

支援 0 到 3 個參數，與同步版本一致。

## 暫時停用函式

在不移除註冊的情況下對單一請求停用函式呼叫：

```csharp
string answer = await service.AskWithoutFunctionsAsync("直接回答即可");

service.WithoutFunctions();  // 設定 FunctionsDisabled = true
```

## 使用 FunctionBuilder

以程式設計方式建構函式定義：

```csharp
using Mythosia.AI.Builders;

var fn = FunctionBuilder
    .Create("get_stock_price", "回傳目前股票價格")
    .AddParameter("ticker", "股票代碼", required: true)
    .Build();

service.AddFunction(fn, ticker => FetchStockPrice(ticker));
```
