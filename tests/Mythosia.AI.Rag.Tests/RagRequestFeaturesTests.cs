using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Rag;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Rag.Tests;

[TestClass]
public class RagRequestFeaturesTests
{
    [TestMethod]
    public async Task FeaturesReachAnswerButNotInternalQueryRewrite_AndAreConsumed()
    {
        var service = new RagFeatureService();
        var rag = service.WithRag(builder => builder.AddText("Shipping takes three days.", id: "shipping")
            .UseLocalEmbedding(64).WithQueryRewriter());
        Assert.AreSame(rag, rag.WithReasoning(ReasoningLevel.High).WithWebSearch());

        await rag.GetCompletionAsync("Shipping policy?");
        Assert.AreEqual(2, service.Requests.Count);
        Assert.IsTrue(service.Requests[0].IsEmpty, "Query rewriting must not inherit answer tools.");
        Assert.AreEqual(ReasoningLevel.High, service.Requests[1].Reasoning!.Level);
        Assert.IsNotNull(service.Requests[1].WebSearch);
        Assert.AreEqual("source", rag.LastCitations.Single().Title);
        Assert.AreEqual("original", service.LastReceivedMessage!.Metadata!["feature_anchor"]);
        StringAssert.Contains(service.LastReceivedPrompt!, "Shipping policy?");

        await rag.GetCompletionAsync("Next question?");
        Assert.IsTrue(service.Requests.Last().IsEmpty);
    }

    [TestMethod]
    public async Task RunForwardsHostedStoreAndKeepsCitationsWithoutAnOutputReader()
    {
        var service = new RagFeatureService();
        var rag = service.WithRag(builder => builder.AddText("LOCAL_CONTEXT", id: "local").UseLocalEmbedding(64))
            .WithFileSearch(new FileSearchStore("OpenAI", "vs_test"));
        await using var run = await rag.StartRunAsync("Question?", streamOptions: StreamOptions.TextOnlyOptions);
        Assert.AreEqual("answer", (await run.Result).Text);
        Assert.AreEqual("vs_test", service.Requests.Single().FileSearch!.Stores.Single().Id);
        Assert.AreEqual("source", run.Citations.Single().Title);
        Assert.AreEqual("original", service.ObservedMetadata!["feature_anchor"]);
        StringAssert.Contains(service.ObservedPrompt!, "LOCAL_CONTEXT");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedFeatureRejectsBeforeRagQueryRewriting(bool useRun)
    {
        var service = new RagFeatureService { SupportsFeatures = false };
        var rewriter = new CountingRewriter();
        var rag = service.WithRag(builder => builder.AddText("unused", id: "unused")
            .UseLocalEmbedding(64).WithQueryRewriter(rewriter)).WithWebSearch();
        if (useRun)
            await Assert.ThrowsAsync<NotSupportedException>(async () => await rag.StartRunAsync("question"));
        else
            await Assert.ThrowsAsync<NotSupportedException>(() => rag.GetCompletionAsync("question"));
        Assert.AreEqual(0, rewriter.Calls);
        Assert.AreEqual(0, service.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);

        service.SupportsFeatures = true;
        await service.GetCompletionAsync("unrelated");
        Assert.IsTrue(service.Requests.Single().IsEmpty, "A rejected RAG request must consume its feature options.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetrievalFailureConsumesFeaturesBeforeTheNextRequest(bool useRun)
    {
        var service = new RagFeatureService();
        var rag = service.WithRag(builder => builder.AddText("unused", id: "unused").UseLocalEmbedding(64)
            .WithQueryRewriter(new FailingRewriter())).WithWebSearch();
        if (useRun)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await rag.StartRunAsync("question"));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => rag.GetCompletionAsync("question"));
        await service.GetCompletionAsync("unrelated");
        Assert.IsTrue(service.Requests.Single().IsEmpty);
    }

    private sealed class FailingRewriter : IQueryRewriter
    {
        public Task<QueryRewriteResult> RewriteAsync(string query, IReadOnlyList<ConversationTurn>? history = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Retrieval preparation failed.");
    }

    private sealed class CountingRewriter : IQueryRewriter
    {
        public int Calls;
        public Task<QueryRewriteResult> RewriteAsync(string query, IReadOnlyList<ConversationTurn>? history = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(QueryRewriteResult.Search(query));
        }
    }

    private sealed class RagFeatureService : MockAIService
    {
        public bool SupportsFeatures = true;
        public List<AIRequestFeatures> Requests { get; } = new();
        public string? ObservedPrompt;
        public Dictionary<string, object>? ObservedMetadata;

        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            if (!SupportsFeatures) base.ValidateRequestFeatures(features);
        }

        public override Task<string> GetCompletionAsync(Message message)
        {
            using var scope = BeginRequestFeaturesScope(message);
            Requests.Add(CurrentRequestFeatures.Clone());
            if (!CurrentRequestFeatures.IsEmpty)
            {
                AddAnchor();
                RecordCitation(Source());
            }
            CompletionResponse = "Shipping policy?";
            return base.GetCompletionAsync(message);
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(StreamOptions options,
            bool useFunctions, FunctionCallingPolicy policy, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            Requests.Add(CurrentRequestFeatures.Clone());
            AddAnchor();
            var outgoing = GetLatestMessages().Last();
            ObservedPrompt = outgoing.Content;
            ObservedMetadata = outgoing.Metadata;
            yield return new StreamingContent { Type = StreamingContentType.Citation, Citation = Source() };
            yield return new StreamingContent { Type = StreamingContentType.Text, Content = "answer" };
        }

        private void AddAnchor()
        {
            CurrentFeatureRequestMessage!.Metadata ??= new Dictionary<string, object>();
            CurrentFeatureRequestMessage.Metadata["feature_anchor"] = "original";
        }
        private static AICitation Source() => new() { Provider = "OpenAI", Title = "source", FileId = "file_test" };
    }
}
