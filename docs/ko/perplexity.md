# Perplexity: 출처 있는 답변, 검색과 임베딩

최신 정보를 바탕으로 답하고 사용자가 출처를 확인해야 할 때 Perplexity를 사용합니다. `PerplexityService`는 Agent API를 호출하며, 독립 검색과 임베딩은 직접 선택한 답변 모델의 검색 기반을 구성할 때 사용합니다.

## 필요한 작업부터 선택하기

최신 정보로 답하는 일, 웹 문서 목록을 가져오는 일, 직접 관리하는 문서 색인의 벡터를 만드는 일은 서로 다릅니다. 검색할 때마다 답변 모델을 호출하기보다 실제 작업을 맡을 구성 요소를 선택하세요.

| 필요한 작업 | 구성 요소 |
| --- | --- |
| 출처를 포함한 조사 답변 | `PerplexityService` |
| 다른 모델이나 화면에서 사용할 웹 문서 목록 | `PerplexitySearchClient` |
| 일반 RAG의 독립적인 문단 벡터 | `PerplexityEmbeddingProvider` |
| 같은 문서의 이웃 청크 관계를 반영하는 벡터 | `PerplexityContextualizedEmbeddingProvider` |

`Mythosia.AI`를 설치합니다. 임베딩 예제에는 `Mythosia.AI.Rag`도 필요합니다. API 키와 애플리케이션이 관리하는 `HttpClient`를 준비하세요. 예제의 `apiKey`, `httpClient`, `cancellationToken`은 애플리케이션에서 전달한 값입니다.

## Agent 프리셋으로 답변하기

프리셋은 모델·지침·도구·추론 수준·토큰 예산을 조합한 설정입니다. 간단한 확인에는 `Fast`, 일상적인 조사에는 `Low`, 여러 단계를 거치는 비교에는 `Medium`, 깊은 조사에는 `High` / `XHigh`를 선택합니다. `WideResearch`는 넓은 범위의 조사에 사용하며, 오래 걸릴 작업에는 백그라운드 실행을 권장합니다. 이 값들은 모델 ID가 아닌 프리셋입니다.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low
    });

