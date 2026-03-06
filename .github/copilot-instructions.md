# Copilot Instructions

## Project Guidelines
- ChatUI의 Document Reference에서 엑셀 파일은 MarkdownTextSplitter로 분할된다. RecursiveTextSplitter가 아님.
- Avoid rigid app-layer input validations that may change over time; trust database constraints unless validation is truly invariant to minimize maintenance burden.
- 사용자 `PostgresStore` 설계 개선 시 하위호환보다 더 나은 DX를 우선하며, 필요하면 브레이킹 체인지도 허용한다.
- Thoroughly investigate code before making changes; do not make assumptions and read all related code carefully first.
- 사용자는 Qdrant 인덱스 옵션 타입명에서 불필요하게 긴 이름(예: Payload 포함)을 줄이는 것을 선호한다.

## Versioning Guidelines
- v10.x 버전 넘버링은 .NET 10 (net10.0) 타겟 프로젝트에만 사용한다. .NET Standard 2.1 프로젝트는 1.x.x 등 일반적인 시맨틱 버전을 사용한다.