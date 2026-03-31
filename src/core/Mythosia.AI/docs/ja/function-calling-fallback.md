# Function Calling (FC) «Õ«©?«ë«Ğ«Ã«¯: FC ON ¡æ FC OFF

## ú·ãıÙığ¹

FC ON ªÎ?ü¥×Û?ªò FC OFF£¨Şª??£©API«Ñ«¹ªÇáêãáª¹ªëªÈ¡¢îï«×«í«Ğ«¤«À?ªÇ `400 Bad Request` «¨«é?ª¬?ßæª¹ªë2ªÄªÎÙığ¹ª¬ª¢ªêªŞª¹:

1. **`Role = Function` ªÏ FC OFF ªÇÙí?** ? Claude¡¢OpenAI¡¢Gemini ª¹ªÙªÆª¬ Function Calling Ùí?ãÁªË `"function"` roleªòËŞÜúª·ªŞª¹¡£`User` ªÈ `Assistant` roleªÎªßúÉÊ¦ªµªìªŞª¹¡£

2. **`Assistant` ªÎ content ª¬Íö** ? FC ON ªÇAIª¬??ªòû¼ªÓõóª¹ğ·¡¢assistant«á«Ã«»?«¸ªÎcontentªÏÍöªÇ¡¢?ğ·ªÎû¼ªÓõóª·ï×ÜÃªÏmetadataªËª¢ªêªŞª¹¡£FC OFF ªÇªÏÍöªÎassistant contentª¬«Ğ«ê«Ç?«·«ç«ó«¨«é?ªòìÚª­ÑÃª³ª·ªŞª¹£¨÷åªËClaude£©¡£

## ú°Ì½

FC OFF ãÁ¡¢áêãáîñªËì¤ù»ªÎªèª¦ªË?üµª·ªŞª¹:

| «á«Ã«»?«¸ | Ùığ¹ | ?×â |
|------------|------|------|
| `Function` role£¨Ì¿Íı£© | `"function"` roleËŞÜú | roleªò `User` ªË?ÌÚ¡¢??Ì¿ÍıªòcontentªËÑÀ? |
| `Assistant`£¨??û¼ªÓõóª·£© | contentª¬Íö | û¼ªÓõóª·ª¿??ï×ÜÃªòcontentªËÑÀ? |

`GetLatestMessagesWithFunctionFallback()` ªÇ?×âª·¡¢ChatBlockªÎêª«á«Ã«»?«¸ªÏ?ÌÚª·ªŞª»ªó¡£

### ?üµÖÇ

```text
[FC ON ? ChatBlockªËÜÁğíªµªìª¿×Û?]
  User: "«½«¦«ëªÎô¸?ªò?ª¨ªÆ"
  Assistant: (Íöcontent, metadata: function_call=get_weather)       ¡ç Ùığ¹: Íöcontent
  Function: "Seoul: 15¡ÆC, Clear"                                    ¡ç Ùığ¹: Ùí?ªÊrole
  Assistant: "«½«¦«ëªÎô¸?ªÏ15¡ÆCªÇôçªìªÇª¹¡£"

[FC OFF áêãáãÁªÎ?üµÌ¿Íı]
  User: "«½«¦«ëªÎô¸?ªò?ª¨ªÆ"
  Assistant: "[Called get_weather({"city":"Seoul"})]"                ¡ç û¼ªÓõóª·ï×ÜÃªÇØØªáªë
  User: "[Function get_weather returned: Seoul: 15¡ÆC, Clear]"      ¡ç roleªòUserªË?ÌÚ
  Assistant: "«½«¦«ëªÎô¸?ªÏ15¡ÆCªÇôçªìªÇª¹¡£"
```

## ??

```csharp
// AIService.cs
internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
{
    foreach (var message in GetLatestMessages())
    {
        // ÍöcontentªÎAssistant£¨??û¼ªÓõóª·£© ¡æ û¼ªÓõóª·ï×ÜÃªòcontentªËÑÀ?
        if (message.Role == ActorRole.Assistant &&
            message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)
                ?.ToString() == "function_call")
        {
            var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
            var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
            yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
            continue;
        }

        // Function role ¡æ User roleªË?ÌÚ¡¢Ì¿ÍıªòcontentªÈª·ªÆë«ò¥
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

ÊÀ«µ?«Ó«¹ªÎŞª?? `BuildRequestBody()` ªËîêéÄ:

- `ClaudeService.Parsing.cs`
- `ChatGptService.Parsing.cs` (`BuildNewApiBody()`, `BuildLegacyApiBody()`)
- `GeminiService.Parsing.cs`

## ?Ö§

**MaxTokensí»ÔÑ«­«ã«Ã«Ô«ó«°**£¨`GetEffectiveMaxTokens()`£©ªÈÖ§ıÍª·ªŞª¹ ? `RELEASE_NOTES.md` v4.0.1 ?ğÎ¡£
