# 로컬 신경망 검색을 기존 검색과 비교하기

질문과 정답 문서가 같은 뜻을 다른 단어로 표현하면, 단어의 단순 일치만으로는 필요한 문서를 놓칠 수 있습니다. 신경망 희소 검색은 관련 단어까지 포함한 어휘별 가중치를 만들어 이 간격을 줄이려는 방식입니다. `Mythosia.AI.Rag.Search.Pixie`는 기존 의미 임베딩 공급자, 문서 분할, RAG 흐름을 유지하면서 비교할 수 있는 로컬 PIXIE 검색을 제공합니다.

**.NET 8 이상**에서 사용하는 선택 패키지 **0.1.0-preview**입니다. 기존 검색이 기본값으로 유지됩니다. 이번 프리뷰의 저장소는 `PixieInMemoryStore`이며, PostgreSQL·Qdrant·Pinecone에 PIXIE를 연결하거나 기존 색인을 변환하는 기능은 아닙니다.

## 프로그램 안에서 무엇이 실행되나요?

```text
문서 청크 ── 기존 임베딩 공급자 ── 의미 벡터 ──┐
          └─ 로컬 PIXIE 모델 ───── 희소 벡터 ──┤
                                             │
질문 ─────── 같은 임베딩 공급자 ── 의미 검색 ──┤
     └────── 로컬 PIXIE 모델 ───── 희소 검색 ──┤
                                             ↓
                                      가중 순위 결합
                                             ↓
                                     기존 RAG 재랭킹
                                     및 답변 문맥 구성
```

문서와 질문의 희소 벡터는 같은 모델과 토크나이저로 만듭니다. 희소 검색은 같은 어휘 ID에 해당하는 가중치의 내적, 의미 검색은 코사인 유사도로 순위를 정합니다. 하이브리드 검색은 두 순위를 가중 Reciprocal Rank Fusion(RRF)으로 합칩니다. 희소 벡터는 검색을 위한 표현이며, 모델이 생성한 답변이나 사람이 미리 지정한 메타데이터 키워드 목록이 아닙니다.

PIXIE는 ONNX Runtime을 통해 프로그램 내부에서 실행합니다. Python 서버, API 키, 외부 추론 서비스가 필요하지 않으며 실행 중 파일을 다운로드하지 않습니다. 선택한 의미 임베딩 공급자·질문 재작성기·재랭커·답변 생성 공급자는 외부 서비스를 호출할 수 있습니다. PIXIE 패키지가 이 구성요소까지 로컬로 바꾸지는 않습니다.

## 기존 RAG 설정에 연결하기

**게시 상태:** 현재 프리뷰는 소스와 로컬 검증용 패키지로 제공하며 NuGet에는 아직 게시하지 않았습니다. 아래 설치 명령은 게시 후에 사용할 수 있습니다. 현재 소스를 검증하려면 이 문서 마지막의 소스 패키지 생성 절차를 따르세요.

게시 후에는 RAG 패키지와 선택 검색 프리뷰를 설치합니다.

```bash
dotnet add package Mythosia.AI.Rag
dotnet add package Mythosia.AI.Rag.Search.Pixie --prerelease
```

아래의 `embeddings`는 현재 애플리케이션에서 사용하는 `IEmbeddingProvider`입니다. 같은 공급자를 유지해야 검색 변경의 효과와 임베딩 모델 변경의 효과가 섞이지 않습니다.

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
    .UseHybridSearch(new HybridSearchOptions
    {
        VectorWeight = 0.7f,
        CandidateMultiplier = 4,
        RrfK = 60
    }));