await using var run = await service.StartRunAsync(
    "최신 배터리 재활용 방법을 비교하고 출처를 제시해 주세요.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

결과만 필요하면 `GetCompletionAsync`, 기존 스트리밍 코드에서는 서비스의 `StreamAsync`, 실행 중 출력을 확인하고 취소하려면 `StartRunAsync`를 사용합니다. `(await run.Result).Text`는 출력된 답변 텍스트를 누적합니다. 인용 이벤트를 읽지 않아도 `run.Citations`와 `LastCitations`에 출처가 남습니다. 추론 이벤트에는 제공자가 공개한 내용만 들어오며 선택한 모델에 따라 달라집니다.

`AIRunResult.RequestedModel`은 제공자별 모델 재정의를 포함해 실제 요청에 넣는 단일 명시 모델을 시작 시 캡처한 값입니다. 프리셋·프로필이나 서버 모델 라우팅으로 선택하여 단일 모델 필드를 보내지 않으면 `null`입니다(예: Perplexity `Models` 목록). 이는 실제 응답 모델인 `Model`과 별개입니다.

## 조사 범위와 도구 제어하기

`WithPerplexityOptions(...)`는 서비스에 지속되는 설정이며 논리적 요청마다 복사됩니다. 공통 `WithReasoning(...)`과 `WithWebSearch(...)`는 다음 논리적 요청과 그 요청의 로컬 도구 라운드·타입 출력 복구에 적용됩니다. 내부 RAG 검색어 재작성에는 최종 답변용 검색 설정이 전달되지 않습니다.

`UsePreset(...)`로 프리셋을 간단히 선택할 수 있습니다. 프리셋·프로필은 자체 모델을 선택하며 `ModelOverride`로 명시적으로 바꿉니다. `DisableWebSearch`는 어댑터 기본 도구만 제거하고 프리셋의 내장 검색까지 끄지는 않습니다. Agent 추론은 모델에 따라 `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`를 사용합니다. `None`은 거절되며 직접 Sonar 모델의 명시적 추론 수준도 거절됩니다. 내부 `DisableReasoning`은 가능한 낮은 수준을 사용하거나 값을 생략하며, 완전히 꺼짐을 보장하지 않습니다.

| 설정 | 사용 목적 |
| --- | --- |
| `Preset` / `ModelOverride` | 조사 설정을 선택하거나 provider/model ID로 해당 모델을 명시적으로 바꿉니다. |
| `MaxSteps` | 제공자가 실행하는 도구 루프의 한도를 정합니다. 0은 제공자 기본값이며, 로컬 함수 후속 요청을 제한하는 라이브러리의 `WithMaxRounds`와 별개입니다. |
| `ReasoningEffort` | 모델이 추론에 들이는 양을 조절합니다. `Auto`는 값을 생략하며 실제 모델이 지원하는 단계만 사용할 수 있습니다. |
| `DisableWebSearch` / `Tools` | 어댑터의 기본 웹 도구와 명시적으로 선택한 호스팅 도구를 제어합니다. |
| `Models` | 우선순위대로 1~5개 대체 모델을 지정합니다. 단일 모델 설정보다 우선하며 요청 기능은 선택한 모든 모델과 호환되어야 합니다. |
| `Profile` | 서버에 저장된 설정을 사용하고 필요하면 버전을 고정합니다. `Preset`과 함께 사용할 수 없습니다. |
| `ServiceTier` | 기본·flex·priority 처리를 요청합니다. 모델이 지원하지 않는 단계는 제공자가 무시할 수 있습니다. |
| `Skills` | 기본·인라인 또는 미리 업로드한 사용자 스킬을 전달합니다. 사용자 리소스는 Perplexity 계정에 속합니다. |
| `LanguagePreference` / `PromptCacheKey` | 응답 언어 또는 캐시 라우팅 힌트를 지정합니다. 힌트가 캐시 적중을 보장하지는 않습니다. |
| `PreviousResponseId` / `Store` | 완료된 제공자 응답을 이어가거나 조회 가능 여부를 정합니다. 이어갈 때는 `StatelessMode`로 새 입력만 전달하세요. `Store = false`는 제공자의 저장 자체를 끄지 않습니다. |

`PerplexityHostedTool`에는 지원하는 `Type`과 문서에 정의된 JSON 호환 `Parameters`를 전달합니다: `web_search`, `fetch_url`, `finance_search`, `people_search`, `sandbox`, `mcp`. MCP 서버와 관리형 커넥터는 제공자를 통해 실행되므로 자격 증명·권한·계정 리소스가 해당 연결과 일치해야 합니다. 애플리케이션 함수는 기존 `Functions` / 함수 빌더 API로 등록합니다. 호스팅 도구 단계와 로컬 핸들러는 실행 주체가 다릅니다.

`PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, `Connector`로 도구 설정을 만듭니다. MCP 호출은 승인 대기 없이 실행되므로 필요하면 `allowedTools`로 제한합니다. 커넥터는 제공자의 프리뷰 기능이며 이미 연결된 통합 리소스를 참조합니다.

```csharp
service.WithPerplexityOptions(new PerplexityAgentOptions
{
    Preset = PerplexityPreset.Low,
    MaxSteps = 8,
    Tools = new List<PerplexityHostedTool>
    {
        PerplexityHostedTools.WebSearch(),
        PerplexityHostedTools.FetchUrl()
    }
});
string comparison = await service.GetCompletionAsync(
    "프로젝트 문서를 읽고 관련 기능을 비교해 주세요.");
```

도구·추론·이미지·스키마 호환성은 선택한 모델에 따라 달라집니다. 공통 `WithFileSearch`는 Perplexity 벡터 저장소 어댑터가 아닙니다. 샌드박스에서 생성한 파일, 업로드한 첨부 파일, 원격 MCP 데이터는 별도 리소스이며 공통 파일 검색 저장소로 자동 전환되지 않습니다.

## 출처, 이미지와 구조화된 답변

애플리케이션에 JSON 필드가 필요하면 타입 지정 completion 또는 타입 지정 스트리밍을 사용합니다. 어댑터는 원본 스키마를 전송하고 기존 복구 절차를 유지합니다. 후속 요청을 위해 원본 응답 항목과 도구 식별자를 보존하므로 프로토콜 이력을 임의로 삭제하거나 순서를 바꾸지 마세요. 이미지 입력은 `Message`와 `ImageContent`에 JPEG/PNG/WebP/GIF 바이트 또는 HTTPS URL을 넣으며 모델의 지원 여부를 따릅니다. 이미지 입력은 이미지 생성 요청과 다릅니다.

원본 응답 기록은 히스토리 메타데이터에 보관하지만 후속 요청에는 허용된 `message`, `function_call`, `function_call_output` 입력 항목만 재전송하며, 제공자 쪽의 전체 도구 실행 상태를 이어가려면 `PreviousResponseId`를 사용합니다.

인용은 웹 검색 결과나 다른 제공자 출처를 나타낼 수 있습니다. 위치 값은 제공자 응답의 개별 콘텐츠 기준이며 Run 누적 결과의 위치가 아닙니다. 표시와 확인에 URL·제목을 사용하세요. 출처가 반환됐다는 사실만으로 생성된 모든 주장이 검증되는 것은 아닙니다.

## 오래 걸리는 작업 계속 실행하기

일시적인 연결 종료 후에도 조사를 계속하거나 ID로 나중에 결과를 찾으려면 제공자의 백그라운드 실행을 사용합니다. 로컬 `AIRun`은 현재 클라이언트 실행을 제어하고, 제공자의 백그라운드 응답은 별도 서버 수명주기를 가집니다. 스트림 읽기를 끝내면 관찰만 종료됩니다. 원격 작업을 멈추려면 제공자 작업을 명시적으로 취소해야 합니다.

```csharp
var researcher = new PerplexityService(apiKey, httpClient)
    .UsePreset(PerplexityPreset.High);
var job = await researcher.StartBackgroundAsync(
    "최신 배터리 재활용 방법을 비교하고 출처를 제시해 주세요.", cancellationToken: cancellationToken);
Console.WriteLine(job.Id);
var completed = await job.WaitForCompletionAsync(
    cancellationToken: cancellationToken);
if (completed.Status != "completed")
    throw new InvalidOperationException(completed.Error ?? completed.Status);
Console.WriteLine(completed.Text);
```

`StartBackgroundAsync`는 대화 이력에 추가하지 않고 입력을 캡처하며, 활성 로컬 함수 또는 `Store = false`를 거절합니다. `GetResponseAsync`는 한 번 조회하고 `WaitForCompletionAsync`는 종료 상태까지 조회합니다. `Id`와 `LastSequenceNumber`를 저장하고 `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`로 재연결합니다. 원격 작업은 `CancelAsync`로 취소합니다. 조회·읽기 토큰 취소는 해당 클라이언트 작업만 멈춥니다. `LastResponse`에는 텍스트·상태·사용량·인용·`OutputJson`이 있으며 답변을 쓰기 전에 종료 상태를 확인해야 합니다.

샌드박스 출력은 핸들의 `ListFilesAsync`와 `DownloadFileAsync(fileId)`로 읽습니다. 서비스에도 `GetAgentResponseAsync`, `GetResponseFilesAsync`, `GetResponseFileContentAsync`가 있습니다. 제공자 응답의 산출물을 읽는 기능이며 벡터 저장소 생성·검색 기능은 아닙니다.

내장 Office 스킬은 이 안내의 백그라운드 경로를 사용하세요. `StartBackgroundAsync`로 시작한 뒤 `WaitForCompletionAsync` / `GetResponseAsync`와 파일 메서드를 사용합니다. 해당 응답의 내부 도구 기록은 일반 로컬 함수 호출과 구분되지 않을 수 있습니다.

백그라운드 제출·조회·취소·스트림 재연결은 실행 중 `SteerAsync` 또는 네이티브 비동기 로컬 도구 호출을 활성화하지 않습니다. 재연결은 원래 작업을 다시 제출하지 않고 기존 응답의 관찰을 이어갑니다. 제공자가 반환한 응답 ID와 커서를 보관하세요.

## 답변을 만들지 않고 검색하기

직접 순위를 매기거나 화면에 표시하거나 다른 LLM에 전달할 웹 문서에는 `PerplexitySearchClient`를 사용합니다. 답변 모델을 실행하거나 `PerplexityService`의 대화 이력을 변경하지 않습니다.

```csharp
var search = new PerplexitySearchClient(apiKey, httpClient);
var pages = await search.SearchAsync(
    "배터리 재활용 방법",
    new PerplexitySearchOptions
    {
        MaxResults = 5,
        DomainFilter = new[] { "nature.com", "science.org" },
        ContentSize = PerplexitySearchContentSize.Medium
    }, cancellationToken);
foreach (var page in pages.Results)
    Console.WriteLine($"{page.Rank}: {page.Title} — {page.Url}");
```

`SearchAsync`에는 검색어 하나 또는 여러 개를 전달할 수 있습니다. 웹/인물 검색, 국가, 도메인, 언어, 게시·수정 날짜 범위와 최신성 설정을 지원합니다. `ContentSize`와 명시적 `MaxTokens` / `MaxTokensPerPage` 중 하나를 선택하세요. 결과는 순위·제목·URL·요약·제공자 날짜를 포함하며 순위는 반환 순서이지 관련성 점수가 아닙니다.

`ContentSize`는 Web 검색에서만 지원합니다. People 검색에서는 생략하세요. 이 조합은 요청을 보내기 전에 클라이언트가 거절합니다.

## 직접 관리하는 문서 색인에 벡터 사용하기

표준 임베딩은 문단을 독립적으로 다루고 `IEmbeddingProvider`를 구현하므로 기존 빌더에 연결됩니다. 문맥 임베딩은 이웃 청크의 순서와 문서별 묶음을 유지합니다. 관련 없는 문서가 하나의 입력으로 합쳐지는 것을 막기 위해 별도 API를 사용합니다.

| 모델 상수 | 제공자 ID | 기본 차원 |
| --- | --- | --- |
| `Standard0_6B` | `pplx-embed-v1-0.6b` | 1024 |
| `Standard4B` | `pplx-embed-v1-4b` | 2560 |
| `Context0_6B` | `pplx-embed-context-v1-0.6b` | 1024 |
| `Context4B` | `pplx-embed-context-v1-4b` | 2560 |

```csharp
using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Embeddings;

var rag = service.WithRag(builder => builder
    .AddText("구매 후 30일 이내 반품할 수 있습니다.", id: "returns")
    .UsePerplexityEmbedding(apiKey, httpClient));
string policy = await rag.GetCompletionAsync("구매한 상품은 언제까지 반품할 수 있나요?");
```

```csharp
var contextual = new PerplexityContextualizedEmbeddingProvider(
    apiKey, httpClient, PerplexityEmbeddingModels.Context0_6B);
var documents = new[]
{
    new[] { "구매 후 30일 이내 반품할 수 있습니다.", "환불 요청 시 영수증을 보관하세요." },
    new[] { "일반 배송은 3일 걸립니다.", "빠른 배송은 평일에 이용할 수 있습니다." }
};
var documentVectors = await contextual.GetDocumentEmbeddingsAsync(
    documents, cancellationToken);
float[] queryVector = await contextual.GetQueryEmbeddingAsync(
    "구매한 상품은 언제까지 반품할 수 있나요?", cancellationToken);
```

색인 문서와 질의에는 같은 모델·차원·인코딩을 사용합니다. `GetQueryEmbeddingAsync`는 질의 하나를 별도 문서로 묶어 같은 문맥 모델에 전달합니다. 문맥 결과는 문서와 청크의 순서를 모두 유지하며, 평면 입력을 받는 RAG 빌더에 자동 연결되지 않습니다.

float API는 제공자의 base64 signed-int8 벡터를 디코딩하고 벡터 유사도용으로 정규화합니다. 명시적 binary API는 압축된 비트를 반환하며 해밍 거리를 사용합니다. 이진 데이터를 float 좌표로 조용히 변환하지 않습니다. 전체 차원은 0.6B가 1024, 4B가 2560이며 차원 축소는 제공자 제한을 따릅니다. 배치 크기·문서 길이·총 토큰·계정 속도 제한도 적용됩니다.

이진 메서드는 `GetBinaryEmbeddingAsync` / `GetBinaryEmbeddingsAsync`, 문맥용 `GetBinaryDocumentEmbeddingsAsync` / `GetBinaryQueryEmbeddingAsync`입니다. `PerplexityBinaryEmbedding`은 `Dimensions`, 복사본 `ToArray()`, `HammingDistance`를 제공하며 거리가 작을수록 유사합니다. 이진 차원은 8의 배수여야 합니다. 표준 배치는 최대 512개 텍스트, 문맥 배치는 512개 문서·16,000개 청크입니다. 텍스트/문서당 32K와 전체 120K 토큰 제한은 제공자가 검사합니다.

## 기존 Sonar 코드 이전하기

이번 릴리스는 제공자가 공지한 2026년 9월 27일 엔드포인트 종료에 앞서 기존 Sonar 어댑터를 제거합니다. `PerplexityService`는 `/v1/agent`로 요청하며 `AIModels.Perplexity.Sonar`는 이제 `perplexity/sonar`를 뜻합니다. 기존 Sonar 전용 검색 함수와 응답 타입은 제거됐습니다. 공통 completion/Run/인용, Agent 프리셋, 독립 검색용 `PerplexitySearchClient`로 이전하세요.

권장 매핑은 Sonar → `Fast`, Sonar Pro → `Low`, Sonar Reasoning Pro → `Medium`, Sonar Deep Research → `High`입니다. 작업 방식을 이전하는 것이며 같은 문장·비용·모델 동작을 보장하지 않습니다. 동적 프리셋은 제공자가 업데이트할 수 있으므로 고정이 필요하면 명시적 모델이나 버전을 지정한 프로필을 사용합니다.

이 어댑터는 네이티브 steering, 네이티브 비동기 로컬 도구, `CachePreservation.Required`를 지원하지 않습니다. Router/Gateway API는 연동 범위 밖입니다. API 사용 가능 여부는 제공자·모델·계정에 따라 달라지며 이 안내가 모든 조합의 유료 실증 통과를 주장하지는 않습니다.

Profile, custom skill, connector는 계정에 미리 등록된 자원이 필요합니다. 요청 형식은 단위 테스트로 검증했으며, 해당 자원을 사용하는 실제 API 성공 호출은 검증하지 않았습니다.

[Agent API](https://docs.perplexity.ai/docs/agent-api/quickstart) · [Search API](https://docs.perplexity.ai/docs/search/quickstart) · [Embeddings](https://docs.perplexity.ai/docs/embeddings/quickstart) · [Migration](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/how-to) · [2026-09-27](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)
