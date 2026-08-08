using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// Verifies the v7 conversation contract: absent an explicit conversation policy,
/// every stored message is sent to the provider.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class FullHistoryTests
{
    private sealed class HistoryProbeService : AIService
    {
        public HistoryProbeService()
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public System.Collections.Generic.List<Message> RequestMessages()
            => GetLatestMessages().ToList();

        public override Task<string> GetCompletionAsync(Message message)
            => Task.FromResult(string.Empty);

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
            => (response, new FunctionCallBatch());
    }

    [TestMethod]
    public void GetLatestMessages_ReturnsCompleteConversationHistory()
    {
        var service = new HistoryProbeService();
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "original question"));

        for (var index = 0; index < 25; index++)
        {
            service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, $"tool call {index}"));
            service.ActivateChat.Messages.Add(new Message(ActorRole.Function, $"tool result {index}"));
        }

        var requestMessages = service.RequestMessages();

        Assert.AreEqual(service.ActivateChat.Messages.Count, requestMessages.Count);
        Assert.AreEqual("original question", requestMessages[0].Content);
        Assert.AreEqual("tool result 24", requestMessages[^1].Content);
    }
}
