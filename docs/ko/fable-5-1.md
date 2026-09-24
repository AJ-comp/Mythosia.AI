# Claude Fable 5.1의 긴 작업을 관찰하고 제어하기

[Claude Opus 5.5](providers.md#claude-opus-55)는 미배포 추가 기능입니다. 추론은 항상 켜져 있고 기본 effort는 medium, 표시는 생략입니다. 읽을 수 있는 진행 안내는 명시적으로 요청하세요. 기본값과 모델 binding 규칙은 Fable 5.1과 다릅니다.

> Fable 5.1 설정은 `Mythosia.AI` 8.0.0과 `Mythosia.AI.Abstractions` 4.0.0 이상이 필요합니다. 기존 Run·추론/검색·GPT-6 Astra API의 최소 버전은 7.1.0 / 3.1.0으로 유지합니다.

## 이 설정이 왜 필요한가요?

문서를 조사하는 작업은 답변을 만들기 전에 여러 번 검색하고 도구를 호출할 수 있습니다. 앱에서는 진행 상황을 보여주거나, 이번 턴에만 특정 확인을 요구하거나, 이전 대화 내용을 수정한 뒤 작업을 이어가야 할 수 있습니다. Fable 5.1은 이런 상황에 필요한 설정을 제공하지만, 보존된 추론을 재사용할 때는 대화 이력 자체도 요청 계약의 일부가 됩니다.

작업 관찰과 취소에는 [Run API](execution-api-transition.md), 추론량과 검색 출처 선택에는 [공통 추론·검색 API](reasoning-and-search.md)를 사용합니다. 진행 안내와 이력 처리는 아래 Claude 전용 설정으로 조절합니다. 모델의 네이티브 기능이 모두 Mythosia API로 제공된다는 뜻은 아닙니다.

## 모델과 추론 수준을 명시적으로 선택하기

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Anthropic;

var service = new AnthropicService(apiKey, httpClient);
service.ChangeModel(AIModels.Anthropic.ClaudeFable5_1);
service.WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High);

string answer = await service.GetCompletionAsync("이 마이그레이션 계획을 검토해 주세요.");
```

`ClaudeFable5_1`은 `claude-fable-5-1`을 선택합니다. `ClaudeMythos5_1`은 `claude-mythos-5-1`을 선택하며 Project Glasswing 접근 권한이 필요합니다. 기존 Fable 5와 Mythos 5 상수도 유지합니다. 두 5.1 모델은 텍스트·이미지를 입력받아 텍스트를 출력하며, 컨텍스트는 1M토큰, 최대 출력은 128K토큰입니다. [공식 모델 개요](https://platform.claude.com/docs/en/models/fable-5-1/overview).

모델 자체의 기본 effort는 `high`이지만, Mythosia의 `ClaudeReasoningEffort.Auto`는 기존 `ThinkingBudget` 매핑을 유지합니다. 추론이 활성화된 예산은 기본 `High`, 32,768 이상은 `XHigh`, 100,000 이상은 `Max`로 적용합니다. 추론 끄기 요청은 낮은 adaptive effort와 읽을 수 있는 추론 생략으로 표현합니다. `High`가 필요하면 명시적으로 선택하세요. `Auto`가 항상 effort를 생략해 모델 기본값에 맡긴다는 뜻은 아닙니다.

## 도구 호출 사이의 진행 상황 보여주기

`ClaudeThinkingDisplay.Updates`는 내부 추론을 감추면서 읽을 수 있는 진행 안내를 요청합니다. `Summarized`는 요약된 추론도 함께 포함하며, `Omitted`는 읽을 수 있는 thinking 블록을 생략합니다. 안내는 모델이 생성할 때만 오므로 일정한 간격의 상태 알림을 보장하지 않습니다. [공식 진행 안내](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1#progress-updates-between-tool-calls-beta).

```csharp
using Mythosia.AI.Models.Streaming;

service.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates);

