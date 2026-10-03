# 백엔드 설정

## InMemory

가장 간단한 백엔드 — 외부 의존성 없음. 데이터는 RAM에 보관되며 프로세스 종료 시 사라집니다. 개발, 테스트, 데모에 적합합니다.

```bash
dotnet add package Mythosia.VectorDb.InMemory
```

```csharp
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

var store = new InMemoryVectorStore();
```

**내장 하이브리드 검색**: RRF(Reciprocal Rank Fusion)로 코사인 유사도와 BM25 키워드 점수를 병합합니다.

### 동시 사용과 레코드 수정

공유 저장소를 검색하는 동안 문서를 갱신하더라도 본문과 키워드 인덱스는 같은 버전을 가리켜야 합니다. `InMemoryVectorStore`는 저장·삭제·조회 작업을 동기화해 벡터·키워드·하이브리드 검색이 각각 일관된 상태를 읽도록 합니다. 하이브리드 검색의 두 검색 경로도 같은 상태를 사용합니다.

저장할 때 레코드와 벡터 배열·메타데이터를 복사하며, 조회·검색·진단 결과도 독립된 복사본으로 반환합니다. 입력 객체나 반환된 레코드를 수정하는 것만으로는 저장 내용이 바뀌지 않습니다. 변경 사항을 저장하려면 `UpsertAsync`를 다시 호출하세요. 호출이 입력을 복사하거나 읽는 동안에는 입력 레코드·벡터·메타데이터를 변경하지 마세요.

다른 작업이 저장소 잠금을 해제하기를 기다리는 중에도 전달한 `CancellationToken`으로 호출을 취소할 수 있습니다. 대기 취소가 저장소를 사용 중인 다른 작업을 강제로 중단하지는 않습니다. 이미 시작한 개별 레코드 갱신은 본문과 인덱스를 함께 변경한 뒤 마치므로 취소로 인해 한쪽만 갱신되지는 않습니다.

배치를 취소해도 이미 저장된 레코드는 남을 수 있습니다. `ReplaceByFilterAsync`는 여전히 삭제 후 배치 저장을 순서대로 실행하며 트랜잭션을 제공하지 않습니다. 다른 검색이 그 사이의 빈 상태를 볼 수 있고, 실패나 취소가 앞서 완료된 저장을 되돌리지는 않습니다.

<a id="vector-store-diagnostics"></a>

### 진단 메서드

`IVectorStoreDiagnostics`는 `Mythosia.VectorDb.Abstractions` 4.2.0의 선택적 계약입니다. InMemory가 직접 구현하며 `IVectorStore`에 필수 멤버를 추가하지 않습니다. `ListAllRecordsAsync`는 모든 레코드를 나열하고, `ScoredListAsync`는 TopK 제한 없이 모든 유사도 점수를 내림차순으로 반환합니다. 두 메서드는 저장소 전체를 검사하므로 메타데이터 필터를 받지 않으며 RAG 파이프라인의 `StoreFilter`도 적용하지 않습니다. `GetTotalRecordCount()`는 계약에 포함되지 않는 InMemory 편의 메서드입니다. RAG 전용 청크 분석, 상태 검사와 보고서는 `Mythosia.AI.Rag`의 `RagDiagnostics`와 `RagDiagnosticSession`에 남습니다.

**InMemory 5.0.0 업그레이드:** RAG 9.0.0과 함께 사용하세요. 새 InMemory와 이전 RAG 패키지의 조합은 지원하지 않습니다. InMemory는 더 이상 `IRagDiagnosticsStore`를 구현하지 않으므로 할당, 캐스트, 기능 확인을 `IVectorStoreDiagnostics`로 변경하고 해당 소비자를 다시 빌드하세요. RAG Abstractions 6.5.0은 기존 사용자 정의 구현을 위해 사용 중단된 인터페이스, 원래의 두 메서드 선언과 기본 브리지를 유지합니다. 이 브리지는 InMemory의 이전 인터페이스 관계를 복원하거나 모든 기존 바이너리의 호환성을 보장하지 않습니다.

`IRagDiagnosticsStore`를 구현하는 사용자 정의 저장소의 경우 RAG는 내부 어댑터를 통해 원래 인터페이스 메서드를 호출합니다. 따라서 동일한 시그니처의 public 도우미 메서드가 있더라도 명시적 구현을 계속 사용합니다. `IVectorStoreDiagnostics`로 직접 캐스트한 뒤 메서드를 호출하면 해당 public 메서드가 대신 선택될 수 있습니다.

```csharp
// 저장된 모든 레코드 나열
IVectorStoreDiagnostics diagnostics = store;
var all = await diagnostics.ListAllRecordsAsync();
Console.WriteLine($"전체: {store.GetTotalRecordCount()}");

// 원시 유사도 점수 확인
var scored = await diagnostics.ScoredListAsync(queryVector);
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

### 외부 QdrantClient 사용

이미 구성된 `QdrantClient`가 있다면(예: DI 컨테이너에서) 직접 전달할 수 있습니다:

```csharp
var store = new QdrantStore(options, existingQdrantClient);
```

외부에서 제공된 클라이언트는 스토어가 Dispose하지 **않습니다**.

> 모든 벡터 스토어는 `IDisposable`을 구현합니다. 표준 생성자로 스토어를 생성한 경우 `Dispose()` 또는 `using`을 사용해 내부 리소스를 해제하세요.

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
    Namespace              = "production",     // 선택: 모든 작업에 적용

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

### 외부 HttpClient 사용

이미 구성된 `HttpClient`가 있다면(예: `IHttpClientFactory`에서):

```csharp
var store = new PineconeStore(options, existingHttpClient);
```

외부에서 제공된 클라이언트는 스토어가 Dispose하지 **않습니다**.

---

## PostgreSQL (pgvector)

표준 PostgreSQL 데이터베이스에 벡터 유사도 검색을 추가하는

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
var opts = new HnswSearchRuntimeOptions
{
    Profile = SearchProfile.HighRecall,  // Fast | Balanced | HighRecall
    EfSearch = 80                        // HNSW ef_search 직접 재정의
};

var results = await store.SearchAsync(queryVector, topK: 5, filter: null, runtimeOptions: opts);
```

> 하이브리드 검색의 두 검색 경로가 모두 활성화된 경우에도 벡터 검색 설정을 적용하려면 `Mythosia.VectorDb.Postgres` 10.8.1 이상이 필요합니다. [패치 노트](../../src/vectordb/Mythosia.VectorDb.Postgres/RELEASE_NOTES.md#v1081).

하이브리드 검색에서 벡터 후보를 얼마나 넓게 탐색할지는 `PostgresOptions.Index`의 `HnswIndexOptions.EfSearch` 또는 `IvfFlatIndexOptions.Probes`로 설정합니다. 이 기본값은 일반 벡터 검색과 하이브리드 검색의 벡터 경로 모두에서 검색 쿼리와 같은 트랜잭션에 적용됩니다. 위 예제의 요청별 런타임 재정의는 `SearchAsync`에만 적용됩니다. 근사 검색과 필터링 때문에 결과가 `topK`보다 적을 수는 있습니다.
