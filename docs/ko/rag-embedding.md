# 임베딩

> 📍 **질문 응답 파이프라인:** [쿼리 재작성](rag-query-rewriting.md) → [필터링](rag-filtering.md) → **`임베딩(필요 시)`** → [검색](rag-hybrid-search.md) → [재순위](rag-reranking.md) → [컨텍스트 구성](rag-context-build.md)

질문의 `Embedding` 단계는 선택한 검색기에 따라 실행됩니다. 키워드 검색은 이 단계를 보고하지 않습니다. 커스텀 검색기는 `request.ProgressAsync`로 실제 단계의 진행 상황을 알릴 수 있습니다. 문서 등록 임베딩은 그대로입니다.

## 임베딩이란?

임베딩은 텍스트를 **숫자 벡터**(숫자 배열)로 변환하는 과정입니다. 변환된 벡터는 고차원 공간에 배치되며, **의미가 비슷한 텍스트끼리 가까운 위치에 모입니다**.

지도에 도시를 배치하는 걸 떠올려보세요. 서울과 인천은 지리적으로 가까우니 지도에서도 가깝게 표시됩니다. 마찬가지로 "구독 해지 방법"과 "멤버십을 끝내고 싶어요"는 전혀 다른 단어를 쓰고 있지만, 의미가 비슷하기 때문에 가까운 벡터를 생성합니다.

RAG 파이프라인에서 임베딩은 두 곳에서 사용됩니다:

1. **문서 인덱싱 시** — 각 청크를 벡터로 변환해 벡터 스토어에 저장
2. **쿼리 시** — 사용자의 질문을 벡터로 변환해 저장된 청크와 유사도 비교

이 페이지에서는 **쿼리 시점의 임베딩**(2번)을 상세히 설명합니다.

## 내장 임베딩 프로바이더

문서 언어, 운영 환경, 검색 요구에 맞는 임베딩 프로바이더를 선택하세요.

### Perplexity

표준 임베딩은 문단을 독립적으로 다루고 `IEmbeddingProvider`를 구현하므로 기존 빌더에 연결됩니다. 문맥 임베딩은 이웃 청크의 순서와 문서별 묶음을 유지합니다. 관련 없는 문서가 하나의 입력으로 합쳐지는 것을 막기 위해 별도 API를 사용합니다.

[Perplexity Agent API, 검색과 임베딩](perplexity.md).

### OpenAI Embedding

가장 대중적인 클라우드 기반 옵션입니다. 품질이 높지만 API 키가 필요합니다:

```csharp
using Mythosia.AI.Rag.Embeddings;

var embedder = new OpenAIEmbeddingProvider(
    apiKey: "sk-...",
    httpClient: new HttpClient(),
    model: "text-embedding-3-small",   // 기본값
    dimensions: 1536                    // 기본값
);
```

빌더 단축 구문도 사용할 수 있습니다:

```csharp
.WithRag(rag => rag
    .UseOpenAIEmbedding(apiKey, model: "text-embedding-3-small", dimensions: 1536)
    .AddDocument("docs.txt")
)
```

<a id="openai-dimensions"></a>

`text-embedding-ada-002`는 **1536차원으로 고정**되어 있습니다. 공급자는 단건·배치 요청에서 이 모델이 지원하지 않는 `dimensions` 필드를 생략하며, 다른 차원을 설정하면 API 호출 전에 `ArgumentOutOfRangeException`이 발생합니다. `text-embedding-3-small`과 `text-embedding-3-large`는 기존처럼 설정한 `dimensions`를 요청에 포함합니다.

### Ollama (로컬 실행)

