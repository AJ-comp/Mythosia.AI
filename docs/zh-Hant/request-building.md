# 讓每個請求的設定互相獨立

> Grok 4.7 是尚未發布的新增功能；請參閱[模型選擇、推理與處理速度](providers.md#grok-47)。

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

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

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

使用者正在等待的請求可選擇付費低延遲處理，背景報告可使用一般處理。`WithSpeed` 保持模型和推理層級，只選擇處理模式。此功能尚未發布，需要相符的 core 與 abstractions 變更；已發布的 8.0.0 / 4.0.0 不包含此功能。

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
| OpenAI — `api.openai.com` | `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.3-codex` |
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