RagProcessedQuery result = await rag.QueryAsync("환불 정책은 무엇인가요?");
```

저장소를 사용하는 모든 작업이 끝날 때까지 인코더를 유지하세요. 인코더의 수명은 호출자가 관리하며 `PixieInMemoryStore`가 대신 해제하지 않습니다. 장시간 실행하는 서버에서는 저장소와 함께 유지하고, 종료 시 진행 중인 요청이 끝난 뒤 해제합니다.

같은 `.UseStore(searchStore)` 설정을 `service.WithRag(...)`에서도 사용할 수 있습니다. Agentic RAG도 선택한 검색 경로를 사용합니다. 이 패키지는 검색을 담당하며, 답변 생성에는 기존 RAG·AI API를 연결합니다.

## 희소 검색·의미 검색·하이브리드 검색 선택하기

| RAG 설정 | `PixieInMemoryStore`에서 질문을 처리하는 방식 |
| --- | --- |
| `.UseKeywordSearch()` | PIXIE로 질문을 분석하고 희소 검색을 실행합니다. 의미 벡터용 질문 임베딩 공급자는 호출하지 않습니다. |
| `.UseVectorSearch()` | 기존 의미 임베딩 공급자와 의미 벡터 검색을 사용합니다. |
| `.UseHybridSearch(options)` | 활성화한 두 검색을 실행하고 설정한 가중 RRF로 순위를 합칩니다. |

기존 API 이름인 `UseKeywordSearch()`는 저장소의 텍스트 검색 기능을 선택한다는 뜻입니다. PIXIE 저장소에서는 신경망 희소 검색이 실행됩니다. 질문 추론을 모두 생략하는 것이 아니라 PIXIE가 질문을 처리합니다. RAG의 문서 등록은 질문을 희소 검색만으로 처리하더라도 의미 임베딩을 계속 생성합니다.

`VectorWeight = 0`이면 의미 검색을 위한 질문 임베딩을 생략하고, `VectorWeight = 1`이면 희소 검색을 위한 PIXIE 질문 처리를 생략합니다. `CandidateMultiplier`는 활성 검색별 후보 개수를 늘립니다. RAG 재랭커의 후보 배수와는 다른 설정입니다. 희소 내적 점수·코사인 점수·RRF 결합 점수는 같은 척도나 확률이 아닙니다. 원점수의 크기 대신 정답 문서와 순위 지표로 비교하세요.

## 모델 파일과 실행 제한

패키지에는 버전을 고정한 토크나이저, 라이선스, 출처 기록과 **동적 8비트 양자화를 적용한 파생 ONNX 모델**이 들어갑니다. 채널별 가중치는 UINT8로 표현하며, 파일은 애플리케이션 출력의 `models/pixie`로 복사됩니다. 파생 모델은 패키지 압축 전 약 **190MB**이며, 약 752MB인 공식 FP32 ONNX 원본은 포함하지 않습니다. 실제 메모리 사용량은 모델 파일보다 크며 입력과 실행 설정에 따라 달라집니다. 양자화로 검색 결과가 달라질 수 있으므로 원본 모델의 공개 평가 점수를 이 패키지의 성능으로 제시해서는 안 됩니다.

인코더는 기본적으로 `AppContext.BaseDirectory/models/pixie`에서 파일을 읽습니다. 배포 과정에서 위치를 바꾼다면 디렉터리를 지정하세요. 같은 버전의 파일을 다른 위치에서 읽는 옵션이며 임의의 다른 모델을 받는 옵션이 아닙니다. 생성 시 모델과 토크나이저의 SHA-256 해시를 검증합니다.

```csharp
using var encoder = new PixieSparseEncoder(new PixieOptions
{
    ModelDirectory = modelDirectory,
    MaxSequenceLength = 512,
    IntraOpThreads = 0,
    MinimumWeight = 0
});
```

| 옵션 | 동작 |
| --- | --- |
| `ModelDirectory` | 버전이 고정된 모델 파일이 있는 로컬 디렉터리입니다. 파일이 없을 때 자동으로 다운로드하지 않습니다. |
| `MaxSequenceLength` | 경계 토큰 2개를 포함해 기본 512토큰이며 최대 5,632까지 설정할 수 있습니다. 초과 입력을 몰래 자르지 않고 오류로 알립니다. |
| `IntraOpThreads` | `0`은 ONNX Runtime 기본 스레드 설정입니다. 애플리케이션 동시 요청 수와 함께 조정하세요. |
| `MinimumWeight` | 기본값은 `0`입니다. 높이면 작은 가중치의 희소 항목이 줄지만 검색 재현율도 달라질 수 있습니다. |

긴 문서는 토크나이저의 길이 제한에 맞게 청크로 나누세요. 문자 수와 토큰 수는 같지 않으며, 제한을 높이면 메모리 사용량과 지연 시간이 크게 늘 수 있습니다. 모델·토크나이저·희소 인코딩 옵션을 바꾸면 새 색인을 만드세요. 서로 다른 모델 설정으로 만든 벡터를 섞으면 안 됩니다.

`PixieInMemoryStore`의 의미·희소 색인은 모두 메모리에만 보관하므로 프로그램을 재시작하면 다시 구축해야 합니다. 기존 벡터 저장소 계약을 통한 필터 검색과 문서 교체를 지원하지만, 영구 저장소나 운영 DB 마이그레이션 도구는 아닙니다. 쓰기 작업마다 색인 스냅샷을 다시 만들므로 대량 동시 등록보다는 비교 실험과 작은 문서 집합을 위한 구현입니다. 배치 쓰기는 들어온 레코드의 인코딩을 모두 마친 뒤 새 스냅샷을 한 번에 반영합니다.

인코더는 생성 시 옵션을 복사하고, 추론 메모리 사용을 제한하기 위해 같은 인코더에 들어온 호출을 순차 처리합니다. 비동기 메서드에 `CancellationToken`을 전달하면 대기 중인 호출을 취소하거나 진행 중인 로컬 ONNX 실행의 중단을 요청합니다. 중단은 협조적이며 이미 완료한 저장 작업을 되돌리지는 않습니다.

## 희소 벡터를 직접 확인하기

RAG 흐름 없이 인코더만 사용할 수도 있습니다. `EncodeQueryAsync`와 `EncodeDocumentAsync`는 정렬된 어휘 ID `Indices`와 대응하는 양수 가중치 `Values`를 가진 불변 `PixieSparseVector`를 반환합니다.

```csharp
PixieSparseVector vector = await encoder.EncodeQueryAsync(
    "환불 정책", cancellationToken);

