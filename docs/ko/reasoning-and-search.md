# 추론 깊이를 조절하고 출처로 답변 보완하기

요청마다 설정을 분리하고 공통 요청에서 여러 변형을 만들려면 [요청 빌더](request-building.md)를 사용하세요. `CreateRequest(...)` 다음에 `With...`를 연결합니다. 서비스에 직접 지정하는 속성과 fluent 메서드는 기존 동작을 유지합니다.

> 이 API는 `Mythosia.AI` 7.1.0 이상에서 사용할 수 있으며, `Mythosia.AI.Abstractions` 3.1.0 이상이 함께 포함됩니다. RAG 예제는 `Mythosia.AI.Rag` 7.6.0 이상이 필요합니다.

> `CreateRequest` 예제는 현재 작업 중인 릴리스의 기능입니다. Run과 공통 요청 기능이 처음 추가된 이전 7.1 버전에는 빌더가 없습니다. 이전 패키지에서는 기존 서비스 오버로드를 사용하세요.

[Claude Fable 5.1](fable-5-1.md)의 진행 안내, 턴별 지시, thinking binding 진단은 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0부터 제공합니다. Mythos 5.1은 초대 접근이 필요하며, 두 모델 모두 강제 도구 선택을 거부합니다.

## 이 기능이 왜 필요한가요?

작업의 단계마다 필요한 도움이 다릅니다. 초안을 잡을 때는 빠른 답이 유용하지만, 그 초안의 가정을 검토할 때는 더 깊은 추론에 시간을 쓸 만합니다. 오늘 있었던 일을 묻는 질문에는 최신 정보가, 우리 제품의 정책을 묻는 질문에는 해당 문서가 필요합니다. 추론 수준만 높여서는 두 종류의 근거를 얻을 수 없습니다.

공통 Fluent API로 다음 작업에 필요한 조건을 표현하면, 선택한 공급자가 지원하는 네이티브 API로 연결합니다. 완성된 답변만 필요하면 `GetCompletionAsync`를 유지하고, 진행 상황을 보여 주거나 같은 작업을 제어하려면 `StartRunAsync`를 사용할 수 있습니다.

| 필요한 상황 | 설정 |
| --- | --- |
| 빠르게 초안을 잡고 다음 단계에서 꼼꼼히 검토 | `WithReasoning(...)` |
| 재사용 가능한 대화 캐시 접두부를 유지하며 추론 수준 변경 | `WithReasoning(..., cache: CachePreservation.Required)` |
| 웹의 최신 정보를 근거로 답변 | `WithWebSearch()` |
| 공급자에 이미 색인한 문서를 근거로 답변 | `WithFileSearch(store)` |

예제의 `service`는 해당 기능을 지원하는 모델로 초기화한 서비스입니다. `Mythosia.AI.Extensions`, `Mythosia.AI.Models`를 가져오고, 스트림 이벤트에는 `Mythosia.AI.Models.Streaming`도 사용합니다.

## 빠른 초안에서 깊은 검토로 전환하기

개요를 만들 때는 추론량을 줄이고, 같은 대화에서 어려운 세부 사항을 검토할 때 높일 수 있습니다.

