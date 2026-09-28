# 실행 중인 모델 서버 관리하기

모델 선택 화면이나 운영 도구는 프롬프트를 보내기 전에 서버 상태, 사용 가능한 모델과 로드 상태를 알아야 합니다. Serving 패키지는 Ollama·llama.cpp·vLLM에서 이 정보를 하나의 인터페이스로 확인하고, 런타임별 관리 작업은 명시적으로 실행할 수 있게 합니다.

모델 선택 목록을 채우거나 서버 연결 상태를 표시할 때, 지원하는 런타임에서 모델을 메모리에 올리고 내릴 때, 엔진 메트릭을 읽을 때 사용할 수 있습니다. 런타임을 바꾸더라도 애플리케이션의 공통 조회 코드를 유지할 수 있습니다.

이 클라이언트는 기존 HTTP 서버에 연결합니다. 엔진 설치·호스팅, GPU 대여, 채팅과 임베딩 생성은 별도 구성 요소의 역할입니다. 채팅에는 vLLM의 `QwenService`처럼 해당 AI 서비스를 사용하고, RAG 임베딩 공급자도 별도로 사용합니다. 조회가 모델을 자동으로 로드하지는 않습니다. SGLang은 구현하지 않았습니다.

## 패키지 선택하기

| 패키지 | 버전 | 사용하는 경우 |
| --- | --- | --- |
| `Mythosia.AI.Serving.Abstractions` | 1.0.0 | 애플리케이션이나 사용자 정의 관리 어댑터에서 공통 인터페이스를 사용할 때. 다른 패키지에 의존하지 않습니다. |
| `Mythosia.AI.Serving.Ollama` | 1.0.0 | Ollama 상태를 조회하고 모델을 다운로드하거나 명시적으로 미리 로드·언로드할 때. |
| `Mythosia.AI.Serving.LlamaCpp` | 1.0.0 | llama.cpp 상태와 메트릭을 조회하고 Router 모드에서 모델을 관리할 때. |
| `Mythosia.AI.Serving.Vllm` | 1.1.0 | 공통 API 또는 기존 vLLM 전용 API로 상태와 메트릭을 조회할 때. |

네 패키지 모두 .NET Standard 2.1을 대상으로 합니다. 사용할 어댑터를 설치하면 공통 인터페이스 패키지도 자동으로 설치됩니다. 어댑터는 공통 인터페이스와 Newtonsoft.Json에만 의존하며, 핵심 AI·RAG 패키지와 독립적입니다.

## 서버 상태를 바꾸지 않고 조회하기

사용하는 런타임의 구체 패키지를 설치하세요. 아래 예제는 Ollama를 사용하며, 다른 서버에는 해당 네임스페이스의 `VllmServer` 또는 `LlamaCppServer`를 선택합니다. 조회는 읽기 전용 요청만 사용하고 로드·생성·다운로드 명령을 보내지 않습니다.

```bash
dotnet add package Mythosia.AI.Serving.Ollama --version 1.0.0
```

```csharp
using System;
using System.Net.Http;
using System.Threading;
using Mythosia.AI.Serving;
using Mythosia.AI.Serving.Ollama;

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
using var cancellation = new CancellationTokenSource();
IModelServer server = new OllamaServer("http://localhost:11434", http,
    apiKey: Environment.GetEnvironmentVariable("MODEL_SERVER_API_KEY"));

var health = await server.GetHealthAsync(cancellation.Token);
var info = await server.GetInfoAsync(cancellation.Token);
var capabilities = await server.GetCapabilitiesAsync(cancellation.Token);
var models = await server.GetModelsAsync(cancellation.Token);

foreach (var model in models)
    Console.WriteLine($"{model.Id}: {model.InstallationState} / {model.LoadState}");
```

엔드포인트에는 서버 루트 주소를 전달하며, 리버스 프록시의 경로 접두사도 사용할 수 있습니다. API 키는 선택 사항이고 요청마다 Bearer 인증으로 전달됩니다. 클라이언트는 `HttpClient.DefaultRequestHeaders`를 변경하거나 전달받은 `HttpClient`를 해제하지 않으므로, 애플리케이션의 수명에 맞게 재사용하고 해제하세요. 이 Ollama 예제에서는 타임아웃이 스트리밍 응답 본문을 읽는 동안에도 적용되므로 모델 다운로드에 충분한 시간을 설정하세요.

