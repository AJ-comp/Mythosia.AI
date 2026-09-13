# 各供應商特性

> `CreateRequest`範例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和共通請求功能的舊7.1版本不包含建構器；舊套件可繼續使用原有服務多載。

<a id="image-options-migration"></a>
## 圖片選項型別遷移

透過列舉自動完成選擇品質與格式，並區分精確像素和解析度級距。這能減少字串拼字錯誤，避免指定像素尺寸被悄悄轉成其他解析度。

這是Mythosia.AI 8.0.0 的破壞性變更：`Quality`、`Background`、`OutputFormat`改為列舉，`Size`改為`ImageSize`，移除請求中獨立的`AspectRatio`屬性。`OutputFormat`預設值改為`ImageOutputFormat.Auto`。生成與編輯方法維持不變。

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)`要求精確尺寸。`Preset(resolution, aspectRatio)`指定解析度級距及比例，由供應商決定實際像素。沒有尺寸要求時使用`ImageSize.Auto`。只有應用程式接受近似尺寸時，才應將像素請求改成預設級距。

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

未定義的列舉值與供應商或模型不支援的組合會在HTTP請求前拒絕。列舉成員並不表示所有模型都支援。Google品質僅支援`ImageQuality.Auto`，xAI支援`Auto`、`Low`、`Medium`。

`EditImagesAsync`傳回`Task`後即可重用輸入緩衝區。已開始的請求會獨立保留影像位元組，包括OpenAI的遮罩位元組；之後修改原始`ImageInput.Data`陣列不會改變上傳內容。

為避免將中斷的輸出儲存為完整影像，Google生成和編輯要求所有傳回的候選結果都以`finishReason: STOP`結束。只要有一個遭封鎖、未完成或缺少該結束狀態，整個呼叫就會擲回`AIServiceException`。 內嵌base64資料或影像MIME資訊缺漏、無效時，整個呼叫也會失敗，不會猜測為PNG。這些檢查不驗證實際檔案位元組是否與宣告的影像格式一致。

## OpenAI (OpenAIService)

> GPT-6 Astra 支援與非同步工具呼叫從 `Mythosia.AI` 7.1.0 開始提供，共用型別包含在 `Mythosia.AI.Abstractions` 3.1.0 中。

天氣查詢較慢時，模型仍可先介紹不依賴天氣結果的一般旅行用品。模型原生非同步工具呼叫用於在這種等待期間繼續獨立工作；依賴查詢結果的判斷仍應等結果傳回後再進行。

透過 `FunctionDefinition.AllowAsync = true` 或 `FunctionBuilder.WithAsync()`，可選擇允許 GPT-6 Astra 在 Responses API 中非同步呼叫工具。預設值為 `false`；不支援的模型仍等待同一個處理常式的結果。範例與請求生命週期請參見[函式呼叫指南](function-calling.md)。

若要跨供應商設定推理等級，並使用最新資訊或已索引文件作為依據，請參閱[推理與搜尋指南](reasoning-and-search.md)，其中列出模型支援、快取保留及組合限制。

### 推理強度

GPT-6 Astra 和 GPT-5.1–5.6 支援調整推理強度，以平衡回應速度和分析深度：

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6：Sol 是旗艦模型；Terra 與 Luna 是更經濟的選擇。
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra 預設使用 Responses API，函式呼叫也必須使用此 API。`Auto` 對應函式庫預設值 `Medium`，不支援 `None` 和 `Minimal`。`AIRequestProfile.DisableReasoning = true` 會在 `Standard` 模式下將推理強度設為 `Low` 並省略推理摘要。選擇 `Gpt6ReasoningMode.Pro` 即可透過同一個模型 ID `gpt-6-astra` 使用 Pro 模式。

### 文字轉語音

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "你好，世界！",
    voice: "alloy",
    model: "tts-1"
);
await File.WriteAllBytesAsync("output.mp3", audio);
```

