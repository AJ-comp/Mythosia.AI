# 검색 평가 인프라

검색 구현을 바꾸었을 때 필요한 것은 실행 성공 여부만이 아닙니다. 같은 문서와 질문에서 정답 문서가 더 잘 검색되는지, 어느 종류의 질문이 나빠졌는지, 검색 시간이 얼마나 달라졌는지를 반복해서 확인해야 합니다. 이 프로젝트는 문서·정답·검색 조건을 고정하고 여러 검색 구현을 같은 기준으로 비교하는 저장소의 공통 평가 실행기입니다.

`Mythosia.AI.Rag.Evaluation`은 .NET 10 테스트 도구이며 NuGet 제품 패키지로 게시하지 않습니다. 제품의 기본 검색 방식을 변경하지 않고 데이터셋·검색 어댑터·평가 결과를 확장할 수 있습니다. 예전 PIXIE 비교 프로젝트와 `build/test-pixie-search.ps1`도 이 실행기에 위임하므로 기존 호출을 계속 사용할 수 있습니다.

## 1. 모델과 API 없이 먼저 실행하기

저장소 루트의 PowerShell에서 다음 명령을 실행합니다. 첫 실행에는 .NET 의존성 복원이 필요할 수 있지만 이 평가 모드는 모델을 다운로드하거나 임베딩 API를 호출하지 않습니다.

```powershell
./build/test-retrieval-evaluation.ps1 `
    -Dataset tests/Mythosia.AI.Rag.Evaluation/Datasets/smoke.json `
    -Methods bm25,dense,hybrid-bm25 `
    -Dense local-hash
```

`smoke.json`은 테넌트 격리, 한국어, `C#`·`C++`, 관련성 등급, 정답 없는 질문을 포함한 작은 연결·회귀 검사 자료입니다. `local-hash`는 `LocalEmbeddingProvider`의 결정적 1,024차원 해시 벡터이며 신경망 의미 검색 품질을 측정하는 대체재가 아닙니다.

결과 경로는 실행이 끝날 때 표시합니다. 경로를 생략하면 `artifacts/retrieval-evaluation/<UTC 시각>-<고유 ID>/` 아래에 새 실행 이력을 만듭니다. 직접 `-OutputDirectory`를 지정할 수도 있지만 이미 존재하는 디렉터리는 거부하므로 이전 실험이 덮어써지지 않습니다.

지표 계산·데이터 검증·캐시·실행기 연결 검사는 별도의 단위 테스트로 실행합니다.

```powershell
dotnet test --project tests/Mythosia.AI.Rag.Evaluation.Tests/Mythosia.AI.Rag.Evaluation.Tests.csproj `
    --configuration Release --filter "TestCategory=Unit"
```

## 2. PIXIE와 실제 의미 임베딩 비교하기

PIXIE를 선택하려면 소스 체크아웃에 모델 파일을 준비해야 합니다. [PIXIE 사용 안내의 모델 준비 절차](../../docs/ko/rag-pixie-search.md#소스에서-모델-포함-패키지-만들기)를 따르세요. 모델 준비용 Python과 다운로드는 빌드 단계에서 필요하며 검색 평가 자체는 로컬 .NET/ONNX로 실행합니다.

희소 검색끼리 비교합니다. 기본 데이터셋은 `Datasets/repository-docs.json`의 한국어 라이브러리 설명서와 사람이 작성한 정답 목록입니다.

```powershell
./build/test-retrieval-evaluation.ps1 `
    -Methods bm25,pixie `
    -Dense none `
    -Warmup 1 -Repeat 3
```

OpenAI 임베딩을 사용하려면 실행 환경에 `OPENAI_API_KEY`를 설정한 다음 명시적으로 `-Dense openai`를 선택합니다. 캐시에 없는 문서 청크와 질문을 외부 API에 전송하고 비용이 발생합니다. 키는 결과 파일에 기록하지 않습니다.

```powershell
# 실행 환경에 OPENAI_API_KEY가 설정되어 있어야 합니다.
./build/test-retrieval-evaluation.ps1 `
    -Methods bm25,pixie,dense,hybrid-bm25,hybrid-pixie `
    -Dense openai `
    -EmbeddingModel text-embedding-3-small `
    -Dimensions 1536 `
    -Warmup 1 -Repeat 3
