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
using Mythosia.AI.Extensions;

public sealed class ProductFunctions
{
    [AiFunction("search_products", "搜尋產品目錄")]
    public string SearchProducts(
        [AiParameter("搜尋關鍵字", required: true)] string query,
        [AiParameter("最大回傳數量")] int limit = 5)
    {
        return JsonSerializer.Serialize(results);
    }
}
```

然後註冊：

```csharp
service.WithFunctions(new ProductFunctions());
```

## 函式呼叫策略

控制模型何時允許呼叫函式：

```csharp
using Mythosia.AI.Models.Functions;

// 由模型自行決定（預設）
service.FunctionCallMode = FunctionCallMode.Auto;

// 強制模型始終呼叫函式
service.ForceFunctionName = "search_products";

// 停用函式呼叫
service.FunctionCallMode = FunctionCallMode.None;
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
using Mythosia.AI.Extensions;

var fn = FunctionBuilder
    .Create("get_stock_price")
    .WithDescription("回傳目前股票價格")
    .AddParameter("ticker", "string", "股票代碼", required: true)
    .WithHandler(args => FetchStockPrice(args["ticker"].ToString() ?? string.Empty))
    .Build();

service.WithFunction(fn);
```

## 模型非同步工具呼叫

天氣查詢較慢時，模型仍可先介紹不依賴天氣結果的一般旅行用品。模型原生非同步工具呼叫用於在這種等待期間繼續獨立工作；依賴查詢結果的判斷仍應等結果傳回後再進行。

GPT-6 Astra 支援與非同步工具呼叫從 `Mythosia.AI` 7.1.0 開始提供，共用型別包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

`FunctionDefinition.AllowAsync` 預設為 `false`。只有在函式執行期間允許模型繼續其他工作時，才設為 `true`，或呼叫 `FunctionBuilder.WithAsync()`。使用 `WithAsync(false)` 可關閉此選項。同一份函式定義與處理常式可以在多個供應商之間重複使用。

使用屬性註冊時，也可以透過 `[AiFunction("lookup", "查詢資料", AllowAsync = true)]` 指定同一許可。

```csharp
using System.Threading.Tasks;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
var weatherTool = FunctionBuilder.Create("get_demo_weather")
    .WithDescription("傳回首爾的範例天氣")
    .WithAsync()
    .WithHandler(async _ =>
    {
        await Task.Delay(5000);
        return "{\"city\":\"Seoul\",\"temperature_c\":24,\"source\":\"demo\"}";
    })
    .Build();
service.WithFunction(weatherTool);
var answer = await service.GetCompletionAsync(
    "查詢首爾的範例天氣。等待時請介紹三件旅行必備物品。");
```

Mythosia 在 GPT-6 Astra 的 Responses API 中傳送 `async: true`。對於不支援的模型和 API，會省略此欄位並等待同一個處理常式的結果，不會變更使用者設定的 `AllowAsync` 值。供應商也必須將實際呼叫標示為非同步（`FunctionCall.IsAsync`）；開啟許可不代表一定會以非同步方式執行。

`WithFunctionAsync` 用於註冊 .NET 非同步處理常式，`FunctionExecutionMode.Parallel` 控制本機處理常式的排程，兩者都不會自動開啟此選項。`AllowAsync` 允許模型在函式結果傳回前繼續工作。 `FunctionExecutionMode` 仍控制一般呼叫。允許的非同步工作即使在 `Sequential` 模式下也可重疊執行，並在獨立工作池內共用 `MaxConcurrency` 上限。

執行中的工具工作由 `GetCompletionAsync`、接收輸入的舊 `StreamAsync`，或 `StartRunAsync` 傳回的 `AIRun` 管理；完成後使用原始呼叫 ID 傳送結果。成功的最終傳回或 Run 完成會等待待處理結果完成處理。Run 獨立於輸出觀察繼續執行，但工具工作並不是脫離 Run 存續的公開背景工作階段。

使用非同步工具時，`GetCompletionAsync` 在要求結束後依序彙整中間的獨立說明與最終文字。舊 `StreamAsync` 和 `run.StreamAsync()` 都在各回合文字抵達時通知讀取者；`run.Result` 同樣是 Run 中所有文字的串接結果。

處理常式不接收取消權杖。因此，在取消、逾時、錯誤或提前結束接收輸入的舊串流時，清理仍會等待已啟動的處理常式完成。僅停止讀取 `run.StreamAsync()` 不會停止執行；取消 Run 應使用 `run.Cancel()`、啟動時的權杖或釋放 Run。此整合適用於已註冊的函式處理常式。底層通訊協定見[官方 API 指南](https://developers.openai.com/api/docs/guides/async-tool-calling)。

串流呼叫在確認函式呼叫完整且到達有效回應邊界後才啟動處理常式，接著可在非同步工作執行期間繼續下一回合模型要求。未完成的呼叫事件不會觸發執行。如果模型沒有發起新呼叫但仍有工作未完成，Mythosia 會等待結果後再繼續。為避免未完成的呼叫從歷程記錄中遺失，有待處理呼叫時會停用內容超限後的自動摘要與重試。
