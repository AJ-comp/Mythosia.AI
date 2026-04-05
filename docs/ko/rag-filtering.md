# 필터링

> 📍 **질문 응답 파이프라인:** [쿼리 재작성](rag-query-rewriting.md) → [임베딩](rag-embedding.md) → **`필터링`** → [검색](rag-hybrid-search.md) → [재순위](rag-reranking.md) → [컨텍스트 구성](rag-context-build.md)

## 필터링이란?

필터링은 유사도 검색을 실행하기 **전에** 어떤 청크를 검색 대상에 포함할지 좁히는 단계입니다. 벡터 스토어 전체를 뒤지는 대신, 메타데이터나 스코어 기준으로 범위를 한정합니다.

도서관에서 자료를 찾는 상황을 떠올려보세요. 필터링 없이는 건물 전체의 모든 책을 뒤져야 합니다. 필터링을 쓰면 먼저 "의학" 또는 "법률" 서가로 직행한 뒤, 그 서가만 찾아보면 됩니다. 검색 속도가 빨라지고, 결과도 훨씬 정확해집니다.

파이프라인에서 적용되는 필터링은 두 가지입니다:

1. **메타데이터 필터링** — 카테고리, 테넌트, 날짜 등 청크에 붙은 메타데이터 기반 필터
2. **스코어 필터링** — 유사도 점수의 최소 기준을 설정해 낮은 품질의 결과를 제거

## 메타데이터 필터링

벡터 스토어에 저장된 각 청크에는 인덱싱 시 부여된 메타데이터(키-값 쌍)가 있습니다. 특정 조건에 맞는 청크만 검색 대상으로 삼을 수 있습니다.

### 쿼리별 필터

`VectorFilter`를 전달해 검색 범위를 지정합니다:

```csharp
var filter = new VectorFilter()
    .Where("category", "refund-policy");

var result = await pipeline.QueryAsync("환불 절차가 어떻게 되나요?", filter: filter);
```

### 플루언트 필터 API

`VectorFilter`는 다양한 연산자를 제공합니다:

```csharp
var filter = new VectorFilter()
    .Where("department", "engineering")         // 정확히 일치
    .WhereNot("status", "archived")             // 불일치
    .WhereIn("region", "us-east", "eu-west")    // 집합에 포함
    .WhereGreaterThan("year", "2023")           // 범위 비교
    .WhereLike("title", "%kubernetes%");        // 패턴 매칭
```

사용 가능한 연산자:

| 메서드 | SQL 대응 | 설명 |
| --- | --- | --- |
| `Where` | `=` | 정확히 일치 |
| `WhereNot` | `!=` | 불일치 |
| `WhereIn` | `IN (...)` | 집합에 포함 |
| `WhereNotIn` | `NOT IN (...)` | 집합에 미포함 |
| `WhereGreaterThan` | `>` | 초과 |
| `WhereGreaterThanOrEqual` | `>=` | 이상 |
| `WhereLessThan` | `<` | 미만 |
| `WhereLessThanOrEqual` | `<=` | 이하 |
| `WhereLike` | `LIKE` | 패턴 매칭 (`%` = 임의 문자열, `_` = 임의 1문자) |
| `WhereExists` | `IS NOT NULL` | 메타데이터 키 존재 |
| `WhereNotExists` | `IS NULL` | 메타데이터 키 미존재 |

### 논리 그룹

AND/OR 로직으로 조건을 조합할 수 있습니다:

```csharp
var filter = new VectorFilter()
    .Where("tenant", "acme")
    .Or(f => f
        .Where("category", "billing")
        .Where("category", "refund")
    );
// 매칭 조건: tenant = "acme" AND (category = "billing" OR category = "refund")
```

## 파이프라인 레벨 StoreFilter

테넌트 격리처럼 **항상 적용되어야 하는 조건**에는 `RagQueryOptions`의 `StoreFilter`를 설정하세요. 이 필터는 쿼리별 필터와 자동으로 병합됩니다:

```csharp
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", currentTenantId)
};

var response = await ragService.GetCompletionAsync("질문", ragOptions: options);
```

EF Core의 Global Query Filter 패턴과 동일합니다. StoreFilter는 항상 적용되고, 쿼리별 필터가 그 위에 추가 조건을 덧붙입니다.

### 필터 병합 방식

파이프라인 레벨 `StoreFilter`와 쿼리별 필터가 동시에 존재하면, AND로 결합됩니다:

```
최종 필터 = StoreFilter 조건 AND 쿼리별 필터 조건
```

어느 쪽도 무시되지 않습니다. StoreFilter 조건(권한/테넌트 제약)이 먼저 배치되고, 그 뒤에 쿼리별 조건이 추가됩니다.

## 스코어 필터링

`MinScore`는 유사도 점수가 일정 수준 이하인 청크를 제거합니다. 관련성 낮은 청크가 컨텍스트를 오염시키는 것을 방지합니다:

```csharp
var options = new RagQueryOptions
{
    FinalFilter = new RagFilter
    {
        TopK = 5,
        MinScore = 0.7   // 0.7 미만은 제거
    }
};
```

[재순위기](rag-reranking.md)가 설정된 경우, 파이프라인은 검색 단계의 스코어 기준을 자동으로 완화합니다(`RetrievalDerivation.MinScoreDivider` 사용). 재순위기에 더 넓은 후보군을 제공한 뒤, 재순위 이후에 엄격한 `MinScore`를 적용합니다.

## 실전 활용 사례

### 멀티테넌트 격리

테넌트별로 자기 문서만 볼 수 있도록 합니다:

```csharp
// 인덱싱 시 — 테넌트 메타데이터 부여
var doc = new RagDocument
{
    Id = "doc-1",
    Content = "...",
    Metadata = { ["tenant_id"] = "tenant-abc" }
};

// 쿼리 시 — 테넌트로 필터링
var options = new RagQueryOptions
{
    StoreFilter = new VectorFilter().Where("tenant_id", "tenant-abc")
};
```

### 카테고리별 검색

특정 문서 카테고리 안에서만 검색합니다:

```csharp
var filter = new VectorFilter().Where("category", "troubleshooting");
var result = await pipeline.QueryAsync("에러 404", filter: filter);
```

### 시간 기반 필터링

최근 문서로 결과를 한정합니다:

```csharp
var filter = new VectorFilter()
    .WhereGreaterThanOrEqual("updated_at", "2024-01-01");
```

## 내부 동작

필터링 단계는 [임베딩](rag-embedding.md)과 [검색](rag-hybrid-search.md) 사이에 위치합니다:

```
쿼리 벡터 (임베딩에서 취득) + VectorFilter 조건
    → StoreFilter와 병합 (존재 시)
    → MinScore 임계값 적용
    → 검색 전략으로 전달해 검색 실행
```

필터링은 별도의 DB 쿼리를 수행하지 않습니다. 벡터 스토어의 검색 메서드로 조건이 전달되어, 유사도 검색 내에서 조건이 적용됩니다. 효율적이고 원자적인 처리입니다.

## 다음 단계

- [검색 (하이브리드 검색)](rag-hybrid-search.md) — 벡터 검색과 키워드 검색을 동시에
- [VectorFilter 레퍼런스](vector-filter.md) — 필터 API의 전체 문서
- [재순위](rag-reranking.md) — 검색 후 결과 정확도를 한 단계 높이기