var strongest = Enumerable.Range(0, vector.Indices.Count)
    .OrderByDescending(i => vector.Values[i])
    .Take(10);

foreach (int i in strongest)
    Console.WriteLine($"{encoder.GetToken(vector.Indices[i])}: {vector.Values[i]}");
```

`GetToken`이 보여주는 어휘는 사람이 읽는 완전한 단어가 아니라 부분 단어일 수도 있습니다. 공백 입력은 빈 벡터를 반환하고 특수 토큰 ID는 희소 출력에서 제외합니다. 표시되는 가중치는 검색에 기여한 표현을 이해하는 자료이며, 관련성 확률이나 문장의 사실 여부를 뜻하지 않습니다.

[TelePIX 공식 모델 설명](https://huggingface.co/telepix/PIXIE-Splade-v1.0)은 한국어·영어 학습과 항공우주 분야 특화를 설명합니다. 모델은 Apache-2.0, 패키지 코드는 저장소의 MIT 라이선스를 따르며 모델 라이선스와 출처를 함께 보존합니다. 지원 언어와 공개 평가는 후보를 선정하는 근거이지 우리 문서에서의 검색 성능을 증명하는 결과가 아닙니다.

## 기본 검색을 바꾸기 전에 비교하기

같은 문서, 청크 경계, 의미 벡터, 가공된 질문, 후보 수, 필터, 재랭킹 설정을 사용하세요. 먼저 재랭커 없이 비교해 검색 단계에서 정답 후보가 누락되는지 확인하고, 그다음 같은 재랭커를 연결해 전체 RAG 결과를 비교합니다. 현재 사용 중인 검색이 PostgreSQL trigram·전문 검색이라면 그 저장소와도 비교해야 합니다. InMemory의 BM25는 다른 기준선입니다.

사람이 검토한 관련 문서를 기준으로 Recall@K·nDCG@K와 식별자·제외 조건 실패를 기록하세요. 한국어 바꿔 말하기, 영어 용어, `C#`·`C++`, 제품 코드, 부정 표현, 정답이 없는 질문을 포함하고 문서 등록 시간·질문 지연·메모리를 따로 측정합니다. 작은 합성 사례는 회귀 오류를 찾는 데 도움이 되지만 일반 성능을 입증하거나 논문의 독립된 사례 실험을 대신하지는 못합니다.