```

동일한 문서 청크와 질문의 벡터를 한 번 준비한 뒤 모든 검색 방식에 공유합니다. `-EmbeddingCache`의 기본값은 `artifacts/retrieval-evaluation/cache`입니다. 공급자·모델·차원 정보와 텍스트 해시로 캐시를 구분하고, 캐시의 차원·유한값·0벡터 여부를 검증합니다. 캐시 재사용으로 API 호출과 비용을 줄이며 실행 결과에 재사용·생성 벡터 수를 기록합니다.

| 검색 ID | 동작 | 필요한 자원 |
| --- | --- | --- |
| `bm25` | InMemory Lucene BM25 텍스트 검색 | 없음 |
| `pixie` | PIXIE 신경망 희소 검색 | 로컬 PIXIE 모델 |
| `dense` | 공통 의미 벡터 검색 | `local-hash` 또는 `openai` |
| `hybrid-bm25` | 공통 벡터 + BM25, 가중 RRF | `local-hash` 또는 `openai` |
| `hybrid-pixie` | 공통 벡터 + PIXIE, 가중 RRF | 로컬 PIXIE 모델 및 의미 벡터 |

여기서 BM25는 PostgreSQL의 전문 검색·trigram과 다릅니다. PostgreSQL을 교체할지를 판단하려면 해당 저장소를 별도 어댑터로 연결해 같은 데이터셋에서 평가해야 합니다. 질문 재작성·재랭킹·최종 답변 생성은 현재 기본 비교에 포함하지 않습니다.

## 3. 데이터셋 확장하기

새 데이터셋 JSON을 추가한 뒤 `-Dataset`으로 선택합니다. 다음 예시는 파일 없이 그대로 저장해 사용할 수 있습니다.

```json
{
  "schemaVersion": 1,
  "id": "customer-support-ko",
  "version": "1",
  "description": "환불 질문과 회사별 격리를 검증하는 검토용 사례",
  "documents": [
    {
      "id": "a-refund",
      "text": "A 회사는 구매 후 7일 이내 환불할 수 있습니다. 영수증을 제출하세요.",
      "metadata": { "tenant": "A", "language": "ko" }
    },
    {
      "id": "a-contact",
      "text": "A 회사의 환불 담당자에게 주문 번호와 영수증을 보내 상담할 수 있습니다.",
      "metadata": { "tenant": "A", "language": "ko" }
    },
    {
      "id": "b-refund",
      "text": "B 회사는 구매 후 30일 이내 환불할 수 있습니다.",
      "metadata": { "tenant": "B", "language": "ko" }
    }
  ],
  "cases": [
    {
      "id": "refund-period",
      "query": "산 지 며칠까지 환불할 수 있나요?",
      "category": "paraphrase",
      "language": "ko",
      "filter": { "tenant": "A" },
      "judgments": { "a-refund": 3, "a-contact": 1, "b-refund": 0 }
    },
    {
      "id": "unknown-company",
      "query": "환불 기간이 궁금합니다.",
      "category": "no-answer-filter",
      "language": "ko",
      "filter": { "tenant": "missing" },
      "judgments": {}
    }
  ]
}
```

문서는 `text` 또는 `path` 중 하나만 지정합니다. `path`는 JSON 파일의 위치가 아니라 데이터 루트 기준입니다. 기본 데이터 루트는 저장소 루트이므로 `"path": "docs/ko/rag-filtering.md"`처럼 지정합니다. 자료를 저장소에 복사할 필요 없이 `-DataRoot`로 별도 자료 디렉터리를 지정할 수 있습니다.

```powershell
./build/test-retrieval-evaluation.ps1 `
    -DataRoot C:/evaluation-data `
    -Dataset C:/evaluation-data/support/dataset.json `
    -Methods bm25 -Dense none
```

이 예시에서 문서의 `"path": "manuals/refund.md"`는 `C:/evaluation-data/manuals/refund.md`를 읽습니다. 직접 .NET CLI로 실행할 때 같은 옵션은 `--data-root`입니다. `-Dataset`의 상대 경로는 계속 저장소 루트 기준이므로 외부 JSON은 위처럼 절대 경로로 지정하면 분명합니다.

데이터셋 계약은 다음과 같습니다.

