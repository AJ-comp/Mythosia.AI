using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System.Linq;
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

    public override string Provider => nameof(AIProvider.OpenAI);

    public override Task<string> GetCompletionAsync(Message message)
    {
        ActivateChat.Messages.Add(message);
        var resolved = GetLatestMessages().LastOrDefault();
        LastReceivedPrompt = resolved?.Content ?? message.Content;
        return Task.FromResult(CompletionResponse);
    }

    public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
    {
        ActivateChat.Messages.Add(message);
        var resolved = GetLatestMessages().LastOrDefault();
        LastReceivedPrompt = resolved?.Content ?? message.Content;
        return messageReceivedAsync(CompletionResponse);
    }

    protected override HttpRequestMessage CreateMessageRequest() => new();
    protected override HttpRequestMessage CreateFunctionMessageRequest() => new();
    protected override string ExtractResponseContent(string responseContent) => responseContent;
    protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string responseContent)
        => (string.Empty, new FunctionCallBatch());
    protected override string StreamParseJson(string jsonData) => jsonData;
    public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
    public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
}
