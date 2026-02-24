using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System.Net.Http;

namespace Mythosia.AI.Rag.Tests;

/// <summary>
/// Minimal mock AIService for testing RagEnabledService wrapper logic.
/// Captures the prompt sent to GetCompletionAsync so tests can verify RAG augmentation.
/// </summary>
internal class MockAIService : AIService
{
    public string? LastReceivedPrompt { get; set; }
    public string CompletionResponse { get; set; } = "Mock LLM response";

    public MockAIService() : base("fake-key", "https://localhost/", new HttpClient())
    {
        AddNewChat();
    }

    public override AIProvider Provider => AIProvider.OpenAI;

    public override Task<string> GetCompletionAsync(Message message)
    {
        LastReceivedPrompt = message.Content;
        return Task.FromResult(CompletionResponse);
    }

    public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
    {
        LastReceivedPrompt = message.Content;
        return messageReceivedAsync(CompletionResponse);
    }

    protected override HttpRequestMessage CreateMessageRequest() => new();
    protected override HttpRequestMessage CreateFunctionMessageRequest() => new();
    protected override string ExtractResponseContent(string responseContent) => responseContent;
    protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string responseContent) => (string.Empty, default!);
    protected override string StreamParseJson(string jsonData) => jsonData;
    public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
    public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024") => Task.FromResult(Array.Empty<byte>());
    public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024") => Task.FromResult(string.Empty);
}
