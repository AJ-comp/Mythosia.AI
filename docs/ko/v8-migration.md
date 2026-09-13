# Mythosia.AI 8로 업그레이드하기

요청마다 설정을 따로 보관하고, 사용자가 진행 중인 작업을 중지하며, 답변과 사용량·출처를 함께 저장해야 할 때 이번 릴리스를 사용하세요. 아래 여섯 가지 구조 개선과 공급자·모델 업데이트, 세 차례 적대적 검증에서 수정한 내용을 하나의 메이저 업데이트로 묶었습니다.

앱에서 사용하는 패키지만 함께 업데이트하고 소비 프로젝트를 다시 빌드하세요. Mythosia.AI는 맞는 Abstractions 의존성을 자동으로 가져옵니다. 아래 표는 게시된 기준 버전과 이번 릴리스에서 함께 사용하는 호환 버전을 보여줍니다.

| 패키지 | 게시된 기준 버전 | 게시 목표 버전 |
| --- | --- | --- |
| `Mythosia.AI` | 7.1.0 | [8.0.0](../../src/core/Mythosia.AI/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Abstractions` | 3.1.0 | [4.0.0](../../src/core/Mythosia.AI.Abstractions/RELEASE_NOTES.md#v400) |
| `Mythosia.AI.Providers.Alibaba` | 2.0.1 | [3.0.0](../../src/core/Mythosia.AI.Providers.Alibaba/RELEASE_NOTES.md#v300) |
| `Mythosia.AI.Rag` | 7.6.0 | [8.0.0](../../src/rag/Mythosia.AI.Rag/RELEASE_NOTES.md#v800) |
| `Mythosia.AI.Mcp` | 0.0.1-preview | [0.1.0-preview](../../src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v010-preview) |
| `Mythosia.AI.Serving.Vllm` | 1.0.0-preview | [1.0.0](../../src/serving/Mythosia.AI.Serving.Vllm/RELEASE_NOTES.md#v100) |

`Mythosia.AI.Serving.Vllm`은 이번 릴리스에서 `1.0.0-preview`를 정식 `1.0.0`으로 전환합니다. 기존 모델·상태·서버 버전·메트릭 API를 유지하며, 핵심 AI 패키지에 의존하지 않는 독립 패키지입니다.

## 필요한 동작에서 변경 내용 찾기

| 필요한 이유 | 변경과 전환 방법 |
| --- | --- |
| 이미지 옵션 오타를 전송 전에 확인 | 문자열 대신 `ImageQuality`, `ImageBackground`, `ImageOutputFormat`, `ImageSize.Pixels(...)` / `ImageSize.Preset(...)`을 사용합니다. 공급자별 지원 차이는 유지합니다. |
| 여러 요청을 준비해도 서로 설정이 바뀌지 않게 처리 | `CreateRequest(...)`로 시작하면 `With...`마다 독립적인 빌더를 반환합니다. 반환값을 보관하세요. 서비스 수준 setter는 여전히 공유 기본값을 바꿉니다. |
| 비동기 도구에서 앱의 데이터를 그대로 반환 | 특성으로 등록한 메서드도 `Task<T>` / `ValueTask<T>`의 객체를 반환하고 주입된 `CancellationToken`을 받을 수 있습니다. 예외는 실패로 기록하며 기존 문자열 핸들러도 유지합니다. |
| 사용자가 중지하면 더 기다리지 않기 | 완료·Run·지원 RAG 진입점에 `cancellationToken`을 전달합니다. 로컬 작업과 협조적인 도구를 중지하며, 공급자 원격 작업 중지나 완료된 외부 동작 취소를 보장하지는 않습니다. |
| 답변·사용량·출처를 함께 보관 | `AIRun.Result`는 `Task<AIRunResult>`를 반환합니다. 문자열이 필요하면 `(await run.Result).Text`를 사용하세요. 스트림을 읽지 않아도 결과를 모읍니다. |
| 선택한 모델에 맞는 조작 항목 표시 | `request.GetCapabilities()` 또는 서비스·이미지 기능 조회를 사용합니다. `Supported`, `Unsupported`, `Unknown`은 라이브러리의 로컬 지원 정보이며 실시간 계정 접근 확인이 아닙니다. |

## 호출 코드와 사용자 정의 공급자 변경

이미지 옵션 타입, `AIRun.Result`, 취소 토큰이 추가된 시그니처는 호환성이 깨지는 계약입니다. 사용자 정의 `IAIService` 구현과 변경된 공개 오버로드의 override는 토큰을 추가하고 전달해야 합니다. 공급자용 `GetCompletionAsync(Message)` override는 기존 시그니처를 유지하고 `RequestCancellationToken`을 전달합니다. 사용자 정의 `AIRun`은 `AIRunResult`를 반환해야 합니다. GetCompletionAsync의 문자열 반환형, 타입 지정 완료 및 `StructuredStreamRun<T>.Result`의 타입 반환형은 유지합니다. 입력을 받는 기존 서비스·RAG StreamAsync도 v8에서 공개로 유지합니다. RunAgentAsync와 RunAgentStreamAsync는 호환 동작과 obsolete 경고를 유지하며, 새 진행 표시·취소·지원 모델 추가 지시에는 Run을 사용합니다.

## 요청 하나로 최종 결과와 선택적 진행 표시 받기

```csharp
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Capabilities;

AIRequestBuilder request = service.CreateRequest("Summarize this document.");
var capabilities = request.GetCapabilities();
if (capabilities.Temperature == CapabilitySupport.Supported)
    request = request.WithTemperature(0.2f);

await using var run = await request
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

AIRunResult result = await run.Result;
Console.WriteLine(result.Text);
```

이미지 옵션은 OpenAI에서 픽셀, Google·xAI에서는 `ImageSize.Preset(...)`으로 지정합니다. 선택한 공급자가 명시적 형식을 지원할 때만 Auto를 바꾸고, 저장 형식은 반환된 `GeneratedImage.MediaType`을 따르세요.

```csharp
using Mythosia.AI.Models.Images;

var imageRequest = new ImageGenerationRequest
{
    Prompt = "A simple architectural study",
    Quality = ImageQuality.Auto,
    Background = ImageBackground.Auto,
    OutputFormat = ImageOutputFormat.Auto,
    Size = ImageSize.Pixels(1536, 1024)
};
var generated = await images.GenerateImagesAsync(imageRequest, cancellationToken);
```

등록한 도구에서는 아래처럼 앱의 객체를 반환하세요. 저수준 `HandlerWithCancellation`은 여전히 `Task<string>`을 반환하며 새 객체 래퍼를 요구하지 않습니다.

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;

public sealed record FileResult(string Path, string Text);

public sealed class FileTools
{
    [AiFunction("read_file", "Read a text file")]
    public async Task<FileResult> ReadFileAsync(
        string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        return new FileResult(path, text);
    }
}
```

## 공급자 업데이트와 검증 범위

이번 릴리스에는 준비해 둔 Fable 5.1, Gemini 3.7/3.8 Flash, Grok 4.6, DeepSeek Flash, Perplexity Agent 연동과 OpenAI·Google·xAI 공통 이미지 생성·편집도 포함됩니다. 제거된 모델 상수와 Perplexity 엔드포인트 변경은 호출 코드 수정이 필요할 수 있습니다. 정확한 범위는 공급자 가이드와 패키지 릴리스 노트를 확인하세요.

Perplexity 연구 설정은 `PerplexityAgentOptions`로 지정합니다. Profile·Custom Skill·Connector 실증 테스트는 준비되어 있지만 실행하려면 등록된 계정 자원이 필요합니다. MCP는 preview 패키지를 유지합니다. 해제가 시작된 연결의 호출은 `ObjectDisposedException`, 읽기 루프가 이미 종료된 연결의 새 호출은 `McpException`으로 실패하여 무기한 대기를 피합니다.

세 차례 적대적 검증으로 요청 복사, 도구 반환값, 취소·정리, 토큰 계산, 공급자 응답 검증, MCP 연결 수명을 보강했습니다. 세 번째 검증에서 회귀 사례 43개를 추가했고 전체 2,703개 테스트가 통과했습니다. 문서는 13개 언어로 확인했습니다. 해당 검증에서 실제 공급자 API를 호출하지 않았으며, 단위 테스트 통과가 모든 계정 자원을 사용하는 연동까지 실증했다는 뜻은 아닙니다.

## 상세 사용 안내

- [CreateRequest / AIRequestBuilder](request-building.md)
- [AIRun / AIRunResult](execution-api-transition.md)
- [ImageQuality / ImageSize](providers.md#image-options-migration)
- [CancellationToken](completions.md#completion-cancellation)
- [Task<T> / ValueTask<T> / HandlerWithCancellation](function-calling.md#tool-execution-contract)
- [GetCapabilities](model-capabilities.md)
- [PerplexityAgentOptions](perplexity.md)
