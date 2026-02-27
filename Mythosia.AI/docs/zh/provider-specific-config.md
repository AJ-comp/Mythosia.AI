# ğ«ÍêßÂ÷åêóÛÕöÇÊ­?

## ê«?

| ÛÕöÇ?úş | êÈöÇ | ãÆÖÇ |
|----------|------|------|
| **÷×éÄÛÕöÇ** | `ChatBlock` | Temperature, TopP, MaxTokens, FrequencyPenalty Ôõ |
| **ğ«ÍêßÂ÷åêó** | ÊÀÜ×?? | ThinkingBudget (Gemini), ReasoningEffort (GPT) Ôõ |

## ?îñ??: Ü×???

ğ«ÍêßÂ÷åêó?öÇíÂ?ÊÀÜ×??îÜ?àõ?ú¼Î·×â¡£

```csharp
// ÷×éÄ?öÇ ¡æ ChatBlock
geminiService.ActivateChat.Temperature = 0.7f;
geminiService.ActivateChat.MaxTokens = 4096;

// ğ«ÍêßÂ÷åêó?öÇ ¡æ Ü×?
geminiService.ThinkingBudget = 1024;
```

### ?ïÃ
- ChatBlock?ğ«ÍêßÂèÇîïÙé?£¨ÊÎ?îÜİÂ?£©
- İ¬ùêOOPê«?£¨Ü×?Î·×âí»ĞùîÜ÷åêóÛÕöÇ£©
- ìé?Ü×??ÖÇìé÷ß÷åêó?öÇ ¡æ ????

### ÌÀïÃ
- ìé?Ü×??îÜÒı?ChatBlockÍìú½ßÓÔÒîÜ÷åêó?öÇ

## âÍé©?ì¹ÓğChatBlock??îÜï×?

åıÍıÚ±?õó? **Øß?ChatBlockâÍé©?Ø¡??÷åêó?öÇîÜâÍÏ´**£¬÷×?î¤ChatBlock?ôÕÊ¥æÅ?ôøã·ûùîÜÛÕöÇ??ú¼?ì¹¡£

```csharp
// ãÆÖÇ£¨?îñÚ±??£©
public class ChatBlock
{
    private GeminiConfig _gemini;
    public GeminiConfig Gemini => _gemini ??= new GeminiConfig();
}

// ŞÅéÄ
chatBlock.Gemini.ThinkingBudget = 1024;
```

### âÍé©ó®Û°ãÒîÜ?ÌØ
- ìé?Ü×??ÖÇñéChatBlock AûúBâÍé©ŞÅéÄÜôÔÒîÜThinkingBudget
- ??ß¾??ï×???ùÖ?£¬ì×ó®ÙÍîñ?ò¥Ü×???

## ?óşìíò¤

- **2026-02-12**: õÌôøì¤Option B£¨ChatBlock??£©??ı¨£¬üŞ?ÓğÜ×???¡£÷÷?÷åêó?öÇÛ¯î¤Ü×?ñéÌÚí»æÔ¡£