정식 [검색 평가 인프라](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Rag.Evaluation/README.md)에서 버전을 지정한 데이터셋과 등록한 검색 방식을 비교합니다. 질문별 순위, 분류별 지표, 지연 시간, 사용 문서와 이전 실행 대비 회귀를 기록합니다. `--dense local-hash`는 결정적 연결 검사이며 신경망 의미 검색의 품질 평가가 아닙니다. `--dense openai`는 실제 의미 임베딩을 사용하고 `OPENAI_API_KEY`가 필요합니다. 비교하는 경로는 같은 캐시의 의미 벡터를 사용합니다. 기존 PIXIE 실행기와 스크립트도 공통 인프라를 호출합니다.

```powershell
./build/test-pixie-search.ps1 -Dense none
./build/test-pixie-search.ps1 -Dense local-hash
# 실제 의미 임베딩 API 비교: OPENAI_API_KEY가 필요합니다.
./build/test-pixie-search.ps1 -Dense openai

# 공통 실행기: 검색 방식과 데이터셋, 지연 시간 반복 횟수를 선택합니다.
./build/test-retrieval-evaluation.ps1 -Methods bm25,pixie -Dense none -Repeat 3
# 같은 조건에서 지표가 0.02(2%포인트)보다 많이 떨어지면 실패합니다.
./build/test-retrieval-evaluation.ps1 -Methods bm25,pixie -Dense none -Baseline artifacts/retrieval-evaluation/PRIOR-RUN/results.json -MaxRegression 0.02
```

새 실행은 `artifacts/retrieval-evaluation` 아래의 고유 폴더에 `results.json`, `summary.md`, `measurements.csv`, `corpus.json`을 남기며 이전 결과를 덮어쓰지 않습니다. 공통 스크립트의 `-Dataset`, 기존 호환 스크립트의 `-Cases`로 다른 문서·질문 집합을 지정합니다. 회귀 비교에는 문서 내용·정답·검색 조건·공통 벡터가 같아야 합니다. 아래의 최초 비교 결과는 과거 기록으로 보존하며 새 기준선으로 자동 변환하지 않습니다. OpenAI 모드는 캐시에 없는 문서 청크와 질문을 API로 전송하고 비용이 발생합니다. 일반 CI는 결정적 테스트와 작은 오프라인 기준선 회귀를 실행하고, `Retrieval Evaluation` 워크플로에서는 오프라인·PIXIE·OpenAI 실행을 직접 선택합니다.

PIXIE는 자연어로 표현한 제외 조건을 강제하지 않으며 기호가 포함된 식별자의 정확한 구분도 보장하지 않습니다. 반드시 지켜야 하는 조건은 명시적인 메타데이터 필터로 적용하세요. 실제 평가 결과를 바탕으로 기본 검색 전환을 결정하고, 색인 재구축과 구형 구현 제거는 그 뒤에 진행합니다. 이번 프리뷰 설치만으로 BM25·trigram이 제거되지는 않습니다.

### 저장소 문서로 실측한 결과 — 2026년 9월 23일

실제 OpenAI 임베딩을 호출해 **공개 한국어 설명서 22개, 동일한 청크 364개, 직접 작성한 질문 32개**로 비교했습니다. 두 하이브리드 검색은 같은 `text-embedding-3-small` 벡터(1,536차원)를 재사용하고 `VectorWeight = 0.5`, `CandidateMultiplier = 2`, `RrfK = 60`을 적용했습니다. 질문 재작성과 재랭킹은 모두 껐습니다. 문서 페이지 단위의 정답 표시는 완전하지 않으며, 독립된 연구 평가나 운영 품질 우위를 증명하는 실험이 아닌 작은 확인용 비교입니다.

| 검색 방식 | Recall@5 | nDCG@10 | 검색 시간 중앙값(ms) |
| --- | ---: | ---: | ---: |
| InMemory BM25 | 0.7188 | 0.6662 | 16.57 |
| PIXIE | 0.9375 | 0.7647 | 24.67 |
| OpenAI 의미 벡터 | 0.8906 | 0.8167 | 1.25 |
| OpenAI 의미 벡터 + BM25 | 0.9219 | 0.7922 | 17.01 |
| OpenAI 의미 벡터 + PIXIE | 0.9688 | 0.8355 | 28.85 |

