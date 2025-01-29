# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides an abstraction for various AI models, including support for **OpenAI GPT-4**, **Anthropic Claude**, and **DeepSeek**, **Sonar** models. 
This library allows easy interaction with AI services, enabling both synchronous completion and streaming capabilities for models like GPT-3.5, GPT-4, Claude, DeepSeek, and more.

### Supported Models

- OpenAI GPT-4 and GPT-4 Turbo, GPT-3.5 Turbo
- Claude 3.x variants (Claude3_5Sonnet, Claude3Opus, Claude3Haiku)
- DeepSeek models (deepseek-chat, deepseek-reasoner)
- Sonar models (sonar, sonar-pro, sonar-reasoning)

### Key Features

- Synchronous completion and streaming support for AI models
- Customizable model selection, temperature, and max tokens
- Abstraction over the complexity of managing HTTP requests and responses
- Extendable structure for adding support for new AI models

## How to Use
### allocation
```csharp
using Mythosia.AI;
using System.Net.Http;

var httpClient = new HttpClient();

var aiService = new ChatGptService("your_openai_api_key", httpClient);
var aiService = new ClaudeService("your_anthropic_api_key", httpClient);
var aiService = new DeepSeekService("your_deepseek_api_key", httpClient);
var aiService = new SonarService("your_sonar_api_key", httpClient);
```

### completion
```csharp
string response = await aiService.GetCompletionAsync("What is AI?");
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
```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddHttpClient();
    
    // Existing registrations
    services.AddScoped<ChatGptService>(sp => 
        new ChatGptService("openai_key", sp.GetRequiredService<HttpClient>()));
    
    services.AddScoped<ClaudeService>(sp => 
        new ClaudeService("claude_key", sp.GetRequiredService<HttpClient>()));
    
    // DeepSeek registration
    services.AddScoped<DeepSeekService>(sp => 
        new DeepSeekService("deepseek_key", sp.GetRequiredService<HttpClient>()));
}
```

### Service Usage in Controllers
```csharp
public class AIController : ControllerBase
{
    // Existing services
    private readonly ChatGptService _chatGptService;
    private readonly ClaudeService _claudeService;
    
    // DeepSeek addition
    private readonly DeepSeekService _deepseekService;

    public AIController(ChatGptService chatGptService, 
                       ClaudeService claudeService,
                       DeepSeekService deepseekService)
    {
        _chatGptService = chatGptService;
        _claudeService = claudeService;
        _deepseekService = deepseekService;
    }

    // Add new endpoint
    [HttpPost("deepseek-completion")]
    public async Task<IActionResult> GetDeepSeekCompletion([FromBody] string prompt)
    {
        var result = await _deepseekService.GetCompletionAsync(prompt);
        return Ok(result);
    }
}
```

## Conclusion
The Mythosia.AI library simplifies integration with AI services, offering a unified API for OpenAI's GPT, Anthropic's Claude, and DeepSeek models. It provides support for real-time streaming and synchronous completion across all integrated services.

### ASP.NET Core Integration
```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddHttpClient();
    
    // Existing registrations
    services.AddScoped<ChatGptService>(sp => 
        new ChatGptService("openai_key", sp.GetRequiredService<HttpClient>()));
    
    services.AddScoped<ClaudeService>(sp => 
        new ClaudeService("claude_key", sp.GetRequiredService<HttpClient>()));
    
    // DeepSeek registration
    services.AddScoped<DeepSeekService>(sp => 
        new DeepSeekService("deepseek_key", sp.GetRequiredService<HttpClient>()));
}
```

### Service Usage in Controllers
```csharp
public class AIController : ControllerBase
{
    // Existing services
    private readonly ChatGptService _chatGptService;
    private readonly ClaudeService _claudeService;
    
    // DeepSeek addition
    private readonly DeepSeekService _deepseekService;

    public AIController(ChatGptService chatGptService, 
                       ClaudeService claudeService,
                       DeepSeekService deepseekService)
    {
        _chatGptService = chatGptService;
        _claudeService = claudeService;
        _deepseekService = deepseekService;
    }

    // Add new endpoint
    [HttpPost("deepseek-completion")]
    public async Task<IActionResult> GetDeepSeekCompletion([FromBody] string prompt)
    {
        var result = await _deepseekService.GetCompletionAsync(prompt);
        return Ok(result);
    }
}
```

## Conclusion
The Mythosia.AI library simplifies integration with AI services, offering a unified API for OpenAI's GPT, Anthropic's Claude, and DeepSeek models. It provides support for real-time streaming and synchronous completion across all integrated services.