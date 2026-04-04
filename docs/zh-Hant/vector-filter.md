# VectorFilter

`VectorFilter` 是一個流式 API，用於按元資料過濾向量儲存查詢。適用於 `IVectorStore.SearchAsync`、`HybridSearchAsync` 和 RAG 查詢。

## 基本等值比對

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "zh-Hant");
```

## 比較運算子

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| 方法 | SQL 等價 |
|------|----------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## 集合成員判斷

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## 鍵是否存在

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // 鍵必須存在
    .WhereNotExists("deprecated");   // 鍵必須不存在
```

## 邏輯分組（AND / OR）

同一層級的條件預設以 AND 組合。使用 `.Or()` 建立 OR 分組：

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

巢狀 AND：

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "zh-Hant").Where("region", "tw"))
        .And(a => a.Where("lang", "en").Where("region", "us"))
    );
// (lang = "zh-Hant" AND region = "tw") OR (lang = "en" AND region = "us")
```

## 分數閾值

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## 在向量儲存中使用

```csharp
var filter = new VectorFilter()
    .Where("document_type", "contract")
    .WhereGreaterThan("year", "2023");

var results = await vectorStore.SearchAsync(
    queryVector: embedding,
    topK: 5,
    filter: filter
);
```

## 在 RAG 中使用

作為 `RagQueryOptions` 中的 `StoreFilter` 傳入：

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "product-manual.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("如何重設裝置？", options);
```

## 合併過濾器

使用 `AppendConditionsFrom` 合併兩個過濾器（例如將管道級過濾器與查詢級過濾器合併）：

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "zh");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter 現在包含兩個條件
```
