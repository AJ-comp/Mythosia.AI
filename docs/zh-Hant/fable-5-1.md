# 觀察 Claude Fable 5.1 的長時間工作

[Claude Opus 5.5](providers.md#claude-opus-55) 是尚未發布的新增功能：推理始終啟用，預設 effort 為 medium，並省略顯示。可讀進度需明確要求；預設值和模型綁定規則與 Fable 5.1 不同。

> Fable 5.1 控制需要 `Mythosia.AI` 8.0.0 與 `Mythosia.AI.Abstractions` 4.0.0 或更新版本。既有 Run、推理/搜尋及 GPT-6 Astra API 的最低版本仍為 7.1.0 / 3.1.0。

## 為什麼需要這些控制？

文件調查可能經過多次搜尋與工具呼叫才能產生答案。應用程式可能需要顯示進度、要求僅在目前回合執行某項檢查，或在修改早期對話後繼續工作。Fable 5.1 為這些情況提供控制，但重用已保留的思考時，對話歷史本身也是請求契約的一部分。

使用 [Run API](execution-api-transition.md) 觀察及取消工作，使用[通用推理與搜尋選項](reasoning-and-search.md)選擇推理深度與資訊來源，再用下列 Claude 專用設定控制進度與歷史處理。模型的原生能力不代表 Mythosia 已公開所有供應商 API。

## 明確選擇模型與推理深度

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("Review this migration plan.");
```

`ClaudeFable5_1` 選擇 `claude-fable-5-1`。`ClaudeMythos5_1` 選擇 `claude-mythos-5-1`，需要 Project Glasswing 存取權限。既有 Fable 5 與 Mythos 5 常數仍保留。兩個 5.1 模型接受文字與影像輸入並輸出文字，上下文視窗為 1M token，最大輸出為 128K token。[模型概述](https://platform.claude.com/docs/en/models/fable-5-1/overview)。

模型原生預設 effort 為 `high`，但 Mythosia 的 `ClaudeReasoningEffort.Auto` 保留既有 `ThinkingBudget` 對應：啟用的預算預設為 `High`，達到 32,768 時為 `XHigh`，達到 100,000 時為 `Max`。關閉推理的請求會使用低 adaptive effort 並省略可讀思考。需要 `High` 時請明確選擇；`Auto` 不表示函式庫一律省略 effort 並完全採用模型預設值。

## 在工具呼叫之間顯示進度

`ClaudeThinkingDisplay.Updates` 請求可讀進度，同時隱藏推理。`Summarized` 還包含推理摘要，`Omitted` 則省略可讀 thinking 區塊。只有模型產生更新時才會回傳，因此不保證固定間隔的狀態通知。[進度更新](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta)。

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "Use the registered tools to investigate the report.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"Progress: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

更新使用既有 `StreamingContentType.Reasoning` 事件；透過 `StreamOptions.FullOptions` 或 `StreamOptions.Default.WithReasoning()` 開啟觀察。非串流呼叫結束後可讀取 `service.LastThinkingContent`。進度文字與最終答案分開，不會公開原始思考過程。

## 不要為單回合變更改寫早期歷史

Fable 5.1 的 thinking 區塊綁定於產生它的系統提示、工具與先前訊息。保留後續 thinking 的同時改寫這些輸入，可能使其失效。僅要求目前回合先檢查客服政策時，可以附加回合限定指令並將其留在歷史中；後續使用者訊息出現後，指令停止生效。如此不必反覆修改頂層系統提示。修改 effort 與附加回合指令是兩種獨立控制。

```csharp
await service
    .WithTurnInstruction("Check the registered support-policy tool before answering this turn.")
    .GetCompletionAsync("Can I return an opened product?");

await service
    .WithConversationInstruction("Use Korean for the remaining conversation.")
    .GetCompletionAsync("Explain the next step.");
```

兩個輔助方法都會為下一個邏輯請求擷取指令。Mythosia 保留先前訊息，在使用者輸入或工具結果之後附加系統訊息。`WithTurnInstruction` 使用 `clear_at: "next_user_message"`；同一個邏輯請求內，每次工具結果回合之後都會重新附加指令，使其生效至本次請求結束。`WithConversationInstruction` 會持續適用於後續回合。必須在啟動工作前設定；兩者都不是 `run.SteerAsync`，也不會向已執行中的回應注入指令。

要在請求之間調整 effort 並保留可重用的快取前綴，使用 `Mythosia.AI.Extensions` 的 `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)`。函式庫傳送訊息級 effort 更新並保留其歷史。支援組合見[通用指南](reasoning-and-search.md)。在 5.1 中，`AIRequestContext` 的請求級系統前綴/後綴會轉為附加的回合指令，不會改寫早期系統提示。

Fable 5.1 可以讀取較早 Claude 模型的 thinking，但較早模型無法讀取 Fable 5.1 的 thinking。Mythos 5.1 具有相同的 5.1 能力，但不強制執行 Fable 的前綴綁定檢查。歷史修改、模型切換或思考區塊遭捨棄時，應觀察這些變化，不能假設推理維持不變。[遷移指南](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)。

## 診斷刻意進行的歷史修改

`ThinkingPrefixMismatchBehavior = null` 將檢查交由供應商的帳號政策決定。`WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)` 明確要求伺服器驗證。使用者對歷史、`SystemMessage` 或工具的修改仍會傳送給 Anthropic；使用 `Error` 時，前綴不符由供應商回傳 400。重送相同的無效請求無法修復它。

若應用程式刻意修改早期內容，並接受失去受影響的推理，可以選擇 `DropBlock`。Mythosia 會將該控制傳送給 Anthropic，不會在請求前默默移除 thinking。

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "You are reviewing the revised policy.";
await service.GetCompletionAsync("Reassess the recommendation.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations` 提供供應商回報的 `Type`、`Path`、`Reason`，以及用於歸屬的 `ResponseId` 與 `Model`。`prefix_binding_mismatch` 表示前綴改變，`model_binding_mismatch` 表示目標模型無法讀取該 thinking。捨棄不是修復推理。需要保留時應維持完整對話紀錄；需要重設時則開始新對話。

