using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// Pins the request/history boundary used by ContextManagementTest without depending on
/// model-generated acknowledgements. These tests do not explain a provider's live refusal.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class AnthropicContextManagementTests
{
    private const string Question = "What numbers did I mention?";
    private const string FinalAnswer = "You mentioned 1, 2, 3, 4, and 5.";

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    public async Task ContextAppendix_KeepsFullRoleSeparatedHistory_AndSelectsLastFiveMessages(string model)
    {
        using var handler = CreateHandler(Answer(FinalAnswer));
        using var client = new HttpClient(handler);
        var service = new AnthropicService("offline-test-key", model, client);
        await AddFiveTurnsAsync(service);
        var retainedHistory = service.ActivateChat.Messages.ToArray();

        var answer = await service.GetCompletionWithContextAsync(Question, contextMessages: 5);

        Assert.AreEqual(FinalAnswer, answer);
        AssertRequestSequence(handler, model);
        AssertRetainedHistory(service, retainedHistory);
        Assert.AreEqual(12, service.ActivateChat.Messages.Count);
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[10].Role);
        Assert.AreEqual(ExpectedContextPrompt(), service.ActivateChat.Messages[10].GetDisplayText());
        Assert.AreEqual(ActorRole.Assistant, service.ActivateChat.Messages[11].Role);
        Assert.AreEqual(FinalAnswer, service.ActivateChat.Messages[11].GetDisplayText());
    }

    [TestMethod]
    [DataRow(AIModels.Anthropic.ClaudeOpus5)]
    [DataRow(AIModels.Anthropic.ClaudeOpus5_5)]
    public async Task ContextRefusal_PreservesProviderDetails_WithoutSavingPartialAssistantOrRetrying(string model)
    {
        const string refusal = """
            {"id":"refused-context","role":"assistant","content":[{"type":"text","text":"PARTIAL_REFUSAL_TEXT"}],
             "stop_reason":"refusal","stop_details":{"category":"general_harms","explanation":"synthetic provider refusal"},
             "usage":{"input_tokens":10,"output_tokens":1}}
            """;
        using var handler = CreateHandler(Response.Json(refusal));
        using var client = new HttpClient(handler);
        var service = new AnthropicService("offline-test-key", model, client);
        await AddFiveTurnsAsync(service);
        var retainedHistory = service.ActivateChat.Messages.ToArray();

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionWithContextAsync(Question, contextMessages: 5));

        Assert.AreEqual(nameof(AIProvider.Anthropic), exception.ServiceName);
        StringAssert.Contains(exception.Message, "stop_reason=refusal");
        Assert.IsNotNull(exception.ErrorDetails);
        using var details = JsonDocument.Parse(exception.ErrorDetails);
        Assert.AreEqual("general_harms", details.RootElement.GetProperty("category").GetString());
        Assert.AreEqual("synthetic provider refusal", details.RootElement.GetProperty("explanation").GetString());

        AssertRequestSequence(handler, model);
        AssertRetainedHistory(service, retainedHistory);
        Assert.AreEqual(11, service.ActivateChat.Messages.Count,
            "The refused user input remains, but no successful or partial assistant response may be appended.");
        Assert.AreEqual(5, service.ActivateChat.Messages.Count(message => message.Role == ActorRole.Assistant));
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[^1].Role);
        Assert.AreEqual(ExpectedContextPrompt(), service.ActivateChat.Messages[^1].GetDisplayText());
        Assert.IsFalse(service.ActivateChat.Messages.Any(message =>
            message.GetDisplayText().Contains("PARTIAL_REFUSAL_TEXT", StringComparison.Ordinal)));
    }

    private static QueueHttpMessageHandler CreateHandler(Response finalResponse) =>
        new(Enumerable.Range(1, 5)
            .Select(number => Answer($"Acknowledged {number}."))
            .Append(finalResponse).ToArray());

    private static Response Answer(string text) => Response.Json(JsonSerializer.Serialize(new
    {
        id = "synthetic-context-response",
        role = "assistant",
        content = new[] { new { type = "text", text } },
        stop_reason = "end_turn",
        usage = new { input_tokens = 10, output_tokens = 1 }
    }));

    private static async Task AddFiveTurnsAsync(AnthropicService service)
    {
        for (var number = 1; number <= 5; number++)
            Assert.AreEqual($"Acknowledged {number}.",
                await service.GetCompletionAsync($"Remember number {number}"));
    }

    private static void AssertRequestSequence(QueueHttpMessageHandler handler, string model)
    {
        Assert.AreEqual(6, handler.RequestBodies.Count,
            "Five memory turns and one context request should produce exactly six HTTP requests.");
        for (var requestIndex = 0; requestIndex < handler.RequestBodies.Count; requestIndex++)
        {
            using var document = JsonDocument.Parse(handler.RequestBodies[requestIndex]);
            var root = document.RootElement;
            Assert.AreEqual(model, root.GetProperty("model").GetString());
            var messages = root.GetProperty("messages");
            Assert.AreEqual(requestIndex * 2 + 1, messages.GetArrayLength(),
                $"Request {requestIndex + 1} must retain every preceding completed turn.");

            for (var messageIndex = 0; messageIndex < messages.GetArrayLength(); messageIndex++)
            {
                var isUser = messageIndex % 2 == 0;
                var number = messageIndex / 2 + 1;
                Assert.AreEqual(isUser ? "user" : "assistant",
                    messages[messageIndex].GetProperty("role").GetString());
                var expected = requestIndex == 5 && messageIndex == 10
                    ? ExpectedContextPrompt()
                    : isUser ? $"Remember number {number}" : $"Acknowledged {number}.";
                Assert.AreEqual(expected, ReadText(messages[messageIndex]));
            }
        }
    }

    private static void AssertRetainedHistory(AnthropicService service, Message[] original)
    {
        Assert.AreEqual(10, original.Length);
        for (var index = 0; index < original.Length; index++)
        {
            Assert.AreSame(original[index], service.ActivateChat.Messages[index],
                "Building the context appendix must not replace or remove retained history messages.");
            Assert.AreEqual(index % 2 == 0 ? ActorRole.User : ActorRole.Assistant,
                service.ActivateChat.Messages[index].Role);
            var number = index / 2 + 1;
            Assert.AreEqual(index % 2 == 0 ? $"Remember number {number}" : $"Acknowledged {number}.",
                service.ActivateChat.Messages[index].GetDisplayText());
        }
    }

    private static string ExpectedContextPrompt() =>
        string.Join(Environment.NewLine,
            "Assistant: Acknowledged 3.",
            "User: Remember number 4",
            "Assistant: Acknowledged 4.",
            "User: Remember number 5",
            "Assistant: Acknowledged 5.") +
        Environment.NewLine + "\nBased on the above context, " + Question + Environment.NewLine;

    private static string ReadText(JsonElement message)
    {
        var content = message.GetProperty("content");
        return content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : string.Concat(content.EnumerateArray()
                .Where(block => block.GetProperty("type").GetString() == "text")
                .Select(block => block.GetProperty("text").GetString()));
    }
}
