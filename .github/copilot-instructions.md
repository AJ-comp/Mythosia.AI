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

## Versioning Guidelines
- v10.x 버전 넘버링은 .NET 10 (net10.0) 타겟 프로젝트에만 사용한다. .NET Standard 2.1 프로젝트는 1.x.x 등 일반적인 시맨틱 버전을 사용한다.