데이터를 외부로 보내지 않고 로컬에서 임베딩을 실행합니다. 머신에 [Ollama](https://ollama.com/)가 실행 중이어야 합니다:

```csharp
var embedder = new OllamaEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "qwen3-embedding:4b",       // 기본값
    dimensions: 1024,                    // 기본값
    baseUrl: "http://localhost:11434"    // 기본값
);
```

<a id="ollama-dimensions"></a>

문서와 질문의 벡터는 같은 모델과 차원으로 만들어야 합니다. `OllamaEmbeddingProvider`는 설정한 `dimensions`를 `/api/embed`에 보내고, 반환된 각 벡터가 그 길이인지 검증합니다. 공급자 기본값은 계속 `qwen3-embedding:4b`와 **요청 차원 1024**이며, 모델 자체의 원래 출력은 2560차원입니다. Ollama 서버와 선택한 모델이 요청 차원을 지원해야 합니다. 지원하지 않는 요청이나 설정을 무시한 응답은 실패로 처리하며, `Dimensions`를 임의로 바꾸거나 로컬에서 벡터를 잘라 맞추지 않습니다.

모델이나 차원을 변경하면 질문과 같은 설정으로 문서를 다시 임베딩하고 벡터 저장소의 차원도 맞춰야 합니다. 기존 벡터는 자동 변환하지 않습니다.

### vLLM (셀프호스팅)

[vLLM](https://docs.vllm.ai/)으로 자체 임베딩 서버를 운영하는 팀을 위한 옵션입니다:

```csharp
var embedder = new VllmEmbeddingProvider(
    httpClient: new HttpClient(),
    model: "Qwen/Qwen3-Embedding-0.6B", // 기본값
    dimensions: 1024,                     // 기본값
    baseUrl: "http://localhost:8002"      // 기본값
);
```

### Local (API 불필요)

특징 해싱 기반의 경량 프로바이더로, API 키나 외부 서비스가 필요 없습니다. 하지만 임베딩 품질이 뉴럴 모델에 비해 크게 떨어지므로 **실제 사용에는 추천하지 않습니다**.

```csharp
.WithRag(rag => rag
    .UseLocalEmbedding(dimensions: 1024)
    .AddDocument("docs.txt")
)
```

> **팁:** 대신 `OpenAIEmbeddingProvider`의 `text-embedding-3-small` 모델을 사용하세요. 무료에 가까울 정도로 매우 저렴하면서 훨씬 좋은 결과를 얻을 수 있습니다.

## 배치 처리

문서 인덱싱 시, 파이프라인은 수천 개의 텍스트를 한 번에 보내는 대신 배치 단위로 임베딩합니다. 배치 크기는 설정 가능합니다:

```csharp
var options = pipeline.Options.Clone();
options.EmbeddingBatchSize = 100; // 기본값: API 호출 1회당 100개 청크
pipeline.Options = options;
```

배치 크기가 클수록 API 호출 횟수는 줄지만, 호출당 메모리 사용량이 늘어납니다. API 속도 제한이나 메모리 문제가 발생하면 이 값을 줄여보세요.

`EmbeddingBatchSize`는 양수여야 합니다. 파이프라인은 문서 색인 호출을 시작할 때 임베딩이나 저장 레코드 교체 전에 값을 검증하고 해당 호출에 사용할 값을 고정합니다. 빈 배치를 반복하거나, 응답을 기다리는 중 설정이 바뀌어 청크를 건너뛰는 문제를 막기 위한 처리입니다. 이후 색인 호출에는 변경된 설정을 사용할 수 있습니다.

<a id="embedding-validation"></a>

## 청크와 벡터가 잘못 연결되지 않게 하기

HTTP 요청이 성공해도 벡터가 빠지거나 순서가 바뀌면 텍스트에 다른 청크의 의미가 연결됩니다. 사용자 정의 `IEmbeddingProvider`는 입력 순서대로 입력 하나당 null이 아닌 `float[]` 하나를 반환해야 하며, `Dimensions`는 양수여야 합니다. 각 벡터의 길이는 그 차원과 같아야 하고 모든 값은 유한해야 합니다(`NaN`·무한대 불가).

문서를 색인할 때 파이프라인은 잘못된 차원·응답 개수·벡터를 저장이나 `onDocumentEmbedded` 호출 전에 `InvalidOperationException`으로 거부합니다. 각 벡터를 다음 배치 요청 전에 복사하므로 공급자가 다음 배치에서 버퍼를 재사용해도 앞선 청크가 바뀌지 않습니다. 호출자가 읽는 동안에는 반환 데이터를 안정적으로 유지해야 하며, 검증·복사 중의 동시 변조는 지원하지 않습니다. 검증 실패 시 해당 문서의 기존 레코드는 유지됩니다.

`OpenAIEmbeddingProvider`는 모든 응답 항목에 유효하고 중복되지 않는 `index`를 요구하고 입력 순서로 재정렬합니다. `VllmEmbeddingProvider`도 인덱스가 있으면 같은 규칙을 적용하지만, 호환성을 위해 모든 항목에서 `index`를 생략한 응답은 응답 순서대로 허용합니다. 일부 항목만 인덱스를 생략하거나, 중복·범위 밖 인덱스를 반환하면 거부합니다. 사용자 정의 공급자나 인덱스 없는 응답의 순서 정확성은 공급자의 책임이며, 형태 검증만으로 벡터의 의미까지 확인하지는 못합니다.

<a id="query-embedding-validation"></a>

## 검색 전에 질문 벡터 보호하기

진행 상황 알림이나 검색을 기다리는 사이 공급자가 버퍼를 재사용해 질문 벡터가 다른 질문으로 바뀌면 안 됩니다. `IRetrievalStrategy` 어댑터를 포함한 기본 밀집 벡터 검색은 양수인 `Dimensions`, 정확히 그 길이인 null이 아닌 벡터, 유한한 값을 요구합니다. 잘못된 결과는 검색 전에 `InvalidOperationException`으로 거부합니다. 정상 벡터는 공급자 반환 직후, 이후 진행 알림이나 검색 전에 복사합니다. 공급자는 호출자가 읽는 동안 반환 데이터를 안정적으로 유지해야 하며, 사용자 정의 `IRagRetriever`는 자체 질문 준비·검증을 담당합니다.

`OllamaEmbeddingProvider`를 직접 호출할 때도 단일·배치 응답의 구조, 정확한 벡터 개수, 차원, 유한한 값을 검증합니다. 잘못된 JSON이나 벡터는 불완전한 결과 대신 `InvalidOperationException`을 발생시킵니다. 전달한 `HttpClient`는 계속 호출자 소유이며, 개별 HTTP 요청·응답을 정리해도 해당 클라이언트는 해제하지 않습니다.

## 벡터 차원 수

`Dimensions` 속성은 각 임베딩 벡터의 크기를 결정합니다. 이게 중요한 이유는:

- **벡터 스토어와 반드시 일치해야 합니다** — 임베딩이 1536차원이면 벡터 스토어 컬럼도 1536이어야 합니다
- **차원이 높을수록 = 더 정밀** — 대신 저장 공간이 늘고 검색이 느려집니다
- **차원이 낮을수록 = 더 빠름** — 대신 미묘한 의미 차이를 놓칠 수 있습니다

주요 모델별 기본 차원 수:

| 프로바이더 | 모델 | 기본 차원 수 |
| --- | --- | --- |
| OpenAI | text-embedding-3-small | 1536 |
| OpenAI | text-embedding-ada-002 | 1536 |
| Perplexity | pplx-embed-v1-0.6b | 1024 |
| Perplexity | pplx-embed-v1-4b | 2560 |
| OpenAI | text-embedding-3-large | 3072 |
| Ollama | qwen3-embedding:4b | 요청 1024 (원래 출력: 2560) |
| vLLM | Qwen/Qwen3-Embedding-0.6B | 1024 (32–1024) |
| vLLM | Qwen/Qwen3-Embedding-4B | 2560 (32–2560) |
| Local | (특징 해싱) | 1024 |

## 커스텀 임베딩 프로바이더

다른 임베딩 서비스를 사용하려면 `IEmbeddingProvider`를 구현합니다:

```csharp
public class MyEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 768;

    public async Task<float[]> GetEmbeddingAsync(
        string text, CancellationToken cancellationToken = default)
    {
        // 임베딩 API 호출
    }

    public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        // 배치 임베딩 호출
    }
}
```

빌더에 등록합니다:

```csharp
.WithRag(rag => rag
    .UseEmbedding(new MyEmbeddingProvider())
    .AddDocument("docs.txt")
)
```

## 내부 동작

`QueryAsync`가 실행되면 임베딩 단계는 딱 한 가지만 수행합니다:

```
사용자 질문 (문자열) → EmbeddingProvider.GetEmbeddingAsync() → 쿼리 벡터 (float[])
```

이 쿼리 벡터는 다음 단계인 [필터링](rag-filtering.md)으로 전달되고, 메타데이터 필터와 함께 [검색](rag-hybrid-search.md)에서 유사도 검색이 수행됩니다.

## 다음 단계

- [필터링](rag-filtering.md) — 검색 대상 청크를 좁히기
- [검색 (하이브리드 검색)](rag-hybrid-search.md) — 벡터 검색과 키워드 검색을 동시에
- [파이프라인 커스터마이징](rag-pipeline.md) — 임베딩 프로바이더를 여러 서비스에서 공유
