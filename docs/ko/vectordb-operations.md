# 벡터 스토어 작업

## Upsert

단일 레코드를 삽입하거나 업데이트합니다. 동일한 `Id`를 가진 레코드가 이미 존재하면 교체됩니다.

```csharp
var record = new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = await embeddingService.GetEmbeddingAsync("환불은 30일 이내에 가능합니다."),
    Content = "환불은 30일 이내에 가능합니다.",
    Metadata = new Dictionary<string, string>
    {
        ["source"]   = "faq.pdf",
        ["language"] = "ko",
        ["section"]  = "returns"
    }
};

await store.UpsertAsync(record);
```

## 배치 Upsert

단일 호출로 여러 레코드를 upsert합니다. 루프에서 `UpsertAsync`를 호출하는 것보다 효율적입니다 — 백엔드는 내부적으로 배치 API를 사용합니다.

```csharp
var records = chunks.Select(chunk => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = chunk.Embedding,
    Content = chunk.Text,
    Metadata = new Dictionary<string, string>
    {
        ["source"] = "manual.pdf",
        ["page"]   = chunk.Page.ToString()
    }
});

await store.UpsertBatchAsync(records);
```

## 검색

쿼리 벡터에 가장 유사한 top-K 레코드를 반환합니다. 점수 계산 전에 메타데이터로 필터링할 수 있습니다.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("환불 정책이 무엇인가요?");

var results = await store.SearchAsync(queryVector, topK: 5);

foreach (var r in results)
{
    Console.WriteLine($"[{r.Score:F3}] {r.Record.Content}");
    Console.WriteLine($"  출처: {r.Record.Metadata["source"]}");
}
```

### 필터 검색

벡터 유사도와 메타데이터 필터링을 결합합니다:

```csharp
var filter = new VectorFilter()
    .Where("language", "ko")
    .Where("section", "returns")
    .WithMinScore(0.7);

var results = await store.SearchAsync(queryVector, topK: 5, filter: filter);
```

전체 필터링 API는 [VectorFilter](vector-filter.md)를 참고하세요.

## 하이브리드 검색

밀집 벡터 유사도와 키워드(BM25) 검색을 병합합니다. 특정 용어, 이름, 코드가 포함된 쿼리에서 더 높은 재현율을 제공합니다.

```csharp
float[] queryVector = await embeddingService.GetEmbeddingAsync("주문 #12345 상태");

var results = await store.HybridSearchAsync(
    denseVector: queryVector,
    query: "주문 #12345 상태",   // BM25에 사용할 원본 텍스트
    topK: 5
);
```

백엔드별 하이브리드 검색 방식:

| 백엔드 | 방식 |
|--------|------|
| **InMemory** | RRF로 코사인 유사도 + Lucene BM25 점수 병합 |
| **Qdrant** | 서버 측: 밀집 + 희소 벡터를 RRF 또는 DBSF로 융합 |
| **Pinecone** | 희소 + 밀집 벡터를 서버 측에서 병합 |
| **Postgres** | 벡터 유사도 + `tsvector`/`trigram` 점수를 SQL에서 병합 |

### 텍스트 전용 및 설정 가능한 하이브리드 검색

위 오버로드는 각 백엔드의 기존 하이브리드 동작을 유지합니다. `ITextSearchStore`와 `IConfigurableHybridSearchStore`는 Mythosia.VectorDb.Abstractions 4.1.0의 선택적 계약이며 InMemory 4.2.0, PostgreSQL 10.8.0, Qdrant 4.2.0에서 구현합니다. Pinecone은 이 API를 구현하지 않습니다.

```csharp
using Mythosia.VectorDb;

var filter = new VectorFilter().Where("tenant", "acme");
var textResults = await ((ITextSearchStore)store).TextSearchAsync(
    "주문 #12345 상태", topK: 5, filter: filter);

var hybridResults = await ((IConfigurableHybridSearchStore)store).HybridSearchAsync(
    queryVector, "주문 #12345 상태",
    new HybridSearchOptions { VectorWeight = 0.7f, CandidateMultiplier = 4, RrfK = 60 },
    topK: 5, filter: filter);
