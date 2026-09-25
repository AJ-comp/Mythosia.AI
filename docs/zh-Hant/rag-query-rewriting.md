# 查詢改寫

> 📍 **問答檢索管線：** **`查詢改寫`** → 過濾 → 嵌入（按需） → [檢索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → 上下文構建

問題的 `Embedding` 階段依檢索器需要執行，關鍵字搜尋不會回報此階段。自訂檢索器可透過 `request.ProgressAsync` 回報實際階段。文件嵌入不變。

## 為什麼需要查詢改寫？

在多輪對話中，使用者經常使用代名詞和簡短引用：

> 使用者：「介紹一下退款政策。」
> 使用者：「**它**有哪些例外情況？」

**查詢改寫**在檢索前解析這些引用，將「它」展開為「退款政策的例外情況」。它還實作了**搜尋閘道** — 如果查詢不需要檢索（如「謝謝！」），則跳過向量搜尋。

## 設定

```csharp
.WithRag(rag => rag
    .WithQueryRewriter(250)
    .AddDocument("docs.txt")
)
```

## 多輪 RAG

直接查詢 `RagStore` 時，傳入對話歷史以便改寫器解析引用：

```csharp
var history = new List<ConversationTurn>
{
    new ConversationTurn("退款政策是什麼？", "30 天內可以退貨。"),
    new ConversationTurn("數位產品呢？", "數位產品不可退款。")
};

var result = await store.QueryAsync(
    query: "有沒有例外情況？",
    conversationHistory: history
);
```

<a id="runtime-query-rewriter"></a>

## 在處理查詢時變更改寫設定

> 若要讓執行期間的改寫器變更套用至現有的 `WithRag(store)` 包裝器，需要 `Mythosia.AI.Rag` 8.1.1 或更新版本。[修補說明](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v811)。

若要在不重建索引的情況下新增或替換改寫器，請使用 `store.SetQueryRewriter(rewriter)`；使用 `store.SetQueryRewriter(null)` 可停用改寫和搜尋詞擷取。接受 `conversationHistory` 的 `RagStore.QueryAsync` 多載，以及已透過 `service.WithRag(store)` 連接的包裝器，都會為每次要求選取儲存區目前的改寫器。要求在等待進度通知或改寫期間會繼續使用已選定的執行個體；新增、替換或清除設定會影響後續要求，包括透過這些包裝器進行的檢索、產生回答、串流輸出和 Run。

```csharp
var rag = service.WithRag(store);
store.SetQueryRewriter(rewriter);
var rewritten = await rag.RetrieveAsync("退款政策有哪些例外？");

store.SetQueryRewriter(null);
var original = await rag.RetrieveAsync("退款政策有哪些例外？");
```

`WithQueryRewriter()` 啟用的預設 `LlmQueryRewriter` 只在延遲初始化時建立一次；清除後不會在下次要求時自動重建。不接受 `conversationHistory` 的儲存區多載仍會略過改寫。Agentic RAG 也不使用改寫器，因為搜尋問題由代理自行建立。

## 搜尋閘道的運作方式

並非每則使用者訊息都需要文件搜尋。改寫器會對查詢進行分類，對「謝謝！」、「了解了，很有幫助。」等訊息回傳空改寫結果，跳過整個檢索管線。