await using var run = await service.StartRunAsync(
    "등록된 도구로 보고서의 내용을 조사해 주세요.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine($"진행: {item.Content}");
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string result = (await run.Result).Text;
```

진행 안내는 기존 `StreamingContentType.Reasoning` 이벤트로 전달합니다. `StreamOptions.FullOptions` 또는 `StreamOptions.Default.WithReasoning()`으로 추론 관찰을 켜세요. 비스트리밍 호출에서는 완료 후 `service.LastThinkingContent`를 읽습니다. 진행 안내는 최종 답변과 별개이며 원본 사고 과정을 노출하지 않습니다.

## 이번 턴의 변경을 이전 이력에 덮어쓰지 않기

Fable 5.1의 thinking 블록은 생성 당시의 시스템 프롬프트, 도구, 이전 메시지에 묶입니다. 이후 thinking을 남겨둔 채 앞부분을 바꾸면 기존 추론이 무효화될 수 있습니다. 이번 답변 전에 고객지원 정책을 확인하라는 조건처럼 한 턴에만 적용할 지시는 대화 뒤에 추가하고 이력에 유지한 뒤, 다음 사용자 메시지가 오면 적용을 끝냅니다. 매 요청마다 최상위 시스템 프롬프트를 바꿀 필요가 없습니다. 추론 수준 변경과 턴별 지시는 별도 설정입니다.

```csharp
await service
    .WithTurnInstruction("이번 답변 전에 등록된 고객지원 정책 도구를 확인하세요.")
    .GetCompletionAsync("개봉한 제품도 반품할 수 있나요?");

await service
    .WithConversationInstruction("남은 대화에서는 한국어로 답변하세요.")
    .GetCompletionAsync("다음 절차를 설명해 주세요.");
```

두 메서드는 다음 논리적 요청에 지시를 적용합니다. Mythosia는 기존 메시지를 유지하고 사용자 입력이나 도구 결과 뒤에 시스템 메시지를 추가합니다. `WithTurnInstruction`은 `clear_at: "next_user_message"`를 사용합니다. 한 논리적 요청 안에서는 도구 결과 턴마다 지시를 다시 추가해 해당 요청이 끝날 때까지 효력을 유지합니다. `WithConversationInstruction`은 이후 턴에도 적용됩니다. 두 설정 모두 작업을 시작하기 전에 지정하며, 이미 실행 중인 응답에 개입하는 `run.SteerAsync`와는 다릅니다.

요청 사이에 추론 수준을 바꾸면서 재사용 가능한 캐시 접두부를 보존하려면 `Mythosia.AI.Extensions`의 `.WithReasoning(ReasoningLevel.High, cache: CachePreservation.Required)`를 사용합니다. 라이브러리는 메시지별 effort 변경을 전송하고 이력에 유지합니다. 지원 조합은 [공통 가이드](reasoning-and-search.md)를 참고하세요. 5.1에서 `AIRequestContext`의 요청별 시스템 접두부·접미부는 이전 시스템 프롬프트를 바꾸는 대신 뒤에 추가하는 턴별 지시로 변환합니다.

Fable 5.1은 이전 Claude 모델의 thinking을 읽을 수 있지만 이전 모델은 Fable 5.1의 thinking을 읽지 못합니다. Mythos 5.1은 같은 5.1 기능을 제공하지만 Fable의 접두부 binding 검사를 강제하지 않습니다. 이력 수정, 모델 전환, 추론 삭제가 발생하면 이전 추론이 그대로 유지되었다고 가정하지 말고 변경을 확인해야 합니다. [공식 마이그레이션 안내](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

## 의도적인 이력 변경 진단하기

`ThinkingPrefixMismatchBehavior = null`이면 공급자의 계정별 검사 정책을 따릅니다. `WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)`는 서버 검사를 명시적으로 요청합니다. 사용자가 이력, `SystemMessage`, 도구를 수정하면 Anthropic에 그대로 전송하며, `Error`에서 접두부가 맞지 않으면 서버가 400을 반환합니다. 같은 잘못된 요청을 재시도해도 해결되지 않습니다.

앱에서 이전 내용을 의도적으로 변경하고 관련 추론이 사라져도 괜찮다면 `DropBlock`을 선택합니다. Mythosia가 thinking을 미리 조용히 제거하지 않고, Anthropic에 이 제어를 전송합니다.

```csharp
service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.DropBlock);
service.SystemMessage = "수정된 정책을 검토하는 어시스턴트입니다.";
await service.GetCompletionAsync("권고안을 다시 검토해 주세요.");

foreach (ClaudeInputTransformation change in service.LastInputTransformations)
    Console.WriteLine($"{change.Type}: {change.Reason} ({change.Model})");
