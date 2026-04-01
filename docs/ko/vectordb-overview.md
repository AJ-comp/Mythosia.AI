# 벡터 데이터베이스 개요

Mythosia.AI는 여러 벡터 데이터베이스 백엔드에서 동작하는 통합 `IVectorStore` 추상화를 제공합니다. 인터페이스에 대해 한 번만 작성하면 검색 로직을 변경하지 않고도 백엔드를 교체할 수 있습니다.

## 핵심 인터페이스: `IVectorStore`

```csharp
// Upsert
Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken = default);
Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken cancellationToken = default);

// 검색
Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryVector, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
    float[] denseVector, string query, int topK = 5, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);

// ID로 가져오기
Task<VectorRecord?> GetAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<VectorRecord>> GetBatchAsync(IEnumerable<string> ids,
    VectorFilter? filter = null, CancellationToken cancellationToken = default);

// 삭제
Task DeleteAsync(string id, VectorFilter? filter = null,
    CancellationToken cancellationToken = default);
Task DeleteByFilterAsync(VectorFilter filter, CancellationToken cancellationToken = default);
Task ReplaceByFilterAsync(VectorFilter filter, IReadOnlyList<VectorRecord> records,
    CancellationToken cancellationToken = default);

// 유틸리티
Task<long> CountAsync(VectorFilter? filter = null, CancellationToken cancellationToken = default);
Task VerifyConnectionAsync(CancellationToken cancellationToken = default);
```

## 데이터 모델

### VectorRecord

저장되는 모든 항목은 `VectorRecord`입니다:

```csharp
public class VectorRecord
{
    public string Id { get; set; }                            // 고유 식별자
    public float[] Vector { get; set; }                       // 임베딩 벡터
    public string Content { get; set; }                       // 원본 텍스트
    public Dictionary<string, string> Metadata { get; set; } // 커스텀 키-값 메타데이터
}
```

`Metadata` 딕셔너리를 커스텀 필드(소스 파일, 언어, 날짜, 카테고리 등)에 활용하세요:

```csharp
var record = new VectorRecord
{
    Id = Guid.NewGuid().ToString(),
    Vector = await embeddingService.GetEmbeddingAsync("어떤 텍스트"),
    Content = "어떤 텍스트",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "manual.pdf",
        ["language"] = "ko",
        ["date"]     = "2024-01-15",
        ["category"] = "policy"
    }
};
```

### VectorSearchResult

검색 결과는 레코드와 유사도 점수를 함께 반환합니다:

```csharp
public class VectorSearchResult
{
    public VectorRecord Record { get; set; }
    public double Score { get; set; }  // 0.0–1.0 (높을수록 더 유사)
}
```

## 사용 가능한 백엔드

| 백엔드 | 패키지 | 사용 사례 |
|--------|--------|---------|
| **InMemory** | `Mythosia.VectorDb.InMemory` | 개발, 테스트, 데모 |
| **Qdrant** | `Mythosia.VectorDb.Qdrant` | 프로덕션, 네이티브 하이브리드 검색 |
| **Pinecone** | `Mythosia.VectorDb.Pinecone` | 서버리스 관리형 서비스 |
| **PostgreSQL** | `Mythosia.VectorDb.Postgres` | 기존 Postgres 환경, ACID 보장 |

모든 백엔드는 동일한 `IVectorStore` 인터페이스를 구현합니다. 백엔드별 설정은 [백엔드 설정](vectordb-backends.md)을 참고하세요.

## 의존성 주입

임의의 백엔드를 `IVectorStore`로 등록합니다:

```csharp
// InMemory
services.AddSingleton<IVectorStore>(new InMemoryVectorStore());

// Qdrant
services.AddSingleton<IVectorStore>(new QdrantStore(new QdrantOptions
{
    CollectionName = "my-collection",
    Dimension = 1536
}));

// PostgreSQL
services.AddSingleton<IVectorStore>(new PostgresStore(new PostgresOptions
{
    ConnectionString = "Host=localhost;Database=vectors;",
    Dimension = 1536,
    EnsureSchema = true
}));
```

## 백엔드별 필터 실행

`VectorFilter` 조건은 가능한 경우 백엔드에서 처리됩니다:

| 연산자 | InMemory | Qdrant | Pinecone | Postgres |
|--------|----------|--------|----------|---------|
| Eq / Ne | 클라이언트 | **서버** | **서버** | **SQL** |
| In / NotIn | 클라이언트 | **서버** | **서버** | **SQL** |
| Gt / Gte / Lt / Lte | 클라이언트 | 클라이언트 | 클라이언트 | **SQL** |
| Like | 클라이언트 | 클라이언트 | 클라이언트 | **SQL** |
| Exists / NotExists | 클라이언트 | 클라이언트 | 클라이언트 | **SQL** |

Postgres는 모든 연산자에 대해 전체 SQL 푸시다운을 지원합니다.
