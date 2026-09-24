using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class StatelessSummaryIsolationTests
{
    private const string PreviousSummary = "COMPANY_A_PRIVATE_SUMMARY";
    private const string PreviousQuestion = "COMPANY_A_PRIVATE_QUESTION";
    private const string PreviousAnswer = "COMPANY_A_PRIVATE_ANSWER";
    private const string CurrentQuestion = "COMPANY_B_CURRENT_QUESTION";
    private const string ScoringInstructions = "Score the supplied documents independently.";

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task StatelessCompletion_ExcludesSummaryAndHistory_ThenRestoresStatefulConversation(
        bool anthropic, bool useRequestProfile)
    {
        using var handler = new RecordingHandler(anthropic);
        using var http = new HttpClient(handler);
        var service = CreateService(anthropic, http);
        var originalChat = service.ActivateChat;
        var originalMessages = originalChat.Messages.ToArray();
        var originalPolicy = service.ConversationPolicy;
        var originalModel = service.Model;
        var originalTemperature = service.Temperature;
        var originalMaxTokens = service.MaxTokens;
        service.StatelessMode = !useRequestProfile;
        var profile = useRequestProfile ? new AIRequestProfile { Stateless = true } : null;

        var result = await service.GetCompletionAsync(
            new Message(ActorRole.User, CurrentQuestion), profile: profile);

        Assert.AreEqual("score", result);
        Assert.AreEqual(1, handler.Requests.Count);
        AssertWireIsolation(handler.Requests[0]);
        Assert.AreSame(originalChat, service.ActivateChat);
        CollectionAssert.AreEqual(originalMessages, service.ActivateChat.Messages.ToArray());
        Assert.AreSame(originalPolicy, service.ConversationPolicy);
        Assert.AreEqual(PreviousSummary, service.ConversationPolicy!.CurrentSummary);
        Assert.AreEqual(!useRequestProfile, service.StatelessMode);
        Assert.AreEqual(originalModel, service.Model);
        Assert.AreEqual(originalTemperature, service.Temperature);
        Assert.AreEqual(originalMaxTokens, service.MaxTokens);
        Assert.AreEqual(ScoringInstructions, service.SystemMessage);

        // A stateless profile must not permanently hide the existing conversation or its summary.
        service.StatelessMode = false;
        await service.GetCompletionAsync(new Message(ActorRole.User, "Continue the original conversation"), profile: null);

        Assert.AreEqual(2, handler.Requests.Count);
        AssertStatefulWire(handler.Requests[1]);
        Assert.IsFalse(handler.Requests[1].Contains(CurrentQuestion, StringComparison.Ordinal));
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(originalMessages.Length + 2, originalChat.Messages.Count);
        Assert.AreSame(originalMessages[0], originalChat.Messages[0]);
        Assert.AreSame(originalMessages[1], originalChat.Messages[1]);
        Assert.AreSame(originalPolicy, service.ConversationPolicy);
        Assert.AreEqual(PreviousSummary, service.ConversationPolicy!.CurrentSummary);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StatefulCompletion_StillIncludesSummaryAndHistory(bool anthropic)
    {
        using var handler = new RecordingHandler(anthropic);
        using var http = new HttpClient(handler);
        var service = CreateService(anthropic, http);
        var originalChat = service.ActivateChat;
        var originalPolicy = service.ConversationPolicy;

        var result = await service.GetCompletionAsync(new Message(ActorRole.User, CurrentQuestion), profile: null);

        Assert.AreEqual("score", result);
        Assert.AreEqual(1, handler.Requests.Count);
        AssertStatefulWire(handler.Requests[0]);
        StringAssert.Contains(handler.Requests[0], CurrentQuestion);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(4, originalChat.Messages.Count);
        Assert.AreSame(originalPolicy, service.ConversationPolicy);
        Assert.AreEqual(PreviousSummary, service.ConversationPolicy!.CurrentSummary);
        Assert.IsFalse(service.StatelessMode);
    }

    private static AIService CreateService(bool anthropic, HttpClient http)
    {
        // Fable 5.1 always uses Claude's preserved wire-history system-message path;
        // GPT-4o exercises the common system-message composition on Chat Completions.
        AIService service = anthropic
            ? new AnthropicService("offline-test-key", AIModels.Anthropic.ClaudeFable5_1, http)
            : new OpenAIService("offline-test-key", AIModels.OpenAI.Gpt4o, http);
        service.SystemMessage = ScoringInstructions;
        service.Temperature = 0.2f;
        service.MaxTokens = 2048;
        service.ConversationPolicy = new SummaryConversationPolicy { CurrentSummary = PreviousSummary };
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, PreviousQuestion));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, PreviousAnswer));
        return service;
    }

    private static void AssertWireIsolation(string body)
    {
        StringAssert.Contains(body, CurrentQuestion);
        StringAssert.Contains(body, ScoringInstructions);
        Assert.IsFalse(body.Contains(PreviousSummary, StringComparison.Ordinal));
        Assert.IsFalse(body.Contains(PreviousQuestion, StringComparison.Ordinal));
        Assert.IsFalse(body.Contains(PreviousAnswer, StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("Previous conversation summary", StringComparison.Ordinal));
    }

    private static void AssertStatefulWire(string body)
    {
        StringAssert.Contains(body, ScoringInstructions);
        StringAssert.Contains(body, PreviousSummary);
        StringAssert.Contains(body, PreviousQuestion);
        StringAssert.Contains(body, PreviousAnswer);
    }

    private sealed class RecordingHandler(bool anthropic) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            Assert.AreEqual(anthropic ? AIModels.Anthropic.ClaudeFable5_1 : AIModels.OpenAI.Gpt4o,
                document.RootElement.GetProperty("model").GetString());
            Assert.AreEqual(anthropic ? "/v1/messages" : "/v1/chat/completions", request.RequestUri!.AbsolutePath);
            Requests.Add(body);
            var response = anthropic
                ? """
                  {"id":"offline-response","model":"claude-fable-5-1","role":"assistant","content":[{"type":"text","text":"score"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}
                  """
                : """
                  {"id":"offline-response","choices":[{"index":0,"message":{"role":"assistant","content":"score"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                  """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