- `schemaVersion`은 현재 `1`만 지원합니다. `version`은 평가 자료의 개정판을 나타내는 문자열이며 둘 다 명시해야 합니다.
- 문서 ID와 질문 ID는 각각 고유해야 합니다. 정답은 실제 문서 ID를 참조해야 합니다.
- `judgments`의 정수 등급은 `0..30`입니다. `0`은 관련 없음, 양수는 관련 있음입니다. 일반적으로 `0/1/2/3`처럼 작은 척도로 검토 기준을 통일하세요.
- `judgments: {}` 또는 양성 등급이 없는 질문은 의도적으로 정답이 없는 사례입니다. 정답 필드 누락을 정답 없음으로 취급하지 않습니다.
- `filter`는 문자열 메타데이터의 **모든 조건을 만족하는 동등 비교(AND)**입니다. 질문 언어인 `language`만 지정해도 필터가 자동 생성되지는 않습니다.
- 양성 정답이 질문의 필터를 통과하지 못하면 데이터셋을 거부합니다. 필터 밖 문서에 `0`을 표시하는 것은 허용합니다.
- `__evaluation_document_id`, `document_id`는 문서·청크 매핑을 위해 실행기가 사용하는 예약 메타데이터 키입니다.
- 절대 문서 경로, `..` 경로, 데이터 루트 밖의 파일, 심볼릭 링크·reparse point 경유는 거부합니다. JSON 속성 중복과 잘못된 속성 이름도 거부합니다.

기존 PIXIE 벤치마크의 `sources`/`relevantSources` 형식도 읽습니다. 관련 문서를 등급 `1`로 바꾸고 파일명을 데이터셋 ID, `legacy-1`을 버전으로 기록합니다.

```json
{
  "description": "기존 이진 정답 형식",
  "sources": ["docs/ko/rag-filtering.md"],
  "cases": [
    {
      "id": "tenant",
      "category": "filter",
      "query": "회사별 문서를 분리해서 검색하려면?",
      "relevantSources": ["docs/ko/rag-filtering.md"]
    }
  ]
}
```

저장소 설명서 사례는 작은 확인용 자료입니다. 운영 전환이나 논문의 실험에는 실제 사용 문서, 독립적으로 검토한 질문·정답, 충분한 질문 수가 필요합니다. 파라미터 조정에 사용한 자료와 최종 평가 자료를 나누고, 정답을 일부만 표시했다면 미표시 관련 문서가 오답으로 계산될 수 있음을 함께 기록하세요.

## 4. 측정 조건과 결과 읽기

| PowerShell 옵션 | 기본값 | 의미 |
| --- | ---: | --- |
| `-DataRoot` | 저장소 루트 | 데이터셋의 상대 문서 경로를 해석할 루트 |
| `-ChunkSize`, `-ChunkOverlap` | `450`, `50` | 모든 방식에 공통 적용하는 텍스트 분할 크기·중첩 |
| `-CandidateLimit` | `50` | 검색 방식이 반환하는 최대 청크 수 |
| `-RecallK`, `-RankK` | `5`, `10` | Recall/Hit와 MRR/nDCG의 문서 순위 컷오프 |
| `-VectorWeight` | `0.5` | 하이브리드 의미 벡터 가중치 |
| `-CandidateMultiplier`, `-RrfK` | `2`, `60` | 하이브리드 후보 확장 배수·RRF 상수 |
| `-Warmup`, `-Repeat` | `1`, `1` | 검색 방식별 워밍업 수·전체 질문 반복 수 |
| `-Threads` | `4` | PIXIE ONNX 내부 스레드 수 |
| `-ModelDirectory` | 소스의 `models/pixie` | 준비된 PIXIE 모델 위치 |
| `-NoBuild` | 사용 안 함 | 이미 빌드한 Release 실행기를 재사용 |

검색 결과의 청크를 원래 문서 ID로 바꾸고 **최초 등장 순서를 유지하며 중복 문서를 제거한 후** 지표를 계산합니다. 후보 수는 청크 수이므로 `CandidateLimit = 50`이 서로 다른 문서 50개를 보장하지는 않습니다.

결과에는 실제 청크 ID·내용의 `chunkFingerprint`와 `maxChunkLength`도 기록합니다. 청킹 구현이 바뀌면 희소 검색만 실행해도 비교 조건이 달라졌음을 감지합니다. 수정 전후 효과를 실험하려면 원문·질문을 고정한 별도 실행을 비교하고, 변경된 조건을 검토한 뒤 CI 기준 결과를 갱신하세요. 임베딩 캐시는 텍스트 해시가 같은 청크만 재사용합니다.

