# 프로바이더별 기능

> `CreateRequest` 예제는 Mythosia.AI 8.0.0 / Abstractions 4.0.0의 기능입니다. Run과 공통 요청 기능이 처음 추가된 이전 7.1 버전에는 빌더가 없습니다. 이전 패키지에서는 기존 서비스 오버로드를 사용하세요.

<a id="image-options-migration"></a>
처리 모드와 반환되는 적용 정보는 공급자·모델·API별로 다릅니다. [공통 속도 옵션](request-building.md#inference-speed)과 capability를 확인하고, Fast를 요청했다는 사실과 실제 Fast 적용 결과를 구분하세요.

## 이미지 옵션 타입 전환

품질과 파일 형식은 자동완성으로 선택하고, 정확한 픽셀 크기와 해상도 등급은 구분해서 요청합니다. 문자열 오타를 줄이고, 지정한 픽셀 크기가 다른 해상도 등급으로 조용히 바뀌는 일을 방지하기 위한 변경입니다.

Mythosia.AI 8.0.0의 호환성 변경입니다. `Quality`, `Background`, `OutputFormat`은 enum, `Size`는 `ImageSize`로 바뀌고 요청의 별도 `AspectRatio` 속성은 제거됩니다. `OutputFormat`의 기본값은 `ImageOutputFormat.Auto`입니다. 생성·편집 함수는 그대로 사용합니다.

**Before**

```text
var request = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Quality = "max",
    Background = "transparent",
    OutputFormat = "png",
    Size = "1536x1024"
};

// Google / xAI
Size = "2K";
AspectRatio = "3:2";
```

**After**

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

var request = new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "A glass pavilion at sunrise",
    Quality = ImageQuality.Max,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png,
    Size = ImageSize.Pixels(1536, 1024)
};

// Google / xAI
var presetRequest = new ImageGenerationRequest
{
    Prompt = "A glass pavilion at sunrise",
    Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.ThreeByTwo),
    OutputFormat = ImageOutputFormat.Auto
};
```

`Pixels(width, height)`는 정확한 픽셀 크기를 요청합니다. `Preset(resolution, aspectRatio)`는 해상도 등급과 비율을 지정하며 실제 픽셀 크기는 공급자가 결정합니다. 크기 제약이 없으면 `ImageSize.Auto`를 사용하세요. 대략적인 크기도 괜찮은 경우에만 기존 픽셀 요청을 프리셋으로 바꾸세요.

| Provider | `Size` | `OutputFormat` |
| --- | --- | --- |
| OpenAI | `ImageSize.Auto`, `Pixels(...)` | `Auto` → PNG, `Png`, `Jpeg`, `WebP` |
| Google | `ImageSize.Auto`, `Preset(...)` | `Auto`, `Jpeg` |
| xAI | `ImageSize.Auto`, `Preset(...)` | `Auto` |

정의되지 않은 enum 값과 공급자·모델이 지원하지 않는 조합은 HTTP 요청 전에 거절합니다. enum에 있는 값이라도 모든 모델이 지원하는 것은 아닙니다. Google의 품질은 `ImageQuality.Auto`만, xAI는 `Auto`, `Low`, `Medium`만 지원합니다.

`EditImagesAsync`가 `Task`를 반환한 뒤에는 입력 버퍼를 재사용할 수 있습니다. 시작된 요청은 OpenAI 마스크를 포함한 이미지 바이트를 별도로 보관하므로, 원본 `ImageInput.Data` 배열을 나중에 바꿔도 업로드 내용은 바뀌지 않습니다.

중단된 출력을 완성된 이미지로 저장하지 않도록, Google 생성·편집은 반환된 모든 후보가 `finishReason: STOP`으로 끝나야 성공합니다. 하나라도 차단·미완료 상태이거나 이 종료 상태가 없으면 전체 호출이 `AIServiceException`을 발생시킵니다. 인라인 base64 데이터나 이미지 MIME이 누락되거나 잘못되어도 전체 호출이 실패하며, PNG로 추측하지 않습니다. 이 검사는 실제 파일 바이트가 선언된 이미지 형식과 일치하는지까지 확인하지는 않습니다.

## OpenAI (OpenAIService)

> GPT-6 Astra 지원과 비동기 도구 호출은 `Mythosia.AI` 7.1.0부터 제공하며, 공통 타입은 `Mythosia.AI.Abstractions` 3.1.0에 포함됩니다.

초안과 검토 단계의 추론량을 바꾸거나 웹·문서를 근거로 답하려면 [공통 추론·검색 API](reasoning-and-search.md)를 사용합니다. 지원 범위 안에서 OpenAI·Anthropic·Google에 같은 호출 방식을 적용하며, 캐시 보존 변경과 출처 수집도 제공합니다. 아래의 공급자별 세부 설정도 계속 사용할 수 있습니다.

외부 도구의 결과를 기다리는 동안에도 독립적인 설명이나 준비 작업을 진행하고 싶을 때 GPT-6의 비동기 도구 호출을 사용할 수 있습니다. 예를 들어 날씨 조회와 일반 여행 준비물 안내를 함께 처리하는 상황입니다.

`FunctionDefinition.AllowAsync = true` 또는 `FunctionBuilder.WithAsync()`로 GPT-6 Astra / Sol / Luna의 Responses API에서 비동기 도구 호출을 선택적으로 허용합니다. 기본값은 `false`이며, 미지원 모델에서는 같은 핸들러의 결과를 기다립니다. 예제와 요청 수명은 [함수 호출 가이드](function-calling.md)를 참고하세요.

<a id="gpt-6-sol-luna"></a>

### GPT-6 Sol / Luna (미배포)

복잡한 코딩·도구 활용·에이전트 작업에는 GPT-6 Sol을, 텍스트나 이미지 입력을 대량 처리하며 비용을 줄이고 싶을 때는 Luna를 선택하세요. 기존 완료 응답·스트리밍·Run API를 그대로 사용하므로 모델을 바꿔도 애플리케이션의 호출 흐름은 유지됩니다.

> 아직 배포되지 않은 추가 기능으로, 서로 맞는 코어·추상화 빌드가 필요합니다. 게시된 Mythosia.AI 8.0.0 / Abstractions 4.0.0에는 `Gpt6Sol`, `Gpt6Luna`, `Gpt6Reasoning.None`이 없습니다. 기존 Astra 기능의 최소 버전과 서비스 기본 모델은 바뀌지 않습니다.

`AIModels.OpenAI.Gpt6Sol` (`gpt-6-sol`) 또는 `AIModels.OpenAI.Gpt6Luna` (`gpt-6-luna`)로 선택합니다. 둘 다 텍스트·이미지를 입력받아 텍스트를 출력하며, 문맥 창은 1,050,000토큰, 최대 입력은 922,000토큰, 최대 출력은 128,000토큰입니다. 입력·추론·출력을 합쳐 문맥 한도를 지켜야 합니다. `MaxTokens`는 문맥 크기가 아니라 요청한 출력 예산입니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);
service.ChangeModel(AIModels.OpenAI.Gpt6Sol);
await using var run = await service.CreateRequest("Review this design.")
    .WithReasoning(ReasoningLevel.High)
    .StartRunAsync(onText: text => Console.Write(text));
var result = await run.Result;

service.ChangeModel(AIModels.OpenAI.Gpt6Luna);
string answer = await service.CreateRequest("Summarize this paragraph.")
    .WithReasoning(ReasoningLevel.None)
    .WithTemperature(0.2f)
    .GetCompletionAsync();
```

