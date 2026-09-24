using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public sealed class RequestMessageOverrideIsolationTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ExplicitContext_StaysOnInputAcrossTools_ForCompletionAndBuilder(bool builder, bool stateless)
    {
        using var wire = new Wire((call, _) => Reply(call == 1 ? ToolCall : Answer("done")));
        using var http = new HttpClient(wire);
        var service = Service(http);
        var original = new Message(ActorRole.User, "original input");
        var oldChat = service.ActivateChat;
        var oldMessage = new Message(ActorRole.User, "older input");
        oldChat.Messages.Add(oldMessage);
        var context = Context("augmented input");

        string result;
        if (builder)
            result = await service.CreateRequest(original).WithContext(context).WithStatelessMode(stateless).GetCompletionAsync();
        else
            result = await service.GetCompletionAsync(original, new AIRequestProfile { Stateless = stateless }, context);

        Assert.AreEqual("done", result);
        Assert.AreEqual(2, wire.Bodies.Count);
        AssertInputAndTool(wire.Bodies[1], "augmented input");
        Assert.AreEqual("original input", original.Content);
        Assert.AreEqual("augmented input", context.RequestMessageOverride!.Content);
        Assert.AreSame(oldChat, service.ActivateChat);
        Assert.AreSame(oldMessage, oldChat.Messages[0]);
        if (stateless)
            Assert.AreEqual(1, oldChat.Messages.Count);
        else
        {
            Assert.IsTrue(oldChat.Messages.Any(m => m.Content == "original input"));
            Assert.IsFalse(oldChat.Messages.Any(m => m.Content == "augmented input"));
        }
    }

    [TestMethod]
    public async Task DynamicContext_KeepsAdditionalMessagesAndToolResults()
    {
        using var wire = new Wire((call, _) => Reply(call == 1 ? ToolCall : Answer("done")));
        using var http = new HttpClient(wire);
        var service = Service(http);
        var context = Context("dynamic augmented input");
        context.AdditionalMessages = new[] { new Message(ActorRole.User, "additional constraint") };
        var contextCalls = 0;
        service.WithSystemMessageProvider(() => { contextCalls++; return context; });

        await service.GetCompletionAsync("original input");

        Assert.AreEqual(1, contextCalls);
        Assert.AreEqual(2, wire.Bodies.Count);
        AssertInputAndTool(wire.Bodies[1], "dynamic augmented input");
        using var doc = JsonDocument.Parse(wire.Bodies[1]);
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.AreEqual("additional constraint", messages[^1].GetProperty("content").GetString());
        Assert.AreEqual(1, messages.Count(m => m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() == "additional constraint"));
        Assert.IsFalse(service.ActivateChat.Messages.Any(m => m.Content == "additional constraint"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedOrCanceledRequest_DoesNotAnchorNextRequestToTheOldInput(bool cancel)
    {
        using var cts = new CancellationTokenSource();
        using var wire = new Wire((call, _) =>
        {
            if (call == 1)
            {
                if (cancel) { cts.Cancel(); return Reply(Answer("ignored")); }
                return Reply("{\"error\":{\"message\":\"synthetic rejection\"}}", HttpStatusCode.BadRequest);
            }
            return Reply(call == 2 ? ToolCall : Answer("done"));
        });
        using var http = new HttpClient(wire);
        var service = Service(http);
        var failedInput = new Message(ActorRole.User, "failed original input");

        Task<string> First() => service.GetCompletionAsync(failedInput, context: Context("first augmentation"), cancellationToken: cts.Token);
        if (cancel) await Assert.ThrowsAsync<OperationCanceledException>(First);
        else await Assert.ThrowsAsync<AIServiceException>(First);

        await service.GetCompletionAsync(new Message(ActorRole.User, "next original input"), context: Context("next augmentation"));

        Assert.AreEqual(3, wire.Bodies.Count);
        AssertInputAndTool(wire.Bodies[2], "next augmentation");
        using var doc = JsonDocument.Parse(wire.Bodies[2]);
        var texts = doc.RootElement.GetProperty("messages").EnumerateArray()
            .Where(m => m.TryGetProperty("content", out var value) && value.ValueKind == JsonValueKind.String)
            .Select(m => m.GetProperty("content").GetString()).ToArray();
        CollectionAssert.Contains(texts, "failed original input");
        CollectionAssert.DoesNotContain(texts, "first augmentation");
        CollectionAssert.DoesNotContain(texts, "next original input");
        Assert.AreEqual("failed original input", failedInput.Content);
    }

    [TestMethod]
    public async Task StructuredRepair_OverridesOriginalInputWithoutReplacingCorrectionPrompt()
    {
        using var wire = new Wire((call, _) => Reply(Answer(call == 1 ? "not JSON" : "{\"Value\":7}")));
        using var http = new HttpClient(wire);
        var service = Service(http);
        service.Functions.Clear();
        service.WithSystemMessageProvider(() => Context("augmented structured input"));

        var result = await service.GetCompletionAsync<StructuredAnswer>("original structured input");

        Assert.AreEqual(7, result.Value);
        Assert.AreEqual(2, wire.Bodies.Count);
        using var doc = JsonDocument.Parse(wire.Bodies[1]);
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var userTexts = messages.Where(m => m.GetProperty("role").GetString() == "user")
            .Select(m => m.GetProperty("content").GetString()).ToArray();
        Assert.AreEqual(2, userTexts.Length);
        Assert.AreEqual("augmented structured input", userTexts[0]);
        StringAssert.Contains(userTexts[1]!, "[STRUCTURED OUTPUT CORRECTION]");
        StringAssert.Contains(userTexts[1]!, "not JSON");
        Assert.AreEqual("original structured input", service.ActivateChat.Messages[0].Content);
    }

    public sealed class StructuredAnswer { public int Value { get; set; } }

    [TestMethod]
    public async Task ReusingMessageForAnotherTurn_OnlyAugmentsTheCurrentOccurrence()
    {
        using var wire = new Wire((call, _) => Reply(call == 2 ? ToolCall : Answer("done")));
        using var http = new HttpClient(wire);
        var service = Service(http);
        var reused = new Message(ActorRole.User, "repeated question");
        await service.GetCompletionAsync(reused, profile: null);

        await service.GetCompletionAsync(reused, context: Context("current augmentation"));

        Assert.AreEqual(3, wire.Bodies.Count);
        foreach (var body in wire.Bodies.Skip(1))
        {
            using var doc = JsonDocument.Parse(body);
            var users = doc.RootElement.GetProperty("messages").EnumerateArray()
                .Where(m => m.GetProperty("role").GetString() == "user")
                .Select(m => m.GetProperty("content").GetString()).ToArray();
            CollectionAssert.AreEqual(new[] { "repeated question", "current augmentation" }, users,
                "Reusing a Message must not attach this turn's context to its earlier occurrence.");
        }
        AssertInputAndTool(wire.Bodies[2], "current augmentation");
        Assert.AreEqual("repeated question", reused.Content);
    }

    private static OpenAIService Service(HttpClient http)
    {
        var service = new OpenAIService("offline-test-key", AIModels.OpenAI.Gpt4o, http);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup", Description = "Read a fixture value.",
            Handler = _ => Task.FromResult("tool result value")
        });
        return service;
    }

    private static AIRequestContext Context(string text) => new() { RequestMessageOverride = new Message(ActorRole.User, text) };

    private static void AssertInputAndTool(string body, string augmented)
    {
        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.AreEqual(1, messages.Count(m => m.GetProperty("role").GetString() == "user" && m.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() == augmented));
        var tool = messages.SingleOrDefault(m => m.GetProperty("role").GetString() == "tool");
        Assert.AreNotEqual(JsonValueKind.Undefined, tool.ValueKind, "The tool response must remain in the outgoing request.");
        Assert.AreEqual("call_lookup", tool.GetProperty("tool_call_id").GetString());
        Assert.AreEqual("tool result value", tool.GetProperty("content").GetString());
        var assistant = messages.Single(m => m.GetProperty("role").GetString() == "assistant" && m.TryGetProperty("tool_calls", out _));
        Assert.AreEqual("call_lookup", assistant.GetProperty("tool_calls")[0].GetProperty("id").GetString());
    }

    private const string ToolCall = """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_lookup","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""";
    private static string Answer(string value) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = value }, finish_reason = "stop" } } });
    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Wire(Func<int, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return response(Bodies.Count, cancellationToken);
        }
    }
}