| 지표 | 계산·해석 |
| --- | --- |
| Recall@K | 상위 K개 문서에서 찾은 양성 정답 수 / 전체 양성 정답 수 |
| Hit@K | 상위 K개에 양성 정답이 하나라도 있으면 1, 없으면 0 |
| MRR@K | K위 안에서 첫 양성 정답 순위의 역수. 없으면 0 |
| nDCG@K | 등급별 이득 `2^grade - 1`에 순위 할인을 적용하고 이상적인 순서로 정규화 |
| No-answer FP | 정답 없는 질문에 검색 결과를 하나라도 반환했는지. 전체 값은 그 비율 |

Recall/Hit는 `RecallK`, MRR/nDCG는 `RankK`를 사용합니다. 전체와 `category`별 값은 질문마다 같은 비중을 주는 macro 평균입니다. 정답 없는 질문은 Recall/MRR/nDCG/Hit에서 제외하고 별도 오탐률로 집계합니다. 해당 종류의 질문이 없으면 그 지표는 `null`/`n/a`이며 0점이나 만점으로 만들지 않습니다. 정답 없음 오탐률은 검색 결과 반환 여부이고, 최종 LLM 답변의 환각률이 아닙니다.

워밍업은 검색 방식마다 첫 질문으로 수행하고 측정에서 제외합니다. `Repeat`는 **지연 시간을 반복 측정**하기 위한 설정입니다. 검색 품질은 첫 측정 반복의 질문별 결과만 사용하므로 반복 수를 늘려도 정답 사례 수나 통계적 표본 수가 늘지 않습니다. 반복 사이 문서 순위가 변했는지도 결과에 기록합니다. 실행기는 질문·반복에 따라 검색 방식의 실행 순서를 바꿉니다.

검색 중앙값과 P95에는 PIXIE 질문 인코딩이 포함되지만, 미리 준비한 의미 임베딩 생성·네트워크 시간은 포함하지 않습니다. 임베딩 준비, 모델 로드, 색인 생성 시간은 따로 기록합니다. 메모리는 모든 색인·벡터·모델·결과 객체를 포함한 전체 프로세스 값이며 검색 방식별 메모리 사용량이 아닙니다.

각 실행 디렉터리에는 다음 파일이 생깁니다.

| 파일 | 내용 |
| --- | --- |
| `summary.md` | 전체·분류별 품질, 지연 시간, 회귀 비교 요약 |
| `results.json` | 스키마 2 보고서: 설정, 질문별 청크·문서 순위와 점수, 모든 반복, 지표, 환경, 코드 리비전·실행 어셈블리 해시 |
| `measurements.csv` | 방식·질문·분류·반복별 Recall/MRR/nDCG 및 시간 |
| `corpus.json` | 실제 평가 데이터셋, 문서 청크, 공유 문서·질문 벡터 |
| `run.lock` | 같은 출력 디렉터리에 동시에 쓰지 않기 위한 실행 표식 |

결과에는 원문과 질문, 벡터가 포함되므로 해당 문서를 공유할 수 있는 범위에서 결과 파일도 관리하세요. 결과·캐시는 Git에 자동으로 추가하지 않습니다.

## 5. 이전 실행과 회귀 비교하기

같은 조건의 이전 `results.json`을 `-Baseline`으로 지정합니다. 기본 허용 회귀는 **절대 지표 차이 0.02**, 즉 2%포인트입니다. 예를 들어 0.90에서 0.88은 허용 범위이며 0.87은 실패입니다. 정답 없음 오탐률은 높아지는 방향을 악화로 판단합니다.

```powershell
./build/test-retrieval-evaluation.ps1 `
    -Dataset tests/Mythosia.AI.Rag.Evaluation/Datasets/smoke.json `
    -Methods bm25,dense,hybrid-bm25 `
    -Dense local-hash `
    -Baseline tests/Mythosia.AI.Rag.Evaluation/Datasets/Baselines/smoke-local-hash.json `
    -MaxRegression 0.02