## 공통 인터페이스와 선택 기능

| 인터페이스 | 역할 |
| --- | --- |
| `IModelServer` | 서버 정보, 상태, 모델과 확인된 기능 지원 여부. |
| `IModelLifecycle` | 명시적인 모델 로드·언로드 명령. 선택 기능입니다. |
| `IModelDownloader` | 진행 상황을 제공하는 명시적 다운로드. 선택 기능입니다. |
| `IModelMetricsProvider` | 라벨을 보존하는 메트릭 샘플. 선택 기능입니다. |

인터페이스 구현은 클라이언트에 해당 작업이 있음을 뜻하고, `ServingCapabilities`는 연결된 엔드포인트에서 확인할 수 있는 지원 상태를 나타냅니다. `Supported`는 모든 모델의 권한이나 성공을 보장하지 않습니다. `Unsupported`는 관찰한 모드나 엔드포인트에서 사용할 수 없다는 뜻입니다. `Unknown`은 인증·연결 실패 등을 포함해 근거가 부족하다는 뜻이며 미지원으로 취급하면 안 됩니다.

`InstallationState`와 `LoadState`는 서로 다른 상태를 관찰합니다. `Unknown`은 모델이 없거나 언로드되었다는 뜻이 아닙니다. 보고되지 않은 `SizeBytes`·`MemoryBytes`·`ContextLength`는 0이 아닌 `null`입니다. 관리 엔드포인트가 정상이어도 특정 모델의 추론 준비가 끝났다고 단정할 수 없습니다.

## 런타임별 차이

| 작업 | Ollama | llama.cpp 단일 모델 | llama.cpp Router | vLLM |
| --- | --- | --- | --- | --- |
| 서버 정보·상태·모델 목록 | 지원 | 지원 | 지원 | 지원 |
| 명시적 로드·언로드 | 빈 generate 요청으로 지원 | 미지원 | Router 모드 확인 후 지원 | 이 클라이언트에서 미지원 |
| 모델 다운로드 | 스트리밍 진행 상황 제공 | 미지원 | 명시적 실행 가능. 다운로드 엔드포인트와 SSE 이벤트 필요 | 이 클라이언트에서 미지원 |
| 메트릭 | 미구현 | 활성화된 서버 메트릭 | 구체 클래스의 모델별 오버로드. 이미 로드된 모델 필요 | 엔드포인트가 제공하는 서버 메트릭 |

이 표는 클라이언트의 작업 범위를 설명하며, 모든 서버 버전·권한·모델에서 성공한다는 의미는 아닙니다. 연결한 엔드포인트의 지원 정보를 확인하고 실행 실패도 처리하세요.

**Ollama:** `/api/tags`는 등록 모델, `/api/ps`는 현재 실행 중인 모델을 제공합니다. 원격 모델은 로컬 가중치 없이 등록될 수 있으며 로컬 실행 항목이 없으면 로드 상태는 알 수 없음으로 유지합니다. 미리 로드는 빈 `/api/generate` 요청과 서버의 기본 keep-alive를 사용합니다. 임베딩 전용 모델을 다른 엔드포인트로 자동 전환하지 않습니다. 언로드는 `keep_alive: 0`을 사용하며 파일을 삭제하지 않습니다. 메트릭은 구현하지 않았습니다.

**llama.cpp:** 수명 주기나 다운로드 명령 전에 `/props`에서 라우터 모드가 명시적으로 확인되어야 합니다. 단일 모델 모드는 해당 명령을 지원하지 않으며 관찰한 절전 상태를 보존합니다. 라우터 다운로드는 `/models/sse`를 구독한 뒤 `POST /models`를 보내고 해당 모델의 `download_finished` 이벤트에서만 성공합니다. SSE가 열리는 것만으로는 다운로드 지원을 확정하지 못합니다. 서버 단위 메트릭은 단일 모델 모드에 적용됩니다. 라우터에서는 구체 클래스의 `GetMetricsAsync(modelId, token)` 오버로드가 필요하며 `autoload=false`를 보내 조회 중 모델 로드를 방지합니다.

