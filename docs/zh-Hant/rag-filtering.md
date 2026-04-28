# 過濾

> 📍 **問答檢索管線：** [查詢改寫](rag-query-rewriting.md) → [嵌入](rag-embedding.md) → **`過濾`** → [檢索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → [上下文構建](rag-context-build.md)

## 什麼是過濾？

過濾在相似度搜尋執行**之前**，縮小**哪些文字區塊會被納入搜尋範圍**。透過中繼資料或分數閾值限定搜尋對象，而不是掃描整個向量儲存。

管線應用兩種過濾：

1. **中繼資料過濾** — 根據文字區塊的中繼資料（分類、租戶、日期）包含或排除
2. **分數過濾** — 設定最低相似度閾值

## 中繼資料過濾

### 按查詢過濾

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("怎麼退款？", filter: filter);
```

### Fluent API

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")
    .WhereNot("status", "archived")
    .WhereIn("region", "us-east", "eu-west")
    .WhereGreaterThan("year", "2023")
    .WhereLike("title", "%kubernetes%");
```

| 方法 | SQL 對應 | 說明 |
| --- | --- | --- |
| `Where` | `=` | 精確匹配 |
| `WhereNot` | `!=` | 不等於 |
| `WhereIn` | `IN (...)` | 值在集合中 |
| `WhereNotIn` | `NOT IN (...)` | 值不在集合中 |
| `WhereGreaterThan` | `>` | 大於 |
| `WhereGreaterThanOrEqual` | `>=` | 大於等於 |
| `WhereLessThan` | `<` | 小於 |
| `WhereLessThanOrEqual` | `<=` | 小於等於 |
| `WhereLike` | `LIKE` | 模式匹配 |
| `WhereExists` | `IS NOT NULL` | 鍵存在 |
| `WhereNotExists` | `IS NULL` | 鍵不存在 |

### 邏輯分組

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
```

## 管線級 StoreFilter

對於**始終生效**的條件（如租戶隔離）：

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};
```

`StoreFilter` 與查詢過濾器透過 AND 合併——兩者都不會被忽略。

## 透過 `Clone()` 進行單次查詢覆寫

當你維護一個基線 `RagQueryOptions`（例如租戶 `StoreFilter` + `ProgressAsync` 回呼），並希望在某些查詢上做單欄位調整時，使用 `RagQueryOptions.Clone()` 可保留其他所有欄位：

```csharp
// 多次查詢重複使用的基線
var baseline = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId),
    ProgressAsync = stage => { Console.WriteLine($"Stage: {stage}"); return Task.CompletedTask; }
};

// 單次查詢覆寫 —— Clone() 保留 StoreFilter 與 ProgressAsync
var highRecall = baseline.Clone();
highRecall.FinalFilter.TopK = 15;
highRecall.FinalFilter.MinScore = 0.2;

await ragStore.QueryAsync("退款政策", highRecall);
```

`Clone()` 對選項記錄（`FinalFilter`、`RetrievalDerivation`、`FinalSelection`）進行深拷貝,對句柄型欄位（`ProgressAsync`、`StoreFilter`）進行參考拷貝。若該次呼叫需要不同的回呼或過濾器,請在複製後明確重新指派這些屬性。

> 從零建構 `new RagQueryOptions { FinalFilter = ... }` 會默默丟棄基線上的其他所有欄位。`Clone()` 讓「繼承預設值,僅覆寫單一欄位」的模式變得安全。

## 分數過濾

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7
    }
};
```

配置了[重排序](rag-reranking.md)時，檢索階段的閾值會自動放寬，為重排序器提供更多候選項，之後再套用嚴格的 `MinScore`。

## 常見場景

### 多租戶隔離

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### 按分類搜尋

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("404 錯誤", filter: filter);
```

### 時間過濾

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## 後續步驟

- [混合檢索](rag-hybrid-search.md) — 結合向量搜尋與關鍵字搜尋
- [VectorFilter 參考](vector-filter.md) — 過濾 API 完整文件
- [重排序](rag-reranking.md) — 檢索後進一步優化結果
