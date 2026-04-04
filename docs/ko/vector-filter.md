# VectorFilter

`VectorFilter`는 메타데이터로 벡터 스토어 쿼리를 필터링하는 플루언트 API입니다. `IVectorStore.SearchAsync`, `HybridSearchAsync`, RAG 쿼리에 적용됩니다.

## 기본 동등 비교

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Where("language", "ko");
```

## 비교 연산자

```csharp
var filter = new VectorFilter()
    .WhereGreaterThan("date", "2024-01-01")
    .WhereLessThanOrEqual("priority", "3")
    .WhereNot("status", "archived");
```

| 메서드 | SQL 동등 |
|--------|---------|
| `.Where(key, value)` | `key = value` |
| `.WhereNot(key, value)` | `key != value` |
| `.WhereGreaterThan(key, value)` | `key > value` |
| `.WhereGreaterThanOrEqual(key, value)` | `key >= value` |
| `.WhereLessThan(key, value)` | `key < value` |
| `.WhereLessThanOrEqual(key, value)` | `key <= value` |
| `.WhereLike(key, pattern)` | `key LIKE pattern` |

## 집합 멤버십

```csharp
var filter = new VectorFilter()
    .WhereIn("category", "legal", "compliance", "policy")
    .WhereNotIn("type", "draft", "archived");
```

## 키 존재 여부

```csharp
var filter = new VectorFilter()
    .WhereExists("reviewed_by")      // 키가 존재해야 함
    .WhereNotExists("deprecated");   // 키가 없어야 함
```

## 논리 그룹화 (AND / OR)

같은 수준의 조건은 기본적으로 AND로 결합됩니다. `.Or()`를 사용해 OR 그룹을 만듭니다:

```csharp
var filter = new VectorFilter()
    .Where("source", "manual.pdf")
    .Or(f => f
        .Where("type", "urgent")
        .Where("priority", "high")
    );
// source = "manual.pdf" AND (type = "urgent" OR priority = "high")
```

중첩 AND:

```csharp
var filter = new VectorFilter()
    .Or(f => f
        .And(a => a.Where("lang", "ko").Where("region", "kr"))
        .And(a => a.Where("lang", "en").Where("region", "us"))
    );
// (lang = "ko" AND region = "kr") OR (lang = "en" AND region = "us")
```

## 점수 임계값

```csharp
var filter = new VectorFilter()
    .Where("source", "faq.pdf")
    .WithMinScore(0.75);
```

## 벡터 스토어와 함께 사용

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

## RAG와 함께 사용

`RagQueryOptions`의 `StoreFilter`로 전달합니다:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter()
        .Where("source", "product-manual.pdf")
        .WithMinScore(0.7)
};

var response = await ragService.GetCompletionAsync("기기를 어떻게 초기화하나요?", options);
```

## 필터 병합

`AppendConditionsFrom`을 사용하여 두 필터를 결합합니다(예: 파이프라인 수준 필터와 쿼리별 필터 병합):

```csharp
var baseFilter = new VectorFilter().Where("tenant", "acme");
var queryFilter = new VectorFilter().Where("language", "ko");

baseFilter.AppendConditionsFrom(queryFilter);
// baseFilter에 두 조건이 모두 포함됨
```
