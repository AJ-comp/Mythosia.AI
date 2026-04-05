# 查詢改寫

> 📍 **問答檢索管線：** **`查詢改寫`** → 嵌入 → 過濾 → [檢索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → 上下文構建

## 為什麼需要查詢改寫？

在多輪對話中，使用者經常使用代名詞和簡短引用：

> 使用者：「介紹一下退款政策。」
> 使用者：「**它**有哪些例外情況？」

**查詢改寫**在檢索前解析這些引用，將「它」展開為「退款政策的例外情況」。它還實作了**搜尋閘道** — 如果查詢不需要檢索（如「謝謝！」），則跳過向量搜尋。

## 設定

```csharp
.WithRag(rag => rag
    .WithQueryRewriter()
    .WithQueryRewriteMaxTokens(250)
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

## 搜尋閘道的運作方式

並非每則使用者訊息都需要文件搜尋。改寫器會對查詢進行分類，對「謝謝！」、「了解了，很有幫助。」等訊息回傳空改寫結果，跳過整個檢索管線。
