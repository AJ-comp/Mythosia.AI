# 백엔드 설정

## InMemory

가장 간단한 백엔드 — 외부 의존성 없음. 데이터는 RAM에 보관되며 프로세스 종료 시 사라집니다. 개발, 테스트, 데모에 적합합니다.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**내장 하이브리드 검색**: RRF(Reciprocal Rank Fusion)로 코사인 유사도와 BM25 키워드 점수를 병합합니다.

### 진단 메서드

```csharp
// 저장된 모든 레코드 나열
var all = await store.ListAllRecordsAsync();
Console.WriteLine($"전체: {store.GetTotalRecordCount()}");

// 원시 유사도 점수 확인
var scored = await store.ScoredListAsync(queryVector);
foreach (var r in scored)
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content[..60]}");
```

---

## Qdrant

네이티브 하이브리드 검색을 갖춘 프로덕션 급 벡터 데이터베이스입니다. Docker 또는 Qdrant Cloud로 실행합니다.

```bash
dotnet add package Mythosia.VectorDb.Qdrant
```

```bash
# 로컬에서 Qdrant 시작
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

```csharp
using Mythosia.VectorDb.Qdrant;

var store = new QdrantStore(new QdrantOptions
{
    Host             = "localhost",
    Port             = 6334,             // gRPC 포트
    CollectionName   = "my-docs",
    Dimension        = 1536,             // 임베딩 모델과 일치해야 함
    AutoCreateCollection = true          // 첫 upsert 시 컬렉션 생성
});
```

### 전체 옵션

```csharp
new QdrantOptions
{
    Host                   = "localhost",
    Port                   = 6334,
    UseTls                 = false,
    ApiKey                 = null,              // Qdrant Cloud에 필요

    CollectionName         = "my-collection",   // 필수
    Dimension              = 1536,              // 필수

    DistanceStrategy       = QdrantDistanceStrategy.Cosine,
    HybridFusionStrategy   = QdrantHybridFusionStrategy.Rrf,
    AutoCreateCollection   = true,

    // 서버 측 필터링 속도 향상을 위한 추가 페이로드 인덱스
    AdditionalPayloadIndexes = new List<QdrantIndexOption>
    {
        new QdrantIndexOption { Field = "meta.language", SchemaType = PayloadSchemaType.Keyword },
        new QdrantIndexOption { Field = "meta.date",     SchemaType = PayloadSchemaType.Integer }
    }
}
```

### 거리 전략

| 값 | 설명 |
|----|------|
| `Cosine` | 코사인 유사도 — 정규화된 임베딩에 적합 (기본값) |
| `Euclidean` | L2 거리 — 거리가 낮을수록 더 유사 |
| `DotProduct` | 내적 — 단위 정규화 벡터와 함께 사용 |

### 하이브리드 융합 전략

| 값 | 설명 |
|----|------|
| `Rrf` | Reciprocal Rank Fusion — 순위 기반 병합 (기본값) |
| `Dbsf` | 분포 기반 점수 융합 — 점수 분포로 병합 |

### Qdrant Cloud

```csharp
new QdrantOptions
{
    Host           = "your-cluster.cloud.qdrant.io",
    Port           = 6334,
    UseTls         = true,
    ApiKey         = "your-qdrant-cloud-key",
    CollectionName = "production",
    Dimension      = 1536
}
```

---

## Pinecone

완전 관리형 서버리스 벡터 데이터베이스입니다. 인프라 관리가 필요 없습니다.

```bash
dotnet add package Mythosia.VectorDb.Pinecone
```

```csharp
using Mythosia.VectorDb.Pinecone;

