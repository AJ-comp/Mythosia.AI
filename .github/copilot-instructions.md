# Copilot Instructions

## Project Guidelines
- ChatUI의 Document Reference에서 엑셀 파일은 MarkdownTextSplitter로 분할된다. RecursiveTextSplitter가 아님.
- Avoid rigid app-layer input validations that may change over time; trust database constraints unless validation is truly invariant to minimize maintenance burden.
- 사용자 `PostgresStore` 설계 개선 시 하위호환보다 더 나은 DX를 우선하며, 필요하면 브레이킹 체인지도 허용한다.
- Thoroughly investigate code before making changes; do not make assumptions and read all related code carefully first.
- 사용자는 Qdrant 인덱스 옵션 타입명에서 불필요하게 긴 이름(예: Payload 포함)을 줄이는 것을 선호한다.
- 사용자는 구현 패키지의 직접 참조를 일반적으로 피하고, 추상화 기반 의존성을 선호한다.
- 사용자는 무거운 구현 패키지 직접 참조를 선호하지 않으며, 가능하면 코어 패키지는 abstractions만 참조하는 구조를 선호한다.
- RAG 결과 모델 확장 시 진단/관측 필드는 평면 필드보다 전용 진단 클래스로 묶는 구조를 선호한다.
- 사용자는 아직 배포되지 않은 버전에 대해 새 버전 섹션을 만들기보다 기존 목표 버전(예: v3.0.0) 릴리즈 노트에 변경사항을 통합하길 원한다.
- RAG query 정책은 `RagQueryOptions` 한 타입으로 통합하고, `RagPipelineOptions`는 그 기본 실행 정책을 `DefaultQuery`로 보유하는 구조를 선호한다. 예를 들어 query 정책 관련 값(`FinalFilter`, `RetrievalDerivation`, `Namespace`)은 `RagPipelineOptions`와 별도 타입에 이중 분산하지 않는다.
- nullable primitive는 정말 의미적으로 불가피한 경우에만 허용한다. `null`과 실값이 실질적으로 같은 의미라면 확정 타입과 기본값으로 단순화한다.
- `MinScore`처럼 `null`이 실제로 “필터 없음”을 의미하는 경우에만 nullable을 유지한다.
- optional callback, optional prompt template, optional rewritten query처럼 “부재”가 실제 의미를 갖는 참조 타입만 nullable로 두는 방향을 선호한다.
- 설계를 단순화할 때 이름을 늘리거나 wrapper/resolved/request 타입을 불필요하게 늘리기보다, 개념 수 자체를 줄이는 방향을 우선한다.
- DX에 실질적으로 도움이 되는 편의 오버로드는 유지한다. 단순화를 이유로 사용성이 분명히 좋아지는 public convenience API를 불필요하게 제거하지 않는다.
- 사용자는 패키지의 의존성 구조를 파악하지 않고 마구잡이식으로 병합하는 제안을 극도로 싫어한다. 패키지 경계와 의존성 구조를 명확히 인식하고 제안해야 한다.
- 메이저 브레이킹 체인지의 기준은 철저하게 라이브러리 사용자 코드가 바껴야 할 때가 기준이다. 사용자 코드 변화 없이 그냥 호환이 되면 그건 메이저 브레이킹 체인지가 절대 아니다. 파라미터 확장(AIService → IAIService)처럼 기존 코드가 그대로 컴파일되는 변경은 브레이킹이 아니다.
- 과거 릴리즈 노트(이미 배포된 버전)는 수정하지 않는다. 해당 시점의 기록으로 그대로 유지한다.

## Versioning Guidelines
- v10.x 버전 넘버링은 .NET 10 (net10.0) 타겟 프로젝트에만 사용한다. .NET Standard 2.1 프로젝트는 1.x.x 등 일반적인 시맨틱 버전을 사용한다.
- 패키지 버전 업데이트 시 시맨틱 버저닝을 반드시 적용한다: 버그 수정 → patch(x.x.+1), 기능 확장 → minor(x.+1.0), 브레이킹 체인지 → major(+1.0.0). 코드 변경 내역에 따라 버전을 반드시 함께 올려야 한다.

## Commit Message Guidelines
- Commit messages must always be written in English.
- 다국어 문서 작성 시 영어를 직역하지 않는다. 내용과 논리는 그대로 유지하면서, 해당 언어 문화권에 친화적인 자연스러운 어투와 문장으로 작성한다.

## File Editing Guidelines
- 문서 파일(.md 등)이나 주석을 편집할 때는 절대로 터미널 명령(PowerShell Set-Content, Get-Content -replace 등)을 사용하지 않는다. 인코딩이 깨질 수 있으므로 반드시 replace_string_in_file 또는 create_file 같은 에디터 도구만 사용한다.