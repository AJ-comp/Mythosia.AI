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

若要在不重建索引的情況下暫時停用改寫或更換實作，請使用 `store.SetQueryRewriter(null)` 或 `store.SetQueryRewriter(rewriter)`。透過接受 `conversationHistory` 的多載直接呼叫 `RagStore.QueryAsync` 時，會在查詢開始時保留所選改寫器。即使在等待進度通知或改寫期間停用或更換設定，該查詢仍使用同一個執行個體，後續查詢才使用新設定。此行為適用於直接查詢儲存區，不會更新 `RagEnabledService` 包裝器已保留的改寫器。

## 搜尋閘道的運作方式

並非每則使用者訊息都需要文件搜尋。改寫器會對查詢進行分類，對「謝謝！」、「了解了，很有幫助。」等訊息回傳空改寫結果，跳過整個檢索管線。