```csharp
string outline = await service
    .CreateRequest("마이그레이션 계획의 개요를 작성해 줘.")
    .WithReasoning(ReasoningLevel.Low)
    .GetCompletionAsync();

string review = await service
    .CreateRequest("그 계획의 실패 가능성과 복구 절차를 검토해 줘.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

Gemini 3.7/3.8 Flash는 `WithReasoning`의 `Low`, `Medium`, `High`를 지원합니다. `Minimal`, `None`, `CachePreservation.Required`는 지원하지 않습니다. 기존 완성 응답·스트리밍·Run·도구 호출·내장 검색 경로와 Google 조합 제한을 그대로 적용합니다. [Google 설정 예제](providers.md#google-googleaiservice)를 참고하세요.

`ReasoningLevel`은 요청하는 추론 수준입니다. 고정된 토큰 예산이나 답변 품질을 보장하는 값은 아니며, 모델마다 허용하는 수준이 다릅니다. `Auto`는 공급자에 설정된 동작이나 기본값을 유지한다는 뜻입니다. 지원하지 않는 수준을 자동으로 다른 수준으로 바꾼다는 뜻은 아닙니다. 이름 붙은 수준 대신 토큰 예산을 제공하는 모델에는 기존 공급자별 예산 속성을 사용할 수 있습니다.

대화가 길어지면 요청 최상위의 추론 설정을 바꾸는 것만으로 재사용 가능한 프롬프트 접두부가 무효화될 수 있습니다. 지원 모델에서는 그 접두부를 유지하는 변경 방식을 요구할 수 있습니다.

```csharp
string review = await service
    .CreateRequest("앞선 답변의 가정을 다시 검토해 줘.")
    .WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)
    .GetCompletionAsync();
```

`Required`는 변경을 **어떤 방식으로 전송할지**에 대한 계약입니다. 캐시 적중, 무료 토큰, 지연 시간 감소를 보장하지는 않습니다. 공급자의 캐시 적용 조건·보존 시간·요금은 그대로 적용됩니다. 미지원 모델은 요청을 보내기 전에 `NotSupportedException`을 발생시킵니다. 라이브러리가 추적하는 같은 대화·모델·엔드포인트를 유지하고, 변경 기록이 있는 대화를 잘라 내거나 순서를 바꾸지 마세요. 조건을 바꿀 때는 새 대화를 시작합니다. 보존할 접두부가 필요한 동안에는 자동 대화 압축도 차단합니다.

수락된 캐시 보존 추론 수준은 명시적으로 다시 변경할 때까지 해당 대화에 유지됩니다. 일반 `WithReasoning(level)`은 그 논리적 요청에만 적용되며, 대화에 유지되는 설정을 조용히 덮어쓰지 않습니다. 이 변경은 **모델 응답과 다음 응답 사이**에 적용됩니다. 이미 진행 중인 응답의 추론량을 바꾸는 기능은 아닙니다. 지원되는 실행에 추가 지시를 보내는 `run.SteerAsync`와도 별개입니다.

## 최신 정보가 필요한 질문에 답하기

모델의 학습 데이터 밖에 있는 정보가 필요하면 내장 웹 검색을 활성화합니다.

```csharp
string answer = await service
    .CreateRequest("최신 릴리스 발표를 검색하고 출처와 함께 설명해 줘.")
    .WithWebSearch()
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.Url}");
```

검색 도구는 공급자가 실행합니다. 애플리케이션에 로컬 함수 핸들러를 등록하거나 직접 실행할 필요가 없습니다. 검색을 켜면 모델이 사용할 수 있게 되며, 특정 질문에는 검색이 필요 없다고 판단할 수도 있습니다. 출처는 공급자가 반환했을 때 제공됩니다.

OpenAI와 Anthropic에는 `WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { "example.com" } })`으로 도메인을 제한할 수도 있습니다. Google의 연결된 도구에는 이 허용 목록이 없으므로, 제한을 무시하고 전체 웹을 검색하는 대신 요청을 거절합니다.

## 공급자에 이미 등록한 문서에서 답 찾기

공급자가 관리하는 문서 색인이 이미 있다면, 애플리케이션에서 검색 단계를 직접 구현하지 않고 그 저장소를 근거로 답하게 할 수 있습니다.

```csharp
var documents = new FileSearchStore("OpenAI", "vs_your_existing_store");

string answer = await service
    .CreateRequest("정책 문서를 검색해 줘. 계약 취소 기간은 얼마야?")
    .WithFileSearch(documents)
    .GetCompletionAsync();

foreach (AICitation source in service.GetLastCitations())
    Console.WriteLine($"{source.Title}: {source.FileId ?? source.Url}");
