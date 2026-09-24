using System.Net;
using System.Text;
using System.Text.Json;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.OpenAI;
using Mythosia.VectorDb;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RagToolContinuationTests
{
    private const string Question = "CURRENT_POLICY_QUESTION_57319";
    private const string DocumentText = "RETRIEVED_POLICY_91842";
    private const string ImageUrl = "https://example.invalid/policy.png";
    private const string PreviousQuestion = "PREVIOUS_QUESTION_13672";

    [TestMethod]
    [DataRow("string", false, false)]
    [DataRow("string", false, true)]
    [DataRow("string", true, false)]
    [DataRow("string", true, true)]
    [DataRow("message", false, false)]
    [DataRow("message", false, true)]
    [DataRow("message", true, false)]
    [DataRow("message", true, true)]
    [DataRow("media", false, false)]
    [DataRow("media", false, true)]
    [DataRow("media", true, false)]
    [DataRow("media", true, true)]
    public async Task Completion_MultipleToolBatchesPreserveResultsAndAugmentedUser(
        string inputKind, bool hasReferences, bool withOptions)
    {
        using var handler = new ToolConversationHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var previous = new Message(ActorRole.User, PreviousQuestion);
        service.ActivateChat.Messages.Add(previous);
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "previous answer"));
        var retriever = new FixedRetriever(hasReferences);
        var rag = service.WithRag(b => b.UseRetriever(retriever));
        var input = CreateMessage(inputKind == "media");

        var answer = inputKind == "string"
            ? withOptions ? await rag.GetCompletionAsync(Question, new RagQueryOptions()) : await rag.GetCompletionAsync(Question)
            : withOptions ? await rag.GetCompletionAsync(input, new RagQueryOptions()) : await rag.GetCompletionAsync(input);

        Assert.AreEqual("final answer", answer);
        Assert.AreEqual(1, retriever.Calls);
        AssertConversationRequests(handler.Requests, hasReferences, inputKind == "media", hasPrevious: true);
        Assert.AreSame(previous, service.ActivateChat.Messages[0]);
        var storedInput = service.ActivateChat.Messages[2];
        if (inputKind != "string") Assert.AreSame(input, storedInput);
        Assert.AreEqual(Question, storedInput.Content);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Content?.Contains(DocumentText, StringComparison.Ordinal) == true),
            "Retrieval augmentation must remain request-only and not overwrite the original history.");
        AssertHistoryToolResults(service);
        AssertOriginalInput(input, inputKind == "media");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Completion_StatelessToolBatchesKeepOriginalConversation(bool media)
    {
        using var handler = new ToolConversationHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        service.StatelessMode = true;
        var originalChat = service.ActivateChat;
        var previous = new Message(ActorRole.User, PreviousQuestion);
        originalChat.Messages.Add(previous);
        var input = CreateMessage(media);
        var rag = service.WithRag(b => b.UseRetriever(new FixedRetriever(true)));

        Assert.AreEqual("final answer", await rag.GetCompletionAsync(input));

        AssertConversationRequests(handler.Requests, hasReferences: true, media, hasPrevious: false);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreSame(previous, Assert.ContainsSingle(originalChat.Messages));
        AssertOriginalInput(input, media);
    }

    [TestMethod]
    public async Task Completion_NextTurnDoesNotRetainPreviousRequestOverride()
    {
        using var handler = new ToolConversationHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var first = CreateMessage(media: true);
        var rag = service.WithRag(b => b.UseRetriever(new FixedRetriever(true)));
        Assert.AreEqual("final answer", await rag.GetCompletionAsync(first));

        Assert.AreEqual("final answer", await service.GetCompletionAsync("NEXT_PLAIN_QUESTION"));

        Assert.AreEqual(4, handler.Requests.Count);
        var messages = handler.Requests[3].GetProperty("messages").EnumerateArray().ToArray();
        Assert.AreEqual("NEXT_PLAIN_QUESTION", messages[^1].GetProperty("content").GetString());
        var originalUser = messages.Single(message => Role(message) == "user" && MessageText(message).Contains(Question, StringComparison.Ordinal));
        Assert.AreEqual(Question, MessageText(originalUser));
        Assert.IsFalse(handler.Requests[3].GetRawText().Contains(DocumentText, StringComparison.Ordinal));
        Assert.AreEqual(4, messages.Count(message => Role(message) == "tool"));
        AssertOriginalInput(first, media: true);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task StartRun_MultipleToolBatchesPreserveResultsAndAugmentedUser(bool hasReferences, bool media)
    {
        using var handler = new ToolConversationHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var input = CreateMessage(media);
        var rag = service.WithRag(b => b.UseRetriever(new FixedRetriever(hasReferences)));

        await using var run = await rag.StartRunAsync(input);
        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual("final answer", result.Text);
        AssertConversationRequests(handler.Requests, hasReferences, media, hasPrevious: false);
        AssertOriginalInput(input, media);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stream_TextOnlyControlPreservesAugmentedUser(bool hasReferences)
    {
        using var handler = new ToolConversationHandler(toolRounds: 0);
        using var client = new HttpClient(handler);
        var service = CreateService(client);
        var rag = service.WithRag(b => b.UseRetriever(new FixedRetriever(hasReferences)));
        var text = new StringBuilder();

        await foreach (var chunk in rag.StreamAsync(Question)) text.Append(chunk);

        Assert.AreEqual("final answer", text.ToString());
        var request = Assert.ContainsSingle(handler.Requests);
        var user = Assert.ContainsSingle(request.GetProperty("messages").EnumerateArray().Where(message => Role(message) == "user"));
        StringAssert.Contains(MessageText(user), Question);
        Assert.AreEqual(hasReferences, MessageText(user).Contains(DocumentText, StringComparison.Ordinal));
        Assert.AreEqual(Question, service.ActivateChat.Messages[0].Content);
    }

    private static OpenAIService CreateService(HttpClient client)
    {
        var service = new OpenAIService("offline-tool-continuation-test", AIModels.OpenAI.Gpt4o, client);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Description = "Read the requested policy value",
            Parameters = new FunctionParameters
            {
                Properties = new() { ["key"] = new ParameterProperty { Type = "string" } },
                Required = new() { "key" }
            },
            Handler = arguments => Task.FromResult("TOOL_RESULT_" + arguments["key"])
        });
        return service;
    }

    private static Message CreateMessage(bool media) => new(ActorRole.User, media
        ? new List<MessageContent>
        {
            new TextContent(Question),
            new ImageContent(ImageUrl) { IsHighDetail = true },
            new ImageContent(new byte[] { 1, 2, 3 }, "image/png")
        }
        : new List<MessageContent> { new TextContent(Question) })
    {
        Timestamp = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc),
        Metadata = new() { ["request_marker"] = "unchanged" }
    };

    private static void AssertOriginalInput(Message input, bool media)
    {
        Assert.AreEqual(Question, input.Content);
        Assert.AreEqual(Question, Assert.ContainsSingle(input.Contents.OfType<TextContent>()).Text);
        Assert.AreEqual(media ? 2 : 0, input.Contents.OfType<ImageContent>().Count());
        Assert.AreEqual("unchanged", input.Metadata!["request_marker"]);
        Assert.AreEqual(new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc), input.Timestamp);
    }

    private static void AssertHistoryToolResults(OpenAIService service)
    {
        var results = service.ActivateChat.Messages
            .Where(message => message.FunctionCallResultBatch != null)
            .SelectMany(message => message.FunctionCallResultBatch!.Results).ToArray();
        Assert.AreEqual(4, results.Length);
        foreach (var result in results)
            Assert.AreEqual("TOOL_RESULT_" + result.Call.Id, result.Content);
    }

    private static void AssertConversationRequests(
        IReadOnlyList<JsonElement> requests, bool hasReferences, bool media, bool hasPrevious)
    {
        Assert.AreEqual(3, requests.Count, "Two tool rounds followed by the final answer must issue three requests.");
        for (var requestIndex = 0; requestIndex < requests.Count; requestIndex++)
        {
            var messages = requests[requestIndex].GetProperty("messages").EnumerateArray().ToArray();
            var tools = messages.Where(message => Role(message) == "tool").ToArray();
            Assert.AreEqual(requestIndex * 2, tools.Length,
                $"Request {requestIndex + 1} must keep all completed tool results, not replace the latest result with the RAG user message.");
            for (var round = 1; round <= requestIndex; round++)
            for (var slot = 1; slot <= 2; slot++)
            {
                var id = $"call_{round}_{slot}";
                var tool = tools.Single(message => message.GetProperty("tool_call_id").GetString() == id);
                Assert.AreEqual("TOOL_RESULT_" + id, tool.GetProperty("content").GetString());
                Assert.IsTrue(messages.Any(message => Role(message) == "assistant" &&
                    message.TryGetProperty("tool_calls", out var calls) &&
                    calls.EnumerateArray().Any(call => call.GetProperty("id").GetString() == id)));
            }

            var users = messages.Where(message => Role(message) == "user").ToArray();
            Assert.AreEqual(hasPrevious ? 2 : 1, users.Length);
            if (hasPrevious) Assert.AreEqual(PreviousQuestion, MessageText(users[0]));
            var current = users[^1];
            StringAssert.Contains(MessageText(current), Question);
            Assert.AreEqual(hasReferences, MessageText(current).Contains(DocumentText, StringComparison.Ordinal),
                $"Request {requestIndex + 1} must retain retrieval augmentation on the original user message.");
            if (media)
            {
                var images = current.GetProperty("content").EnumerateArray()
                    .Where(part => part.GetProperty("type").GetString() == "image_url").ToArray();
                Assert.AreEqual(2, images.Length);
                Assert.AreEqual(ImageUrl, images[0].GetProperty("image_url").GetProperty("url").GetString());
                Assert.AreEqual("high", images[0].GetProperty("image_url").GetProperty("detail").GetString());
                Assert.AreEqual("data:image/png;base64,AQID", images[1].GetProperty("image_url").GetProperty("url").GetString());
            }
        }
    }

    private static string? Role(JsonElement message) => message.GetProperty("role").GetString();

    private static string MessageText(JsonElement message)
    {
        var content = message.GetProperty("content");
        return content.ValueKind == JsonValueKind.String ? content.GetString()! : string.Join(" ",
            content.EnumerateArray().Where(part => part.GetProperty("type").GetString() == "text")
                .Select(part => part.GetProperty("text").GetString()));
    }

    private sealed class FixedRetriever(bool hasReferences) : IRagRetriever
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<VectorSearchResult>> RetrieveAsync(RagRetrievalRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(hasReferences
                ? new[] { new VectorSearchResult(new VectorRecord("policy", new float[] { 1 }, DocumentText), 1) }
                : Array.Empty<VectorSearchResult>());
        }
    }

    private sealed class ToolConversationHandler(int toolRounds = 2) : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(body.RootElement.Clone());
            var round = Requests.Count;
            var calls = Enumerable.Range(1, 2).Select(slot => new
            {
                index = slot - 1,
                id = $"call_{round}_{slot}",
                type = "function",
                function = new { name = "lookup", arguments = JsonSerializer.Serialize(new { key = $"call_{round}_{slot}" }) }
            }).ToArray();
            var stream = body.RootElement.TryGetProperty("stream", out var streamValue) && streamValue.GetBoolean();
            string response;
            if (stream)
            {
                object delta = round <= toolRounds ? new { tool_calls = calls } : new { content = "final answer" };
                response = "data: " + JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta, finish_reason = (string?)null } } }) + "\n\n"
                    + "data: " + JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = new { }, finish_reason = round <= toolRounds ? "tool_calls" : "stop" } } }) + "\n\n"
                    + "data: [DONE]\n\n";
            }
            else
            {
                object message = round <= toolRounds
                    ? new { role = "assistant", content = (string?)null, tool_calls = calls }
                    : new { role = "assistant", content = "final answer" };
                response = JsonSerializer.Serialize(new { choices = new[] { new { message, finish_reason = round <= toolRounds ? "tool_calls" : "stop" } } });
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
