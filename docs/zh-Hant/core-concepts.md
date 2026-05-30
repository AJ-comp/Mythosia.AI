# 核心概念

本頁收錄了在其餘文件中被反覆引用的基礎概念。未來會逐步在此增加更多概念。

## 什麼是 round？

> [!NOTE]
> **Round** 是你的應用程式與模型之間一次完整的來回呼叫——app 發送一個 prompt，模型回覆，這次交換就是一個 round。一則普通的聊天訊息是 1 個 round。function calling 與 agent 可以為一則使用者訊息串接多個 round。

### 最單純的情況：1 個 round

對一則普通的聊天訊息而言，整段對話發生在單一個 round 內。

```
app  →  "2 加 2 等於多少？"     →  模型
app  ←  "等於 4。"               ←  模型
```

`RoundUsage` 會在這次呼叫的 token 確定時觸發一次。`Completion.Usage` 在 stream 結束時觸發，由於只有一個 round，其總數與 RoundUsage 相同。

### 多個 round：function calling

當模型無法獨自作答時，round 會累積。例如使用者問 *「現在台北的天氣如何？」* — 模型無法存取即時天氣，因此必須呼叫工具。

**Round 1 — 模型決定呼叫工具**

你的 app 把使用者訊息和已註冊工具列表（例如 `GetWeather`）一併傳送給模型。此時模型看到的對話是：

```
system：你是一個天氣 assistant，可以呼叫 GetWeather(city)。
user：  現在台北的天氣如何？
```

模型不會直接寫最終答案，而是回傳一個**工具呼叫請求**：

```
tool_call: GetWeather(city="Taipei")
```

模型這一輪結束，round 1 也隨之結束。此時 `RoundUsage` 觸發，包含 round 1 消耗的 token。**此時還沒有給使用者的最終答案。**

**Round 之間 — 你的 app 執行函式**

這一步**不是**對 LLM 的呼叫。Mythosia.AI runtime 會呼叫你註冊的 `GetWeather` 實作，並取得 `「15°C，多雲」` 的結果。不消耗任何 token。

**Round 2 — 模型寫出最終回答**

你的 app 把 **Round 1 中模型發出的 function_call 和它的執行結果一起**附加到對話中，並**第二次**呼叫模型。模型現在看到：

```
system：     你是一個天氣 assistant，可以呼叫 GetWeather(city)。
user：       現在台北的天氣如何？
assistant：  [已呼叫 GetWeather(city="Taipei")]
tool_result：15°C，多雲
```

有了所需資訊後，模型開始寫文字：

```
台北目前 15°C，多雲。
```

Round 2 結束。`RoundUsage` 第二次觸發 —— 這次只包含 round 2 的 token（由於對話變長，input 通常會比 round 1 多）。stream 關閉後，`Completion.Usage` 觸發一次，其值為 **round 1 + round 2 的總和**。

### 一覽表

| 步驟 | 是否呼叫 LLM？ | 發生了什麼 | 事件 |
|---|---|---|---|
| Round 1 | ✅ | 模型決定呼叫 `GetWeather` | `RoundUsage`（`RoundIndex=1`） |
| Round 之間 | ❌ | App 執行函式，得到 `「15°C，多雲」` | `FunctionCall`、`FunctionResult` |
| Round 2 | ✅ | 模型看到結果並寫出最終回答 | `RoundUsage`（`RoundIndex=2`、`IsFinalRound=true`） |
| Stream 結束 | — | — | `Completion`（Usage = round 1 + round 2） |

### 工具越多，round 越多

若模型需要連續呼叫多個工具，round 會持續累加。例如 *「比較台北和高雄的天氣」*：

1. **Round 1** — 模型呼叫 `GetWeather("Taipei")`
2. App 執行 → `「15°C，多雲」`
3. **Round 2** — 模型看到結果後又呼叫 `GetWeather("Kaohsiung")`
4. App 執行 → `「22°C，晴朗」`
5. **Round 3** — 模型把兩個結果彙整成最終回答

共 3 個 round，`Completion.Usage` 為三者之和。UI 的 context 計量器應使用最後一個 round 的 `RoundUsage.Usage.InputTokens` — 本例中即 round 3 的值。

如需查看 context 計量器如何隨 round 變化的數字範例，請參閱 [Token Usage — Context 大小如何變化](token-usage.md#how-context-size-changes)。