이 문서 집합에서는 PIXIE 하이브리드가 지정한 정답 문서를 더 잘 찾았지만, 검색 시간 중앙값은 **17.01ms에서 28.85ms**로 늘었습니다. 색인 생성은 **BM25 2.72초, PIXIE 40.91초**였으며 PIXIE 모델 로딩에 별도로 **1.22초**가 걸렸습니다. 검색 시간에는 희소 추론을 포함하지만 미리 만든 의미 임베딩과 네트워크 시간은 포함하지 않습니다. 공통 문서·질문 임베딩 준비에는 8.24초가 걸렸습니다. Windows·.NET 10.0.9·ONNX 내부 스레드 4개 환경의 측정이며, 비교 기준은 **이 라이브러리의 InMemory Lucene BM25**입니다. PostgreSQL trigram·전문 검색과의 비교가 아닙니다. 또한 PIXIE 단독의 nDCG는 의미 벡터 단독보다 낮았으므로, 이 결과가 의미 검색을 제거할 근거는 아닙니다.

재현 기록은 `artifacts/pixie-comparison-openai/summary.md`, `results.json`, `corpus.json`에 있으며 문서·모델 해시, 질문별 결과와 공통 벡터를 포함합니다. 기본 검색을 결정하기 전에 실제 사용할 문서와 검토한 정답으로 다시 비교하세요.

## 소스에서 모델 포함 패키지 만들기

모델 가중치는 Git에 커밋하지 않습니다. 일반 소스 빌드와 결정적 단위 테스트에는 모델 다운로드가 필요하지 않지만, 실제 추론과 패키지 생성에는 필요합니다. 릴리스 모델 준비는 빌드 작업이며 .NET만으로 실행하는 사용자 애플리케이션과는 별개입니다.

Windows에서는 Python 3.12 독립 환경에 고정된 빌드 의존성을 설치하고, 모델 변환을 검증한 다음 실제 패키지 소비자 검사까지 진행합니다.

```powershell
py -3.12 -m venv artifacts/pixie-model-env
./artifacts/pixie-model-env/Scripts/python.exe -m pip install -r build/pixie-model-requirements.txt
./artifacts/pixie-model-env/Scripts/python.exe build/prepare-pixie-model.py --validate
./build/test-pixie-model.ps1
./build/pack-pixie.ps1
```

준비 스크립트는 버전이 고정된 공식 파일만 다운로드하고 해시를 확인한 뒤 8비트 모델을 만듭니다. 패키지 생성은 파일 누락·해시 불일치를 거부하고 배포 크기 기준을 검사하며, 기본적으로 새 패키지를 설치한 별도 소비자에서도 실행합니다. 로컬 패키지를 생성하는 작업이며 게시하지 않습니다.

`test-pixie-model.ps1`은 모델 경로를 직접 지정해 실제 로컬 ONNX 테스트를 실행합니다. 최소 5개 테스트가 모두 실행되고 통과해야 하며, 건너뛴 테스트는 검증 성공으로 세지 않습니다. TRX 결과는 `artifacts/test-results/pixie-local-model`에 저장합니다. 결정적 단위 테스트 및 검색 품질 비교와는 별도의 검사입니다. 현재 검증에서는 **결정적 테스트 32개와 실제 로컬 모델 테스트 5개**가 통과했습니다.

검증한 로컬 NuGet 패키지는 **184,022,040바이트(약 184MB)**입니다. 새 소비자에서 패키지를 직접 참조하거나 다른 NuGet 패키지를 통해 간접 참조한 경우 모두 포함된 모델을 읽고 추론했으며, `dotnet publish` 출력에서도 실행을 확인했습니다. 직접 소비자는 검색까지 검증했습니다. 근거는 `artifacts/pixie-package/pixie-package-validation.json`에 있습니다. 패키지 사용 검증이며 NuGet 게시를 완료했다는 뜻은 아닙니다.

현재 소스의 `Mythosia.VectorDb.Abstractions`에는 새 텍스트·하이브리드 검색 계약이 추가되어 있지만 버전 표기는 아직 `4.0.1`입니다. 이미 게시된 같은 버전의 패키지가 이 소스 계약을 대신하지는 못합니다. `pack-pixie.ps1`은 소비자 검증을 위해서만 로컬 의존 패키지를 만듭니다. 실제 PIXIE 게시 전에는 변경된 계약의 릴리스 버전을 정하고 의존 패키지와 맞춰야 합니다. 이번 구현에서 기존 버전을 올리지는 않으며, 기존 GitHub 게시 워크플로에도 새 패키지 배포는 아직 연결하지 않았습니다.