```

`Datasets/Baselines/smoke-local-hash.json`은 검토한 결정적 smoke 기준선입니다. 개별 실험에서는 이전 실행 디렉터리의 `results.json`을 그대로 사용할 수 있습니다. 기준선은 자동으로 최신 결과로 교체하지 않습니다. 의도적인 변경이면 질문별 순위와 변화 원인을 검토한 뒤 기준선을 별도로 갱신합니다.

비교 전에 데이터셋 본문·정답·버전, 청크·후보·지표·하이브리드 설정, 의미 벡터 정보와 실제 벡터 값에서 만든 호환성 fingerprint를 확인합니다. 검색 방식 ID 집합과 사례 수도 같아야 합니다. 조건이 다르면 **비교 불가를 실패로 처리**합니다. 결과 지표가 누락되거나 비정상이어도 통과시키지 않습니다.

검색 구현을 수정해 회귀를 발견하는 것이 목적이므로 실행 어셈블리 해시와 어댑터 `Identity`는 결과에 기록하되 fingerprint의 일치 조건으로 사용하지 않습니다. 모델이나 검색 알고리즘을 바꾼 실험은 이 정보를 함께 살펴 변경 대상을 확인하세요. 기존 PIXIE 벤치마크의 이전 스키마 보고서는 역사적 실험 기록이며 새 기준선으로 바로 사용할 수 없습니다.

현재 자동 회귀 판정은 방식별 전체 품질 지표에 적용합니다. 분류별 결과와 반복 안정성·지연 시간은 보고서에서 검토할 수 있지만 별도의 자동 임계값으로 판정하지는 않습니다. 기준선을 지정하지 않은 실행의 성공은 평가 완료를 뜻하며 이전보다 품질이 좋아졌다는 판정이 아닙니다.

실행기 종료 코드는 정상/회귀 통과 `0`, 입력·실행 오류 `1`, 회귀 또는 비교 조건 불일치 `2`, 취소 `130`입니다. PowerShell 스크립트는 0이 아닌 종료 코드를 오류로 전달합니다. 실패 결과가 작성되었다면 새 실행 디렉터리에서 원인을 확인할 수 있습니다.

## 6. 다른 검색 엔진을 연결하기

새 엔진은 `IEvaluationMethod`를 구현하고 `EvaluationMethodRegistry`에 등록합니다. 코퍼스 준비·같은 임베딩 공유·지표 계산·결과 저장을 다시 작성할 필요가 없습니다. 다음 코드는 어댑터 구조를 보여주기 위해 기존 InMemory BM25를 별도 ID로 연결하는 실행 가능한 예시입니다.

```csharp
using Mythosia.AI.Rag.Evaluation;
using Mythosia.VectorDb;
using Mythosia.VectorDb.InMemory;

// 이 예제의 프로젝트는 Mythosia.AI.Rag.Evaluation.csproj를 ProjectReference합니다.
var options = EvaluationOptions.Parse(
[
    "--root", @"C:/projects/packages/Mythosia.AI",
    "--dataset", "tests/Mythosia.AI.Rag.Evaluation/Datasets/smoke.json",
    "--methods", "team-bm25",
    "--dense", "none"
]);

EvaluationRunResult result = await EvaluationRunner.RunAsync(
    options,
    configureMethods: registry => registry.Register(
        "team-bm25",
        context => new TeamBm25Method(context)));

Console.WriteLine(result.OutputDirectory);

sealed class TeamBm25Method(EvaluationContext context) : IEvaluationMethod
{
    private readonly InMemoryVectorStore store = new();

