using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class CompletionCancellationConvenienceTests
{
    [TestMethod]
    [DataRow("quick")]
    [DataRow("quick-image")]
    [DataRow("image")]
    [DataRow("image-url")]
    [DataRow("once-image")]
    [DataRow("retry")]
    [DataRow("context")]
    [DataRow("without-functions")]
    [DataRow("agent")]
    public async Task PreCancelledConvenienceCall_SkipsPreparationAndPreservesState(string entryPoint)
    {
        using var client = new HttpClient(new RejectNetwork());
        var service = new OpenAIService("offline", client);
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "original question"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "original answer"));
        var history = service.ActivateChat.Messages.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

#pragma warning disable CS0618 // The supported compatibility agent must propagate cancellation too.
        Task SendAsync() => entryPoint switch
        {
            // An invalid model/path would fail before any network if preparation incorrectly ran.
            "quick" => AIService.QuickAskAsync("offline", "question", "invalid-model", cancellation.Token),
            "quick-image" => AIService.QuickAskWithImageAsync("offline", "question", "missing.png", "invalid-model", cancellation.Token),
            "image" => service.GetCompletionWithImageAsync("question", "missing.png", cancellation.Token),
            "image-url" => service.GetCompletionWithImageUrlAsync("question", "invalid-url", cancellation.Token),
            "once-image" => service.AskOnceWithImageAsync("question", "missing.png", cancellation.Token),
            "retry" => service.RetryLastMessageAsync(cancellation.Token),
            "context" => service.GetCompletionWithContextAsync("question", cancellationToken: cancellation.Token),
            "without-functions" => service.AskWithoutFunctionsAsync("question", cancellation.Token),
            "agent" => service.RunAgentAsync("question", cancellationToken: cancellation.Token),
            _ => throw new AssertFailedException("Unexpected test entry point.")
        };
#pragma warning restore CS0618

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(SendAsync);
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        CollectionAssert.AreEqual(history, service.ActivateChat.Messages.ToArray());
        Assert.IsFalse(service.FunctionsDisabled);
        Assert.IsFalse(service.StatelessMode);
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("A pre-cancelled call must not reach HTTP.");
    }
}
