# 하이브리드 검색

상품 코드는 키워드 검색이 유리하고, 문서와 표현이 다른 질문은 의미 검색이 필요합니다. 선택한 검색기는 각 방식에 필요한 처리만 실행합니다. 키워드 검색을 위해 질문 임베딩부터 만들 필요가 없습니다.

## 내장 검색 방식

```csharp
// 의미 검색 (기본값)
.UseVectorSearch()

// 질문 임베딩 없는 키워드 검색
.UseKeywordSearch()

// 가중 하이브리드 검색
.UseHybridSearch(new HybridSearchOptions
{
    VectorWeight = 0.7f,
    CandidateMultiplier = 4,
    RrfK = 60
})
```

`HybridSearchOptions`: `using Mythosia.VectorDb;`

`UseKeywordSearch()`는 질문 임베딩을 생략합니다. 문서 등록은 여전히 기존 벡터 저장소를 위해 청크를 나누고 임베딩을 생성합니다. 문서까지 키워드만으로 저장하는 API는 아닙니다. 지연 초기화로 첫 질문에서 문서를 등록하면 문서 임베딩 호출이 발생할 수 있습니다.

## 키워드와 의미 검색 결과 결합하기

`VectorWeight`는 벡터 검색 비중(0–1)이며 키워드 비중은 `1 - VectorWeight`입니다. `CandidateMultiplier`는 각 검색의 후보 수를 조절하며 `RrfK`는 가중 Reciprocal Rank Fusion의 순위 보정값입니다. RAG 재랭커의 후보 배수와는 별개입니다. 실제 문서와 질문으로 설정을 비교하세요.

벡터·키워드 단독 모드는 저장소 고유 점수를 유지합니다. 설정 가능한 하이브리드는 한쪽 검색만 실행해도 정규화된 가중 RRF 점수를 사용하며 벡터 비중이 0이면 질문 임베딩을 생략합니다. 점수는 확률이 아닙니다. `WeightedBlend`는 검색 점수와 재랭커 점수를 보정 없이 합칩니다. 입력 점수를 보정하지 않았다면 키워드 검색에는 기본값인 `RerankerOnly`를 권합니다.

```csharp
using Mythosia.AI.Rag;
using Mythosia.VectorDb;

var service = new OpenAIService(apiKey, http)
    .WithRag(rag => rag
        .AddDocument("manual.txt")
        .UseHybridSearch(new HybridSearchOptions
        {
            VectorWeight = 0.7f,
            CandidateMultiplier = 4,
            RrfK = 60
        }));

string answer = await service.GetCompletionAsync("What is the refund policy?");
```

## 저장소 지원 범위와 호환성

InMemory, PostgreSQL, Qdrant는 새 키워드 검색과 설정 가능한 가중 RRF를 지원합니다. 텍스트 점수는 InMemory의 BM25, PostgreSQL의 설정된 전문 검색 또는 trigram, Qdrant의 희소 색인으로 계산합니다. 엔진 간 점수는 같은 의미가 아닙니다.

Pinecone은 호환되는 `dotproduct` 인덱스에서 기본 설정의 `UseHybridSearch()`를 통한 기존 네이티브 하이브리드 검색을 유지합니다. 이 어댑터는 키워드 모드와 두 검색을 결합하는 설정 가능한 가중 RRF를 지원하지 않습니다. 다른 저장소도 해당 선택 인터페이스를 구현해야 합니다. 지원하지 않는 모드나 옵션은 오류로 알리며 벡터 검색으로 몰래 전환하거나 가중치를 무시하지 않습니다.

기본 InMemory·PostgreSQL·Qdrant 어댑터는 신경망 모델을 설치하거나 기존 색인을 변환하지 않습니다. `C#`·`C++` 같은 기호 구별은 각 분석기에 달려 있습니다. 아래의 별도 PIXIE 방식도 식별자 구분 성능을 실제로 검증해야 합니다.

[검색 방식과 저장소 지원 범위](rag.md#retrieval-modes), [커스텀 검색기](rag-pipeline.md#custom-retriever)를 확인하세요.

<a id="pixie-search"></a>

## PIXIE로 로컬 신경망 검색 비교하기

질문과 문서의 표현이 다르면 단어의 단순 일치만으로 관련 자료를 놓칠 수 있습니다. 선택 패키지 `Mythosia.AI.Rag.Search.Pixie`는 질문과 문서를 모두 로컬 PIXIE 모델로 분석해 관련 단어와 가중치를 만들고, 기존 의미 벡터 검색과 결합합니다. PIXIE 실행에는 Python 서버나 API 키가 필요하지 않습니다. 선택한 의미 임베딩·답변 생성 공급자는 여전히 외부 API를 사용할 수 있습니다.

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Search.Pixie;
using Mythosia.VectorDb;

using var encoder = new PixieSparseEncoder(new PixieOptions());
var searchStore = new PixieInMemoryStore(encoder);
RagStore rag = await RagStore.BuildAsync(builder => builder
    .UseEmbedding(embeddings)
    .UseStore(searchStore)
    .AddDocument("manual.txt")
    .UseHybridSearch(new HybridSearchOptions { VectorWeight = 0.7f }));

RagProcessedQuery result = await rag.QueryAsync("refund policy");
```

이 저장소에서 `UseKeywordSearch()`는 신경망 희소 검색을 선택합니다. 의미 벡터용 질문 임베딩은 생략하지만 PIXIE의 질문 분석은 실행합니다. RAG 문서 등록은 여전히 의미 임베딩을 생성합니다. `UseHybridSearch(...)`는 희소 벡터 내적 순위와 의미 벡터 코사인 유사도 순위를 설정한 가중 RRF로 합칩니다.

이 프리뷰는 메모리에 색인을 보관하는 `PixieInMemoryStore`를 제공합니다. PostgreSQL·Qdrant·Pinecone에 PIXIE를 연결하거나 기존 색인을 변환하지는 않습니다. 재시작하거나 모델·설정을 바꾸면 문서를 다시 색인하세요. 저장소의 모든 작업이 끝날 때까지 인코더를 유지하고, 이후 직접 해제합니다. 기존 검색은 기본값으로 유지되므로 같은 문서와 정답이 지정된 질문으로 비교한 뒤 전환하세요. PIXIE가 `C#`·`C++`의 정확한 구분이나 제외 조건을 보장하는 것은 아닙니다.

[PIXIE 연결과 비교 안내](rag-pixie-search.md).