```

`LastInputTransformations`는 공급자가 보고한 `Type`, `Path`, `Reason`과 출처를 구분하는 `ResponseId`, `Model`을 제공합니다. `prefix_binding_mismatch`는 앞부분 변경, `model_binding_mismatch`는 대상 모델이 읽을 수 없는 thinking을 뜻합니다. Drop은 추론 복구가 아니라 폐기입니다. 추론을 보존해야 한다면 이력을 그대로 유지하고, 초기화가 필요하면 새 대화를 시작하세요.

Mythosia는 내부 RAG·context 처리 때문에 이력이 의도치 않게 바뀌지 않도록 전송 이력을 보존합니다. 일반 Fable 5.1 대화의 기본값·`Error` 경로에서는 자동 로컬 압축을 사전에 차단합니다. `DropBlock`에서는 허용하지만 추론이 폐기될 수 있으며 캐시 적중을 보장하지 않습니다. 별도 옵션인 `CachePreservation.Required`는 더 엄격한 이력 보호를 유지합니다. `WithWebSearch()` 같은 공통 옵션은 요청 후 소비됩니다. 다음 턴에서 생략하면 네이티브 tools 배열이 바뀌어 접두부가 맞지 않을 수 있습니다. 이력을 보존하려면 같은 도구·검색 설정을 다시 적용하고, 의도적으로 바꿀 때는 `DropBlock` 또는 새 대화를 사용하세요. 이 옵션은 자동으로 다음 요청에 이월하지 않습니다.

보존된 전송 스냅샷은 서비스와 해당 `ChatBlock`에 속합니다. `ChatBlock`만 새 서비스로 복사하면 이전 RAG/context·턴별 시스템 메시지 스냅샷은 함께 옮겨지지 않습니다. 추론을 보존하려면 같은 서비스와 대화로 이어가고, 원본 이력만 옮겼다면 보존을 가정하지 말고 새 대화를 시작하세요.

## 일반 도구 선택 사용하기

Fable 5.1과 Mythos 5.1은 강제 도구 선택을 거부합니다. `ForceFunctionName`을 지정하지 않고, 등록한 도구를 언제 사용해야 하는지 요청에 설명하세요. 해당 턴에서 도구를 호출하면 안 되는 경우에는 `FunctionsDisabled`를 사용합니다. 정해진 타입의 답변이 필요하면 JSON을 얻기 위해 함수를 강제하는 대신 기존 구조화 출력 API를 사용합니다.

## 서버가 자동으로 처리하는 변경 이해하기

| 네이티브 옵션 | 필요한 Anthropic beta |
| --- | --- |
| 메시지별 effort | `mid-conversation-output-config-2026-07-01` |
| 한 턴에만 적용하는 시스템 메시지 | `mid-conversation-system-clear-at-2026-08-21` |
| `thinking.display: "updates"` | `thinking-display-updates-2026-08-18` |
| thinking binding 제어와 `input_transformations` | `thinking-binding-controls-2026-08-01` |

Mythosia는 지원되는 해당 설정을 켤 때 필요한 헤더를 추가합니다. 하나의 beta를 켠다고 나머지가 모두 활성화되지는 않습니다. 이번 통합은 서버 측 compaction, 네이티브 도구 추가·삭제 블록, 자동 모델 fallback을 새로 제공하지 않습니다.

두 모델은 공급자가 정한 30일 보관 조건이 필요하며, ZDR은 Anthropic의 명시적 승인이 필요합니다. Adaptive thinking은 항상 켜져 있고 수동 `budget_tokens`나 추론 비활성화는 사용할 수 없으며, 사용자 지정 샘플링 필드는 전송하지 않습니다. 계정 접근과 보관 조건은 서버 요구사항입니다. [공식 마이그레이션 조건](https://platform.claude.com/docs/en/models/fable-5-1/migration-guide).

텍스트 워터마크, 지원되는 미디어의 출처 정보, 캐시 읽기 가격은 Anthropic이 적용합니다. 이를 위해 Mythosia에 별도 요청 옵션을 추가할 필요는 없습니다. 이 통합은 미디어 출처 생성 API, 워터마크 스위치, 과금 제어를 추가하지 않습니다. [Fable 5.1 변경 사항](https://platform.claude.com/docs/en/models/fable-5-1/whats-new-fable-5-1)을 참고하세요.