### 語音轉文字（轉錄）

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");
string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "zh"
);
```

`TranscribeAudioAsync` 使用 `gpt-transcribe`，公開簽章保持不變。

### 圖像生成

#### GPT Image 2.5

需要快速製作視覺草稿時選擇 Flare，需要精確執行細節編輯指令時選擇 Sunburst。兩者皆透過現有的 `IImageGenerationService` 支援影像生成與編輯；選擇影像模型不會改變聊天模型。

| 模型 | 適用情境 |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | 快速、高品質的日常影像生成。 |
| `AIModels.OpenAI.GptImage2_5Sunburst` | 重視編輯精度的影像生成與修改。 |

在 `ImageGenerationRequest.Model` 或繼承它的 `ImageEditRequest.Model` 中明確指定。OpenAI 預設仍為 `AIModels.OpenAI.GptImage2`。別名是 `gpt-image-2.5-flare` 和 `gpt-image-2.5-sunburst`；要固定到 2026 年 9 月 8 日的快照，請使用 `GptImage2_5Flare_260908` 或 `GptImage2_5Sunburst_260908`，對應 ID 以 `-2026-09-08` 結尾。

先用 Flare 生成草稿：

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "日出時的玻璃亭，建築概念圖",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

再用 Sunburst 編輯生成的影像：

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "保持亭子的設計，移除周圍景物，並將背景改為透明。",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

這兩個模型及其快照的 `Quality` 支援 `Auto`、`Low`、`Medium`、`High`、`XHigh`、`Max`。草稿可用較低品質，最終素材可比較較高等級。`OutputFormat` 支援 `Auto` / `Png`、`Jpeg`、`WebP`；`OutputCompression` 僅適用於 JPEG/WebP，範圍為 0–100。`Transparent` 背景需要 PNG/WebP，`Count` 為 1–10。

`Size`使用`ImageSize.Auto`或`ImageSize.Pixels(width, height)`：兩邊是16的倍數、比例1:3–3:1、單邊最多3840像素、總面積655360–8294400像素。超過2560×1440的尺寸屬實驗性功能。OpenAI拒絕`Preset`。

編輯接受 1–16 張非空 JPEG/PNG/WebP 參考影像，每張小於 50 MiB。選用遮罩須為小於 50 MiB 的 PNG/WebP，與第一張參考影像的格式和像素尺寸一致，並有 alpha 通道。程式庫驗證 MIME 類型與位元組長度，供應商驗證像素尺寸與 alpha。

這些範例使用現有 Image API 的位元組回傳與 multipart 編輯路徑。本次整合不公開 Responses `image_generation` 工具、部分影像串流或 `input_fidelity`。請讀取結果中的 `GeneratedImage.Data` 和 `MediaType`。

參閱官方[影像指南](https://developers.openai.com/api/docs/guides/image-generation)、[Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst) 和 [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare) 模型頁面。

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md) 的進度更新、單回合指令與 thinking 綁定診斷從 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 開始提供。Mythos 5.1 需要邀請存取，兩個模型都拒絕強制工具選擇。

### Token 計數（原生 API）

Anthropic 的實作呼叫官方 `messages/count_tokens` 端點，回傳**精確**的 Token 數量：

```csharp
uint tokens = await service.GetInputTokenCountAsync("你的提示詞");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

審查長文件或執行多輪工具任務時，可以透過現有 Google 適配器選擇 Gemini 3.7 Flash 或 3.8 Flash。支援從 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 開始，服務預設模型仍為 Gemini 3.6 Flash。

### 思考深度

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("比較滾動部署和藍綠部署，包括回滾風險。")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

