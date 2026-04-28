# 过滤

> 📍 **问答检索管道：** [查询改写](rag-query-rewriting.md) → [嵌入](rag-embedding.md) → **`过滤`** → [检索](rag-hybrid-search.md) → [重排序](rag-reranking.md) → [上下文构建](rag-context-build.md)

## 什么是过滤？

过滤在相似度搜索执行**之前**，缩小**哪些文本块会被纳入搜索范围**。通过元数据或分数阈值限定搜索对象，而不是扫描整个向量存储。

管道应用两种过滤：

1. **元数据过滤** — 根据文本块的元数据（分类、租户、日期）包含或排除
2. **分数过滤** — 设置最低相似度阈值

## 元数据过滤

### 按查询过滤

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("怎么退款？", filter: filter);
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

| 方法 | SQL 对应 | 说明 |
| --- | --- | --- |
| `Where` | `=` | 精确匹配 |
| `WhereNot` | `!=` | 不等于 |
| `WhereIn` | `IN (...)` | 值在集合中 |
| `WhereNotIn` | `NOT IN (...)` | 值不在集合中 |
| `WhereGreaterThan` | `>` | 大于 |
| `WhereGreaterThanOrEqual` | `>=` | 大于等于 |
| `WhereLessThan` | `<` | 小于 |
| `WhereLessThanOrEqual` | `<=` | 小于等于 |
| `WhereLike` | `LIKE` | 模式匹配 |
| `WhereExists` | `IS NOT NULL` | 键存在 |
| `WhereNotExists` | `IS NULL` | 键不存在 |

### 逻辑分组

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
```

## 管道级 StoreFilter

对于**始终生效**的条件（如租户隔离）：

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};
```

`StoreFilter` 与查询过滤器通过 AND 合并——两者都不会被忽略。

## 通过 `Clone()` 进行单次查询覆盖

当你维护一个基线 `RagQueryOptions`（例如租户 `StoreFilter` + `ProgressAsync` 回调），并希望在某些查询上做单字段调整时，使用 `RagQueryOptions.Clone()` 可保留其他所有字段：

```csharp
// 多次查询复用的基线
var baseline = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId),
    ProgressAsync = stage => { Console.WriteLine($"Stage: {stage}"); return Task.CompletedTask; }
};

// 单次查询覆盖 —— Clone() 保留 StoreFilter 和 ProgressAsync
var highRecall = baseline.Clone();
highRecall.FinalFilter.TopK = 15;
highRecall.FinalFilter.MinScore = 0.2;

await ragStore.QueryAsync("退款政策", highRecall);
```

`Clone()` 对选项记录（`FinalFilter`、`RetrievalDerivation`、`FinalSelection`）进行深拷贝,对句柄类型字段（`ProgressAsync`、`StoreFilter`）进行引用拷贝。如果该次调用需要不同的回调或过滤器,请在克隆后显式重新赋值这些属性。

> 从零构造 `new RagQueryOptions { FinalFilter = ... }` 会静默丢弃基线上的其他所有字段。`Clone()` 让"继承默认值,仅覆盖单个字段"的模式变得安全。

## 分数过滤

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

配置了[重排序](rag-reranking.md)时，检索阶段的阈值会自动放宽，为重排序器提供更多候选项，之后再应用严格的 `MinScore`。

## 常见场景

### 多租户隔离

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### 按分类搜索

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("404 错误", filter: filter);
```

### 时间过滤

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## 后续步骤

- [混合检索](rag-hybrid-search.md) — 结合向量搜索与关键词搜索
- [VectorFilter 参考](vector-filter.md) — 过滤 API 完整文档
- [重排序](rag-reranking.md) — 检索后进一步优化结果
