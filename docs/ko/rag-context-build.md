# 컨텍스트 구성

> 📍 **질문 응답 파이프라인:** [쿼리 재작성](rag-query-rewriting.md) → [임베딩](rag-embedding.md) → [필터링](rag-filtering.md) → [검색](rag-hybrid-search.md) → [재순위](rag-reranking.md) → **`컨텍스트 구성`**

## 컨텍스트 구성이란?

컨텍스트 구성은 RAG 파이프라인의 **마지막 단계**입니다. 가장 관련성 높은 청크를 검색하고 순위를 매긴 뒤, 이 단계에서 **LLM이 이해하고 활용할 수 있는 프롬프트로 조립**합니다.

회의 전에 상사에게 브리핑 자료를 준비하는 상황을 떠올려보세요. 관련 정보를 모두 모으고(검색), 중요도순으로 정렬했습니다(재순위). 마지막으로 읽는 사람이 무엇을 해야 하는지 알 수 있도록 **명확하게 정리**해야 합니다.

이 단계의 품질이 LLM 응답 품질에 직접적인 영향을 미칩니다. 잘 구성된 프롬프트는 할루시네이션을 줄이고, 모델이 제공된 컨텍스트에 기반해 답변하도록 유도합니다.

## 기본 컨텍스트 빌더

별도 설정이 없으면 파이프라인은 `DefaultContextBuilder`를 사용하며, 다음과 같은 형식을 생성합니다:

```
Answer the question based on the following context:

[1] (Source: manual.txt)
30일 이내 반품 시 전액 환불됩니다...

[2] (Source: policy.txt)
디지털 제품은 환불 불가입니다...

Question: 환불 정책이 뭔가요?
```

기본 빌더에는 커스터마이징 가능한 속성이 있습니다:

```csharp
var contextBuilder = new DefaultContextBuilder
{
    Header = "다음 컨텍스트를 기반으로 질문에 답변하세요:",
    QueryPrefix = "질문:",
    IncludeScores = false,    // 유사도 점수 표시 여부
    IncludeSource = true      // 출처 메타데이터 표시 여부
};

.WithRag(rag => rag
    .WithContextBuilder(contextBuilder)
    .AddDocument("docs.txt")
)
```

### 스코어 표시

`IncludeScores = true`를 설정하면 각 청크에 유사도 점수가 표시됩니다:

```
[1] (Source: manual.txt) [Score: 0.892]
30일 이내 반품 시 전액 환불됩니다...
```

디버깅이나 특정 청크가 선택된 이유를 파악할 때 유용합니다.

## 프롬프트 템플릿

최종 프롬프트를 더 세밀하게 제어하려면 `{context}`와 `{question}` 자리표시자가 있는 **프롬프트 템플릿**을 설정합니다:

```csharp
.WithRag(rag => rag
    .WithPromptTemplate("""
        당신은 고객 지원 어시스턴트입니다.
        아래 문서의 내용만을 사용해서 질문에 답변하세요.
        문서에 답이 없으면 "해당 정보를 찾을 수 없습니다"라고 답변하세요.

        문서:
        {context}

        고객 질문: {question}
        """)
    .AddDocument("support-kb.txt")
)
```

파이프라인이 `{context}`를 번호가 매겨진 청크 목록으로, `{question}`을 사용자의 질문으로 치환합니다. 내부적으로는 `TemplateContextBuilder`가 생성되며, 청크는 다음과 같이 포맷됩니다:

```
[1] 첫 번째 청크 내용...

[2] 두 번째 청크 내용...
```

### 템플릿이 특히 효과적인 상황

- **동작 제한** — "컨텍스트에 없는 정보는 '모르겠습니다'라고 답하세요"
- **톤 설정** — "정중하고 간결한 어투로 답변하세요"
- **역할 부여** — "당신은 의료 어시스턴트입니다" 또는 "당신은 법률 자문입니다"
- **언어 지정** — "항상 한국어로 답변하세요"

### 템플릿 설계 요령

| 요령 | 예시 |
| --- | --- |
| 모델을 컨텍스트 안에 묶기 | "제공된 문서의 내용만을 근거로 답변하세요" |
| 정보 부족 시 대응 | "답이 없으면 '해당 정보를 찾을 수 없습니다'라고 답하세요" |
| 출력 형식 지정 | "글머리 기호로 답변하세요" |
| 언어 제약 | "질문과 같은 언어로 답변하세요" |

## 커스텀 컨텍스트 빌더

완전한 제어가 필요하면 `IContextBuilder`를 구현합니다:

```csharp
public class MyContextBuilder : IContextBuilder
{
    public string BuildContext(string query, IReadOnlyList<VectorSearchResult> searchResults)
    {
        var sb = new StringBuilder();

        sb.AppendLine("### 관련 정보 ###");
        sb.AppendLine();

        foreach (var result in searchResults)
        {
            var source = result.Record.Metadata.TryGetValue("source", out var s) ? s : "알 수 없음";
            sb.AppendLine($"📄 출처: {source} (관련도: {result.Score:P0})");
            sb.AppendLine(result.Record.Content);
            sb.AppendLine("---");
        }

        sb.AppendLine();
        sb.AppendLine($"위 정보를 바탕으로 답변하세요: {query}");

        return sb.ToString();
    }
}
```

빌더에 등록합니다:

```csharp
.WithRag(rag => rag
    .WithContextBuilder(new MyContextBuilder())
    .AddDocument("docs.txt")
)
```

## 내부 동작

컨텍스트 구성 단계는 다음을 받습니다:

1. 원래 쿼리 문자열
2. 최종 `VectorSearchResult` 목록 (필터링, 검색, 선택적 재순위 이후)

이것들로 하나의 프롬프트 문자열을 만들어 LLM에 전달합니다:

```
검색 결과 + 쿼리 → ContextBuilder.BuildContext() → 프롬프트 문자열 → LLM
```

어떤 컨텍스트 빌더가 사용되는지의 우선순위:

1. **커스텀 `IContextBuilder`** — `.WithContextBuilder()`로 설정한 경우
2. **`TemplateContextBuilder`** — `.WithPromptTemplate()`으로 템플릿을 설정한 경우
3. **`DefaultContextBuilder`** — 기본 폴백

## 다음 단계

- [파이프라인 커스터마이징](rag-pipeline.md) — RAG 전체 동작을 세밀하게 조정
- [재순위](rag-reranking.md) — 컨텍스트 구성 전 청크 품질 향상
- [RAG 기초](rag.md) — RAG 전체 흐름 복습
