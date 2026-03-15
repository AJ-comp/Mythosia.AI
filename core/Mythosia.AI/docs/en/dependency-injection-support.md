# [To-Be] Consumer API Improvement

> **Core goal**: The external API must be clean and elegant. Model switching must be a single line.

## As-Is ? Current friction

```csharp
// Must know the concrete service type per provider, manage HttpClient manually
var httpClient = new HttpClient();
var gpt = new ChatGptService("sk-...", httpClient);
var response = await gpt.GetCompletionAsync("hello");

// Switch model? ¡æ must create a new service instance
var httpClient2 = new HttpClient();
var claude = new ClaudeService("sk-ant-...", httpClient2);
```

## To-Be ? Ideal consumer experience

### 1. One-line registration

```csharp
services.AddMythosiaAI(o =>
{
    o.AddOpenAI("sk-...");
    o.AddAnthropic("sk-ant-...");
    o.AddGoogle("AIza...");
});
```

### 2. Model-based usage ? no need to know the provider

```csharp
public class ChatController(IAIServiceFactory ai)
{
    public async Task<string> Ask(string prompt)
    {
        // Just specify the model, provider is resolved automatically
        var service = ai.Create(AIModel.Gpt4oMini);
        return await service.GetCompletionAsync(prompt);
    }
}
```

### 3. Model switching in one line

```csharp
// GPT ¡æ Claude
var service = ai.Create(AIModel.Claude4Sonnet);

// Carry over conversation history
var service = ai.Create(AIModel.Claude4Sonnet).CopyFrom(previousService);
```

### 4. Streaming with the same pattern

```csharp
var service = ai.Create(AIModel.Gpt4oMini);

await foreach (var chunk in service.StreamAsync("explain quantum computing"))
{
    Console.Write(chunk);
}
```

## Design principles

| Principle | Description |
|-----------|-------------|
| **Provider-agnostic** | Consumer only needs to know `AIModel` enum |
| **HttpClient transparent** | `IHttpClientFactory` used internally, never exposed to consumer |
| **Backward compatible** | `new ChatGptService(key, httpClient)` still works |
| **Separation of concerns** | API keys at registration, model selection at usage |