var store = new PineconeStore(new PineconeOptions
{
    IndexHost = "https://my-index-xxxx.svc.us-east1-gcp.pinecone.io",
    ApiKey    = "your-api-key"
});
```

### 인덱스 자동 생성

아직 인덱스가 없으면 SDK가 생성하게 할 수 있습니다:

```csharp
new PineconeOptions
{
    ApiKey          = "your-api-key",
    AutoCreateIndex = true,
    IndexName       = "my-index",
    Dimension       = 1536,
    Cloud           = "aws",          // "aws", "gcp", "azure"
    Region          = "us-east-1"
}
```

> `AutoCreateIndex = true`일 때 하이브리드 검색에 필요한 `dotproduct` 메트릭으로 인덱스를 생성합니다.

### 전체 옵션

```csharp
new PineconeOptions
{
    IndexHost              = "https://...",    // 필수 (또는 AutoCreateIndex 사용)
    ApiKey                 = "...",            // 필수
    DefaultNamespace       = "production",     // 선택: 모든 작업에 적용

    UpsertBatchSize        = 100,              // 배치 upsert당 레코드 수
    RequestTimeoutSeconds  = 100,

    AutoCreateIndex        = false,
    IndexName              = null,
    Dimension              = 0,
    Cloud                  = null,
    Region                 = null,
    ControlPlaneHost       = "https://api.pinecone.io"
}
```

---

## PostgreSQL (pgvector)

표준 PostgreSQL 데이터베이스에 벡터 유사도 검색을 추가하는 [`pgvector`](https://github.com/pgvector/pgvector) 확장을 사용합니다.

```bash
dotnet add package Mythosia.VectorDb.Postgres
```

### 사전 준비

```sql
-- PostgreSQL 서버에서 한 번 실행
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;  -- Trigram 텍스트 검색 사용 시만
```

또는 `EnsureSchema = true`로 SDK가 자동 처리하게 할 수 있습니다.

```csharp
using Mythosia.VectorDb.Postgres;

var store = new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=user;Password=pass;",
    Dimension        = 1536,
    EnsureSchema     = true    // 확장, 테이블, 인덱스 자동 생성
});
```

### 인덱스 타입

| 타입 | 클래스 | 사용 시점 |
|------|--------|---------|
| HNSW | `HnswIndexOptions` | 기본값. 빠른 근사 검색. 대부분의 사용 사례에 적합. |
| IVFFlat | `IvfFlatIndexOptions` | 메모리가 적음. 대형 정적 데이터셋에 적합. |
| None | `NoIndexOptions` | 순차 스캔. 소규모 데이터셋에만 사용. |

```csharp
// HNSW (기본값)
new PostgresOptions
{
    Index = new HnswIndexOptions
    {
        M              = 16,   // 노드당 최대 이웃 연결 수
        EfConstruction = 64,   // 인덱스 구성 시 검색 범위
        EfSearch       = 40    // 런타임 검색 범위
    }
}

// IVFFlat
new PostgresOptions
{
    Index = new IvfFlatIndexOptions
    {
        Lists  = 100,  // 반전 목록 수
        Probes = 10    // 쿼리 시 탐색할 목록 수
    }
}
```

### 텍스트 검색 모드

하이브리드 검색의 키워드 부분에 사용됩니다:

| 모드 | 적합한 언어 |
|------|-----------|
| `TsVector` | 일반 전문 검색 — 영어, 대부분의 서구권 언어 |
| `Trigram` | CJK 언어 (한국어, 중국어, 일본어), 퍼지 매칭 |

```csharp
new PostgresOptions
{
    TextSearchMode   = TextSearchMode.Trigram,
    TextSearchConfig = "simple"
}
```

### 거리 전략

| 값 | Postgres 연산자 | 비고 |
|----|----------------|------|
| `Cosine` | `<=>` | 1 − 코사인 유사도 (기본값) |
| `Euclidean` | `<->` | L2 거리 |
| `InnerProduct` | `<#>` | 음수 내적 — 단위 정규화 벡터 사용 시 |

### 런타임 검색 프로파일

쿼리 시점에 재현율 대 지연 시간을 세부 조정합니다:

```csharp
var opts = new VectorSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // HNSW ef_search 직접 재정의
};

var results = await store.SearchAsync(queryVector, topK: 5, runtimeOptions: opts);
```
