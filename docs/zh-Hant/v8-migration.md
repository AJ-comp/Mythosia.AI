# 遷移至 Mythosia.AI 8

當請求需要獨立設定、使用者需要停止進行中的工作，或應用程式需要一起保存答案、用量與來源時，可使用本次版本。它將以下六項架構改善、提供者與模型更新，以及三輪對抗驗證的修正合併為一次主要版本升級。

僅升級應用程式使用的套件，並重新編譯使用端。Mythosia.AI 會自動引入對應的 Abstractions 相依套件。下表列出已發布基準與本次應搭配使用的相容版本。

| 套件 | 已發布基準 | 發布目標 |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm` 本次從 `1.0.0-preview` 轉為穩定版 `1.0.0`。它保留既有的模型、健康狀態、伺服器版本與指標 API，是不依賴核心 AI 套件的獨立套件。

## 從需求選擇變更

| 需求 | 變更與遷移 |
| --- | --- |
| 傳送前發現圖片選項拼字錯誤 | 以 `ImageQuality`、`ImageBackground`、`ImageOutputFormat` 與 `ImageSize.Pixels(...)` / `ImageSize.Preset(...)` 取代字串。提供者的支援範圍仍有差異。 |
| 準備多個請求時互不影響設定 | 從 `CreateRequest(...)` 開始，保存每次 `With...` 回傳的新建構器。服務層級 setter 仍修改共用預設值。 |
| 非同步工具直接回傳應用資料 | 透過屬性註冊的方法可用 `Task<T>` / `ValueTask<T>` 回傳物件並接收注入的 `CancellationToken`。例外記為失敗，既有字串處理常式仍受支援。 |
| 使用者取消時結束等待 | 將 `cancellationToken` 傳入完成、Run 與支援的 RAG 入口。它停止本機工作及配合取消的工具，不保證遠端停止或撤銷已完成的外部操作。 |
| 一起保存答案、用量與來源 | `AIRun.Result` 回傳 `Task<AIRunResult>`。需要字串時使用 `(await run.Result).Text`；不讀取串流也會收集結果。 |
| 顯示適合所選模型的控制項 | 使用 `request.GetCapabilities()` 或服務、圖片能力查詢。`Supported`、`Unsupported`、`Unknown` 是程式庫的本機資訊，不是即時帳戶權限探測。 |

## 更新呼叫端與自訂提供者

圖片選項型別、`AIRun.Result` 與新增取消權杖的簽章屬破壞性變更。自訂 `IAIService` 及受影響公開多載的 override 必須新增並傳遞權杖；提供者的 `GetCompletionAsync(Message)` override 保留簽章並傳遞 `RequestCancellationToken`。自訂 `AIRun` 必須回傳 `AIRunResult`。GetCompletionAsync 的字串回傳、型別化完成與 `StructuredStreamRun<T>.Result` 的型別化結果維持不變。接收輸入的服務與 RAG StreamAsync 在 v8 仍公開。RunAgentAsync 與 RunAgentStreamAsync 保留相容行為及 obsolete 警告。新的進度、取消與支援模型的追加指令流程使用 Run。

## 一個請求，同時取得結果與選用進度

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

OpenAI 使用像素，Google 與 xAI 使用 `ImageSize.Preset(...)` 指定圖片大小。僅在提供者支援明確格式時變更 Auto，儲存格式依回傳的 `GeneratedImage.MediaType`。

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

註冊工具可如下回傳應用物件。低階 `HandlerWithCancellation` 仍回傳 `Task<string>`，不需要新的物件包裝器。

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

## 提供者更新與驗證範圍

本次還包含已準備的 Fable 5.1、Gemini 3.7/3.8 Flash、Grok 4.6、DeepSeek Flash、Perplexity Agent 整合，以及 OpenAI、Google、xAI 共用的圖片生成和編輯。已移除的模型常數與 Perplexity 端點變更可能需要修改呼叫程式碼；詳見提供者指南及套件發布說明。

用 `PerplexityAgentOptions` 設定研究。Profile、Custom Skill 與 Connector 的實證測試已準備，但需要已註冊帳戶資源才能執行。MCP 維持 preview；釋放開始後呼叫拋出 `ObjectDisposedException`，讀取迴圈已停止時新呼叫拋出 `McpException`，避免無限等待。

三輪對抗驗證加強了請求複製、工具回傳、取消與清理、權杖計算、回應驗證及 MCP 生命週期。第三輪新增 43 個回歸案例，全部 2,703 項測試通過；文件涵蓋 13 種語言。本輪未呼叫真實提供者 API，單元測試通過不代表實測所有依賴帳戶資源的整合。

## 詳細指南

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
