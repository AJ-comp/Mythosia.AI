# Copilot Instructions

## Project Guidelines
- ChatUI의 Document Reference에서 엑셀 파일은 MarkdownTextSplitter로 분할된다. RecursiveTextSplitter가 아님.
- Avoid rigid app-layer input validations that may change over time; trust database constraints unless validation is truly invariant to minimize maintenance burden.
- 사용자 `PostgresVectorStore` 설계 개선 시 하위호환보다 더 나은 DX를 우선하며, 필요하면 브레이킹 체인지도 허용한다.