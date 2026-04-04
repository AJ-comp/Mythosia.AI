# 混合檢索

## 為什麼需要混合檢索？

純向量檢索擅長捕捉語義含義，但可能遺漏使用者原樣輸入的**精確術語**。BM25 關鍵字檢索能處理這些情境但無法理解語義。**混合檢索結合了兩者的優勢**。

## 設定

```csharp
.WithRag(rag => rag
    .UseHybridSearch(vectorWeight: 0.6f)  // 60% 向量，40% BM25
    .AddDocument("knowledge-base.txt")
)
```

`vectorWeight` 範圍從 0.0（純 BM25）到 1.0（純向量）。

## 選擇建議

| 情境 | 建議權重 |
| --- | --- |
| 自然語言通用問答 | 0.7–0.8（偏向量） |
| 含特定術語的技術文件 | 0.4–0.5（均衡） |
| 程式碼或錯誤碼查找 | 0.2–0.3（偏 BM25） |

## 範例

```csharp
var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .UseHybridSearch(vectorWeight: 0.5f)
        .AddDocument("product-catalog.txt")
        .AddDocument("error-codes.txt")
    );

var answer = await service.GetCompletionAsync("如何修復 ERR-4012？");
```
