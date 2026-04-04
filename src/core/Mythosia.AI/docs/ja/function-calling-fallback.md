# Function Calling (FC) フォールバック: FC ON → FC OFF

## 核心問題

FC ON の会話履歴を FC OFF（非関数）APIパスで送信すると、全プロバイダーで `400 Bad Request` エラーが発生する2つの問題があります:

1. **`Role = Function` は FC OFF で無効** — Claude、OpenAI、Gemini すべてが Function Calling 無効時に `"function"` roleを拒否します。`User` と `Assistant` roleのみ許可されます。

2. **`Assistant` の content が空** — FC ON でAIが関数を呼び出す際、assistantメッセージのcontentは空で、実際の呼び出し情報はmetadataにあります。FC OFF では空のassistant contentがバリデーションエラーを引き起こします（特にClaude）。

## 解決

FC OFF 時、送信前に以下のように変換します:

| メッセージ | 問題 | 処理 |
|------------|------|------|
| `Function` role（結果） | `"function"` role拒否 | roleを `User` に変更、関数結果をcontentに記録 |
| `Assistant`（関数呼び出し） | contentが空 | 呼び出した関数情報をcontentに記録 |

`GetLatestMessagesWithFunctionFallback()` で処理し、ChatBlockの元メッセージは変更しません。

### 変換例

```text
[FC ON — ChatBlockに保存された履歴]
  User: "ソウルの天気を教えて"
  Assistant: (空content, metadata: function_call=get_weather)       ← 問題: 空content
  Function: "Seoul: 15°C, Clear"                                    ← 問題: 無効なrole
  Assistant: "ソウルの天気は15°Cで晴れです。"

[FC OFF 送信時の変換結果]
  User: "ソウルの天気を教えて"
  Assistant: "[Called get_weather({"city":"Seoul"})]"                ← 呼び出し情報で埋める
  User: "[Function get_weather returned: Seoul: 15°C, Clear]"      ← roleをUserに変更
  Assistant: "ソウルの天気は15°Cで晴れです。"
```

## 実装

```csharp
// AIService.cs
internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
{
    foreach (var message in GetLatestMessages())
    {
        // 空contentのAssistant（関数呼び出し） → 呼び出し情報をcontentに記録
        if (message.Role == ActorRole.Assistant &&
            message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)
                ?.ToString() == "function_call")
        {
            var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
            var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
            yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
            continue;
        }

        // Function role → User roleに変更、結果をcontentとして維持
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

各サービスの非関数 `BuildRequestBody()` に適用:

- `AnthropicService.Parsing.cs`
- `OpenAIService.Parsing.cs` (`BuildNewApiBody()`, `BuildLegacyApiBody()`)
- `GoogleAIService.Parsing.cs`

## 関連

**MaxTokens自動キャッピング**（`GetEffectiveMaxTokens()`）と連携します — `RELEASE_NOTES.md` v4.0.1 参照。