`Auto`는 `Medium`으로 적용됩니다. Sol/Luna는 `None`, `Low`, `Medium`, `High`, `XHigh`, `Max`를 지원하며 `Minimal`은 지원하지 않습니다. 요청별로 `WithReasoning(ReasoningLevel.None)`을 쓰거나 `WithGpt6Parameters`에 `Gpt6Reasoning.None`을 지정할 수 있습니다. Sol/Luna에서 `None`일 때만 `Temperature` / `TopP`를 전송하고 추론이 켜져 있으면 생략합니다. Astra는 항상 추론이 필요하고 샘플링 값을 생략합니다. `AIRequestProfile.DisableReasoning`은 Sol/Luna에서 `None`, Astra에서 Standard 모드의 `Low`를 사용하며 추론 요약을 생략합니다.

`Gpt6ReasoningMode.Standard`와 `.Pro`는 선택한 동일 모델 ID를 사용합니다. 세 GPT-6 모델 모두 Responses의 도구 호출, 선택적 비동기 도구, WebSocket Run을 통한 추가 지시, Standard·단일 에이전트 모드의 캐시 보존 추론 변경을 지원합니다. 추가 지시 전 `run.CanSteer`를 확인하세요. 접수된 지시가 이미 나온 출력을 되돌리지는 않습니다. `WithSpeed(InferenceSpeed.Fast)`는 추론 수준과 별개로 유료 Fast 처리를 요청합니다. 실제 적용은 `result.Processing`에서 확인하며, 계정 권한이나 서버의 일반 처리 전환은 로컬 지원 여부와 별개입니다.

