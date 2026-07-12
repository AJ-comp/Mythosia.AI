# Mythosia.AI — Claude 작업 규칙

이 파일은 Claude가 이 프로젝트에서 작업할 때 반드시 준수해야 할 규칙입니다.
작업 시작 전 항상 이 파일을 읽고 각 항목을 체크하세요.

---

## 버전 관리

### 버전 올리기 전 반드시 확인
- 현재 버전이 **이미 NuGet에 배포됐는지** 사용자에게 먼저 확인한다.
- 미배포 버전이라면 새 버전을 만들지 않고 **현재 버전에 내용을 병합**한다.
- 패치/마이너/메이저 구분:
  - 내부 리팩토링, 문서, 재컴파일 → **패치**
  - 새 공개 API 추가 (하위 호환) → **마이너**
  - 기존 API 제거/변경 (하위 비호환) → **메이저**

### csproj 버전 일관성 체크 (버전 변경 시 반드시 확인)
버전을 변경할 때마다 아래 항목이 **모두 같은 버전**을 가리키는지 확인한다:

| 항목 | 위치 |
|---|---|
| `<Version>` | csproj |
| `<PackageReleaseNotes>` | csproj |
| `<Description>` 내 "What's New in vX.Y.Z" | csproj |
| RELEASE_NOTES.md 최상단 버전 헤더 | 각 패키지 |

하나라도 불일치하면 작업 전에 수정한다.

### 의존 패키지 연쇄 확인
핵심 패키지 버전이 올라가면 아래 패키지들도 재컴파일이 필요한지 확인한다:

| 변경된 패키지 | 확인 대상 |
|---|---|
| `Mythosia.AI` | `Mythosia.AI.Providers.Alibaba` |
| `Mythosia.AI.Abstractions` | `Mythosia.AI`, `Mythosia.AI.Rag` |

---

## Breaking Change 규칙

- **외부 호출부가 깨지는 변경은 사용자의 명시적 승인 없이 진행하지 않는다.**
- Optional parameter로 오버로드를 합칠 경우, 기존 위치 인자 호출이 깨지는지 반드시 확인한다.
- Breaking change는 원칙적으로 **메이저 버전 업 시점**까지 defer한다.
- 클래스/메서드 리네임 시 반드시 `[Obsolete]` shim을 추가하여 기존 코드가 경고만 받고 동작하도록 한다.

---

## 파일 수정 금지 목록

아래 디렉토리/파일은 **자동 생성 파일**이므로 절대 수동 편집하지 않는다:

| 경로 | 이유 |
|---|---|
| `.vs/` | Visual Studio 캐시 |
| `_site/` | DocFX 빌드 결과물 |
| `api/*.yml` | DocFX 자동 생성 API 문서 |

---

## RELEASE_NOTES.md 작성 규칙

- **과거 버전 항목은 수정하지 않는다.** 역사적 기록이므로 당시 사실 그대로 보존한다.
- 새 항목은 항상 **파일 최상단**에 추가한다.
- 형식: `## vX.Y.Z` 헤더 → `### Added / Changed / Removed / Internal / Compatibility` 섹션

---

## 문서 업데이트 규칙

클래스명/API가 변경되면 아래를 **모두** 업데이트한다:

- `README.md` (루트 및 각 패키지)
- `docs/**/*.md` (en/ko/ja/zh 전체)
- `src/**/docs/**/*.md`
- `src/**/RELEASE_NOTES.md` (해당 패키지)

자동 생성 파일(`_site/`, `api/`)은 수동 편집하지 않는다.

---

## 문제 원인 분석 규칙

버그나 설계 문제를 논의할 때 **"어느 컴포넌트의 책임인가"를 먼저 명확히 확정한 뒤 해결책을 제시**한다.

- 원인 컴포넌트를 확정하기 전에 코드 수정이나 외부 이슈 제기를 제안하지 않는다.
- 레이어별 책임을 혼동하지 않는다:
  - **파서**: 원본 포맷의 구조를 있는 그대로 기록 (해석 X)
  - **Serializer**: 기록된 데이터를 보고 표현 방식을 판단 (해석 O)
  - **Splitter**: Serializer 출력을 청킹
- 사용자가 "이게 A의 문제 아니냐"고 지적하면, 동의하거나 반박하기 전에 실제 코드를 읽고 확인한다.
- 외부 라이브러리/저장소에 이슈를 제기하도록 유도하기 전에, 문제가 반드시 해당 외부 컴포넌트에 있음을 코드로 확인한다.

---

## 패키지 구조

```
Mythosia.AI.Abstractions      # 핵심 인터페이스/모델 (zero dependency)
    ↑
Mythosia.AI                   # 핵심 구현체 (provider 서비스 클래스)
    ↑                    ↑
Mythosia.AI.Rag        Mythosia.AI.Providers.Alibaba

Mythosia.AI.Serving.Vllm      # vLLM 서버 control-plane 클라이언트 (Newtonsoft.Json만 의존, core 미참조)
```

- `Mythosia.AI.Rag`는 `Mythosia.AI.Abstractions`에만 의존 (`Mythosia.AI` 직접 참조 없음)
- `Mythosia.AI.Providers.Alibaba`는 `Mythosia.AI`에 직접 의존
- **Serving 패밀리 taxonomy**: `Providers.*` = 챗 **data plane** (구체 AI 서비스), `Serving.*` = 모델서버 **control plane** (관리/introspection 클라이언트, 챗 없음). vLLM 챗은 계속 `QwenService(EndpointPlatform.Vllm)`.
- `Mythosia.AI.Serving.Abstractions`는 **지금 만들지 않는다** — `Serving.Ollama` 구현이 실제로 생길 때 두 concrete에서 추출한다 (FDG: 구현 여러 개로 검증되기 전 추상화 금지). `VllmServer`의 메서드명은 런타임 중립으로, DTO는 `Vllm` 접두로 유지해 추출이 additive(minor)가 되게 한다.
