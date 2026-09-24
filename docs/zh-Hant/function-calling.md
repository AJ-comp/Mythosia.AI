# 函式呼叫

> GPT-6 Sol/Luna: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型選擇與版本需求](providers.md#gpt-6-sol-luna)

只需完整答案和停止按鈕時，將 `cancellationToken` 傳給 `GetCompletionAsync`。進度事件或支援的中途追加指令使用 Run。參閱[取消回答](completions.md#completion-cancellation)。

若要分離每個請求的設定並衍生多個版本，請使用[請求建構器](request-building.md)。先呼叫`CreateRequest(...)`，再串接`With...`。服務屬性與服務上的fluent方法維持原有行為。

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

[Claude Fable 5.1](fable-5-1.md) 的進度更新、單回合指令與 thinking 綁定診斷從 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 開始提供。Mythos 5.1 需要邀請存取，兩個模型都拒絕強制工具選擇。

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

<a id="tool-execution-contract"></a>

## 非同步工具直接回傳物件，並回應取消

讀取檔案或資料庫的工具通常在非同步I/O完成後回傳物件。使用者按下停止時，取消也應傳遞到仍在執行的操作。同步函式早已支援直接回傳物件；這次更新讓非同步回傳保持一致，並將例外正確記錄為失敗。

Before：非同步函式以前需要自行序列化結果。直接回傳`Task<FileResult>`會遺失物件，只傳回`"Success"`。

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed class FileToolsBefore
{
    [AiFunction("read_file", "讀取文字檔案")]
    public async Task<string> ReadFileAsync(string path)
    {
        string text = await File.ReadAllTextAsync(path);
        return JsonSerializer.Serialize(new { Path = path, Text = text });
    }
}
```

After：直接回傳物件，並把程式庫注入的取消權杖傳給I/O操作。應用程式不必實作新的結果包裝型別或介接器。

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "讀取文字檔案")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

`[AiFunction]`註冊支援一般物件、`Task<T>`和`ValueTask<T>`，非字串值會轉換成JSON。`string`、`Task<string>`和`ValueTask<string>`保持原始文字，不額外加上JSON引號。沒有回傳值的`Task`和`ValueTask`也會等待完成。原有同步物件回傳繼續適用。 null回傳值會變為`"Done"`；沒有回傳值的`Task` / `ValueTask`完成後傳遞`"Success"`。

即使回傳型別宣告為`Task`或`object`，實際回傳的`Task<T>`也會等待完成並按相同規則轉換。以`object`回傳的`ValueTask<T>`和沒有回傳值的`ValueTask`也會等待完成，每個`ValueTask`只取用一次。

`CancellationToken`參數由程式庫注入，不會出現在提供給模型的參數結構中。透過服務或請求建構器的`WithFunctions(...)`、`WithStaticFunctions<T>()`註冊即可。

註冊時會拒絕`async void`工具方法。請回傳`Task`或`ValueTask`，以便執行器等待完成、觀察錯誤並完成取消後的清理。

```csharp
using Mythosia.AI.Extensions;

await using var run = await service
    .CreateRequest("讀取report.txt並摘要。")
    .WithFunctions(new FileTools())
    .StartRunAsync(cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

`run.Cancel()`、取消傳給`StartRunAsync`的權杖，或釋放作用中的run，都會傳遞給支援取消的本機工具。函式必須使用該權杖；無法強制終止忽略權杖的程式碼。尚未開始的呼叫會略過並記錄取消結果，已經開始的函式仍會等待，以保持呼叫與結果成對。執行取消後run保持取消狀態，不會再啟動下一輪模型請求。

Run啟動失敗或釋放MCP連線時，即使取消回呼擲回例外，也會繼續嘗試清理工作階段或傳輸連線。原始錯誤與清理錯誤都會保留，必要時透過`AggregateException`一起傳遞。 並行非同步等待`McpConnection.DisposeAsync()`的呼叫會等待同一次清理完成。先關閉傳輸連線，讓依賴連線關閉的讀取結束，再等待讀取迴圈退出。

為避免關閉期間較晚送入的工具呼叫持續等待，連線開始釋放後，新的 `InitializeAsync`、`RefreshToolsAsync` 和 `CallToolAsync` 操作會以 `ObjectDisposedException` 被拒絕。如果回應的請求 ID 相符但本文格式無效，則略過該回應並保留請求的等待登記；後續有效回應、呼叫端取消或連線清理仍可結束該呼叫。 如果伺服器關閉串流或傳輸讀取失敗導致讀取已結束，新操作會以 `McpException` 失敗，而不是等待無法到達的回應；請建立新連線後繼續。

實際失敗時直接擲回例外。執行器將其記錄為`FunctionCallResult.IsError = true`，不會誤當成正常的`"Error: ..."`字串。函式刻意回傳的字串仍是正常結果。取消的工具結果同時設定`IsCancelled = true`和`IsError = true`。

以程式碼註冊時，使用接收兩個參數的`WithHandler`多載：

```csharp
using System.IO;
using Mythosia.AI.Builders;

var readText = FunctionBuilder.Create("read_text")
    .WithDescription("讀取文字檔案")
    .AddParameter("path", "string", "檔案路徑", required: true)
    .WithHandler(async (args, token) =>
        await File.ReadAllTextAsync(args["path"].ToString()!, token))
    .Build();
```

既有單參數字串處理常式繼續支援。直接建立定義時，可將`Func<Dictionary<string, object>, CancellationToken, Task<string>>`指定給`HandlerWithCancellation`。設定`Handler`或`HandlerWithCancellation`都會取代同一個處理常式，不會註冊兩次執行。此低階API仍回傳字串；物件自動序列化由方法註冊負責。

這是本機.NET函式的回傳值與取消處理，不依賴提供者原生的`AllowAsync`功能。僅停止`run.StreamAsync(token)`讀取只會停止觀察，run繼續執行。請參閱[Run指南](execution-api-transition.md)與[提供者通訊協定](https://developers.openai.com/api/docs/guides/async-tool-calling)。

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

Mythosia 在 GPT-6 Astra / Sol / Luna 的 Responses API 中傳送 `async: true`。對於不支援的模型和 API，會省略此欄位並等待同一個處理常式的結果，不會變更使用者設定的 `AllowAsync` 值。供應商也必須將實際呼叫標示為非同步（`FunctionCall.IsAsync`）；開啟許可不代表一定會以非同步方式執行。

`WithFunctionAsync` 用於註冊 .NET 非同步處理常式，`FunctionExecutionMode.Parallel` 控制本機處理常式的排程，兩者都不會自動開啟此選項。`AllowAsync` 允許模型在函式結果傳回前繼續工作。 `FunctionExecutionMode` 仍控制一般呼叫。允許的非同步工作即使在 `Sequential` 模式下也可重疊執行，並在獨立工作池內共用 `MaxConcurrency` 上限。

執行中的工具工作由 `GetCompletionAsync`、接收輸入的舊 `StreamAsync`，或 `StartRunAsync` 傳回的 `AIRun` 管理；完成後使用原始呼叫 ID 傳送結果。成功的最終傳回或 Run 完成會等待待處理結果完成處理。Run 獨立於輸出觀察繼續執行，但工具工作並不是脫離 Run 存續的公開背景工作階段。

使用非同步工具時，`GetCompletionAsync` 在要求結束後依序彙整中間的獨立說明與最終文字。舊 `StreamAsync` 和 `run.StreamAsync()` 都在各回合文字抵達時通知讀取者；`(await run.Result).Text` 同樣是 Run 中所有文字的串接結果。

本機工具可透過`Task<T>` / `ValueTask<T>`回傳物件，並接收程式庫注入的`CancellationToken`。`run.Cancel()`或啟動權杖的取消會傳遞給配合取消的工具，僅停止串流讀取則不會。例外會記錄為失敗；取消時略過排隊呼叫，清理仍會等待已啟動且忽略權杖的工具。請參閱[結果、錯誤與取消](function-calling.md#tool-execution-contract)。

串流呼叫在確認函式呼叫完整且到達有效回應邊界後才啟動處理常式，接著可在非同步工作執行期間繼續下一回合模型要求。未完成的呼叫事件不會觸發執行。如果模型沒有發起新呼叫但仍有工作未完成，Mythosia 會等待結果後再繼續。為避免未完成的呼叫從歷程記錄中遺失，有待處理呼叫時會停用內容超限後的自動摘要與重試。

Perplexity: [控制研究與工具](perplexity.md).