```

`TextSearchAsync`에는 밀집 쿼리 벡터가 필요하지 않습니다. 설정 가능한 오버로드는 `[0, 1]`로 정규화된 가중 RRF 점수를 사용하며, 가중치 `0`은 벡터 검색을, `1`은 텍스트 검색을 생략합니다. 메타데이터 필터는 top-K 선정 전에 적용되고, `MinScore`는 융합 후에 적용됩니다. 원래 텍스트·벡터 점수의 척도는 서로 다르므로 모드별로 임계값을 설정하세요. Qdrant의 `HybridFusionStrategy`는 위의 기존 오버로드에만 적용됩니다.

## ID로 가져오기

특정 레코드를 ID로 검색합니다:

```csharp
VectorRecord? record = await store.GetAsync("record-id-123");

if (record is null)
    Console.WriteLine("찾을 수 없음");
```

멀티테넌트 네임스페이스 등을 사용할 때 필터로 범위를 지정할 수 있습니다:

```csharp
var filter = new VectorFilter().Where("tenant", "acme");
var record = await store.GetAsync("record-id-123", filter: filter);
```

## 배치 가져오기

단일 호출로 여러 레코드를 ID로 검색합니다:

```csharp
var ids = new[] { "id-1", "id-2", "id-3" };
var records = await store.GetBatchAsync(ids);
```

## ID로 삭제

단일 레코드를 삭제합니다:

```csharp
await store.DeleteAsync("record-id-123");
```

## 필터로 삭제

필터와 일치하는 모든 레코드를 삭제합니다. 주의해서 사용하세요 — 대량 삭제입니다.

```csharp
// 특정 문서의 모든 레코드 삭제
var filter = new VectorFilter().Where("source", "old-manual.pdf");
await store.DeleteByFilterAsync(filter);
```

## 필터로 교체

필터와 일치하는 모든 레코드를 삭제하고 새 레코드를 삽입합니다. 오래된 청크를 남기지 않고 문서를 재인덱싱하는 데 유용합니다. 전체 교체의 원자성은 백엔드에 따라 다릅니다.

```csharp
var filter = new VectorFilter().Where("source", "manual-v1.pdf");

var newRecords = newChunks.Select(c => new VectorRecord
{
    Id      = Guid.NewGuid().ToString(),
    Vector  = c.Embedding,
    Content = c.Text,
    Metadata = new Dictionary<string, string> { ["source"] = "manual-v2.pdf" }
}).ToList();

await store.ReplaceByFilterAsync(filter, newRecords);
```

> Postgres는 데이터베이스 트랜잭션을 사용합니다. InMemory는 삭제 후 배치 저장을 순서대로 실행하므로 다른 검색이 그 사이의 빈 상태를 볼 수 있습니다. 실패나 취소 시 일부만 교체된 상태가 남을 수 있으며, 완료된 저장은 롤백되지 않습니다. 개별 작업의 동기화는 레코드와 BM25 인덱스의 일관성을 보장하지만 전체 교체를 트랜잭션으로 만들지는 않습니다.

## 카운트

저장된 레코드 수를 계산합니다. 선택적으로 필터로 범위를 지정할 수 있습니다:

```csharp
long total  = await store.CountAsync();
long korean = await store.CountAsync(new VectorFilter().Where("language", "ko"));

Console.WriteLine($"전체: {total}, 한국어: {korean}");
```

## 연결 확인

백엔드에 연결 가능한지 확인합니다. 헬스 체크나 시작 시 검증에 유용합니다:

```csharp
try
{
    await store.VerifyConnectionAsync();
    Console.WriteLine("벡터 스토어 연결 정상");
}
catch (Exception ex)
{
    Console.WriteLine($"연결 실패: {ex.Message}");
}
```

## RAG와 함께 사용

임의의 백엔드를 RAG 검색 스토어로 `RagBuilder`에 전달합니다:

```csharp
var store = new QdrantStore(new QdrantOptions
{
    CollectionName = "knowledge-base",
    Dimension      = 1536
});

var ragService = new AnthropicService(apiKey, http)
    .WithRag(rag => rag
        .UseStore(store)
        .UseOpenAIEmbedding(embeddingKey)
        .AddDocuments("docs/")
    );

var answer = await ragService.GetCompletionAsync("반품 정책이 어떻게 되나요?");
```

또는 `RagStore`를 독립적으로 구성하고 여러 AI 서비스에서 공유합니다:

```csharp
RagStore ragStore = await RagStore.BuildAsync(rag => rag
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey)
    .AddDocument("knowledge-base.pdf"));

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
