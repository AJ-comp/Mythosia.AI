# Provider-Specific Configuration 아키텍처

> GPT-6.1 Sol: Mythosia.AI 8.2.0 / Abstractions 4.2.0이 필요합니다. [모델 선택과 전환](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/providers.md#gpt-61-sol)

> GPT-6 Sol/Luna는 Mythosia.AI 8.1.0 / Abstractions 4.1.0부터 지원합니다.

완성된 답변과 사용량·출처를 함께 받아야 한다면 `await run.Result`가 반환하는 `AIRunResult`를 사용하세요. 문자열은 `result.Text`에 있으며 스트림을 읽지 않아도 결과를 모읍니다. Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0의 API 변경이며 `GetCompletionAsync`와 타입 지정 `StructuredStreamRun<T>.Result`의 반환형은 유지합니다. [Run 결과와 전환 안내](../../../../../docs/ko/execution-api-transition.md#run-result).


요청마다 설정을 분리하고 공통 요청에서 여러 변형을 만들려면 [요청 빌더](../../../../../docs/ko/request-building.md)를 사용하세요. `CreateRequest(...)` 다음에 `With...`를 연결합니다. 서비스에 직접 지정하는 속성과 fluent 메서드는 기존 동작을 유지합니다.

> [Claude Fable 5.1](../../../../../docs/ko/fable-5-1.md)의 진행 안내, 턴별 지시, thinking binding 진단은 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0부터 제공합니다. Mythos 5.1은 초대 접근이 필요하며, 두 모델 모두 강제 도구 선택을 거부합니다.

> GPT-6 Astra, `AllowAsync`, `StartRunAsync`, 공통 추론·검색 API는 `Mythosia.AI` 7.1.0부터 제공하며, 공통 타입은 `Mythosia.AI.Abstractions` 3.1.0에 포함됩니다.

<a id="claude-sonnet-55"></a>

## Claude Sonnet 5.5

`AIModels.Anthropic.ClaudeSonnet5_5` (`claude-sonnet-5-5`)는 텍스트·이미지 입력과 텍스트 출력을 지원하며, 컨텍스트 1M 및 최대 출력 128K 토큰을 제공합니다. Mythosia.AI 8.2.0 / Abstractions 4.2.0이 필요합니다. 기존 서비스 기본 모델과 모델 식별자는 유지됩니다.

설정을 변경하지 않으면 adaptive 추론, `High` effort, 읽을 수 있는 추론 생략을 사용합니다. Adaptive 모드는 `Low`, `Medium`, `High`, `XHigh`, `Max`를 지원하고 `Minimal`은 거부합니다. `MaxTokens`에는 추론과 답변이 모두 포함됩니다. 샘플링 매개변수는 전송하지 않습니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeSonnet5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await claude.CreateRequest("Review the plan using the registered tools.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(options: StreamOptions.FullOptions);
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

Adaptive 모드에서는 `ClaudeThinkingDisplay.Updates`로 도구 진행 상황을, `Summarized`로 추론 요약을 요청합니다. 스트림의 `StreamingContentType.Reasoning` 또는 일반 완료 후 `LastThinkingContent`를 확인합니다. Adaptive 헬퍼의 생략된 display 인수는 `Summarized`로, 무설정 기본값과 다릅니다. `between_tools`에서는 도구 진행 상황이 자동으로 반환되며 고정된 진행 알림 주기는 보장하지 않습니다.

`ReasoningLevel.None`, 비활성화된 기존 `ThinkingBudget`, `AIRequestProfile.DisableReasoning`은 high effort의 `between_tools`를 선택합니다. 사전 추론을 끄지만 도구 진행 상황은 thinking 블록으로 반환될 수 있습니다. `WithBetweenToolsThinking(...)`은 `Auto`(high), `Low`, `Medium`, `High`를 허용하고 `XHigh`와 `Max`는 거부합니다. 전송하는 thinking 객체에는 `type`만 있으며 display, budget, binding 필드는 없습니다. 이 모드에서는 메시지별 effort 변경과 `CachePreservation.Required`를 지원하지 않습니다. 공통 `WithReasoning(Low...Max)`는 adaptive 모드로 전환하고 `Auto`는 선택한 제공자 모드를 따릅니다.

```csharp
claude.StartNewConversation(AIModels.Anthropic.ClaudeSonnet5_5);
claude.WithBetweenToolsThinking(ClaudeReasoningEffort.Low);
string quick = await claude.CreateRequest("Use the registered tools to check the status.")
    .GetCompletionAsync();

// A separate conversation using request-scoped high-effort between_tools.
claude.StartNewConversation(AIModels.Anthropic.ClaudeSonnet5_5);
string next = await claude.CreateRequest("Give me the latest status.")
    .WithReasoning(ReasoningLevel.None)
    .GetCompletionAsync();
```

Claude는 모델, 요청 목적, 추론과 thinking 바인딩 설정을 함께 결정합니다. `RequestProfiles.Summarization`이나 `RequestProfiles.QueryRewrite` 같은 보조 프로파일에서는 `DisableReasoning = true`이고 목적이 `Default`가 아니며 실제 요청이 무상태일 때만 상속한 바인딩을 생략합니다. 보존할 대화 접두부가 없는 요청에서 상속한 정책 때문에 추론이 다시 켜지거나 요청이 거부되는 것을 막습니다. 추론을 끌 수 있는 모델은 비활성화하고, 항상 추론하는 Opus 5.5·Fable 5.1·Mythos 5.1은 `Low`와 읽을 수 있는 thinking 생략을 사용합니다. Sonnet 5.5는 high effort의 `between_tools`를 사용합니다.

완료, 스트리밍, 구조화 출력, Run은 같은 순서로 요청을 준비합니다. 설정을 보관하고 실제 프로파일 처리를 한 번 적용한 뒤, 최종 공통 옵션과 공급자 네이티브 옵션을 검증합니다. 자동 요약, 새 입력의 이력 추가, 통신 연결보다 먼저 수행하므로 사용자 정의 공급자의 프로파일 재정의도 실제 검증 대상에 반영됩니다. Claude의 실제 사용되는 수동 `ThinkingBudget`이 모델 출력 한도 이상이면 이 단계에서 거부하며, 유효한 프로파일과 공통 추론 설정의 우선순위는 유지합니다.

애플리케이션 호출은 독립된 논리 요청을 시작합니다. `SystemMessageProvider`나 도구 콜백의 일반 중첩 호출, 같은 `AIRequestProfile`·`Message`를 재사용하는 호출도 마찬가지이며, 객체 재사용이 실행 재사용을 뜻하지는 않습니다. 프레임워크 위임, 도구 실행 단계, 재시도, 형식 수정은 원래 요청을 이어가며 프로파일은 한 번 적용합니다. 일반 자식 요청은 자체 옵션과 서비스 기본값을 가져오고 빌더는 보관한 설정을 사용합니다. 성공·실패·취소 후에는 부모 실행 상태를 복원합니다. 공급자의 가상 어댑터에는 [요청 전달 규칙](../../../../../docs/ko/request-building.md#provider-request-adapters)을 적용합니다. 처음 호출하는 대응 base 진입점은 입력을 교체해도 준비된 요청을 이어가며, 전달 전에 같은 base 진입점에서 별도 보조 작업을 호출하려면 `BeginIndependentRequestScope()`로 await 또는 전체 스트림 열거를 감싸야 합니다.

내장 공급자는 기본 입력 콘텐츠의 자체 복사본을 보관합니다. 같은 `Message`로 다시 호출해도 해당 호출의 컨텍스트와 턴 지시를 적용하고 이미 수락된 이력은 바꾸지 않습니다. 사용자 정의 콘텐츠와 지원되지 않는 메타데이터 객체는 소유자가 관리해야 합니다. 같은 대화를 동시에 호출해도 안전하다는 뜻은 아닙니다.

`StartRunAsync`가 반환된 뒤에도 Run은 최종 설정의 자체 복사본을 유지합니다. 호출 측 프로파일을 복원해도 실행 중인 Run의 설정은 바뀌지 않으며, 실행용 프로파일 훅은 여전히 한 번만 호출됩니다.

무상태 보조 요청은 별도 대화를 사용하고 부모의 출력 스키마, 호스팅 도구, 일회성 옵션을 상속하지 않습니다. 이 격리가 네이티브 옵션 검증을 생략하지는 않으며 OpenAI·Perplexity Run에도 같은 원칙을 적용합니다. 부모 설정, 메시지, `CurrentSummary`, 요청 관측 정보는 보존합니다. 상태를 유지하는 요청은 바인딩과 대화 검증을 유지합니다. 기존 공개 API는 바뀌지 않습니다.

라이브러리가 자동으로 생성하는 대화 요약은 부모의 `SystemMessageProvider` 콜백과 요청 컨텍스트도 제외합니다. 상속한 `RequestMessageOverride`가 내부 요약 프롬프트를 바꾸지 못하게 하기 위한 것입니다. 애플리케이션이 직접 텍스트 요약을 요청하는 경우를 포함한 일반 요청은 동적 컨텍스트를 정상적으로 적용합니다.

무상태 요청은 부모 대화의 자동 요약도 실행하지 않습니다. 기존 `GetCompletionAsync(string, profile)` 오버로드에서도 `Message` 오버로드와 요청 빌더처럼 부모의 `CurrentSummary`와 메시지를 그대로 유지합니다. 상태를 유지하는 요청은 기존 자동 요약 동작을 유지합니다.

`(ClaudeReasoningEffort)1234`처럼 정의되지 않은 `ClaudeReasoningEffort` 값은 실제 추론 설정에서 해당 네이티브 effort를 사용할 때 로컬에서 거부됩니다. 거부된 요청은 부모 대화를 자동 요약하지 않고, 전송하지 않은 입력을 이력에 남기지 않습니다. 유효한 명시적 공통 추론 설정이나 프로파일 재정의는 기존처럼 네이티브 기본 설정보다 우선합니다.

`ClaudeThinkingMode`: `Auto` / `Adaptive` / `BetweenTools`; `AnthropicService.ThinkingMode`.

대화 이력은 뒤에 추가하는 방식으로 유지하세요. 빈 블록과 `progress_updates` 메타데이터를 포함한 서명된 thinking을 턴과 도구 결과 사이에 보존합니다. 저장된 assistant 응답 수정은 `ClaudeThinkingPrefixMismatchBehavior.DropBlock`에서도 로컬에서 거부됩니다. 이전 user/system/tool 접두부 수정은 자동으로 로컬 차단하지 않고 Anthropic의 바인딩 정책에 맡깁니다. Adaptive에서 `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)`를 사용하면 제공자가 엄격하게 검증하며 잘못된 접두부는 HTTP 400이 될 수 있습니다. `DropBlock`은 제공자가 해당 추론을 버리도록 허용하고 null은 제공자 기본 정책을 따릅니다. `LastInputTransformations`로 보고된 삭제를 확인하세요. `between_tools`에서는 바인딩 제어를 지원하지 않습니다. 새 지시는 `WithTurnInstruction` / `WithConversationInstruction`으로 추가하세요. Adaptive 모드의 `CachePreservation.Required`도 과거 메시지 편집을 안전하게 만들지는 않습니다.

기존 완료, 스트리밍, 구조화 출력, 이미지, 로컬 함수, 웹 검색, 일반 Run API를 사용합니다. `ForceFunctionName`을 비워 두세요. 강제 도구 선택(`any` / `tool`)과 assistant prefill은 HTTP 전에 거부하며, 자동 선택과 `FunctionsDisabled`는 사용할 수 있습니다. `Fast`, 제공자 네이티브 비동기 도구, Run steering은 지원하지 않습니다. Computer toolset, advisor 도구, 네이티브 압축, 대화 중 도구 변경, 자동 서버 fallback은 통합하지 않습니다. 모델·계정 전환 시 바인딩된 추론이 사라질 수 있으며 요청 성공만으로 추론 보존을 판단할 수 없습니다.

Mythosia.AI 8.2.0의 알려진 제한: Sonnet 5.5와 Opus 5.5에서 실행 대기 중인 `server_tool_use`로 끝나는 유효한 `pause_turn` 응답을 assistant prefill로 잘못 판단하여 두 번째 HTTP 요청 전에 거부합니다. 완료된 서버 도구 결과로 끝나는 연속 실행은 기존 검증을 통과했습니다. [네이티브 연속 실행 제한](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/providers.md#claude-native-continuation-limitation)을 참고하세요.

공통 구조화 출력 API는 스키마 지시, 역직렬화, 복구 재시도를 사용합니다. 네이티브 `output_config.format` 스키마 제약을 전송하지 않습니다.

[공식 모델 정보](https://platform.claude.com/docs/en/models/sonnet-5-5/overview) · [마이그레이션](https://platform.claude.com/docs/en/models/sonnet-5-5/migration-guide) · [제공자 변경 사항](https://platform.claude.com/docs/en/models/sonnet-5-5/whats-new-sonnet-5-5).


## Claude 토큰 계산

매개변수 없는 `GetInputTokenCountAsync()`는 도구를 처리하는 완료 메시지 직렬화를 재사용하며, 현재 도구가 비활성화되어 있어도 assistant의 `tool_use`와 user의 `tool_result` 블록을 보존합니다. 가져온 기존 병렬 도구 기록이 하나의 `OriginalContent` 배치를 공유하면 해당 배치를 한 번만 직렬화하며, 서명된 콘텐츠와 과거 지시를 보존합니다. 현재 활성화된 도구 정의와 `tool_choice`를 포함하고, 생성 전용 필드는 생략하며 토큰 계산 전용 thinking 제한은 유지합니다. `GetInputTokenCountAsync(string prompt)`는 독립된 프롬프트의 토큰을 계산하는 기존 동작을 유지하며, 저장된 대화 이력이나 도구 정의를 포함하지 않습니다.

일반 완료, thinking을 보존하는 후속 요청, 토큰 계산은 같은 도구 이력 변환을 사용합니다. 함수를 비활성화하거나 제거한 뒤 보내는 일반 요청에서도 과거 호출과 결과는 네이티브 도구 블록으로 유지합니다. 가져온 `FunctionSource` 메타데이터는 호출과 결과 모두에서 같은 의미의 정의된 enum, 정수, 문자열, JSON 표현을 처리합니다. 같은 병렬 assistant 배치의 중복 기록은 도구 순서, 서명, 공급자 필드를 바꾸지 않고 한 번만 전송합니다. 메타데이터 표현만 달라져도 이미 수락된 접두부가 중복되지 않습니다.

Claude는 직렬화, 토큰 계산, 요약 압축 보호에서 같은 원본 assistant 콘텐츠를 확인합니다. 네이티브 콘텐츠, 타입 지정 배치, 기존 `Message.Metadata[OriginalContent]`를 모두 포함하므로 가져온 형식과 무관하게 서명된 thinking의 접두부를 동일하게 보호합니다. 이는 로컬 이력 보존이며 실제 서버의 서명 수락을 보장하지 않습니다.

## 원칙

애플리케이션은 [공통 추론·검색 API](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/reasoning-and-search.md)로 작업에 필요한 추론량과 내장 검색을 표현할 수 있습니다. `AIRequestFeatures`는 한 논리적 요청에 복사하고 공급자 어댑터가 검증·변환하며, 공급자별 기본값은 서비스에 유지합니다. 캐시 보존 변경의 프로토콜 상태는 추적 중인 대화에 남습니다. `AICitation`은 스트림 관찰과 독립적으로 출처를 보관합니다. 사용자 서비스는 `IAIRequestFeatureService`로 선택적으로 지원하며 `IAIService`에 필수 멤버를 추가하지 않습니다.

| 설정 유형 | 위치 | 예시 |
|-----------|------|------|
| **공통 설정** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty 등 |
| **전용 설정** | 각 서비스 클래스 | ThinkingLevel/ThinkingBudget (Gemini), ReasoningEffort (GPT) 등 |
| **함수별 실행 허용** | `FunctionDefinition` | `AllowAsync` (기본값 `false`) |

`AllowAsync`는 호출자가 선택하는 허용 옵션이며, 모델·API의 지원 여부는 서비스가 내부적으로 판단합니다. `FunctionBuilder.WithAsync()`와 `[AiFunction("lookup", "데이터 조회", AllowAsync = true)]`도 같은 옵션을 켭니다. GPT-6.1 Sol / GPT-6 Astra / Sol / Luna는 Responses에서 이를 사용하고, 미지원 모델은 API 옵션을 생략한 뒤 같은 핸들러의 결과를 기다립니다. 사용자가 지정한 허용 값은 바꾸지 않습니다.

## 현재 구현: 서비스 레벨

전용 설정은 해당 서비스 클래스의 프로퍼티로 관리합니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;

geminiService.ChangeModel(AIModels.Google.Gemini3_8Flash);

// 공통 설정 → ChatBlock
geminiService.ActivateChat.MaxTokens = 4096;

// 전용 설정 → 서비스
geminiService.ThinkingLevel = GeminiThinkingLevel.Low;
```

가벼운 초안에는 `Low`, 까다로운 검토에는 `High`를 사용할 수 있습니다. 추론을 늘리면 지연 시간과 토큰 사용량이 증가할 수 있습니다. 두 모델은 `Low`, `Medium`, `High`를 지원하며 `Minimal`과 `None`은 지원하지 않습니다. `GeminiThinkingLevel.Auto`는 재정의를 생략하며 3.8의 공급자 기본값은 `Medium`입니다. `ThinkingLevel`은 서비스 기본 설정이고 `WithReasoning(...)`은 한 논리적 요청만 재정의합니다. 두 모델의 `temperature`, `topP`, `topK`는 전송하지 않습니다. 공급자 한도는 입력 1,048,576토큰, 출력 65,536토큰입니다.

### Grok 4.6

빠른 초안에는 낮은 추론 수준을 쓰고, 응답 속도보다 답변 품질이 중요한 까다로운 검토에는 추론을 더 배정할 수 있습니다. 추가된 `XHigh` 수준을 사용하려면 Grok 4.6을 명시적으로 선택합니다.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_6);
grok.WithGrokReasoning(GrokReasoning.Low);

string review = await grok
    .CreateRequest("롤링 배포와 블루 그린 배포를 장애 복구 절차까지 포함해 비교해 주세요.")
    .WithReasoning(ReasoningLevel.XHigh)
    .GetCompletionAsync();
```

Grok 4.6은 `Low`, `Medium`, `High`, `XHigh` (`GrokReasoning.XHigh`)를 지원합니다. `Auto`는 `reasoning_effort`를 생략하므로 공급자의 기본값 `High`가 적용되며, `None`으로 추론을 끌 수는 없습니다. 추론을 늘리면 지연 시간과 토큰 사용량이 증가할 수 있습니다. 호환성을 위해 `XAIService`의 기본 모델은 Grok 4.5로 유지합니다. 4.5는 `Low`부터 `High`, 4.3은 `None`부터 `High`까지 지원하며, 두 이전 모델의 `XHigh` 요청은 전송 전에 거절합니다.

`WithGrokReasoning(...)`과 기존 `WithGrokParameters(...)`는 서비스의 기본 추론 설정을 바꿉니다. Grok 4.6의 공통 `WithReasoning(...)`은 도구 라운드와 구조화 출력 수정 호출을 포함한 한 논리적 요청만 재정의한 뒤 기본 설정으로 돌아갑니다. 추론이 항상 켜진 이 모델에서 내부 `DisableReasoning` 프로필은 `Low`를 사용합니다. 공통 옵션을 통한 xAI 캐시 유지 변경과 호스팅 웹·파일 검색은 연결되어 있지 않습니다.


### Grok Imagine Image 2.0

상품 설명을 이미지 시안으로 만들거나, 서로 다른 사진의 피사체와 배경을 합치고 싶을 때 사용합니다. `XAIService`도 OpenAI·Google과 같은 `IImageGenerationService`로 생성과 편집을 지원합니다. `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0부터 사용할 수 있습니다.

독립적인 기본 이미지 모델은 `AIModels.xAI.GrokImagineImage2_0` (`grok-imagine-image-2.0`)입니다. 이미지 요청은 선택한 채팅 모델을 바꾸거나 채팅 대화에 메시지를 추가하지 않습니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;

IImageGenerationService images = new XAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.xAI.GrokImagineImage2_0,
    Prompt = "해 뜰 무렵의 유리 파빌리온, 가로로 넓은 구도",
    Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
    OutputFormat = ImageOutputFormat.Auto
});

var image = generated.Images[0];
var extension = image.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(image.MediaType)
};
await File.WriteAllBytesAsync("pavilion" + extension, image.Data);
```

각 `GeneratedImage.Data`에는 디코딩된 이미지 바이트가 들어 있습니다. 파일 확장자는 `MediaType`을 확인해 정하세요. 어댑터는 인라인 base64 응답을 요청하며 제공자 이미지 URL을 다운로드하지 않습니다. `Count`는 결과 1~10장, 편집 입력은 JPEG·PNG·WebP 참조 이미지 1~5장을 지원합니다.

xAI는 새로운 공통 기본값인 `ImageOutputFormat.Auto`만 지원합니다. 출력 코덱 선택 기능이 없어 명시적인 `Jpeg`, `Png`, `WebP`는 전송 전에 거절합니다. `GeneratedImage.MediaType`에 맞는 확장자로 저장하세요. 라이브러리는 이미지를 변환하지 않습니다. 품질은 `ImageQuality.Auto`, `Low`, `Medium`, 배경은 `ImageBackground.Auto`만 지원하며 명시적 압축과 별도 `Mask`는 지원하지 않습니다.

Google은 `ImageSize.Auto` 또는 모델별 해상도·비율의 `Preset`을 사용합니다. [Google 모델별 이미지 옵션](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/providers.md#google-image-options). 출력 형식은 `ImageOutputFormat.Auto` 또는 명시적 `Jpeg`이며 `Png`·`WebP`는 거절합니다. Google·xAI는 `Pixels`를 거절하고, OpenAI는 `Auto`·`Pixels`를 지원하며 `Preset`을 거절합니다. [전환 예제](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/providers.md#image-options-migration)를 참고하세요.

Google의 `Resolutions`와 `AspectRatios`는 선택한 이미지 모델에 따라 달라지며 생성·편집 검증에도 적용됩니다. Flash-Lite의 보수적인 1K 정책을 포함한 [모델별 표](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/ko/providers.md#google-image-options)를 참고하세요. 지원하지 않는 명시적 값은 HTTP 전에 거절하며 사용자 정의 미확인 모델은 `Unknown` 지원 정보와 공급자 공통 옵션 검증을 유지합니다.

### DeepSeek Flash

빠른 답변을 받은 뒤 더 깊게 검토하거나 차트·스크린샷을 설명해야 할 때 DeepSeek Flash를 사용할 수 있습니다. `AIModels.DeepSeek.Flash` (`deepseek-flash`)는 2026년 9월 10일 출시된 비전 지원 V4.1 Flash를 선택합니다. 기존 완성 응답·스트리밍·Run·함수 호출·RAG API를 그대로 사용하며 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0부터 지원합니다.

> 배포된 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0에는 Flash 기본 지원이 포함되어 있습니다. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, Files API, `DeepSeekImageFileContent`는 아직 배포되지 않은 소스 변경이며 서로 맞는 코어·추상화 소스 빌드가 필요합니다. 위 배포 패키지에는 이 추가 기능이 포함되어 있지 않습니다. [미배포 변경 사항](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased).

텍스트 작업에는 `AIModels.DeepSeek.V4Pro` (`deepseek-v4-pro`, V4-Pro-0813)를 선택할 수 있습니다. 기본 모델 Flash는 이미지를 지원하며 두 모델 모두 Low/High/Max 추론과 같은 출력 한도를 제공합니다. 기존 완성 응답·스트리밍·Run·로컬 함수 API에서 DeepSeek Responses를 사용하려면 요청 생성 전에 `UseResponsesApi = true`를 설정하세요. 기존 앱의 Chat Completions 동작을 유지하도록 기본값은 `false`이며, 설정은 요청과 후속 도구 라운드 전체에 캡처됩니다. Responses는 서버에 저장된 응답 ID 대신 전체 대화와 원본 추론 이력을 다시 전송합니다.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.DeepSeek;

var pro = new DeepSeekService(apiKey, AIModels.DeepSeek.V4Pro, httpClient)
{
    UseResponsesApi = true
};
pro.WithDeepSeekReasoning(DeepSeekReasoning.High);
string answer = await pro.CreateRequest("Review this deployment plan.").GetCompletionAsync();
```

같은 이미지를 여러 질문이나 대화에서 재사용하려면 한 번 업로드하세요. `UploadFileAsync`는 경로 또는 호출자가 소유한 스트림과 파일명을 받으며 목적은 `user_data`입니다. JPEG·PNG·GIF·WebP의 업로드 한도는 64 MiB입니다. `DeepSeekImageFileContent`는 두 전송 방식의 Flash에서 업로드한 이미지를 참조합니다. PDF·문서 입력이 아니며 V4 Pro에서는 거절됩니다. 만료를 생략하면 영구 보관하고 `expiresAfterSeconds`에는 3600~2592000초를 지정합니다. 해당 이미지를 참조하는 모든 대화가 끝날 때까지 파일을 유지하세요.

```csharp
using Mythosia.AI.Models.Messages;

var vision = new DeepSeekService(apiKey, AIModels.DeepSeek.Flash, httpClient)
{
    UseResponsesApi = true
};
var file = await vision.UploadFileAsync("chart.png", expiresAfterSeconds: 3600);
var question = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("Explain the trend in this chart."),
    new DeepSeekImageFileContent(file.Id)
});
string uploadedDescription = await vision.GetCompletionAsync(question);
var metadata = await vision.GetFileAsync(file.Id);
```

`GetFileAsync`로 메타데이터를 조회하고 `ListFilesAsync(new DeepSeekFileListOptions { After = lastId, Limit = 20, Order = DeepSeekFileOrder.Ascending })`로 한 페이지를 읽으며 필요가 끝나면 `DeleteFileAsync`로 삭제합니다. `HasMore`가 true이면 반환된 `LastId`를 다음 `After`에 사용합니다. `Descending`도 지원하며 파일 내용을 내려받는 엔드포인트는 공식 문서에 명시되어 있지 않습니다. Chat UI는 Flash·V4 Pro를 제공하고 재작성 모델 목록도 현재 카탈로그에서 가져옵니다. 저장된 옛 `DeepSeekChat` UI 값만 Flash로 이전하며 임의 모델 ID는 유지합니다.

[Responses](https://api-docs.deepseek.com/guides/responses_api/) · [Files](https://api-docs.deepseek.com/guides/files_api/) · [Models and limits](https://api-docs.deepseek.com/quick_start/pricing/)

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;

var deepseek = new DeepSeekService(apiKey, httpClient);
deepseek.ChangeModel(AIModels.DeepSeek.Flash);
deepseek.WithDeepSeekReasoning(DeepSeekReasoning.Low);

string draft = await deepseek.GetCompletionAsync("롤링 배포와 블루그린 배포를 롤백 위험까지 비교해 주세요.");

await using var run = await deepseek
    .CreateRequest("그 비교에서 사용한 가정을 검토해 주세요.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(
        options: new StreamOptions().WithReasoning());
await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.Write(item.Content);
}
string review = (await run.Result).Text;
```

라이브러리의 `ThinkingEnabled` 기본값은 `false`로 유지합니다. `WithDeepSeekReasoning(...)`는 추론을 켜고 서비스의 지속적 `ReasoningEffort` (`Auto`, `Low`, `High`, `Max`)를 지정합니다. 제공자별 `Auto`는 effort를 생략하여 제공자 기본값 `High`를 사용합니다. 공통 `WithReasoning(...)`은 도구 라운드를 포함한 한 논리적 요청만 재정의합니다. `None`은 추론을 끄며 `Minimal`/`Low`는 `Low`, `Medium`/`High`/`XHigh`는 `High`, `Max`는 `Max`로 매핑됩니다. 공통 `Auto`는 설정된 기본 동작을 유지합니다. 추론을 늘리면 응답 시간과 토큰 사용량이 증가할 수 있습니다. `ReasoningEffort` 속성만 바꾸면 추론이 켜지지는 않습니다.

`WithFunction(...)`으로 로컬 함수를 등록하면 모델이 애플리케이션 코드를 통해 데이터를 조회하거나 작업할 수 있습니다. 함수 호출은 추론을 켜거나 끈 상태 모두 지원합니다. Chat Completions에서는 추론 중 강제·필수 도구 선택을 거절하므로 자동 선택을 사용하세요. `UseResponsesApi = true`이면 추론 중에도 `ForceFunctionName`으로 함수를 지정할 수 있으며, 어댑터는 `type`과 `name`이 최상위에 있는 Responses `tool_choice`로 보냅니다. 네이티브 비동기 도구가 활성화되는 것은 아닙니다. 어댑터는 후속 도구 라운드에 필요한 원본 `reasoning_content`와 호출 ID를 보존합니다. Run과 기존 스트리밍은 `StreamOptions.WithReasoning()`을 켜면 제공자 추론을 `StreamingContentType.Reasoning`으로 노출합니다. 이 관찰 옵션 자체가 추론을 켜지는 않습니다. 제공자가 보고한 캐시·추론 토큰도 사용량에 반영합니다. 자동 컨텍스트 복구는 공통 스트리밍 루프를 사용합니다. 도구에 이전 원본 추론 이력이 필요한 경우에는 그 이력을 보존하도록 자동 압축을 막고 초과 오류를 그대로 전달합니다.

두 모델의 한도는 컨텍스트 1M, 출력 최대 384K (`393216`) 토큰이며 기본 요청 예산은 8,000토큰입니다. 추론 시 temperature·penalty를 생략하고 `top_p`는 최소 0.95로 보내며 비추론 시에는 `top_p`를 생략합니다. Responses의 네이티브 JSON schema는 기존 타입 출력 API로 사용합니다. 백그라운드 실행, 서버 `store`/`previous_response_id`, 호스팅 웹·파일 검색, `CachePreservation.Required`, 네이티브 비동기 도구, `SteerAsync`, 이미지 생성은 지원하지 않습니다. 로컬 RAG와 일반 도구 라운드는 사용할 수 있습니다.

### Perplexity Agent API

최신 정보를 바탕으로 답하고 사용자가 출처를 확인해야 할 때 Perplexity를 사용합니다. `PerplexityService`는 Agent API를 호출하며, 독립 검색과 임베딩은 직접 선택한 답변 모델의 검색 기반을 구성할 때 사용합니다.

```csharp
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient)
    .WithPerplexityOptions(new PerplexityAgentOptions
    {
        Preset = PerplexityPreset.Low,
        MaxSteps = 8
    });
string answer = await service.GetCompletionAsync("최신 배터리 재활용 방법을 비교하고 출처를 제시해 주세요.");
```

`WithPerplexityOptions(...)`는 서비스에 지속되는 설정이며 논리적 요청마다 복사됩니다. 공통 `WithReasoning(...)`과 `WithWebSearch(...)`는 다음 논리적 요청과 그 요청의 로컬 도구 라운드·타입 출력 복구에 적용됩니다. 내부 RAG 검색어 재작성에는 최종 답변용 검색 설정이 전달되지 않습니다.

`UsePreset(...)`로 프리셋을 간단히 선택할 수 있습니다. 프리셋·프로필은 자체 모델을 선택하며 `ModelOverride`로 명시적으로 바꿉니다. `DisableWebSearch`는 어댑터 기본 도구만 제거하고 프리셋의 내장 검색까지 끄지는 않습니다. Agent 추론은 모델에 따라 `Minimal`, `Low`, `Medium`, `High`, `XHigh`, `Max`를 사용합니다. `None`은 거절되며 직접 Sonar 모델의 명시적 추론 수준도 거절됩니다. 내부 `DisableReasoning`은 가능한 낮은 수준을 사용하거나 값을 생략하며, 완전히 꺼짐을 보장하지 않습니다.

Perplexity는 프로파일 적용 후 같은 최종 요청 계획으로 검증과 직렬화를 수행합니다. 억제된 부모 도구나 추론 설정 때문에 보조 Sonar 요청을 거부하지 않으며, 실제 적용되는 옵션은 계속 검증합니다. 프리셋과 폴백 목록의 모델 선택도 유지합니다.

`PerplexityHostedTools.WebSearch`, `FetchUrl`, `Sandbox`, `FinanceSearch`, `PeopleSearch`, `Mcp`, `Connector`로 도구 설정을 만듭니다. MCP 호출은 승인 대기 없이 실행되므로 필요하면 `allowedTools`로 제한합니다. 커넥터는 제공자의 프리뷰 기능이며 이미 연결된 통합 리소스를 참조합니다.

`StartBackgroundAsync`는 대화 이력에 추가하지 않고 입력을 캡처하며, 활성 로컬 함수 또는 `Store = false`를 거절합니다. `GetResponseAsync`는 한 번 조회하고 `WaitForCompletionAsync`는 종료 상태까지 조회합니다. `Id`와 `LastSequenceNumber`를 저장하고 `ResumeBackgroundRun(id).StreamAsync(startingAfter: cursor)`로 재연결합니다. 원격 작업은 `CancelAsync`로 취소합니다. 조회·읽기 토큰 취소는 해당 클라이언트 작업만 멈춥니다. `LastResponse`에는 텍스트·상태·사용량·인용·`OutputJson`이 있으며 답변을 쓰기 전에 종료 상태를 확인해야 합니다.

도구·추론·이미지·스키마 호환성은 선택한 모델에 따라 달라집니다. 공통 `WithFileSearch`는 Perplexity 벡터 저장소 어댑터가 아닙니다. 샌드박스에서 생성한 파일, 업로드한 첨부 파일, 원격 MCP 데이터는 별도 리소스이며 공통 파일 검색 저장소로 자동 전환되지 않습니다.

[Perplexity Agent API, 검색과 임베딩](../../../../../docs/ko/perplexity.md).


### 장점
- ChatBlock이 프로바이더에 대해 완전히 무관심 (깨끗한 분리)
- 기존 OOP 원칙에 부합 (서비스가 자기 전용 설정 관리)
- 서비스 인스턴스 하나에 전용 설정 하나 → 단순한 구조

### 단점
- 하나의 서비스 내 여러 ChatBlock에 동일한 전용 설정 적용됨

## ChatBlock 레벨로 이동이 필요한 경우

만약 향후 **ChatBlock별로 전용 설정을 독립적으로 유지해야 하는 요구사항**이 생기면, ChatBlock 내에 Lazy 프로퍼티로 전용 설정 클래스를 추가하는 방식으로 마이그레이션합니다.

```csharp
// 예시 (현재는 미구현)
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// 사용
chatBlock.Gemini.ThinkingBudget = 1024;
```

### 이 방식이 필요한 시나리오
- 하나의 서비스 인스턴스에서 ChatBlock A와 B가 서로 다른 ThinkingBudget을 사용해야 할 때
- 현실적으로 이런 케이스는 거의 없으므로, 현재는 서비스 레벨을 유지

## 결정 이력

- **2026-02-12**: 최초 Option B (ChatBlock 레벨)로 구현 후, 서비스 레벨로 롤백. 전용 설정은 서비스에 두는 것이 자연스럽다고 판단.

## 실행 제어를 선택하는 이유

오래 걸리는 작업에서는 진행 상황을 표시하거나 사용자가 중간에 조건을 바꿀 수 있어야 합니다. `StartRunAsync`가 반환하는 `AIRun`으로 해당 작업을 제어하며, 작업 중 추가 지시의 지원 여부는 provider가 결정합니다. 모델 설정은 서비스에서 실행 전에 구성하고, 추가 지시 전에는 `run.CanSteer`를 확인합니다. 사용 상황과 예제·취소·호환성은 [Run 사용 안내](../../../../../docs/ko/execution-api-transition.md)를 참고하세요.

## 사용자 정의 제공자 구현

공개 서비스 속성은 실행 중에도 서비스 기본값을 나타냅니다. 사용자 정의 `AIService` 하위 클래스가 전송 데이터를 만들 때는 `RequestTemperature`, `RequestTopP`, `RequestMaxTokens`, `RequestSystemMessage`, `RequestModel`, `RequestFunctions` 같은 protected 요청 접근자를 사용해야 합니다. `Temperature`를 직접 읽으면 서비스 기본값을 읽으므로 빌더의 변경값을 놓칩니다. 제공자 고유 기본값은 `CaptureRequestSettings`에서 base 구현을 호출한 뒤 저장하고, 변경 가능한 컬렉션은 복사하세요. 자체 값은 `RequestSetting<T>`로 읽습니다. 별도 옵션 객체를 캡처하는 제공자는 `CloneProviderRequestOptions`에서도 복사해야 합니다. base 실행을 거치지 않는 기존 override 진입점은 `BeginRequestSettingsScope()`에 진입하고 기존 기능 범위도 유지해야 합니다. 이는 제공자 구현 확장 계약이며 `IAIService`에 필수 멤버를 추가하지 않습니다.

사용자 정의 도구 실행 코드는 기존 protected virtual `ProcessFunctionCallAsync(FunctionCall)` 시그니처를 계속 재정의할 수 있습니다. protected `FunctionCancellationToken`을 I/O나 `HandlerWithCancellation`에 전달하면 실행 취소가 이어집니다. 기존 `Handler` 델리게이트를 직접 호출하면 `CancellationToken.None`을 사용합니다. 기본 실행부는 이미 취소를 전달하는 경로를 선택합니다. [도구 계약](../../../../../docs/ko/function-calling.md#tool-execution-contract)을 참고하세요.

완성된 답변과 중지 버튼만 필요하면 `GetCompletionAsync`에 `cancellationToken`을 전달하세요. 진행 이벤트나 지원 모델의 추가 지시에는 Run을 사용합니다. [일반 응답 취소](../../../../../docs/ko/completions.md#completion-cancellation)를 참고하세요.

이 취소 계약은 Mythosia.AI 8.0.0 / Mythosia.AI.Abstractions 4.0.0에 포함됩니다. 토큰을 생략한 앱 호출과 기존 profile/context 위치 인자 호출은 소스 수준에서 유지되지만, 사용하는 패키지는 다시 빌드해야 합니다. 사용자 정의 `IAIService` 구현은 두 완료 메서드의 마지막에 `CancellationToken cancellationToken = default`를 추가하고 전달해야 합니다. `AIService`를 상속한 사용자 제공자는 기존 `GetCompletionAsync(Message)` override를 유지하며 protected `RequestCancellationToken`을 통신에 전달해야 합니다. 빌더와 Run 자체에는 이 인터페이스 변경이 필요하지 않았습니다. 문자열·profile/context 완료 호출, 이미지 편의 메서드, `RunAgentAsync` 등 변경된 public virtual 오버로드를 재정의한 하위 클래스도 새 `CancellationToken` 매개변수를 추가하고 전달해야 합니다. 기존 시그니처를 유지하는 것은 `Message` 하나를 받는 제공자 override입니다. 변경된 메서드를 델리게이트에 직접 대입한 코드는 토큰을 전달하거나 생략하는 명시적 람다로 바꿔야 할 수 있습니다.

[공통 지원 정의로 모델별 기능 선택지 구성하기](../../../../../docs/ko/model-capabilities.md).

`ApplyRequestProfile`·`ApplyProviderSpecificRequestProfile`은 논리 요청마다 한 번 실행한 뒤 최종 설정을 검증합니다. 애플리케이션의 일반 중첩 호출은 별도 요청입니다. 프레임워크의 가상 메서드 전달과 내부 후속 처리는 준비된 설정을 유지하며, 전달 전에 같은 base 진입점에서 별도 보조 작업을 호출할 때는 [protected 독립 요청 범위](../../../../../docs/ko/request-building.md#provider-request-adapters)를 사용합니다. 사용자 정의 공급자는 `BeginRequestFeaturesScope` 뒤에 새 protected 훅인 `ResolveRequestMessage(message)`를 호출하고 반환된 입력 복사본을 보관할 수 있습니다. 기존 공급자 재정의는 호환성을 유지합니다. 기능 조회는 부작용 없는 별도 훅을 사용합니다.

사용자 정의 제공자의 프로파일이 고유 모드 플래그를 바꾼다면 `ApplyCapabilityRequestProfile(AIRequestProfile)`를 재정의하고, 조회에 필요한 플래그만 `SetExecutionSetting(...)`으로 적용하세요. 기본 훅은 아무 작업도 하지 않습니다. 공통 프로파일 설정은 빌더에 이미 캡처되어 있으며, 조회 중에는 `ApplyRequestProfile`이나 `ApplyProviderSpecificRequestProfile`을 호출하지 않습니다. 이 훅에서 검증·콜백·직렬화·예산 예약을 수행하거나 서비스 및 호출자 소유 상태를 변경하면 안 됩니다. 임시 설정은 조회가 끝나거나 재정의한 훅에서 예외가 발생해도 복원됩니다.
