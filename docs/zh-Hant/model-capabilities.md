# 顯示所選模型支援的功能選項

聊天畫面的推理、搜尋、工具與影像選項需要配合目前連線。每個應用程式自行維護模型名稱清單，會重複程式庫規則，並在提供者、協定或部署變更時產生差異。能力快照讓畫面與執行驗證使用相同模型定義。

此 API 屬於Mythosia.AI 8.0.0。快照是程式庫已知支援資訊的不可變本機描述，不是帳戶或伺服器的即時探測。型別位於 `Mythosia.AI.Models.Capabilities`。

## Before / After

Before：應用程式自行維護支援清單。下列清單是應用程式碼，不是程式庫 API。

```csharp
bool showReasoning = mySupportedReasoningModels.Contains(service.Model);
bool showSearch = mySupportedSearchModels.Contains(service.Model);
```

After：檢查已設定的要求，再選擇支援選項。只有最後的完成呼叫會要求模型；查詢能力本身不呼叫 API。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("解釋文件內容。");
AIModelCapabilities capabilities = request.GetCapabilities();

if (capabilities.GetReasoningSupport(ReasoningLevel.High)
    == CapabilitySupport.Supported)
{
    request = request.WithReasoning(ReasoningLevel.High);
}

string answer = await request.GetCompletionAsync();
```

`CapabilitySupport` 區分 `Supported`、`Unsupported` 與 `Unknown`。自訂部署或伺服器選模等資訊不足情況為 `Unknown`，不等於不支援。範例僅在確定支援時啟用額外推理。未知時由應用程式選擇保留預設值或允許嘗試要求等策略。

`request.GetCapabilities()` 讀取建構器擷取的模型、提供者選項與設定檔。`service.GetCapabilities()` 檢查服務預設設定，不消耗下一次呼叫的待用選項。兩者都不發 HTTP、不呼叫內容回呼或執行驗證器、不變更歷程，也不啟動工作。傳回清單也是唯讀快照。 服務查詢也會查看待下一次呼叫使用的功能設定，並保留給實際要求使用。 查詢不會序列化函式預設值或託管工具參數，也不會執行執行用設定準備或預留權杖預算。

能力表示連線可以支援什麼，不表示已啟用哪些選項。提供者、API 協定與模式和模型名稱同樣重要。模型識別反映提供者覆寫與 Qwen/Ollama ID 轉換；未選擇單一模型時可以為 `null`。 範例 Chat UI 根據目前連線及其實際設定（包括已註冊的工具）更新選項，而不只依賴模型目錄。推理模式或工具可用性可能改變取樣支援，因此變更這些設定後應重新查詢。未知支援狀態會與不支援明確區分。

| API | 意義 |
| --- | --- |
| `Reasoning`, `ReasoningLevels`, `GetReasoningSupport(...)` | 共通 `WithReasoning` 的支援情況與等級。 |
| `NativeReasoning`, `NativeReasoningLevels`, `ThinkingBudgetPresets`, `ThinkingToggle` | 提供者原生推理設定與預算候選值。 |
| `Streaming`, `FunctionCalling`, `AsyncFunctionCalling`, `Steering` | 串流、工具、原生非同步工具與執行中追加指示。 |
| `WebSearch`, `FileSearch`, `ReasoningCachePreservation`, `ImageInput`, `StructuredOutput` | 託管搜尋、保留快取的推理變更、影像輸入與結構化輸出。 |
| `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty`, `MaxOutputTokens` | 取樣設定的支援情況與已知輸出權杖上限，未知上限為 null。 |
| `Provider`, `Model` | 提供者與實際傳送的模型識別；未知時可以為 null。 |

`ReasoningLevels` 對應共通 `WithReasoning`；`NativeReasoningLevels` 對應提供者本身設定。`ThinkingBudgetPresets` 是適合 UI 的預算候選值，不是全部允許預算或完整數值範圍。`AsyncFunctionCalling` 指原生非同步工具執行，不是本機函式傳回 `Task` 或平行執行。 `StructuredOutput` 包含透過提示與修復實作的共通型別化輸出 API，不保證提供者原生限制解碼。兩種推理等級清單皆使用 `ReasoningLevel`，預算候選值為整數。

快照不保證帳戶權限或伺服器已就緒，也不會使錯誤選項組合變得有效。執行時仍保留既有驗證與錯誤。追加指示前檢查實際執行的 `run.CanSteer`；模型支援不代表工作仍在進行。

## 分開查詢影像生成

影像生成模型與聊天模型獨立。特定模型使用 `service.GetImageCapabilities(imageModel)`，省略參數則查詢提供者預設影像模型。聊天要求建構器不選擇影像生成模型。使用 `Generation`、`Editing` 與 `Mask` 決定顯示哪些影像操作。

```csharp
using Mythosia.AI.Models.Capabilities;

ImageModelCapabilities capabilities = service.GetImageCapabilities();
bool showEditing = capabilities.Editing == CapabilitySupport.Supported;
bool showMask = capabilities.Mask == CapabilitySupport.Supported;

foreach (var quality in capabilities.Qualities)
    Console.WriteLine(quality);

int? maximumImages = capabilities.MaxImages;
```

`Qualities`、`Backgrounds`、`OutputFormats`、`SizeKinds`、`Resolutions` 與 `AspectRatios` 是具型別的唯讀清單。`MaxImages` 與 `MaxInputImages` 為已知上限，未知則為 null。清單值不保證可以任意組合；既有尺寸、格式、品質、遮罩與模型驗證仍適用。自訂或未知影像模型維持未知，不被判定為不支援。

自訂 `AIService` 能提供可靠定義時，可覆寫 protected `ResolveRequestCapabilities()`，預設傳回 `AIModelCapabilities.Unknown`。不能因部署未列入目錄就判定不支援。`IAIService` 不增加必要成員；查詢方法屬於 `AIService` 及其要求建構器。

如果自訂提供者的設定檔會變更原生模式旗標，請覆寫 `ApplyCapabilityRequestProfile(AIRequestProfile)`，並僅透過 `SetExecutionSetting(...)` 套用解析支援資訊所需的旗標。預設掛鉤不執行任何操作。建構器已擷取共用設定覆寫值；查詢不會呼叫 `ApplyRequestProfile` 或 `ApplyProviderSpecificRequestProfile`。此掛鉤不得執行驗證、回呼、序列化、預算預留，或修改服務及呼叫端擁有的狀態。暫時設定會在查詢結束後還原，覆寫方法擲回例外時也一樣。

[要求設定](request-building.md) · [提供者與影像選項](providers.md) · [Run 控制](execution-api-transition.md)