```

Google 서비스에는 `new FileSearchStore("Google", "fileSearchStores/your-existing-store")`를 사용합니다. 저장소는 공급자·계정·배포 환경에 속하며, OpenAI 저장소 ID를 Google에 넘길 수 없습니다. 공급자의 API나 콘솔에서 저장소를 만들고 문서를 업로드·색인한 뒤 사용하세요. 이 API는 기존 저장소를 검색하며 로컬 파일을 업로드하지 않습니다.

`CreateRequest(...).With...`는 독립적인 빌더에 옵션을 보관합니다. 같은 빌더를 재사용하면 실행마다 해당 옵션이 도구 호출 라운드까지 적용됩니다. 기존 `service.WithReasoning`, `service.WithWebSearch`, `service.WithFileSearch`는 구체 서비스 타입을 반환하고 다음 논리적 요청에서 옵션을 소비하는 동작을 유지합니다. `IAIRequestFeatureService`나 RAG 래퍼를 사용하는 기존 코드는 이 확장 메서드를 계속 사용할 수 있습니다. 어느 방식도 같은 서비스의 동시 실행을 보장하지 않습니다.

## 진행 상황을 보여 주면서 출처 보관하기

같은 옵션을 `StartRunAsync` 앞에 붙입니다. 콜백으로 화면에 텍스트를 표시하는 동안, Run은 완료된 답변에 사용할 출처를 보관합니다.

```csharp
await using var run = await service
    .CreateRequest("최근 발표를 검색하고 변경점을 비교해 줘.")
    .WithReasoning(ReasoningLevel.High)
    .WithWebSearch()
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
foreach (AICitation source in run.Citations)
    Console.WriteLine($"{source.Title}: {source.Url ?? source.FileId}");