**vLLM:** 제공 별칭과 선택적 `root` 필드를 유지하지만 공통 설치·로드 상태는 알 수 없음으로 둡니다. 모델 목록과 메트릭은 실제 응답으로 확인하며 수명 주기와 다운로드는 지원하지 않습니다. 기존 `VllmServer` 메서드와 DTO는 구체 클라이언트에서 계속 사용할 수 있고, 공통 상태·모델·메트릭 메서드는 명시적 인터페이스로 제공합니다.

## 관리 작업을 명시적으로 실행하기

다운로드와 모델 상주 상태 변경은 네트워크·디스크·장치 메모리를 사용합니다. 애플리케이션에서 해당 작업이 필요할 때 호출하세요. 아래 코드는 앞의 Ollama 예제에 이어서 작은 모델을 다운로드하고 잠시 로드해 상태를 확인합니다. Ollama 태그나 llama.cpp 양자화 태그를 포함한 정확한 서버 모델 ID를 사용하세요.

```csharp
if (server is IModelDownloader downloader &&
    capabilities.ModelDownloading == ServingFeatureSupport.Supported)
{
    var progress = new Progress<ModelDownloadProgress>(p =>
        Console.WriteLine($"{p.ModelId}: {p.Stage} {p.CompletedBytes}/{p.TotalBytes}"));
    await downloader.DownloadModelAsync("qwen2.5:0.5b", progress, cancellation.Token);
}

if (server is IModelLifecycle lifecycle &&
    capabilities.ModelLoading == ServingFeatureSupport.Supported &&
    capabilities.ModelUnloading == ServingFeatureSupport.Supported)
{
    try
    {
        await lifecycle.LoadModelAsync("qwen2.5:0.5b", cancellation.Token);
        var afterLoad = await server.GetModelsAsync(cancellation.Token);
        foreach (var model in afterLoad)
            Console.WriteLine($"{model.Id}: {model.LoadState}");
    }
    finally
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await lifecycle.UnloadModelAsync("qwen2.5:0.5b", cleanup.Token);
    }
}
```

예제는 전용 테스트 모델을 사용하고 작업 후 언로드합니다. 운영 환경에서는 애플리케이션이 해제 시점을 결정해야 하며, 다른 요청이 사용하는 모델을 언로드하면 안 됩니다. 정리 작업에도 별도 제한 시간을 두었으며 서버에 연결할 수 없으면 정리도 실패할 수 있습니다.

진행 상황은 개별 파일이나 단계를 나타냅니다. 바이트 수가 `null`이면 0이나 전체 모델의 진행률로 해석하지 마세요. 로드 성공은 명령 승인일 뿐, 준비 완료나 영구 상주를 보장하지 않습니다. 준비 상태가 중요하면 제한 시간을 두고 `LoadState`를 확인하세요. llama.cpp Router의 다운로드 프로토콜과 버전 제약은 구체 패키지 안내를 따르세요. 서버 설정을 확인했다면 지원 상태가 `Unknown`이어도 명시적으로 요청한 작업은 시도할 수 있습니다. 기능 조회가 그 작업을 자동으로 실행하지는 않습니다.

## 취소와 오류 처리

조회와 명령에 취소 토큰을 전달하세요. 취소는 이 클라이언트의 HTTP 작업과 대기를 중단하며 원격 작업 취소·롤백·다운로드한 레이어 삭제를 보장하지 않습니다. 작업 시간에 맞게 전달할 `HttpClient`를 설정하세요. 클라이언트가 그 인스턴스의 소유권을 가져가지는 않습니다.

모델이나 엔진을 비교할 때 메트릭 라벨을 유지하세요. 누락된 메트릭은 0이 아니며 값에 `NaN`이나 무한대가 포함될 수 있습니다. 공통 오류 형식은 `ServingException`이고 공통 관리 오류에는 원본 응답 본문이나 자격 증명을 넣지 않습니다. 기존 vLLM 전용 호출은 기존 오류 상세 정보를 유지합니다.