[GPT-6 Sol](https://developers.openai.com/api/docs/models/gpt-6-sol) · [GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna) · [Reasoning](https://developers.openai.com/api/docs/guides/reasoning) · [Fast](https://developers.openai.com/api/docs/guides/fast-mode)

### 추론 수준

속도와 분석 깊이 사이의 균형을 조절합니다:

```csharp
using Mythosia.AI.Models;

// GPT-6 Astra
service.ChangeModel(AIModels.OpenAI.Gpt6Astra);
service.WithGpt6Parameters(
    reasoningEffort: Gpt6Reasoning.Medium, // Auto, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium,
    reasoningSummary: ReasoningSummary.Auto,
    reasoningMode: Gpt6ReasoningMode.Standard);

// GPT-5.6: Sol은 최상위 모델이며, Terra와 Luna는 비용을 낮춘 선택지입니다.
service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
service.WithGpt5_6Parameters(
    reasoningEffort: Gpt5_6Reasoning.Medium, // None, Low, Medium, High, XHigh, Max
    verbosity: Verbosity.Medium);            // Low, Medium, High

// GPT-5.4 시리즈
service.ChangeModel(AIModels.OpenAI.Gpt5_4);
service.Gpt5_4ReasoningEffort = Gpt5_4Reasoning.High; // None, Low, Medium, High, XHigh

// GPT-5.2 시리즈
service.ChangeModel(AIModels.OpenAI.Gpt5_2);
service.Gpt5_2ReasoningEffort = Gpt5_2Reasoning.Medium;

```

GPT-6 Astra는 기본적으로 Responses API를 사용하며, 함수 호출에도 이 API가 필요합니다. `Auto`는 라이브러리 기본값인 `Medium`으로 적용되며 `None`과 `Minimal`은 지원하지 않습니다. `AIRequestProfile.DisableReasoning = true`는 `Standard` 모드에서 추론 수준을 `Low`로 낮추고 추론 요약을 생략합니다. `Gpt6ReasoningMode.Pro`를 선택하면 같은 모델 ID `gpt-6-astra`로 Pro 모드를 사용합니다.

### 텍스트 음성 변환 (TTS)

```csharp
byte[] audio = await service.GetSpeechAsync(
    inputText: "안녕하세요!",
    voice: "alloy",   // alloy, echo, fable, onyx, nova, shimmer
    model: "tts-1"
);

await File.WriteAllBytesAsync("output.mp3", audio);
```

### 음성 텍스트 변환 (STT)

```csharp
byte[] audioData = await File.ReadAllBytesAsync("recording.mp3");

string transcript = await service.TranscribeAudioAsync(
    audioData: audioData,
    fileName: "recording.mp3",
    language: "ko"  // 선택사항, ISO-639-1
);
```

`TranscribeAudioAsync`는 `gpt-transcribe`를 사용하며 공개 시그니처는 유지됩니다.

### 이미지 생성

#### GPT Image 2.5

이미지 시안을 빠르게 만들 때는 Flare를, 세부 편집 지시를 정밀하게 반영해야 할 때는 Sunburst를 선택하세요. 두 모델 모두 기존 `IImageGenerationService`에서 이미지 생성과 편집을 지원하며, 이미지 모델을 바꿔도 채팅 모델은 바뀌지 않습니다.

| 모델 | 선택할 상황 |
| --- | --- |
| `AIModels.OpenAI.GptImage2_5Flare` | 빠르고 품질 높은 일상적인 이미지 생성. |
| `AIModels.OpenAI.GptImage2_5Sunburst` | 편집 정밀도가 중요한 이미지 생성과 수정. |

`ImageGenerationRequest.Model` 또는 이를 상속한 `ImageEditRequest.Model`에 모델을 명시합니다. OpenAI 기본값은 `AIModels.OpenAI.GptImage2`를 유지합니다. 별칭은 `gpt-image-2.5-flare`, `gpt-image-2.5-sunburst`이며, 2026년 9월 8일 스냅샷을 고정하려면 `GptImage2_5Flare_260908` 또는 `GptImage2_5Sunburst_260908`을 사용하세요. 해당 모델 ID에는 `-2026-09-08`이 붙습니다.

Flare로 시안을 만듭니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

IImageGenerationService images = new OpenAIService(apiKey, httpClient);
var generated = await images.GenerateImagesAsync(new ImageGenerationRequest
{
    Model = AIModels.OpenAI.GptImage2_5Flare,
    Prompt = "해 뜰 무렵의 유리 파빌리온, 건축 콘셉트 이미지",
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.Low,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion.png", generated.Images[0].Data);
```

이어서 Sunburst로 생성된 이미지를 편집합니다.

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Model = AIModels.OpenAI.GptImage2_5Sunburst,
    Prompt = "파빌리온 디자인은 유지하고 주변 풍경을 지운 뒤 배경을 투명하게 해주세요.",
    InputImages = new[]
    {
        new ImageInput(generated.Images[0].Data, "image/png", "pavilion.png")
    },
    Size = ImageSize.Pixels(1024, 1024),
    Quality = ImageQuality.XHigh,
    Background = ImageBackground.Transparent,
    OutputFormat = ImageOutputFormat.Png
});

await File.WriteAllBytesAsync("pavilion-cutout.png", edited.Images[0].Data);
```

두 모델과 해당 스냅샷의 `Quality`는 `Auto`, `Low`, `Medium`, `High`, `XHigh`, `Max`를 지원합니다. 시안은 낮은 품질로 빠르게 만들고, 최종 결과물은 높은 수준을 비교해 선택하세요. `OutputFormat`은 `Auto` / `Png`, `Jpeg`, `WebP`이며, `OutputCompression`은 JPEG/WebP에서만 0–100으로 설정합니다. `Transparent` 배경은 PNG/WebP가 필요하고, `Count`는 1–10입니다.

`Size`는 `ImageSize.Auto` 또는 `ImageSize.Pixels(width, height)`입니다. 두 변은 16의 배수, 비율은 1:3–3:1, 한 변은 최대 3840픽셀, 전체 면적은 655360–8294400픽셀이어야 합니다. 2560×1440을 넘는 크기는 실험적입니다. OpenAI는 `Preset`을 거절합니다.

편집 참조 이미지는 비어 있지 않은 JPEG/PNG/WebP 1–16개이며 각각 50 MiB 미만이어야 합니다. 선택적 마스크는 50 MiB 미만의 PNG/WebP로, 첫 번째 참조 이미지와 형식·픽셀 크기가 같고 알파 채널이 있어야 합니다. 라이브러리는 MIME과 바이트 길이를 검사하고 픽셀 크기·알파 채널은 공급자가 검사합니다.

이 예제는 기존 Image API의 바이트 반환과 multipart 편집 경로를 사용합니다. Responses의 `image_generation` 도구, 부분 이미지 스트리밍, `input_fidelity`는 이번 연동에서 공개하지 않습니다. 결과의 `GeneratedImage.Data`와 `MediaType`을 읽으세요.

공식 [이미지 가이드](https://developers.openai.com/api/docs/guides/image-generation), [Sunburst](https://developers.openai.com/api/docs/models/gpt-image-2.5-sunburst), [Flare](https://developers.openai.com/api/docs/models/gpt-image-2.5-flare) 모델 문서를 참고하세요.

---

## Anthropic (AnthropicService)

[Claude Fable 5.1](fable-5-1.md)의 진행 안내, 턴별 지시, thinking binding 진단은 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0부터 제공합니다. Mythos 5.1은 초대 접근이 필요하며, 두 모델 모두 강제 도구 선택을 거부합니다.

<a id="claude-opus-55"></a>

### Claude Opus 5.5: 긴 도구 작업의 진행 상황 보여주기

여러 번 도구를 호출하며 코드를 검토하거나 문서를 조사할 때 Opus 5.5를 사용할 수 있습니다. 기존 completion·Run API를 그대로 쓰지만 기본값에서는 진행 안내가 숨겨지며, 추론을 보존할 때는 이력 변경도 신경 써야 합니다. 이 지원은 현재 작업 중인 미배포 기능이며, 게시된 8.0.0 / 4.0.0 패키지에는 포함되지 않습니다.

`ClaudeOpus5_5`는 `claude-opus-5-5`를 선택합니다. 텍스트·이미지 입력과 텍스트 출력을 지원하며 컨텍스트는 1M, 최대 출력은 128K토큰입니다. 2026-09-24 확인 기준 일반 입력·출력 요금은 100만 토큰당 $4/$20이며, 특수 모드와 도구 요금은 별도입니다. [공식 모델 정보](https://platform.claude.com/docs/en/models/opus-5-5/overview).

서비스 설정을 바꾸지 않으면 `Auto`는 `Medium` effort를 사용하고 읽을 수 있는 추론은 생략합니다. Adaptive thinking은 항상 켜져 있습니다. `Low`, `Medium`, `High`, `XHigh`, `Max`를 명시적으로 선택할 수 있으며, 공통 `ReasoningLevel.None`과 `Minimal`은 거부합니다. 서비스의 기본 모델은 바꾸지 않습니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;

var claude = new AnthropicService(apiKey, httpClient);
claude.ChangeModel(AIModels.Anthropic.ClaudeOpus5_5);
claude.WithAdaptiveThinkingParameters(
    ClaudeReasoningEffort.Medium, ClaudeThinkingDisplay.Updates);

await using var run = await claude.StartRunAsync(
    "Review the migration plan using the registered tools.",
    options: StreamOptions.FullOptions);

await foreach (var item in run.StreamAsync())
{
    if (item.Type == StreamingContentType.Reasoning)
        Console.WriteLine(item.Content);
    else if (item.Type == StreamingContentType.Text)
        Console.Write(item.Content);
}
string answer = (await run.Result).Text;
```

예제는 `Updates`를 요청하고 `StreamingContentType.Reasoning`을 관찰합니다. 요약된 추론은 `Summarized`, 표시 생략은 `Omitted`를 선택합니다. `WithAdaptiveThinkingParameters(effort)`의 표시 인자 기본값은 여전히 `Summarized`이므로, 설정을 전혀 바꾸지 않은 서비스와 구분하세요. 일반 completion에서는 호출 후 `LastThinkingContent`를 읽습니다. 일정한 간격의 진행 안내를 보장하지 않습니다.

기존 `ThinkingBudget`에 양수를 지정하면 high/xhigh/max effort로 매핑하며 정확한 토큰 예산으로 전송하지 않습니다. 0이나 음수로도 Opus 5.5의 추론을 끌 수 없습니다. 추론을 비활성화하는 프로필은 낮은 effort와 읽을 수 있는 추론 생략으로 처리합니다. `MaxTokens`에는 숨겨진 추론과 답변이 함께 포함되므로 이관할 때 출력 한도와 비용을 다시 측정하세요.

Mythosia는 내용이 비어 있어도 서명된 thinking 블록을 대화 턴과 도구 결과 사이에서 보존합니다. 같은 서비스·대화를 이어 쓰고, 추론 보존을 기대한다면 이전 메시지·시스템 지시·도구를 덮어쓰지 마세요. `WithTurnInstruction`, `WithConversationInstruction`, `CachePreservation.Required`로 기존 대화 제어를 사용할 수 있습니다. `WithThinkingBinding`으로 `Error` 또는 `DropBlock`을 선택하고 `LastInputTransformations`에서 보고된 삭제를 확인합니다. Drop은 추론 폐기입니다. 공통 제어는 [이력 가이드](fable-5-1.md)를 참고하되, Opus 5.5의 기본값과 모델 호환성은 별도로 적용합니다.

`ForceFunctionName`은 지정하지 마세요. 일반 도구 선택과 `FunctionsDisabled`는 지원합니다. Assistant prefill은 거부하며 sampling 매개변수는 전송하지 않습니다. Opus 5.5는 Fable/Mythos의 thinking을 읽지 못하지만, Claude API의 Fable 5.1과 Mythos 5.1은 Opus 5.5의 thinking을 읽을 수 있습니다. 모델 전환 시 이전 추론이 사라질 수 있습니다. 이번 추가에는 네이티브 computer toolset, task budget, 대화 중 도구 변경, 서버 압축, 자동 서버 fallback API가 포함되지 않습니다. [이관 조건](https://platform.claude.com/docs/en/models/opus-5-5/migration-guide) · [네이티브 기능 범위](https://platform.claude.com/docs/en/models/opus-5-5/whats-new-opus-5-5).

Opus 5.5에서 저장된 assistant 응답 본문을 직접 수정하면 HTTP 요청 전에 `InvalidOperationException`이 발생합니다. `DropBlock`도 서명된 응답의 재작성을 허용하지 않습니다. 수정할 내용은 새 사용자 입력으로 전달하거나 새 대화를 시작하세요. 이전 user/system 내용의 수정에는 공급자의 접두부 binding 정책을 적용합니다.

Opus 5.5 fast mode는 필요한 계정 권한이 있는 직접 Claude API에서 [WithSpeed](request-building.md#inference-speed)로 사용할 수 있습니다. 선택한 추론 수준을 유지하며 추가 요금이 있는 모드를 요청합니다.

### 토큰 계산 (네이티브 API)

`GetInputTokenCountAsync`는 모든 프로바이더에서 사용 가능합니다([기본 완성](completions.md#토큰-계산) 참조). Anthropic 구현은 공식 `messages/count_tokens` 엔드포인트를 호출하여 로컬 추정 대신 **정확한** 토큰 수를 반환합니다:

```csharp
uint tokens = await service.GetInputTokenCountAsync("프롬프트 내용");
uint total = await service.GetInputTokenCountAsync();
```

---

## Google (GoogleAIService)

긴 문서를 검토하거나 도구를 여러 차례 호출하는 작업에 Gemini 3.7 Flash 또는 3.8 Flash를 선택할 수 있습니다. `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0부터 기존 Google 어댑터로 제공하며, 서비스 기본 모델은 Gemini 3.6 Flash로 유지합니다.

### 사고 수준

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Google;

var gemini = new GoogleAIService(apiKey, httpClient);
gemini.ChangeModel(AIModels.Google.Gemini3_8Flash);
// Gemini 3.7: AIModels.Google.Gemini3_7Flash
gemini.ThinkingLevel = GeminiThinkingLevel.Low;

string review = await gemini
    .CreateRequest("롤링 배포와 블루 그린 배포를 롤백 위험까지 포함해 비교해 주세요.")
    .WithReasoning(ReasoningLevel.High)
    .GetCompletionAsync();
```

가벼운 초안에는 `Low`, 까다로운 검토에는 `High`를 사용할 수 있습니다. 추론을 늘리면 지연 시간과 토큰 사용량이 증가할 수 있습니다. 두 모델은 `Low`, `Medium`, `High`를 지원하며 `Minimal`과 `None`은 지원하지 않습니다. `GeminiThinkingLevel.Auto`는 재정의를 생략하며 3.8의 공급자 기본값은 `Medium`입니다. `ThinkingLevel`은 서비스 기본 설정이고 `WithReasoning(...)`은 한 논리적 요청만 재정의합니다. 두 모델의 `temperature`, `topP`, `topK`는 전송하지 않습니다. 공급자 한도는 입력 1,048,576토큰, 출력 65,536토큰입니다. [Gemini 3.7 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.7-flash), [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash).

<a id="google-image-options"></a>

### Google 이미지 모델별 해상도와 비율

| 모델 | `Resolutions` | `AspectRatios` |
| --- | --- | --- |
| `gemini-3.1-flash-image` | `Auto`, `FiveTwelve` (512), `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 14 + `Auto` |
| `gemini-3.1-flash-lite-image` | `Auto`, `OneK` (1K) | 14 + `Auto` |
| `gemini-3-pro-image` | `Auto`, `OneK` (1K), `TwoK` (2K), `FourK` (4K) | 10 + `Auto` |

기본 비율 10개는 `1:1`, `2:3`, `3:2`, `3:4`, `4:3`, `4:5`, `5:4`, `9:16`, `16:9`, `21:9`입니다. 14개 비율 집합에는 `1:4`, `4:1`, `1:8`, `8:1` 비율이 추가됩니다. 모든 모델에서 `ImageAspectRatio.Auto`도 허용합니다.

`ImageSize.Auto` 또는 `ImageSize.Preset(resolution, aspectRatio)`를 사용합니다. `Auto`는 해당 선택값 전송을 생략합니다. `GetImageCapabilities(model)`과 `GenerateImagesAsync` / `EditImagesAsync`가 같은 모델별 선택지를 사용합니다. 지원하지 않는 명시적 값은 HTTP 전에 `NotSupportedException`으로 거절하며 크기 변경이나 대체 요청을 하지 않습니다. 알 수 없는 사용자 정의 모델 ID는 `Unknown` 지원 정보를 유지하고 공급자 공통 옵션 검증 후 그대로 전송합니다.

Flash-Lite의 [모델 페이지](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite-image)와 가이드 본문은 1K를 명시하지만 [가이드 표](https://ai.google.dev/gemini-api/docs/generate-content/image-generation#aspect_ratios_and_image_size)에는 512 열도 있습니다. 이 불일치를 검증하기 전까지 라이브러리는 보수적으로 1K만 허용합니다. 512에 대한 서버 거절을 실측했다는 뜻은 아닙니다.

---

## xAI (XAIService)

<a id="grok-47"></a>

### Grok 4.7

빠른 초안을 만든 뒤 코드나 문서를 꼼꼼하게 검토하려면 Grok 4.7을 선택하고 요청마다 추론 수준을 조절하세요. 기존 일반 응답·스트리밍·Run·로컬 도구·구조화 출력·이미지 입력 API를 그대로 사용합니다. `grok-4.7`은 텍스트·이미지를 입력받아 텍스트를 반환하며 문맥 창은 500,000토큰입니다. 이 연결에는 서로 맞는 미배포 core·abstractions 빌드가 필요하며, 이미 게시된 8.0.0 / 4.0.0에는 포함되지 않습니다. 서비스 기본 모델은 Grok 4.5로 유지합니다.

```csharp
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.xAI;

var grok = new XAIService(apiKey, httpClient);
grok.ChangeModel(AIModels.xAI.Grok4_7);
grok.WithGrokReasoning(GrokReasoning.Low);

var request = grok.CreateRequest("Review this deployment plan and its rollback risks.")
    .WithReasoning(ReasoningLevel.XHigh)
    .WithSpeed(InferenceSpeed.Fast);

await using var run = await request.StartRunAsync();
await foreach (var content in run.StreamAsync())
    Console.Write(content.Content);
var result = await run.Result;
Console.WriteLine(result.Text);
foreach (var processing in result.Processing)
    Console.WriteLine(processing.AppliedSpeed);
```

`Low`, `Medium`, `High`, `XHigh`를 지원합니다. 네이티브 `GrokReasoning.Auto`는 `reasoning_effort`를 생략해 공급자 기본값인 `High`를 사용하고, 공통 `ReasoningLevel.Auto`도 해당 요청에서 필드를 생략해 공급자 기본값 `High`를 사용합니다. `None`, `Minimal`, `Max`는 전송 전에 거부합니다. 공통 `WithReasoning(...)`은 도구 라운드와 구조화 출력 수정 호출을 포함한 한 논리적 요청에 적용되며, `WithGrokReasoning(...)`은 서비스 기본 설정을 지정합니다. 내부 `DisableReasoning` 프로필은 `Low`를 사용합니다. 스트림의 추론 요약은 공급자가 선택적으로 제공하는 요약이며 전체 내부 추론이 아닙니다.

`WithSpeed(InferenceSpeed.Standard)`는 `service_tier: "default"`, `Fast`는 지원하는 xAI 엔드포인트에서 `"priority"`를 전송하며 추가 요금이 발생할 수 있습니다. `ProviderDefault`는 값을 재정의하지 않습니다. 서버가 일반 처리로 낮출 수 있으므로 `result.Processing`에서 실제 보고된 등급을 확인하세요. 이는 `grok-4.7`의 우선 처리이며, Cursor·Grok Build 전용인 별도 “Grok 4.7 Fast” 모델과 다릅니다. 전용 변형은 공개 API 모델 ID가 없습니다.

`GetCapabilities()`는 선택한 요청의 로컬 지원 정보를 반환하며 계정 권한까지 확인하지는 않습니다. 이번 연결은 Chat Completions를 사용합니다. Responses 전용 암호화 추론, 호스팅 웹·X 검색, 네이티브 비동기 도구, 캐시 유지 변경과 `SteerAsync`는 이번에 연결하지 않습니다. 클라이언트 함수는 기존 로컬 도구 반복 호출을 사용하며 `run.CanSteer`는 false입니다.

[Grok 4.7](https://docs.x.ai/developers/grok-4-7) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning) · [Priority Processing](https://docs.x.ai/developers/advanced-api-usage/priority-processing)

### 작업에 맞는 추론 수준 선택

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

Grok 4.6은 관찰 옵션에 `new StreamOptions().WithReasoning()`을 지정하면 공급자가 생성한 추론 요약을 `StreamingContentType.Reasoning`으로 반환할 수 있습니다. 요약은 선택적으로 제공되며 전체 내부 추론을 의미하지 않습니다. Run에서도 같은 관찰 옵션을 쓰며, 스트림 표시 설정을 바꿔도 요청한 추론 수준은 바뀌지 않습니다.

[Grok 4.6](https://docs.x.ai/developers/grok-4-6) · [reasoning_effort](https://docs.x.ai/developers/model-capabilities/text/reasoning)

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

편집할 때는 프롬프트에서 언급하는 순서대로 기존 이미지 바이트를 전달합니다:

```csharp
var edited = await images.EditImagesAsync(new ImageEditRequest
{
    Prompt = "1번 이미지의 피사체를 2번 이미지의 장면에 배치해 주세요.",
    InputImages = new[]
    {
        new ImageInput(await File.ReadAllBytesAsync("subject.png"), "image/png", "subject.png"),
        new ImageInput(await File.ReadAllBytesAsync("scene.jpg"), "image/jpeg", "scene.jpg")
    },
    Size = ImageSize.Preset(ImageResolution.OneK),
    OutputFormat = ImageOutputFormat.Auto
});

var imageEdited = edited.Images[0];
var extensionEdited = imageEdited.MediaType switch
{
    "image/jpeg" => ".jpg",
    "image/png" => ".png",
    "image/webp" => ".webp",
    _ => throw new NotSupportedException(imageEdited.MediaType)
};
await File.WriteAllBytesAsync("combined" + extensionEdited, imageEdited.Data);
```

각 `GeneratedImage.Data`에는 디코딩된 이미지 바이트가 들어 있습니다. 파일 확장자는 `MediaType`을 확인해 정하세요. 어댑터는 인라인 base64 응답을 요청하며 제공자 이미지 URL을 다운로드하지 않습니다. `Count`는 결과 1~10장, 편집 입력은 JPEG·PNG·WebP 참조 이미지 1~5장을 지원합니다.

xAI에서는 `ImageSize.Auto` 또는 `ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine)`을 사용합니다. 해상도 등급은 `Auto`, `OneK`, `TwoK`이며, 비율은 해당 모델이 지원해야 합니다. 정확한 픽셀을 지정할 수 없으므로 `Pixels(...)`는 거절합니다.

xAI는 새로운 공통 기본값인 `ImageOutputFormat.Auto`만 지원합니다. 출력 코덱 선택 기능이 없어 명시적인 `Jpeg`, `Png`, `WebP`는 전송 전에 거절합니다. `GeneratedImage.MediaType`에 맞는 확장자로 저장하세요. 라이브러리는 이미지를 변환하지 않습니다. 품질은 `ImageQuality.Auto`, `Low`, `Medium`, 배경은 `ImageBackground.Auto`만 지원하며 명시적 압축과 별도 `Mask`는 지원하지 않습니다.

Google은 `ImageSize.Auto` 또는 모델별 해상도·비율의 `Preset`을 사용합니다. [Google 모델별 이미지 옵션](#google-image-options). 출력 형식은 `ImageOutputFormat.Auto` 또는 명시적 `Jpeg`이며 `Png`·`WebP`는 거절합니다. Google·xAI는 `Pixels`를 거절하고, OpenAI는 `Auto`·`Pixels`를 지원하며 `Preset`을 거절합니다. [전환 예제](#image-options-migration)를 참고하세요.

[Grok Imagine Image 2.0](https://docs.x.ai/developers/models/grok-imagine-image-2.0) · [Image API](https://docs.x.ai/developers/rest-api-reference/inference/images)

---

## DeepSeek (DeepSeekService)

빠른 답변을 받은 뒤 더 깊게 검토하거나 차트·스크린샷을 설명해야 할 때 DeepSeek Flash를 사용할 수 있습니다. `AIModels.DeepSeek.Flash` (`deepseek-flash`)는 2026년 9월 10일 출시된 비전 지원 V4.1 Flash를 선택합니다. 기존 완성 응답·스트리밍·Run·함수 호출·RAG API를 그대로 사용하며 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0부터 지원합니다.

> 배포된 `Mythosia.AI` 8.0.0 / `Mythosia.AI.Abstractions` 4.0.0에는 Flash 기본 지원이 포함되어 있습니다. `AIModels.DeepSeek.V4Pro`, `UseResponsesApi`, Files API, `DeepSeekImageFileContent`는 아직 배포되지 않은 소스 변경이며 서로 맞는 코어·추상화 소스 빌드가 필요합니다. 위 배포 패키지에는 이 추가 기능이 포함되어 있지 않습니다. [미배포 변경 사항](../../src/core/Mythosia.AI/RELEASE_NOTES.md#unreleased).

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

차트나 스크린샷은 기존 메시지 타입에 이미지 바이트를 담아 전달합니다:

```csharp
using Mythosia.AI.Models.Messages;

var message = new Message(ActorRole.User, new List<MessageContent>
{
    new TextContent("이 차트의 추세와 읽기 어려운 레이블을 설명해 주세요."),
    new ImageContent(await File.ReadAllBytesAsync("chart.png"), "image/png")
});
string description = await deepseek.GetCompletionAsync(message);
```

`ImageContent`에는 JPEG·PNG·GIF·WebP 바이트 또는 제공자가 가져갈 공개 HTTP(S) URL을 넣을 수 있습니다. 예제는 사용자 메시지를 사용합니다. 현재 API는 도구 메시지의 이미지도 받지만, 등록 함수 핸들러는 공통 결과 계약을 통해 여전히 텍스트를 반환합니다. `ActorRole.Function` 이미지 메시지를 직접 구성할 때는 대응하는 호출 ID를 `MessageMetadataKeys.FunctionId`에 넣어야 합니다(wire의 `tool_call_id`). 이미지 크기·총합 제한은 최신 공식 비전 가이드를 참고하세요. 이미지 생성은 지원하지 않습니다.

두 모델의 한도는 컨텍스트 1M, 출력 최대 384K (`393216`) 토큰이며 기본 요청 예산은 8,000토큰입니다. 추론 시 temperature·penalty를 생략하고 `top_p`는 최소 0.95로 보내며 비추론 시에는 `top_p`를 생략합니다. Responses의 네이티브 JSON schema는 기존 타입 출력 API로 사용합니다. 백그라운드 실행, 서버 `store`/`previous_response_id`, 호스팅 웹·파일 검색, `CachePreservation.Required`, 네이티브 비동기 도구, `SteerAsync`, 이미지 생성은 지원하지 않습니다. 로컬 RAG와 일반 도구 라운드는 사용할 수 있습니다.

`V4Flash`, `Chat`, `Reasoner`는 기존 wire ID를 유지한 경고 전용 obsolete 상수로 남습니다. 종료된 `deepseek-v4-flash` 별칭은 제공자가 임시로 V4.1 Flash에 연결하며 라이브러리가 상수값을 바꾸는 것은 아닙니다. 새 코드에서는 `Flash`를 명시적으로 선택하세요. `UseReasonerModel()`은 Flash와 추론 `High`를 선택합니다.

[DeepSeek V4.1 Flash](https://api-docs.deepseek.com/updates/) · [Thinking](https://api-docs.deepseek.com/guides/thinking_mode/) · [Vision](https://api-docs.deepseek.com/guides/vision/) · [Tools](https://api-docs.deepseek.com/guides/tool_calls/) · [Limits](https://api-docs.deepseek.com/quick_start/pricing/)

---

## Perplexity (PerplexityService)

최신 정보를 바탕으로 답하고 사용자가 출처를 확인해야 할 때 Perplexity를 사용합니다. `PerplexityService`는 Agent API를 호출하며, 독립 검색과 임베딩은 직접 선택한 답변 모델의 검색 기반을 구성할 때 사용합니다.

```csharp
using Mythosia.AI.Models;
using Mythosia.AI.Services.Perplexity;

var service = new PerplexityService(apiKey, httpClient);
service.ChangeModel(AIModels.Perplexity.Sonar);

await using var run = await service.StartRunAsync("최신 배터리 재활용 방법을 비교하고 출처를 제시해 주세요.");
Console.WriteLine((await run.Result).Text);
foreach (var source in run.Citations)
    Console.WriteLine(source.Url);
```

조사 프리셋 선택, 로컬 함수·호스팅 도구 연결, 오래 걸리는 백그라운드 작업 관리는 [Perplexity 안내](perplexity.md)를 참고하세요. 기존 completion, 스트리밍, Run 및 인용 API를 그대로 사용합니다.

이번 릴리스에서 서비스는 `/v1/agent`로 전환됩니다. `AIModels.Perplexity.Sonar`는 이제 `perplexity/sonar`를 선택합니다. 제공자가 기존 Sonar 엔드포인트를 2026년 9월 27일 종료한다고 공지했으므로 기존 Sonar 연동은 이전이 필요합니다. [Perplexity](https://community.perplexity.ai/t/sonar-is-moving-to-the-agent-api/5802)

---

## Alibaba / Qwen (QwenService)

별도 패키지를 설치합니다:

```bash
dotnet add package Mythosia.AI.Providers.Alibaba
```

```csharp
using Mythosia.AI.Providers.Alibaba;

var service = new QwenService(apiKey, http)
{
    Model = AlibabaModels.QwenMax
};
```

사용 가능한 모델: `QwenMax`, `QwenPlus`, `QwenTurbo`, `Qwen3` 및 변형 모델들.

서비스를 생성할 때 `EndpointPlatform`으로 호환 엔드포인트를 선택합니다:

```csharp
var vllmService = new QwenService(
    "http://localhost:8000",
    EndpointPlatform.Vllm,
    http);
```

[공통 지원 정의로 모델별 기능 선택지 구성하기](model-capabilities.md).
