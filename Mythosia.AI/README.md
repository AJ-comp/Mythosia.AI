# Mythosia.AI

## Package Summary

The `Mythosia.AI` library provides an abstraction for various AI models, including support for **OpenAI GPT-4** and **Anthropic Claude** models. This library allows easy interaction with AI services, enabling both synchronous completion and streaming capabilities for models like GPT-3.5, GPT-4, Claude, and more.

This library abstracts the HTTP requests required to communicate with the APIs and provides a unified interface for various AI models, making it easier to integrate into any C# or ASP.NET Core project.

### Supported Models

- OpenAI GPT-4 and GPT-4 Turbo
- OpenAI GPT-3.5 Turbo
- Claude 3.x variants (Claude3_5Sonnet, Claude3Opus, Claude3Haiku)

### Key Features

- Synchronous completion and streaming support for AI models
- Customizable model selection, temperature, and max tokens
- Abstraction over the complexity of managing HTTP requests and responses
- Extendable structure for adding support for new AI models

## How to Use

To use this library, create an instance of either `ChatGptService` or `ClaudeService` depending on the AI model you're working with. Here's an example:

```csharp
using Mythosia.AI;
using System.Net.Http;

// Create HttpClient (best if injected via DI in ASP.NET Core)
var httpClient = new HttpClient();

// For GPT-based completion
var chatGptService = new ChatGptService("your_openai_api_key", httpClient);
string gptResponse = await chatGptService.GetCompletionAsync("What is the weather today?");
Console.WriteLine(gptResponse);

// For Claude-based completion
var claudeService = new ClaudeService("your_anthropic_api_key", httpClient);
string claudeResponse = await claudeService.GetCompletionAsync("What is the weather today?");
Console.WriteLine(claudeResponse);
```

### Streaming Responses
The library also supports streaming responses for real-time interaction. Here's an example of how to use the streaming feature:

```csharp
await chatGptService.StreamCompletionAsync("Tell me a joke", content => 
{
    Console.WriteLine(content); // Streamed content in real time
});
```


### ASP.NET Core Integration
To use this service in an ASP.NET Core application, you can register the services in the Startup.cs or Program.cs file as follows:

```csharp
using Mythosia.AI;

public void ConfigureServices(IServiceCollection services)
{
    // Add HttpClient for dependency injection
    services.AddHttpClient();

    // Register the AI services
    services.AddScoped<ChatGptService>(sp =>
    {
        var httpClient = sp.GetRequiredService<HttpClient>();
        return new ChatGptService("your_openai_api_key", httpClient);
    });

    services.AddScoped<ClaudeService>(sp =>
    {
        var httpClient = sp.GetRequiredService<HttpClient>();
        return new ClaudeService("your_anthropic_api_key", httpClient);
    });
}
```

### Service Usage in Controllers
Once the services are registered, you can inject them into your controllers or other services:

```csharp
public class AIController : ControllerBase
{
    private readonly ChatGptService _chatGptService;
    private readonly ClaudeService _claudeService;

    public AIController(ChatGptService chatGptService, ClaudeService claudeService)
    {
        _chatGptService = chatGptService;
        _claudeService = claudeService;
    }

    [HttpPost("chatgpt-completion")]
    public async Task<IActionResult> GetChatGptCompletion([FromBody] string prompt)
    {
        var result = await _chatGptService.GetCompletionAsync(prompt);
        return Ok(result);
    }

    [HttpPost("claude-completion")]
    public async Task<IActionResult> GetClaudeCompletion([FromBody] string prompt)
    {
        var result = await _claudeService.GetCompletionAsync(prompt);
        return Ok(result);
    }
}
```

## Conclusion
The Mythosia.AI library simplifies integration with AI services, offering a unified API for OpenAI's GPT and Anthropic's Claude models. It provides support for real-time streaming and synchronous completion, making it an ideal choice for applications requiring conversational AI capabilities.