初步檢查可使用 `Low`，複雜審查可使用 `High`；更多推理可能增加延遲和 token 用量。兩種模型都支援 `Low`、`Medium`、`High`，不支援 `Minimal` 或 `None`。`GeminiThinkingLevel.Auto` 不傳送覆寫值，3.8 的供應商預設值為 `Medium`。`ThinkingLevel` 設定服務基準，`WithReasoning(...)` 只覆寫一個邏輯請求。適配器不傳送這兩種模型的 `temperature`、`topP`、`topK`。供應商上限為輸入 1,048,576 token、輸出 65,536 token。 [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

---

## xAI (XAIService)

### 為任務選擇推理強度

快速起草時可以降低推理強度；對於回答品質比回應速度更重要的複雜審查，可以投入更多推理。若要使用新增的 `XHigh` 等級，請明確選擇 Grok 4.6。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("比較滾動部署與藍綠部署，包括故障復原步驟。")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6 支援 `Low`、`Medium`、`High` 和 `XHigh` (`GrokReasoning.XHigh`)。`Auto` 省略 `reasoning_effort`，使用供應商的預設值 `High`；不能用 `None` 關閉推理。更多推理可能增加延遲和權杖用量。為保持相容，`XAIService` 預設模型仍為 Grok 4.5。4.5 支援 `Low` 至 `High`，4.3 支援 `None` 至 `High`；在這些舊模型上要求 `XHigh` 會在傳送前被拒絕。

`WithGrokReasoning(...)` 和現有的 `WithGrokParameters(...)` 設定服務的預設推理組態。在 Grok 4.6 上，共用的 `WithReasoning(...)` 只覆寫一個邏輯請求，包括其工具輪次及結構化輸出修復，之後恢復預設組態。對於這個始終推理的模型，內部 `DisableReasoning` 設定使用 `Low`。共用選項尚未整合 xAI 的快取保留更新及託管網頁、檔案搜尋。

啟用 `new StreamOptions().WithReasoning()` 觀察選項後，Grok 4.6 可能透過 `StreamingContentType.Reasoning` 傳回供應商產生的推理摘要。摘要是選用輸出，不代表完整內部推理。Run 使用相同的觀察選項；更改串流顯示設定不會改變要求的推理強度。

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

### Grok Imagine Image 2.0

需要把商品描述變成視覺草稿，或組合不同照片中的主體與背景時，可以使用影像生成和編輯。`XAIService` 與 OpenAI、Google 共用 `IImageGenerationService`，從 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 起支援。

獨立的預設影像模型為 `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`)。影像請求不會變更所選聊天模型，也不會附加到聊天紀錄中。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "日出時的玻璃展亭，寬幅構圖",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

編輯時，按提示詞引用的順序傳入現有影像的位元組：

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "將圖1中的主體放入圖2的場景。",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

每個 `GeneratedImage.Data` 包含解碼後的影像位元組；請依 `MediaType` 選擇副檔名。配接器要求內嵌 base64 輸出，不下載供應商託管的影像 URL。`Count` 支援1–10張輸出；編輯支援1–5張 JPEG、PNG 或 WebP 參考影像。

xAI使用`ImageSize.Auto`或`ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`，解析度支援`Auto`、`OneK`、`TwoK`，比例須為模型支援的值。由於無法要求精確尺寸，`Pixels(...)`會被拒絕。

xAI僅支援新的共用預設值`ImageOutputFormat.Auto`。它無法選擇輸出編碼，因此明確的`Jpeg`、`Png`、`WebP`會在傳送前拒絕。請依`GeneratedImage.MediaType`選擇副檔名，程式庫不會轉碼。品質支援`ImageQuality.Auto`、`Low`、`Medium`，背景僅支援`ImageBackground.Auto`；不支援明確壓縮或獨立的`Mask`。

Google使用`ImageSize.Auto`或模型支援的`ImageResolution.Auto`、`FiveTwelve`、`OneK`、`TwoK`、`FourK`之`Preset`。輸出支援`ImageOutputFormat.Auto`或明確的`Jpeg`，拒絕`Png`/`WebP`。Google和xAI拒絕`Pixels`，OpenAI支援`Auto`/`Pixels`並拒絕`Preset`。參見[遷移範例](#image-options-migration)。

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

需要快速回答後深入審查，或解釋圖表、截圖時，可使用 DeepSeek Flash。`AIModels.DeepSeek.Flash` (`deepseek-flash`) 選擇2026年9月10日發布、原生支援視覺理解的 V4.1 Flash。沿用補全、串流、Run、函式呼叫和 RAG API，從 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0 起支援。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("比較滾動部署與藍綠部署，並分析回復風險。");

await using var run = await deepseek
    .CreateRequest("檢查上述比較中的假設。")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

程式庫的 `ThinkingEnabled` 預設仍為 `false`。`WithDeepSeekReasoning(...)` 開啟推理並設定持續生效的 `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`)；原生 `Auto` 省略 effort，使用供應商預設 `High`。共用 `WithReasoning(...)` 僅覆寫一個邏輯請求及工具輪次：`None` 關閉推理，`Minimal`/`Low` 對應 `Low`，`Medium`/`High`/`XHigh` 對應 `High`，`Max` 對應 `Max`。共用 `Auto` 保留目前預設設定。增加推理可能提高回應時間和權杖用量。 只修改 `ReasoningEffort` 屬性不會開啟推理。