    public string Identity => "team-bm25/v1";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await store.UpsertBatchAsync(context.Records, cancellationToken);
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        EvaluationQuery query,
        CancellationToken cancellationToken)
    {
        return store.TextSearchAsync(
            query.Query,
            context.Options.CandidateLimit,
            EvaluationContext.CreateFilter(query),
            cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        store.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

이 확장 방식은 코드에서 테스트 프로젝트를 참조해 등록하는 방식입니다. 런타임에 임의 DLL을 자동으로 읽는 플러그인 로더는 아닙니다. 저장소의 기본 CLI에도 넣으려면 `EvaluationMethodRegistry.CreateDefault()`에 등록하세요. 중복 ID는 거부하며 사용자 등록 콜백은 기본 등록을 덮어쓰지 않습니다.

의미 벡터가 필요한 어댑터는 등록 시 `requiresDense: true`를 지정합니다. `context.Records`의 각 문서 청크에 동일한 벡터가 준비되고 `context.GetQueryVector(query.Id)`에서 질문 벡터를 얻을 수 있습니다. 레코드·메타데이터·벡터·설정은 복사본을 전달하므로 한 어댑터의 변경이 다른 어댑터에 영향을 주지 않습니다. `EvaluationQuery`에는 질문과 필터만 있고 정답 판정은 들어 있지 않습니다. 정답은 평가기가 보관하며 검색 결과를 채점할 때만 사용합니다.

어댑터는 다음 계약을 지켜야 합니다.

- 초기화 시 공통 `context.Records`를 등록하고 엔진별 준비를 끝냅니다.
- 검색 결과는 `CandidateLimit` 이하의 청크를 순위 순서대로 반환합니다. 점수는 유한한 값이어야 합니다.
- 공통 레코드의 예약 문서 메타데이터를 유지합니다. 알 수 없는 문서 ID나 필터 밖 문서를 반환하면 실행기가 오류로 처리합니다.
- 필터는 엔진의 후보 선정 단계에 적용해야 합니다. 상위 후보를 자른 뒤 제외하면 검색 품질 자체가 달라질 수 있습니다.
- 취소 토큰을 내부 검색에 전달하고 자신이 만든 연결·색인을 `DisposeAsync`에서 정리합니다. 실행기가 소유한 `EvaluationContext`는 어댑터에서 폐기하지 않습니다.
- 외부 DB·외부 API 어댑터를 추가하면 데이터 전송과 자원 정리도 그 어댑터의 동작입니다. 해당 자원이 필요한 수동 테스트로 분리해 실행하세요.

`Identity`에는 엔진·모델·구현 설정을 식별할 수 있는 문자열을 기록합니다. 같은 질문의 결과가 변했을 때 어떤 구현을 실행했는지 추적할 수 있습니다.

## 7. GitHub에서 반복 실행하기

일반 CI와 NuGet 게시 검증에는 평가 단위 테스트와 `smoke.json`의 오프라인 실행이 연결됩니다. 검토한 `smoke-local-hash.json` 기준선에 대해 허용 회귀 0.02로 비교하므로 단순 실행 성공뿐 아니라 지표 악화도 검사합니다. 자동 검사에서 PIXIE 모델 다운로드나 유료 임베딩 호출을 시작하지 않습니다.

전체 비교는 GitHub Actions의 **Retrieval Evaluation** 수동 워크플로를 실행합니다. [워크플로 정의](../../.github/workflows/retrieval-evaluation.yml)에 모드·데이터셋 경로·반복 수·선택적 기준선·허용 회귀 값을 지정할 수 있습니다.

| 모드 | 실행 방식 | 추가 준비 |
| --- | --- | --- |
| `offline` | BM25, local-hash dense, 두 방식의 hybrid | 없음 |
| `pixie` | BM25와 PIXIE | 고정 모델 준비 및 해시 검증 |
| `openai` | BM25, PIXIE, OpenAI dense, 두 hybrid | 모델 준비 및 저장소 secret `OPENAI_API_KEY` |

`openai`를 명시적으로 선택한 수동 실행만 OpenAI 키를 전달받고 유료 API를 사용할 수 있습니다. 워크플로 입력을 환경 변수로 전달하며 키를 명령 인수나 보고서에 넣지 않습니다. 모델 캐시가 있어도 사용 전 해시를 검증합니다.

실행 요약은 Actions 화면에 표시하고, 전체 보고서·TRX는 실행 ID별 artifact로 업로드합니다. 수동 비교 결과 보존 기간은 30일입니다. 워크플로의 `baseline`은 체크아웃 안에 존재하는 기준선 경로여야 하므로 이전 artifact를 새 기준선으로 사용할 때는 검토 후 별도로 보관해야 합니다. 워크플로가 과거 artifact를 찾아 자동으로 기준선을 바꾸지는 않습니다.

## 구성 파일 찾기

| 파일 | 확장 지점 |
| --- | --- |
| `EvaluationModels.cs`, `DatasetLoader.cs` | 데이터셋 계약·검증·내용 fingerprint |
| `EvaluationMethods.cs` | 검색 어댑터 계약, 기본 등록, 공유 검색 준비 |
| `DenseEmbeddingCache.cs` | 공급자별 공통 벡터 준비·재사용 |
| `RetrievalMetrics.cs`, `RegressionComparer.cs` | 문서 단위 지표·회귀 판정 |
| `EvaluationOptions.cs`, `EvaluationRunner.cs` | 실행 조건, 반복 측정, 결과 저장 |
| `Datasets/` | 버전이 있는 평가 자료와 검토한 기준선 |
| `../Mythosia.AI.Rag.Evaluation.Tests/` | 결정적 단위·연결 검사 |
| `../../build/test-retrieval-evaluation.ps1` | 공통 PowerShell 진입점 |

단순 문서·질문 추가는 데이터셋에서 시작하세요. 새 검색 엔진은 어댑터로 추가하고, 측정 기준을 바꾸는 경우에는 지표 테스트와 비교 계약을 함께 갱신해 과거 숫자와 다른 정의를 섞지 않도록 하세요.
