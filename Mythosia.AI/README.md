# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides an abstraction for various AI models, including support for **OpenAI GPT-4**, **Anthropic Claude**, **DeepSeek**, **Sonar**, **Gemini**, and more.  
This library allows easy interaction with AI services, enabling both synchronous completion and streaming capabilities for GPT-3.5, GPT-4, Claude, DeepSeek, Sonar, Gemini, etc.

### Supported Models

- **OpenAI**: GPT-4, GPT-3.5 (Turbo), GPT-4o variants
- **Anthropic Claude**: Claude 3.x (Claude3_5Sonnet, Claude3Opus, etc.)
- **DeepSeek**: various chat/reasoner models
- **Sonar**: sonar, sonar-pro, sonar-reasoning
- **Gemini**: gemini-1.5-flash (and possible future expansions)

### Key Features

- **Synchronous completion** and **streaming** support for multiple AI providers
- **Customizable model selection**, temperature, and max tokens
- **Single-prompt token counting** and **entire-conversation token counting** built-in
- **Unified abstraction** over HTTP requests/responses for each service
- **Extendable structure** for easily adding new AI model integrations

## How to Use

### Allocation
```csharp
using Mythosia.AI;
using System.Net.Http;

var httpClient = new HttpClient();

// Example usage for each supported service:
var aiService = new ChatGptService("your_openai_api_key", httpClient);
var aiService = new ClaudeService("your_anthropic_api_key", httpClient);
var aiService = new DeepSeekService("your_deepseek_api_key", httpClient);
var aiService = new SonarService("your_sonar_api_key", httpClient);
var aiService = new GeminiService("your_gemini_api_key", httpClient);
```

### completion
```csharp
string response = await aiService.GetCompletionAsync("What is AI?");
```

### Token Counting
```csharp
// Entire conversation token count
uint totalTokens = await aiService.GetInputTokenCountAsync();

// Single-prompt token count
uint promptTokens = await aiService.GetInputTokenCountAsync("One-off prompt to analyze");
```

### Model Switching Example
```csharp
// Add to existing ChatBlock usage
aiService.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
aiService.ActivateChat.ChangeModel("o1-2024-12-17"); // change with string
```

### Streaming Responses
```csharp
await aiService.StreamCompletionAsync("Explain quantum computing", content => {
Console.WriteLine(content);
});
```

### ASP.NET Core Integration
Below is an example of how to register and consume these AI services in an ASP.NET Core application.
```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddHttpClient();
    
    // Register your AI services with the needed keys
    services.AddScoped<ChatGptService>(sp => 
        new ChatGptService("openai_key", sp.GetRequiredService<HttpClient>()));

    services.AddScoped<ClaudeService>(sp => 
        new ClaudeService("claude_key", sp.GetRequiredService<HttpClient>()));

    services.AddScoped<DeepSeekService>(sp => 
        new DeepSeekService("deepseek_key", sp.GetRequiredService<HttpClient>()));

    services.AddScoped<SonarService>(sp =>
        new SonarService("sonar_key", sp.GetRequiredService<HttpClient>()));

    services.AddScoped<GeminiService>(sp =>
        new GeminiService("gemini_key", sp.GetRequiredService<HttpClient>()));
}
```

### Service Usage in Controllers
```csharp
[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    private readonly ChatGptService _chatGptService;
    private readonly ClaudeService _claudeService;
    private readonly DeepSeekService _deepseekService;
    private readonly SonarService _sonarService;
    private readonly GeminiService _geminiService;

    public AIController(
        ChatGptService chatGptService, 
        ClaudeService claudeService,
        DeepSeekService deepseekService,
        SonarService sonarService,
        GeminiService geminiService)
    {
        _chatGptService = chatGptService;
        _claudeService = claudeService;
        _deepseekService = deepseekService;
        _sonarService = sonarService;
        _geminiService = geminiService;
    }

    [HttpPost("deepseek-completion")]
    public async Task<IActionResult> GetDeepSeekCompletion([FromBody] string prompt)
    {
        var result = await _deepseekService.GetCompletionAsync(prompt);
        return Ok(result);
    }
    
    [HttpPost("sonar-completion")]
    public async Task<IActionResult> GetSonarCompletion([FromBody] string prompt)
    {
        var result = await _sonarService.GetCompletionAsync(prompt);
        return Ok(result);
    }
}
```

## Conclusion

The **Mythosia.AI** library simplifies integration with multiple AI providers (OpenAI GPT, Anthropic Claude, DeepSeek, Sonar, Gemini) by offering:

- **Synchronous and streaming** completion methods  
- **Single-prompt** and **full-conversation** token counting  
- **Unified** HTTP request/response handling  
- **Extendable** model and service structure  

It provides a convenient way to switch models, manage prompts, and retrieve results without worrying about each AI provider’s low-level API differences.