# Function Calling (FC) Fallback: FC ON → FC OFF

## 핵심 문제

FC ON 대화 히스토리를 FC OFF (비함수) API 경로로 전송하면 두 가지 문제로 모든 프로바이더에서 `400 Bad Request` 에러가 발생합니다:

1. **`Role = Function`은 FC OFF에서 유효하지 않음** ? Claude, OpenAI, Gemini 모두 Function Calling이 비활성화된 상태에서 `"function"` role을 거부합니다. `User`와 `Assistant` role만 허용됩니다.

2. **`Assistant`의 content가 비어 있음** ? FC ON에서 AI가 함수를 호출할 때, assistant 메시지의 content는 비어 있고 실제 호출 정보는 metadata에 있습니다. FC OFF에서는 빈 assistant content가 검증 에러를 발생시킵니다 (특히 Claude).

## 해결

FC OFF일 때, 전송 전에 다음과 같이 변환합니다:

| 메시지 | 문제 | 처리 |
|--------|------|------|
| `Function` role (결과) | `"function"` role 거부됨 | role을 `User`로 변경, 함수 결과를 content에 기록 |
| `Assistant` (함수 호출) | content가 비어 있음 | 호출한 함수 내역을 content에 기록 |

`GetLatestMessagesWithFunctionFallback()`에서 처리하며, ChatBlock의 원본 메시지는 수정하지 않습니다.

### 변환 예시

```text
[FC ON ? ChatBlock에 저장된 히스토리]
  User: "서울 날씨 알려줘"
  Assistant: (빈 content, metadata: function_call=get_weather)      ← 문제: 빈 content
  Function: "Seoul: 15°C, Clear"                                    ← 문제: 유효하지 않은 role
  Assistant: "서울의 날씨는 15°C이며 맑습니다."

[FC OFF 전송 시 변환 결과]
  User: "서울 날씨 알려줘"
  Assistant: "[Called get_weather({"city":"Seoul"})]"                ← 호출 내역으로 채움
  User: "[Function get_weather returned: Seoul: 15°C, Clear]"      ← role을 User로 변경
  Assistant: "서울의 날씨는 15°C이며 맑습니다."
```

## 구현

```csharp
// AIService.cs
internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
{
    foreach (var message in GetLatestMessages())
    {
        // 빈 content의 Assistant (함수 호출) → 호출 내역을 content에 기록
        if (message.Role == ActorRole.Assistant &&
            message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)
                ?.ToString() == "function_call")
        {
            var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
            var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
            yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
            continue;
        }

        // Function role → User role로 변경, 결과를 content로 유지
        if (message.Role == ActorRole.Function)
        {
            var funcName = message.Metadata?.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "function";
            yield return new Message(ActorRole.User, $"[Function {funcName} returned: {message.Content}]");
            continue;
        }

        yield return message;
    }
}
```

각 서비스의 비함수 `BuildRequestBody()`에 적용:

- `ClaudeService.Parsing.cs`
- `ChatGptService.Parsing.cs` (`BuildNewApiBody()`, `BuildLegacyApiBody()`)
- `GeminiService.Parsing.cs`

## 관련

**MaxTokens 자동 캡핑**(`GetEffectiveMaxTokens()`)과 함께 동작합니다 ? `RELEASE_NOTES.md` v4.0.1 참조.