```

스트림을 읽지 않거나 텍스트만 관찰하거나 출력 관찰 버퍼가 가득 차도 `run.Citations`는 남습니다. 중간 응답을 포함해 실행 중 공급자가 반환한 출처를 담습니다. `service.LastCitations` 또는 `IAIService`의 `GetLastCitations()`는 가장 최근의 논리적 요청을 가리킵니다. 여러 답변을 표시할 때는 해당 Run이나 복사한 출처 스냅샷을 보관하세요.

출처 이벤트도 도착할 때마다 처리하려면 하나의 이벤트 리더를 사용합니다.

```csharp
await using var run = await service
    .CreateRequest("최신 변경 사항을 검색하고 설명해 줘.")
    .WithWebSearch()
    .StartRunAsync(cancellationToken: cancellationToken);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
    else if (item.Type == StreamingContentType.Citation && item.Citation is AICitation source)
        Console.WriteLine($"\n출처: {source.Title} {source.Url ?? source.FileId}");
}
string answer = (await run.Result).Text;
```

공급자가 값을 주지 않은 출처 필드는 null일 수 있습니다. `ResponseId`, `OutputIndex`, `ContentIndex`는 해당 응답과 콘텐츠 조각을 식별합니다. `StartIndex`와 `EndIndex`는 공급자의 로컬 위치와 인덱싱 규칙을 유지한 값이며, 이어 붙인 `(await run.Result).Text`의 위치가 **아닙니다**. 이 값으로 전체 답변을 바로 잘라 출처를 배치하면 안 됩니다.

## 공급자 지원 범위와 설정 수명 확인하기

| 연결된 공급자 | 이름 붙은 추론 수준 | 캐시를 유지하는 변경 | 웹 검색 | 파일 검색 |
| --- | --- | --- | --- | --- |
| OpenAI | 지원 추론 모델, 수준은 모델별 상이 | GPT-6 Astra Standard·단일 에이전트 모드 | 지원 Responses 모델 | 지원 Responses 모델의 기존 벡터 저장소 |
| Anthropic | 네이티브 effort 지원 모델 | 지원 Opus 5 / Fable 5.1 / Mythos 5.1, 공급자 베타 사용 | 지원 Claude 모델 | 네이티브 저장소 어댑터 없음, RAG 사용 |
| Google | Gemini 3 수준, Gemini 2.5는 기존 예산 속성 유지 | 미지원 | 지원 Gemini 텍스트 모델 | 지원 Gemini 텍스트 모델의 기존 파일 검색 저장소 |
| xAI | Grok 4.6: `Auto`, `Low`, `Medium`, `High`, `XHigh` | 미지원 | 공통 어댑터 없음 | 공통 어댑터 없음 |
| DeepSeek | Flash: `Auto`, `None`, `Minimal`/`Low`, `Medium`/`High`/`XHigh`, `Max`; 제공자 별칭을 원본 Low/High/Max로 매핑 | 미지원 | 공통 어댑터 없음 | 공통 어댑터 없음 |
| Perplexity | `Auto` 또는 모델이 지원하는 `Minimal`/`Low`/`Medium`/`High`/`XHigh`/`Max`; Sonar의 명시적 수준은 미지원 | 미지원 | Agent `web_search` | 공통 어댑터 없음 |
| 그 외 서비스 | 기존 공급자별 설정 사용 가능, 공통 옵션에는 어댑터 필요 | 이번 어댑터 범위에서 미지원 | 공통 어댑터 없음 | 공통 어댑터 없음 |

어댑터는 알고 있는 모델·수준·전송 방식·조합의 제약을 요청 전에 검사하며, 로컬에서 확인할 수 없는 모델별 규칙은 제공자가 검증합니다. 특히 **Google 웹 검색과 파일 검색은 한 요청에서 함께 사용할 수 없습니다**. 라이브러리는 기능을 조용히 빼거나 추론 수준을 낮추거나 도메인 제한을 무시하거나 외부 검색 서비스로 바꾸지 않습니다. 지원하는 조합에서는 내장 도구와 등록한 클라이언트 함수를 함께 사용할 수 있습니다. Run의 도구 라운드는 기존 함수 정책과 `WithMaxRounds`를 따릅니다.

`CreateRequest(...).With...`는 독립적인 빌더에 옵션을 보관합니다. 같은 빌더를 재사용하면 실행마다 해당 옵션이 도구 호출 라운드까지 적용됩니다. 기존 `service.WithReasoning`, `service.WithWebSearch`, `service.WithFileSearch`는 구체 서비스 타입을 반환하고 다음 논리적 요청에서 옵션을 소비하는 동작을 유지합니다. `IAIRequestFeatureService`나 RAG 래퍼를 사용하는 기존 코드는 이 확장 메서드를 계속 사용할 수 있습니다. 어느 방식도 같은 서비스의 동시 실행을 보장하지 않습니다.

사용자 구현 `IAIService`는 기존 호환성을 유지합니다. 이 기능은 선택적 `IAIRequestFeatureService`로 지원하며, 해당 인터페이스가 없는 구현에서 도우미를 호출하면 명시적으로 예외가 발생합니다. 기존 완성·스트리밍·공급자별 설정 API도 유지됩니다. 취소·출력 관찰·추가 지시는 [Run 제어](execution-api-transition.md)를 참고하세요.

공급자 프로토콜: [OpenAI 추론 변경](https://developers.openai.com/api/docs/guides/reasoning#change-reasoning-mid-conversation), [OpenAI 도구](https://developers.openai.com/api/docs/guides/tools), [Anthropic effort 변경](https://platform.claude.com/docs/en/build-with-claude/effort#change-effort-mid-conversation), [Anthropic 웹 검색](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool), [Google 검색 근거](https://ai.google.dev/gemini-api/docs/google-search), [Google 파일 검색](https://ai.google.dev/gemini-api/docs/file-search).

Perplexity의 추론 수준 지원은 실제 선택된 모델에 따라 달라지며, 호환되지 않는 조합은 서버에서 거절될 수 있습니다. `None`은 지원하지 않습니다. 기본 웹 검색과 preset/profile 도구는 지속되는 제공자 설정이며, 공통 요청 옵션으로 해당 기본값이 꺼지지는 않습니다.

Perplexity: [Perplexity Agent API, 검색과 임베딩](perplexity.md).
