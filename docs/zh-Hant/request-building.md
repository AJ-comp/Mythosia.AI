# 讓每個請求的設定互相獨立

> Claude Sonnet 5.5: 需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0。[設定與移轉](providers.md#claude-sonnet-55)

> GPT-6.1 Sol: 需要 Mythosia.AI 8.2.0 / Abstractions 4.2.0。[模型選擇與遷移](providers.md#gpt-61-sol)

> Grok 4.7: 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。 [模型選擇、推理與處理速度](providers.md#grok-47)

文件摘要可能需要較低的Temperature，創意草稿則需要較高的值。準備草稿不應悄悄改變已經準備好的摘要請求。不同呼叫需要不同設定，或需要從基礎請求衍生多個版本時，請使用`CreateRequest`。

需要同時取得完整答案、用量與來源時，使用 `await run.Result` 傳回的 `AIRunResult`；字串位於 `result.Text`，不必讀取串流。這是Mythosia.AI 8.0.0 的 API 變更；`GetCompletionAsync` 與 `StructuredStreamRun<T>.Result` 的傳回型別保持不變。 [Run 結果與移轉](execution-api-transition.md#run-result).

只需完整答案和停止按鈕時，將 `cancellationToken` 傳給 `GetCompletionAsync`。進度事件或支援的中途追加指令使用 Run。參閱[取消回答](completions.md#completion-cancellation)。

> `CreateRequest`範例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和共通請求功能的舊7.1版本不包含建構器；舊套件可繼續使用原有服務多載。

## Before：共用服務設定

現有服務的`WithTemperature`會修改服務並傳回同一個執行個體。以下兩個變數參考同一個服務，因此後設定的值會套用至兩者。這些舊方法仍可用於設定服務預設值。

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("請解釋這份文件。"); // 0.8
```

## After：從獨立請求衍生

`CreateRequest`會保存服務預設值。建構器的每個`With...`都傳回新的建構器，不修改原物件。執行時直接使用該請求保存的設定，不會暫時覆寫服務預設值。

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("請解釋這份文件。");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// 使用0.2；creative和服務預設值保持不變。
```

請使用傳回的建構器。只呼叫`basis.WithTemperature(0.2f);`並捨棄傳回值，不會改變`basis`。

建構器驗證輸入而不會默默修正：`WithTemperature`範圍為0–2，`WithTopP`為0–1，懲罰為−2–2，拒絕NaN和無限值。Token數、回合數、並行數和指定的逾時必須為正。無效輸入擲回`ArgumentException` / `ArgumentOutOfRangeException`。舊服務的Temperature方法仍會限制到有效範圍。

## 各物件的職責

`AIService`管理提供者連線、預設值和現有對話狀態。公開型別`Mythosia.AI.Builders.AIRequestBuilder`提供fluent API，內部型別`AIRequest`將確定的輸入與設定交給執行層。使用者不必呼叫`Build()`。`AIRequest`也不是回答結果：`GetCompletionAsync()`傳回`Task<string>`，`StartRunAsync()`傳回`Task<AIRun>`。

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## 以相同設定啟動可控制的執行

只需要完整回答時使用`GetCompletionAsync()`。需要顯示進度或向支援的執行追加指示時，使用`StartRunAsync()`。輸入傳給`CreateRequest`，不再傳給建構器的執行方法。`run.StreamAsync()`繼續觀察該執行，`run.SteerAsync(...)`的模型支援條件保持不變。

```csharp
await using var run = await service
    .CreateRequest("請解釋這份文件。")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

本機工具可透過`Task<T>` / `ValueTask<T>`回傳物件，並接收程式庫注入的`CancellationToken`。`run.Cancel()`或啟動權杖的取消會傳遞給配合取消的工具，僅停止串流讀取則不會。例外會記錄為失敗；取消時略過排隊呼叫，清理仍會等待已啟動且忽略權杖的工具。請參閱[結果、錯誤與取消](function-calling.md#tool-execution-contract)。

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## 重用設定檔與上下文

`WithProfile`複製既有的`AIRequestProfile`，`WithContext`複製`AIRequestContext`。之後修改原物件不會影響已準備的請求。建構器可設定取樣、系統指示、無狀態模式、函式呼叫原則，以及支援的推理、網頁和檔案搜尋。提供者能力檢查仍然適用，建構器不會讓未支援的選項變得可用。

`WithFunctions(params FunctionDefinition[])`向請求加入複製的函式定義。匯入`Mythosia.AI.Extensions`後，也可用`WithFunctions(toolInstance)`和`WithStaticFunctions<T>()`註冊既有的特性函式。在`CreateRequest`之前註冊的是服務預設值，之後註冊的只用於請求。服務待處理的下次呼叫功能與原則在`CreateRequest`時被擷取並消耗；需要重用時請保留傳回的建構器。

```csharp
var request = service
    .CreateRequest("請將這個問題改寫為適合搜尋的形式。")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\n請保留原意。"
    });

string rewritten = await request.GetCompletionAsync();
```

Claude 會統一決定模型、請求用途、推理和 thinking 綁定設定。對於 `RequestProfiles.Summarization` 或 `RequestProfiles.QueryRewrite` 等輔助設定，只有在 `DisableReasoning = true`、用途不是 `Default` 且實際請求為無狀態時，才省略繼承的綁定原則。這可避免沒有對話前綴需要保留的請求因繼承原則而重新啟用推理或遭到拒絕。允許關閉推理的模型會關閉推理；持續推理的 Opus 5.5、Fable 5.1 和 Mythos 5.1 使用 `Low` 並省略可讀 thinking，Sonnet 5.5 則使用 high effort 的 `between_tools`。

完成、串流、結構化輸出和 Run 使用同一請求準備流程：擷取設定，執行一次實際的設定檔處理，再驗證最終的通用選項和供應者原生選項。這些步驟先於自動摘要、新增輸入至歷史及建立傳輸連線，因此自訂供應者的設定檔覆寫也會參與實際驗證。Claude 實際使用的手動 `ThinkingBudget` 若達到或超過模型輸出上限，也會在此階段被拒絕；有效設定檔和通用推理設定的優先順序不變。

應用程式發起的呼叫會啟動獨立的邏輯請求，包括從 `SystemMessageProvider` 或工具回呼發起的一般呼叫，以及重複使用同一個 `AIRequestProfile`、`Message` 的呼叫。物件重用不代表執行共用。框架內部委派、工具輪次、重試和格式修復會延續原請求，其設定檔只套用一次。一般子請求取得自己的選項和服務預設值，建構器保留已擷取的設定。成功、失敗或取消後均還原父請求的執行狀態。轉送框架呼叫的提供者覆寫方法遵循[下方的配接器規則](#provider-request-adapters)。

內建供應者保留內建輸入內容的獨立副本。重複使用 `Message` 發起新呼叫時，會套用本次的內容脈絡和輪次指令，不會改寫已接受的歷史。自訂內容和不支援的中繼資料物件仍由擁有者管理。這不保證同一對話的並行呼叫安全。

`StartRunAsync` 傳回後，Run 仍保有最終設定的獨立副本。還原呼叫端的設定檔不會改變執行中的 Run，執行用的設定檔掛鉤仍只呼叫一次。

無狀態輔助請求使用獨立對話，不繼承父請求的輸出結構描述、託管工具或一次性選項。隔離不會略過原生選項驗證，OpenAI 和 Perplexity 的 Run 也遵循此規則。父請求的設定、訊息、`CurrentSummary` 和觀測資訊保持不變。有狀態請求繼續執行綁定與對話檢查。現有公開 API 不變。

程式庫自動產生的對話摘要也會排除父請求的 `SystemMessageProvider` 回呼和請求上下文，避免繼承的 `RequestMessageOverride` 取代內部摘要提示詞。應用程式主動發起的請求仍正常套用動態上下文，包括明確要求模型摘要文字的請求。

無狀態請求也會略過父對話的自動摘要。既有 `GetCompletionAsync(string, profile)` 多載與 `Message` 多載及請求建構器的行為一致，保留父對話的 `CurrentSummary` 和訊息。有狀態請求繼續使用原有的自動摘要行為。

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

<a id="provider-request-adapters"></a>

## 在自訂提供者中轉送請求

框架呼叫虛擬提供者配接器時，配接器對相應基底類別入口的第一次呼叫會延續已準備的請求，即使覆寫方法替換了輸入 `Message` 也是如此。建構器已擷取的選項和已套用的設定檔在轉送時仍然保留。預設的回呼式串流配接器也會延續同一個已準備的請求。

介接器可以轉送修改後的 `AIRequestProfile`：相同值不會重複套用，變更值依已擷取的設定取代先前的設定層，並在自動摘要和傳送前重新驗證。因此，切換到無狀態模式不會先摘要父對話。OpenAI 無狀態輔助請求不檢查無關的保留歷程，也不會清除父對話的保護狀態。

替換轉送的設定檔時，會保留請求內稍後新增、移除或修改的工具、原則變更及明確設定的值，即使重新指定相同的純量值也一樣。更改其他設定欄位不會恢復介接器已移除的工具，也不會重新讀取服務預設值。

對於函式庫無法了解內部結構的自訂設定物件，介接器應透過 `SetExecutionSetting` 替換其值，而不是修改內部欄位。函式庫不會為了追蹤設定變更而檢查任意應用物件或呼叫其序列化器。

Claude 壓縮保留 `RequestMessageOverride` 和 `AdditionalMessages` 中的呼叫／結果相依關係，包括伺服器工具，並保護 Mythos 5.1 綁定的 thinking 前綴。平行工具的舊格式記錄在一般歷程和附加訊息中都只合併一次，且保留每筆記錄的歸屬。

如果在轉送之前，另一個無關的輔助呼叫也使用相同的基底類別入口，就會產生歧義：框架無法判斷它是否正在延續原請求。請使用 protected 方法 `BeginIndependentRequestScope()` 的範圍包住輔助呼叫及其 `await`；對於串流呼叫，此範圍必須涵蓋整個列舉過程。輔助請求從服務預設值開始，處置範圍會還原外層的設定、功能、上下文及待處理的委派。上下文或工具回呼中的一般巢狀呼叫本身已獨立，不需要此範圍。

例如，具體提供者的子類別可以先改寫文字，再轉送請求：

```csharp
public override async Task<string> GetCompletionAsync(
    Message message, AIRequestProfile? profile = null,
    AIRequestContext? context = null, CancellationToken cancellationToken = default)
{
    string rewritten;
    using (BeginIndependentRequestScope())
    {
        rewritten = await base.GetCompletionAsync(
            new Message(ActorRole.User, message.Content),
            RequestProfiles.QueryRewrite,
            cancellationToken: cancellationToken);
    }

    var replacement = new Message(message.Role, rewritten);
    return await base.GetCompletionAsync(replacement, profile, context, cancellationToken);
}
```

此範圍隔離請求的執行狀態，不會隔離對話歷史或讓服務支援並行使用。範例使用無狀態的 `QueryRewrite` 設定檔，避免輔助請求加入父對話。

Claude 壓縮會檢查以正規傳輸格式保留的歷史，包括透過 `AIRequestContext.AdditionalMessages` 加入的帶簽章 thinking。預設的 thinking 綁定會保護該前綴，防止自動或明確要求的摘要對其進行壓縮。明確指定 `ClaudeThinkingPrefixMismatchBehavior.DropBlock` 可在支援時允許壓縮；其他對話約束仍然適用。

## 哪些內容被複製，哪些狀態仍然共用

共通設定和提供者預設值在呼叫`CreateRequest`時保存。之後變更服務預設值不會改變已準備的請求。內建訊息內容、支援的選項集合、設定檔、上下文和原則都會複製。函式處理常式、動態上下文回呼與自訂訊息內容保留參考。請勿修改自訂內容，並注意委派仍可讀取外部狀態。動態上下文回呼在執行時呼叫。

擷取完成後，即使釋放原始 `JsonDocument` 或修改原始 `JsonNode`，請求中繼資料和函式呼叫引數中保存的 JSON 值也不會改變；每次執行都使用獨立複本。工具結構描述的 `Items` 鏈存在循環或巢狀超過 64 層時，會在擷取階段（`CreateRequest` 或 `WithFunctions`）擲回 `ArgumentException`，以便在執行前正常回報無效結構描述，而不是耗盡處理程序堆疊。

複製也會保留陣列的維度和起始索引，以及標準 `Dictionary<,>`、`SortedDictionary<,>` 和 `SortedList<,>` 的索引鍵比較規則。因此，原本不區分大小寫的索引鍵查找在請求中仍然如此。空的 `default(JsonElement)` 值（`Undefined`）也會原樣保留。未知的自訂中繼資料物件仍保留參考，由其擁有者負責維持不變或協調存取。

標準`ReadOnlyCollection<T>`和`ReadOnlyDictionary<TKey, TValue>`在強型別陣列或字典中也會保留原始型別。複製支援的底層集合時，會保留唯讀檢視、共用參考和循環參考。`Hashtable`及非泛型`SortedList`也會保留索引鍵比較規則。

建構器不是獨立對話。它使用執行時服務的作用中對話記錄，建立時不會凍結記錄。具狀態的呼叫仍會更新共用對話。不想讀取或累積記錄時，請使用`WithStatelessMode()`。每個服務只允許一個作用中Run的限制保持不變。請求設定獨立不代表同一服務支援平行執行；獨立對話的並行工作應使用不同服務。

## 既有呼叫與擴充

`GetCompletionAsync`與既有服務入口繼續受支援。`BeginMessage()` / `MessageChain`保留原來可變的訊息建構方式，執行層使用新的請求路徑。需要分支重用設定時請選擇`CreateRequest`。建構器API屬於`AIService`及其提供者實作，不會為`IAIService`新增必要成員。只使用抽象介面或RAG包裝器的程式碼繼續使用既有設定檔、上下文和執行API。

[用共用支援定義建立模型功能選項](model-capabilities.md).

<a id="inference-speed"></a>

## 按工作選擇處理速度

使用者正在等待的請求可選擇付費低延遲處理，背景報告可使用一般處理。 `WithSpeed` 保持模型和推理層級，只選擇處理模式。 需要 Mythosia.AI 8.1.0 / Abstractions 4.1.0。

`ProviderDefault` 不覆寫現有服務或供應商設定；專案預設值也可能已經是 Fast。`Standard` 明確要求一般處理。`Fast` 請求供應商的付費低延遲模式，可能產生額外費用。請保留回傳的建構器：以下三個分支相互獨立，不修改原始請求。

```csharp
using Mythosia.AI.Models;

var basis = service.CreateRequest("Explain this report.");
var providerDefault = basis.WithSpeed(InferenceSpeed.ProviderDefault);
var standard = basis.WithSpeed(InferenceSpeed.Standard);
var fast = basis.WithSpeed(InferenceSpeed.Fast);
```

顯示選項前檢查 `GetSpeedSupport(InferenceSpeed.Fast)`。`StandardSpeed` 和 `FastSpeed` 同樣區分 Supported、Unsupported、Unknown。本機 Supported 不保證帳戶權限、容量或延遲。明確指定 Standard/Fast 在不支援或未知時會失敗，不會悄悄更改模型或推理層級。使用 `ProviderDefault` 保持原有路徑。

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;

var request = service.CreateRequest("Explain this report.")
    .WithSpeed(InferenceSpeed.Fast);
if (request.GetCapabilities().GetSpeedSupport(InferenceSpeed.Fast)
    != CapabilitySupport.Supported)
    throw new NotSupportedException("Fast processing is not supported here.");

await using var run = await request.StartRunAsync();
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (AIProcessingInfo processing in result.Processing)
{
    Console.WriteLine($"{processing.RequestIndex}: {processing.RequestedSpeed} -> " +
        $"{processing.AppliedSpeed?.ToString() ?? "unknown"}; " +
        $"raw={processing.RawAppliedMode}; response={processing.ResponseId}; " +
        $"downgraded={processing.IsDowngraded}");
}
```

`AIRunResult.Processing` 無需讀取串流即可保留不可變的 `AIProcessingInfo`。`RequestIndex` 從 1 開始，識別供應商推理嘗試，包含伺服器 continuation；不等於工具輪次或 HTTP 請求數量；工具後續呼叫、重試及格式修復可能增加記錄。包括失敗嘗試在內，伺服器未回報可識別模式時 `AppliedSpeed` 為 null。`RawAppliedMode` 和 `ResponseId` 保留回報的原始資訊。僅在要求 Fast 而明確回報 Standard 時，`IsDowngraded` 才為 true；false 不能證明已套用 Fast。

一般 completion 完成後立即讀取 `AIService.LastProcessing`；後續邏輯請求會替換此檢視，已取得的記錄保持不可變。服務擴充只設定下一個邏輯請求及其工具往返，不設定永久預設值。輔助摘要、內部查詢改寫和內部 profile 不繼承主請求的速度覆寫，也不混入主請求的觀測記錄。

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;

string answer = await service
    .WithSpeed(InferenceSpeed.Standard)
    .GetCompletionAsync("Explain this report.");
var processing = service.LastProcessing;
```

這些值是供應商回報的處理模式，不是每秒 token 數的實測值。OpenAI、xAI、Google 可能在伺服器端降級；Mythosia 不會自動換速度重試。Anthropic fast mode 需要權限，僅限直接 Claude API，切換速度可能使提示快取失效。Gemini Developer API priority 需要 Tier 2/3 資格。請另行確認供應商、模型、API 支援及收費；此設定不適用於影像生成、嵌入或原生 Batch API。 [OpenAI](https://developers.openai.com/api/docs/guides/fast-mode) · [Anthropic](https://platform.claude.com/docs/en/build-with-claude/fast-mode) · [xAI](https://docs.x.ai/developers/advanced-api-usage/priority-processing) · [Gemini](https://ai.google.dev/gemini-api/docs/generate-content/priority-inference)

透過 `IAIService` 參照時，使用 `Mythosia.AI.Extensions` 的 `GetLastProcessing()`。它讀取選用的 `IAIProcessingInfoService`；不支援診斷時回傳空清單。`IAIService` 不增加必要成員。RAG 中 `RagEnabledService.WithSpeed(...)` 設定檢索後的下一次回答，`LastProcessing` 描述該回答；內部查詢改寫保持分離。Run 結果提供相同的 `Processing` 記錄。

本次實作的 Fast 支援清單如下。請透過 `GetSpeedSupport(InferenceSpeed.Standard)` 單獨檢查 Standard。清單外模型、第三方端點和 OpenAI 相容供應商不會自動繼承付費處理支援。

| API | Fast |
| --- | --- |
| Anthropic — `api.anthropic.com` | `claude-opus-4-8`, `claude-opus-5`, `claude-opus-5-5` |
| OpenAI — `api.openai.com` | `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
| xAI — `api.x.ai` | `grok-4.7`, `grok-4.6`, `grok-4.5`, `grok-4.5-latest`, `grok-build-latest`, `grok-4.3`, `grok-4.3-latest`, `grok-latest`, `grok-4.20-0309-reasoning`, `grok-4.20-0309-non-reasoning`, `grok-build-0.1` |
| xAI — `us.api.x.ai` | `grok-4.7`, `grok-4.6` |
| Google — Gemini Developer API | `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`, `gemini-3-flash-preview`, `gemini-3.1-pro-preview`, `gemini-3.1-flash-lite`, `gemini-3.5-flash`, `gemini-3.5-flash-lite`, `gemini-3.6-flash`, `gemini-3.7-flash`, `gemini-3.8-flash` |

| API | `Standard` | `Fast` | `RawAppliedMode` |
| --- | --- | --- | --- |
| Anthropic — Opus 4.8 / 5 / 5.5 | `speed: "standard"` + `fast-mode-2026-02-01` | `speed: "fast"` + `fast-mode-2026-02-01` | `usage.speed` |
| Anthropic — 其他已知 Claude 模型，包括 Sonnet 5 | 省略 `speed` 和 fast-mode beta | `Unsupported` | `usage.speed` |
| OpenAI | `service_tier: "default"` | `service_tier: "fast"` | `service_tier` (`fast` / `priority` → Fast) |
| xAI | `service_tier: "default"` | `service_tier: "priority"` | `service_tier` |
| Google | `serviceTier: "standard"` | `serviceTier: "priority"` | `x-gemini-service-tier` / `usageMetadata.serviceTier` |

這些其他 Claude 模型的 Standard 使用原有一般請求。伺服器未回報處理資訊時，`AppliedSpeed` 保持 null，不會僅根據請求值推斷已套用 Standard。
