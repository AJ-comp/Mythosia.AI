# Function Calling (FC) üŞ÷Ü: FC ON ¡æ FC OFF

## ú·ãı??

?FC ONîÜ???ŞÈ÷×?FC OFF£¨ŞªùŞ?£©APIÖØ??áê?£¬á¶êóğ«ÍêßÂÔ´?ì×?????ßæ `400 Bad Request` ??:

1. **`Role = Function` î¤FC OFFñéÙéüù** ? Claude¡¢OpenAI¡¢Geminiî¤Function CallingÚ±?éÄ?Ô´ËŞ? `"function"` role¡£?ïÈáô `User` ûú `Assistant` role¡£

2. **`Assistant` îÜcontent?Íö** ? FC ONñéAI?éÄùŞ??£¬assistantá¼ãÓîÜcontent?Íö£¬???éÄãáãÓî¤metadatañé¡£FC OFFñé£¬ÍöîÜassistant content???????£¨éÖĞìãÀClaude£©¡£

## ú°?Û°äĞ

FC OFF?£¬î¤?áêîñ?ú¼åıù»??:

| á¼ãÓ | ?? | ?×â |
|------|------|------|
| `Function` role£¨?Íı£© | `"function"` roleù¬ËŞ? | ?roleËÇ? `User`£¬?ùŞ??Íı?ìıcontent |
| `Assistant`£¨ùŞ??éÄ£© | content?Íö | ??éÄîÜùŞ?ãáãÓ?ìıcontent |

î¤ `GetLatestMessagesWithFunctionFallback()` ñé?×â£¬ChatBlockñéîÜê«ã·á¼ãÓÜô?ù¬áóËÇ¡£

### ??ãÆÖÇ

```text
[FC ON ? ÜÁğíî¤ChatBlockñéîÜ?ŞÈ]
  User: "Í±?ä²âÏ?îÜô¸?"
  Assistant: (Íöcontent, metadata: function_call=get_weather)       ¡ç ??: Íöcontent
  Function: "Seoul: 15¡ÆC, Clear"                                    ¡ç ??: ÙéüùîÜrole
  Assistant: "âÏ?îÜô¸?ãÀ15¡ÆC£¬ôçô¸¡£"

[FC OFF?áê?îÜ???Íı]
  User: "Í±?ä²âÏ?îÜô¸?"
  Assistant: "[Called get_weather({"city":"Seoul"})]"                ¡ç éÄ?éÄãáãÓ?õö
  User: "[Function get_weather returned: Seoul: 15¡ÆC, Clear]"      ¡ç roleËÇ?User
  Assistant: "âÏ?îÜô¸?ãÀ15¡ÆC£¬ôçô¸¡£"
```

## ??

```csharp
// AIService.cs
internal IEnumerable<Message> GetLatestMessagesWithFunctionFallback()
{
    foreach (var message in GetLatestMessages())
    {
        // ÍöcontentîÜAssistant£¨ùŞ??éÄ£© ¡æ ??éÄãáãÓ?ìıcontent
        if (message.Role == ActorRole.Assistant &&
            message.Metadata?.GetValueOrDefault(MessageMetadataKeys.MessageType)
                ?.ToString() == "function_call")
        {
            var funcName = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionName)?.ToString() ?? "unknown";
            var funcArgs = message.Metadata.GetValueOrDefault(MessageMetadataKeys.FunctionArguments)?.ToString() ?? "{}";
            yield return new Message(ActorRole.Assistant, $"[Called {funcName}({funcArgs})]");
            continue;
        }

        // Function role ¡æ ËÇ?User role£¬ÜÁò¥?ÍıíÂ?content
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

?éÄéÍÊÀÜ×?îÜŞªùŞ? `BuildRequestBody()`:

- `ClaudeService.Parsing.cs`
- `ChatGptService.Parsing.cs` (`BuildNewApiBody()`, `BuildLegacyApiBody()`)
- `GeminiService.Parsing.cs`

## ßÓ?

? **MaxTokensí»?Üæ?**£¨`GetEffectiveMaxTokens()`£©ÛÕùêÍïíÂ ? ?? `RELEASE_NOTES.md` v4.0.1¡£