Mythosia 保留傳輸歷史，避免內部 RAG/context 處理造成意外變動。一般 Fable 5.1 對話在預設/`Error` 路徑下會阻止自動本機壓縮。`DropBlock` 允許壓縮，但可能捨棄推理，也不保證快取命中。獨立的 `CachePreservation.Required` 選項仍保留更嚴格的歷史保護。 `WithWebSearch()` 等通用選項會在請求後消耗。下一回合省略它們會改變原生 tools 陣列，可能造成前綴不符。要保留歷史，應再次套用相同的工具/搜尋設定；刻意變更時使用 `DropBlock` 或新對話。這些選項不會自動延續到下一個請求。

保留的傳輸快照屬於服務與其 `ChatBlock`。只把 `ChatBlock` 複製到新服務，不會移轉先前的 RAG/context 或回合 system 快照。需要保留推理時請繼續使用同一服務與對話；如果只搬移原始歷史，應開始新對話，不要假設保留狀態也已移轉。

## 使用一般工具選擇

Fable 5.1 與 Mythos 5.1 拒絕強制工具選擇。不要設定 `ForceFunctionName`，而是在請求中說明何時使用已註冊工具。不允許目前回合呼叫工具時，仍可使用 `FunctionsDisabled`。需要型別化回應時，請使用既有結構化輸出 API，不要僅為取得 JSON 而強制呼叫函式。

## 了解哪些變更由伺服器負責

| 原生選項 | 所需 Anthropic beta |
| --- | --- |
| 訊息級 effort | `mid-conversation-output-config-2026-07-01` |
| 回合限定系統訊息 | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| Thinking 綁定控制與 `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia 在開啟相應支援設定時加入必要標頭。開啟一項 beta 不表示開啟其他所有 beta。此整合不新增伺服器端 compaction、原生工具新增/刪除區塊或自動模型 fallback。

兩個模型都要求滿足供應商適用的 30 天資料保留安排；ZDR 需要 Anthropic 明確授權。Adaptive thinking 始終啟用，不支援手動 `budget_tokens` 或停用推理，也不傳送自訂取樣參數。帳號存取與保留安排是伺服器要求。[遷移要求](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide)。

文字浮水印、支援媒體的來源資訊，以及快取讀取價格由 Anthropic 套用，無需新增 Mythosia 請求選項。此整合不新增媒體來源建立 API、浮水印開關或計費控制。參見 [Fable 5.1 的變更](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1)。