透過 `WithFunction(...)` 註冊本地函式，讓模型透過應用程式碼查詢資料或執行操作。推理與非推理均支援工具，但推理模式拒絕強制/必選工具，應使用自動選擇。配接器保留原生 `reasoning_content` 和呼叫 ID，供後續工具輪次重播。Run 與原有串流 API 啟用 `StreamOptions.WithReasoning()` 後，以 `StreamingContentType.Reasoning` 輸出推理；觀察選項本身不會開啟推理。用量包含供應商回報的快取和推理權杖。 自動上下文復原使用共用串流迴圈。工具需要先前的原生推理歷史時，為保留歷史會阻止自動壓縮，並傳遞超限錯誤。

圖表或截圖可透過現有訊息型別傳入影像位元組：

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("解釋這張圖表的趨勢，並指出不清楚的標籤。"),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent` 接收 JPEG、PNG、GIF、WebP 位元組或由供應商取得的公開 HTTP(S) URL。範例使用使用者訊息。目前 API 也接受工具訊息中的影像，但註冊函式處理器仍透過共用結果契約回傳文字。手動建立 `ActorRole.Function` 影像訊息時，須透過 `MessageMetadataKeys.FunctionId` 提供相符的呼叫 ID（wire 中的 `tool_call_id`）。影像大小和總量限制請參閱最新官方視覺指南。未整合 `file_id`、Files API 或影像生成。

供應商標示上下文1M、輸出最多384K (`393216`)權杖；程式庫預設請求預算仍為8,000。推理模式省略 temperature/penalty，`top_p` 至少0.95；非推理模式省略 `top_p`。配接器使用 Chat Completions，未整合 Responses、託管搜尋、`CachePreservation.Required`、原生非同步工具或 `SteerAsync`。本地 RAG 和一般工具輪次仍可使用。

`V4Flash`、`Chat`、`Reasoner` 保留原始 wire ID，標記為僅警告的 obsolete 常量。供應商暫時將已退役的 `deepseek-v4-flash` 路由至 V4.1 Flash；程式庫不會改寫常量。新程式碼請明確選擇 `Flash`。`UseReasonerModel()` 選擇 Flash 並啟用 `High` 推理。

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

當回答需要根據最新資訊，並讓讀者能夠核對來源時，可以使用 Perplexity。`PerplexityService` 呼叫 Agent API；獨立搜尋與嵌入則用於替自行選擇的回答模型建立檢索能力。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("比較最新的電池回收方法，並註明來源。");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

研究預設、本機函式與託管工具連接，以及長時間背景工作的管理，請參閱 [Perplexity 指南](perplexity.md)。可以繼續使用現有的 completion、串流、Run 與引用 API。

此版本將服務遷移至 `/v1/agent`。`AIModels.Perplexity.Sonar` 現在選擇 `perplexity/sonar`。提供者已宣布舊 Sonar 端點將於 2026 年 9 月 27 日停止服務，因此現有 Sonar 整合需要遷移。 [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## 阿里巴巴 / 通義千問 (QwenService)

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

可用模型：`QwenMax`、`QwenPlus`、`QwenTurbo`、`Qwen3` 及其變體。

建立服務時，使用 `EndpointPlatform` 選擇相容端點：

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[用共用支援定義建立模型功能選項](model-capabilities.md).
