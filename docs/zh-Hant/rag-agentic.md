# Agent 式 RAG

## 為什麼需要 Agent 式 RAG？

在標準 RAG 中，每條使用者訊息都會觸發**一次**檢索。系統搜尋、建構上下文並生成回應 — 無論如何。這對簡單問題效果不錯，但在以下場景力不從心：

- 問題需要跨不同主題進行**多次搜尋**（如「比較硬體和軟體產品的退款政策」）
- 第一次搜尋結果**不充分**，系統應該優化後重試
- 某些問題**根本不需要檢索**（如「總結一下我們的對話」）
- 回答需要結合**文件檢索和即時 API 資料**

Agent 式 RAG 解決了所有這些問題。它不是固定的檢索-回答管線，而是由 **Agent 自主決定** — 何時搜尋、搜尋什麼、是否再搜一次、何時呼叫其他工具 — 所有操作都在 ReAct 迴圈中完成。

## 快速上手

透過 `WithAgenticRag` 將 `RagStore` 註冊為工具，然後使用 `RunAgentAsync`：

```csharp
// 建構一次索引
var ragStore = await RagStore.BuildAsync(cfg => cfg
    .AddDocument("manual.pdf")
    .AddDocument("policy.docx")
    .UseOpenAIEmbedding(apiKey));

// 註冊 RAG 為工具並執行 Agent
var service = new AnthropicService(apiKey, http);
service.WithAgenticRag(ragStore);

var answer = await service.RunAgentAsync("總結退款政策。");
```

當 Agent 需要文件上下文時，會自動呼叫 `search_documents`，然後從檢索到的片段中綜合生成最終回答。

## 結合其他工具

Agent 式 RAG 與其他工具組合時效果最佳 — Agent 會為每個子任務選擇合適的工具：

```csharp
var service = new AnthropicService(apiKey, http);

service.WithAgenticRag(ragStore)
       .WithFunctionAsync("get_order_status", "透過訂單 ID 查詢訂單狀態。",
           ("order_id", "要查詢的訂單 ID。", required: true),
           async id => await orderApi.GetStatusAsync(id));

// Agent 搜尋文件取得政策，同時呼叫 API 取得即時訂單資料
var answer = await service.RunAgentAsync(
    "訂單 #12345 — 根據目前政策，我是否有資格退款？");
```

在這個範例中，Agent 自主完成：

1. 搜尋文件取得退款政策
2. 呼叫訂單 API 取得 #12345 的狀態
3. 綜合兩方面資訊生成最終回答

## 自訂工具描述

工具描述決定了 Agent 何時呼叫 RAG。根據你的業務領域定制描述，以取得更精準的工具選擇：

```csharp
service.WithAgenticRag(ragStore,
    toolDescription:
        "搜尋內部 HR 政策、產品手冊和合規文件。" +
        "當需要公司特定政策或產品資訊時呼叫此工具。");
```

模糊的描述（如「搜尋文件」）可能導致 Agent 過於頻繁或不夠頻繁地呼叫 RAG。請具體說明文件**包含什麼類型的資訊**。

## 與標準 RAG 的區別

| | 標準 RAG | Agent 式 RAG |
| --- | --- | --- |
| 搜尋時機 | 每條訊息 | Agent 自行決定 |
| 查詢建構 | QueryRewriter | Agent 自身 |
| 搜尋次數 | 每輪一次 | 按需一次或多次 |
| 工具組合 | 不適用 | 任意已註冊工具 |
| 使用方式 | `.WithRag()` | `.WithAgenticRag()` + `RunAgentAsync` |

> **注意：** Agent 式 RAG 中刻意繞過了 `QueryRewriter`。Agent 會自行建構獨立的搜尋查詢，單獨的改寫步驟既多餘又可能扭曲 Agent 的意圖。

## 如何選擇

- **標準 RAG** — 每個問題都基於文件、單一主題、追求最低延遲
- **Agent 式 RAG** — 問題跨越多個主題、需要結合文件和即時資料、或需要迭代檢索
