# [To-Be] á¼?íºAPIËÇ?

> **ú·ãıÙÍ?**: èâİ»ŞÅéÄù±????äº¡£Ù¼úşï·?ù±?ìéú¼?ïÒ¡£

## As-Is ? ?îñîÜÜôøµ

```csharp
// Øß?ğ«ÍêßÂÔ´âÍé©ò±Ô³Îı?îÜÜ×??úş£¬?é©â¢?Î·×âHttpClient
var httpClient = new HttpClient();
var gpt = new ChatGptService("sk-...", httpClient);
var response = await gpt.GetCompletionAsync("hello");

// ï·?Ù¼úş£¿¡æ âÍé©ñìãæ?ËïÜ×??ÖÇ
var httpClient2 = new HttpClient();
var claude = new ClaudeService("sk-ant-...", httpClient2);
```

## To-Be ? ×âßÌîÜá¼?íº??

### 1. ìéú¼ñ¼?

```csharp
services.AddMythosiaAI(o =>
{
    o.AddOpenAI("sk-...");
    o.AddAnthropic("sk-ant-...");
    o.AddGoogle("AIza...");
});
```

### 2. ĞñéÍÙ¼úşŞÅéÄ ? ÙéâÍò±Ô³ğ«ÍêßÂ

```csharp
public class ChatController(IAIServiceFactory ai)
{
    public async Task<string> Ask(string prompt)
    {
        // ñşâÍò¦ïÒÙ¼úş£¬ğ«ÍêßÂí»??ïÒ
        var service = ai.Create(AIModel.Gpt4oMini);
        return await service.GetCompletionAsync(prompt);
    }
}
```

### 3. Ù¼úşï·?ñşâÍìéú¼

```csharp
// GPT ¡æ Claude ï·?
var service = ai.Create(AIModel.Claude4Sonnet);

// ???ŞÈå¥Ê¦ì¤òÁïÈ?ã¯
var service = ai.Create(AIModel.Claude4Sonnet).CopyFrom(previousService);
```

### 4. ×µãÒ?õóÔÒ?îÜÙ¼ãÒ

```csharp
var service = ai.Create(AIModel.Gpt4oMini);

await foreach (var chunk in service.StreamAsync("explain quantum computing"))
{
    Console.Write(chunk);
}
```

## ??ê«?

| ê«? | ?Ù¥ |
|------|------|
| **ğ«ÍêßÂÙé?** | á¼?íºñşâÍò±Ô³ `AIModel` enum |
| **HttpClient÷âÙ¥** | ?İ»ŞÅéÄ `IHttpClientFactory`£¬Üôú¾á¼?íºøìÖÚ |
| **ú¾ı¨ÌÂé»** | `new ChatGptService(key, httpClient)` Û°ãÒ??êóüù |
| **ÛÕöÇİÂ?** | APIÚË?î¤ñ¼??£¬Ù¼úş??î¤ŞÅéÄ? |
