# VectorFilter

`VectorFilter` 是一个流式 API，用于按元数据过滤向量存储查询。适用于 `IVectorStore.SearchAsync`、`HybridSearchAsync` 和 RAG 查询。

## 基本等值匹配

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "zh");
```

## 比较操作符

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| 方法 | SQL 等价 |
|------|----------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## 集合成员判断

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## 键是否存在

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // 键必须存在
    .WhereNotExists("deprecated");   // 键必须不存在
```

## 逻辑分组（AND / OR）

同一层级的条件默认以 AND 组合。使用 `.Or()` 创建 OR 分组：

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

嵌套 AND：

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "zh").Where("region", "cn"))
        .And(a => a.Where("lang", "en").Where("region", "us"))
    );
// (lang = "zh" AND region = "cn") OR (lang = "en" AND region = "us")
```

## 分数阈值

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## 在向量存储中使用

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

作为 `RagQueryOptions` 中的 `StoreFilter` 传入：

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "product-manual.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("如何重置设备？", options);
```

## 合并过滤器

使用 `AppendConditionsFrom` 合并两个过滤器（例如将管道级过滤器与查询级过滤器合并）：

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "zh");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter 现在包含两个条件
```
