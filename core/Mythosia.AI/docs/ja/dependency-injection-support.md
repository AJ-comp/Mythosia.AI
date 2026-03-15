# [To-Be] «³«ó«·«å?«Ş?APIËÇà¼

> **ú·ãıÙÍøö**: èâİ»ª«ªé«¯«ê?«óªÇ«¨«ì«¬«ó«ÈªËŞÅéÄªÇª­ªëª³ªÈ¡£«â«Ç«ëï·ªêôğª¨ª¬1ú¼ªÇª¢ªëª³ªÈ¡£

## As-Is ? úŞî¤ªÎÜôøµªµ

```csharp
// «×«í«Ğ«¤«À?ª´ªÈªË«µ?«Ó«¹úşªòò±ªëù±é©ª¬ª¢ªê¡¢HttpClientªòòÁïÈÎ·×âª¹ªëù±é©ª¬ª¢ªë
var httpClient = new HttpClient();
var gpt = new ChatGptService("sk-...", httpClient);
var response = await gpt.GetCompletionAsync("hello");

// «â«Ç«ëï·ªêôğª¨£¿ ¡æ «µ?«Ó«¹ªòãæĞ®íÂà÷ª¹ªëù±é©ª¬ª¢ªë
var httpClient2 = new HttpClient();
var claude = new ClaudeService("sk-ant-...", httpClient2);
```

## To-Be ? ×âßÌîÜªÊ«³«ó«·«å?«Ş???

### 1. 1ú¼ªÇÔô?

```csharp
services.AddMythosiaAI(o =>
{
    o.AddOpenAI("sk-...");
    o.AddAnthropic("sk-ant-...");
    o.AddGoogle("AIza...");
});
```

### 2. «â«Ç«ë«Ù?«¹ªÎŞÅéÄ ? «×«í«Ğ«¤«À?ªòò±ªëù±é©ªÊª·

```csharp
public class ChatController(IAIServiceFactory ai)
{
    public async Task<string> Ask(string prompt)
    {
        // «â«Ç«ëªòò¦ïÒª¹ªëªÀª±ªÇ¡¢«×«í«Ğ«¤«À?ªÏí»ÔÑÌ½ïÒ
        var service = ai.Create(AIModel.Gpt4oMini);
        return await service.GetCompletionAsync(prompt);
    }
}
```

### 3. «â«Ç«ëï·ªêôğª¨ª¬1ú¼

```csharp
// GPT ¡æ Claude ï·ªêôğª¨
var service = ai.Create(AIModel.Claude4Sonnet);

// ?ü¥×Û?ªòª½ªÎªŞªŞìÚª­?ª®
var service = ai.Create(AIModel.Claude4Sonnet).CopyFrom(previousService);
```

### 4. «¹«È«ê?«ß«ó«°ªâÔÒª¸«Ñ«¿?«ó

```csharp
var service = ai.Create(AIModel.Gpt4oMini);

await foreach (var chunk in service.StreamAsync("explain quantum computing"))
{
    Console.Write(chunk);
}
```

## àâÍªê«öÎ

| ê«öÎ | ?Ù¥ |
|------|------|
| **«×«í«Ğ«¤«À?Şªëîğí** | «³«ó«·«å?«Ş?ªÏ `AIModel` enumªÀª±ò±ªìªĞªèª¤ |
| **HttpClient÷âÎ¦** | `IHttpClientFactory`ªò?İ»ªÇŞÅéÄ¡¢«³«ó«·«å?«Ş?ªËÍëËÒª·ªÊª¤ |
| **?ğíû»üµ** | `new ChatGptService(key, httpClient)` Û°ãÒªâìÚª­?ª­ÔÑíÂ |
| **àâïÒİÂ×î** | API«­?ªÏÔô?ãÁ¡¢«â«Ç«ëàÔ?ªÏŞÅéÄãÁ |
