# 소개

Mythosia.AI는 다양한 AI 프로바이더, RAG 파이프라인, 문서 로더, 벡터 데이터베이스를 단일 인터페이스로 통합한 모듈식 .NET AI 라이브러리입니다.

## 왜 Mythosia.AI인가?

대부분의 AI 프로바이더 SDK는 서로 다른 API를 노출하기 때문에 프로바이더를 교체하거나 기능을 조합하기가 어렵습니다. Mythosia.AI는 이들을 하나의 `IAIService` 인터페이스로 감싸므로, 어떤 모델이나 프로바이더를 사용하든 애플리케이션 코드는 동일하게 유지됩니다.

## 패키지 구조

필요한 것만 설치하세요:

| 단계 | 패키지 | 용도 |
|:----:|---------|------|
| **1** | `Mythosia.AI` | 시작점 — 완성, 스트리밍, 함수 호출, 구조화된 출력 |
| **2** | `Mythosia.AI.Rag` | RAG가 필요할 때 — 분할기, 임베딩, 하이브리드 검색, 재순위 |
| **3** | `Mythosia.VectorDb.*` | 프로덕션 벡터 스토어가 필요할 때 — Postgres, Qdrant, Pinecone |

## 지원 프로바이더

모든 프로바이더는 핵심 `Mythosia.AI` 패키지에 포함됩니다 (Alibaba 제외):

| 프로바이더 | 모델 |
|------------|------|
| **OpenAI** | GPT-5.x, GPT-4.1, GPT-4o, o3 시리즈 |
| **Anthropic** | Claude Opus / Sonnet / Haiku 4.x |
| **Google** | Gemini 2.5 / 3 시리즈 |
| **xAI** | Grok 3, Grok 4 시리즈 |
| **DeepSeek** | Chat, Reasoner |
| **Perplexity** | Sonar, Sonar Pro, Sonar Reasoning |
| **Alibaba / Qwen** | Qwen Max / Plus / Turbo / Qwen3 (`Mythosia.AI.Providers.Alibaba`) |

## 아키텍처 개요

```
Mythosia.AI.Rag                 ← RAG 파이프라인, 오케스트레이션
    └── Mythosia.AI             ← 핵심 AI 서비스 (모든 프로바이더)
        └── Mythosia.AI.Abstractions   ← IAIService 인터페이스

Mythosia.VectorDb.*             ← 벡터 스토어 (하나 이상 선택)
    └── Mythosia.VectorDb.Abstractions

Mythosia.Documents.*            ← 문서 로더 (Word, Excel, PDF, ...)
    └── Mythosia.Documents.Abstractions
```
