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

필터와 일치하는 모든 레코드를 원자적으로 삭제하고 새 레코드를 삽입합니다. 오래된 청크를 남기지 않고 문서를 재인덱싱하는 데 유용합니다.

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

> Postgres에서는 트랜잭션 내에서 실행되어 완전히 원자적입니다.

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
        .UseOpenAIEmbedding(embeddingKey, http)
        .AddDirectory("docs/", ".txt", ".md")
    );

var answer = await ragService.GetCompletionAsync("반품 정책이 어떻게 되나요?");
```

또는 `RagStore`를 독립적으로 구성하고 여러 AI 서비스에서 공유합니다:

```csharp
RagStore ragStore = await RagBuilder.Create()
    .UseStore(store)
    .UseOpenAIEmbedding(apiKey, http)
    .AddDocument("knowledge-base.pdf")
    .BuildAsync();

var claudeRag = new AnthropicService(claudeKey, http).WithRag(ragStore);
var gptRag    = new OpenAIService(openAiKey, http).WithRag(ragStore);
```