`GetHealthAsync`는 엔드포인트 오류를 상태 값으로 분류하지만, 호출자가 요청한 취소는 그대로 전달합니다. 다른 작업은 `ServingException`을 던질 수 있고, llama.cpp의 명확한 미지원 모드에서는 `NotSupportedException`이 발생할 수 있습니다. 타임아웃이나 요청 실패만으로 원격 작업이 롤백됐다고 판단할 수 없습니다. 다운로드 메서드는 런타임의 완료 신호를 확인해야 성공합니다. Ollama에서는 최종 성공과 스트림 종료(EOF)를, llama.cpp Router에서는 해당 모델의 `download_finished` 이벤트를 확인합니다.

## 실제로 검증한 범위

오프라인 테스트는 제어된 성공·비정상 응답, 오류와 취소를 다룹니다. 별도의 실제 서버 검증에서는 NVIDIA A40 한 대와 작은 공개 Qwen 모델을 사용해 다음 엔진 구성을 확인했습니다.

| 런타임 | 테스트 모델 | 검증한 관리 작업 |
| --- | --- | --- |
| Ollama 0.34.4 | `qwen2.5:0.5b` | 조회, 캐시 없는 다운로드, 로드·언로드, 모델 없음 오류의 정보 정제, 사전 취소와 다운로드 진행 중 취소. |
| llama.cpp b11146, Router | `Qwen/Qwen2.5-0.5B-Instruct-GGUF:Q4_K_M` | 조회, 다운로드 이벤트, 로드·언로드, 자동 로드 없는 모델별 메트릭, 오류와 다운로드 취소. |
| llama.cpp b11146, 단일 모델 | 같은 GGUF 모델 | 조회, 서버 메트릭, 취소 및 Router 전용 수명 주기 명령의 명확한 거부. |
| vLLM 0.30.0 | `Qwen/Qwen2.5-0.5B-Instruct` | 조회, 서버 메트릭과 사전 취소. |

네 구성 모두 엔진의 기본 HTTP API로 짧은 추론 요청을 보내 생성된 텍스트도 확인했습니다. 이는 엔진 동작 확인이며 AI 서비스의 채팅 어댑터, 모델 품질, 처리량이나 모든 엔진 버전의 호환성 검증은 아닙니다. 위 버전은 실제 테스트한 구성이고 최소 지원 버전은 아닙니다. 다운로드 취소는 별도의 더 큰 테스트 모델로 확인했으며 원격 롤백은 보장하지 않습니다. 최초 Ollama 다운로드는 한 번 실패했고, 재시도와 모델 삭제 후 새 다운로드는 통과했지만 최초 실패의 정확한 원인은 확정하지 못했습니다.

사용 중인 엔드포인트는 [명시적 참여 방식의 실제 서버 검증 안내](https://github.com/AJ-comp/Mythosia.AI/blob/main/tests/Mythosia.AI.Serving.Live/README.md)에 따라 확인하세요. 저장소의 관리 검증 도구와 검증 과정에서 추가로 사용한 추론·취소 점검의 범위를 구분해 설명합니다. 상세 실행 보고서는 공개 문서에 포함하지 않습니다.

## 패키지별 안내

- [Mythosia.AI.Serving.Abstractions](../../src/serving/Mythosia.AI.Serving.Abstractions/README.md) — 공통 관리 인터페이스와 변경할 수 없는 서버·모델·기능 상태 정보.
- [Mythosia.AI.Serving.Ollama](../../src/serving/Mythosia.AI.Serving.Ollama/README.md) — Ollama 모델 목록·상태, 명시적 미리 로드·언로드와 스트리밍 다운로드.
- [Mythosia.AI.Serving.LlamaCpp](../../src/serving/Mythosia.AI.Serving.LlamaCpp/README.md) — llama.cpp 조회, 라우터 확인 후 수명 주기·다운로드 및 자동 로드 없는 메트릭.
- [Mythosia.AI.Serving.Vllm](../../src/serving/Mythosia.AI.Serving.Vllm/README.md) — vLLM 모델 카드·상태·버전·라벨 보존 메트릭. 기존 구체 API 유지.
