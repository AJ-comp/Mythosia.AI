using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("RequestFeatures")]
public class RequestFeaturesTests
{
    [TestMethod]
    public async Task FluentOptions_RetainConcreteServiceAndMergeForOneRequest()
    {
        var service = new FeatureService();
        FeatureService same = service.WithReasoning(ReasoningLevel.Low)
            .WithWebSearch().WithFileSearch(new FileSearchStore("Test", "store_one"));
        Assert.AreSame(service, same);
        await same.GetCompletionAsync("first");
        var features = service.Snapshots.Single();
        Assert.AreEqual(ReasoningLevel.Low, features.Reasoning!.Level);
        Assert.IsNotNull(features.WebSearch);
        Assert.AreEqual("store_one", features.FileSearch!.Stores.Single().Id);

        await same.GetCompletionAsync("second");
        Assert.IsTrue(service.Snapshots[1].IsEmpty, "Options belong to one logical request, not the service defaults.");
    }

    [TestMethod]
    public async Task Configure_CopiesNestedOptionsAndListsBeforeCallerMutatesThem()
    {
        var domains = new[] { "example.org" };
        var stores = new[] { new FileSearchStore("Test", "original_store") };
        var options = new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = ReasoningLevel.Medium },
            WebSearch = new WebSearchOptions { AllowedDomains = domains },
            FileSearch = new FileSearchOptions { Stores = stores }
        };
        var service = new FeatureService();
        service.ConfigureRequestFeatures(options);
        options.Reasoning.Level = ReasoningLevel.Max;
        options.WebSearch = null;
        domains[0] = "changed.example";
        stores[0] = new FileSearchStore("Test", "changed_store");
        await service.GetCompletionAsync("snapshot");
        var captured = service.Snapshots.Single();
        Assert.AreEqual(ReasoningLevel.Medium, captured.Reasoning!.Level);
        Assert.AreEqual("example.org", captured.WebSearch!.AllowedDomains!.Single());
        Assert.AreEqual("original_store", captured.FileSearch!.Stores.Single().Id);
    }

    [TestMethod]
    public async Task ReconfiguringOneFeature_PreservesOtherPendingFeatures()
    {
        var service = new FeatureService();
        service.WithReasoning(ReasoningLevel.Low).WithWebSearch();
        service.WithReasoning(ReasoningLevel.High, CachePreservation.Required);
        await service.GetCompletionAsync("merged");
        var captured = service.Snapshots.Single();
        Assert.IsNotNull(captured.WebSearch);
        Assert.AreEqual(ReasoningLevel.High, captured.Reasoning!.Level);
        Assert.AreEqual(CachePreservation.Required, captured.Reasoning.Cache);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedCompletion_RejectsBeforeContextHistoryAndTransport_ConsumesOptions(bool messageInput)
    {
        var service = new FeatureService { RejectFeatures = true };
        int contexts = 0;
        service.WithSystemMessageProvider(() => { contexts++; return new AIRequestContext(); });
        int history = service.ActivateChat.Messages.Count;
        service.WithWebSearch();
        if (messageInput)
            await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync(new Message(ActorRole.User, "reject")));
        else
            await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync("reject"));
        Assert.AreEqual(0, contexts);
        Assert.AreEqual(history, service.ActivateChat.Messages.Count);
        Assert.AreEqual(0, service.Snapshots.Count);
        await service.GetCompletionAsync("next unrelated request");
        Assert.IsTrue(service.Snapshots.Single().IsEmpty);
    }

    [TestMethod]
    public async Task UnsupportedRun_RejectsBeforeSessionAndHistory_ThenReleasesService()
    {
        var service = new FeatureService { RejectFeatures = true };
        int history = service.ActivateChat.Messages.Count;
        service.WithWebSearch();
        await Assert.ThrowsAsync<NotSupportedException>(async () => await service.StartRunAsync("reject"));
        Assert.AreEqual(0, service.SessionStarts);
        Assert.AreEqual(history, service.ActivateChat.Messages.Count);
        await using var next = await service.StartRunAsync("next");
        Assert.AreEqual("answer", await next.Result);
        Assert.IsTrue(service.Snapshots.Single().IsEmpty);
    }

    [TestMethod]
    public async Task ProviderFailure_DoesNotLeakOptionsIntoNextRequest()
    {
        var service = new FeatureService { FailNextCompletion = true };
        service.WithWebSearch();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCompletionAsync("fail"));
        await service.GetCompletionAsync("next");
        Assert.IsNotNull(service.Snapshots[0].WebSearch);
        Assert.IsTrue(service.Snapshots[1].IsEmpty);
    }

    [TestMethod]
    public async Task TypedFormatRepair_SharesSnapshotAndLeavesLaterConfigurationPending()
    {
        var service = new FeatureService("not json", "{\"Value\":7}", "next");
        service.AfterCompletion = count =>
        {
            if (count == 1) service.WithFileSearch(new FileSearchStore("Test", "next_request_store"));
        };
        service.WithReasoning(ReasoningLevel.Low).WithWebSearch();
        var result = await service.GetCompletionAsync<Answer>("return structured data");
        Assert.AreEqual(7, result.Value);
        Assert.AreEqual(2, service.Snapshots.Count);
        Assert.IsTrue(service.Snapshots.All(f => f.WebSearch != null && f.Reasoning?.Level == ReasoningLevel.Low && f.FileSearch == null));
        Assert.AreEqual(service.Anchors[0], service.Anchors[1], "A format repair belongs to the original logical request.");
        await service.GetCompletionAsync("next");
        Assert.IsNull(service.Snapshots[2].Reasoning);
        Assert.IsNull(service.Snapshots[2].WebSearch);
        Assert.AreEqual("next_request_store", service.Snapshots[2].FileSearch!.Stores.Single().Id);
    }

    [TestMethod]
    [DataRow(AIRequestPurpose.QueryRewrite)]
    [DataRow(AIRequestPurpose.Summarization)]
    public async Task InternalRequestProfiles_DoNotConsumeOrInheritUserFeatures(AIRequestPurpose purpose)
    {
        var service = new FeatureService();
        service.WithWebSearch().WithReasoning(ReasoningLevel.High);
        await service.GetCompletionAsync("internal helper", new AIRequestProfile { Purpose = purpose });
        Assert.IsTrue(service.Snapshots[0].IsEmpty);
        await service.GetCompletionAsync("user request");
        Assert.IsNotNull(service.Snapshots[1].WebSearch);
        Assert.AreEqual(ReasoningLevel.High, service.Snapshots[1].Reasoning!.Level);
    }

    [TestMethod]
    public async Task Run_CapturesOptionsAcrossAsyncIteratorMoves_WithoutReader()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var domains = new[] { "original.example" };
        var service = new FeatureService { StreamRelease = release.Task };
        service.WithWebSearch(new WebSearchOptions { AllowedDomains = domains });
        await using var run = await service.StartRunAsync("run");
        await service.StreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        domains[0] = "changed.example";
        release.SetResult();
        Assert.AreEqual("answer", await run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("original.example", service.Snapshots.Single().WebSearch!.AllowedDomains!.Single());
        Assert.IsTrue(service.StreamFeatureChecks.All(f => f.WebSearch?.AllowedDomains?.Single() == "original.example"));
        await service.GetCompletionAsync("next");
        Assert.IsTrue(service.Snapshots.Last().IsEmpty);
    }

    [TestMethod]
    public async Task CompletionCitations_DeduplicateExactReferencesButPreserveResponseCoordinates()
    {
        var source = Citation("response_one");
        var secondResponse = Citation("response_two");
        var secondPart = Citation("response_one");
        secondPart.ContentIndex = 1;
        var service = new FeatureService { CitationsToRecord = new[] { source, source, secondResponse, secondPart } };
        await service.GetCompletionAsync("cited answer");
        Assert.AreEqual(3, service.LastCitations.Count);
        Assert.AreEqual(3, service.GetLastCitations().Count);
        source.Title = "caller mutation";
        var snapshot = service.LastCitations;
        snapshot[0].Title = "reader mutation";
        Assert.AreEqual("Source document", service.LastCitations[0].Title);
        service.CitationsToRecord = Array.Empty<AICitation>();
        await service.GetCompletionAsync("uncited answer");
        Assert.AreEqual(0, service.LastCitations.Count);
    }

    [TestMethod]
    public async Task RunCitations_AreAvailableWithoutStreamReader_AndRemainBoundToTheirRun()
    {
        var service = new FeatureService { CitationsToRecord = new[] { Citation("first_run") } };
        await using var first = await service.StartRunAsync("first");
        await first.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("first_run", first.Citations.Single().ResponseId);
        first.Citations[0].Title = "reader mutation";
        Assert.AreEqual("Source document", first.Citations.Single().Title);
        service.CitationsToRecord = new[] { Citation("second_run") };
        await using var second = await service.StartRunAsync("second");
        await second.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("second_run", second.Citations.Single().ResponseId);
        Assert.AreEqual("first_run", first.Citations.Single().ResponseId);
        Assert.AreEqual("second_run", service.LastCitations.Single().ResponseId);
    }

    [TestMethod]
    public async Task RunCitationEvents_AndStoredCitationsShareTheSameSource()
    {
        var service = new FeatureService { CitationsToRecord = new[] { Citation("event_response") } };
        await using var run = await service.StartRunAsync("citations");
        var citations = new List<AICitation>();
        await foreach (var item in run.StreamAsync())
            if (item.Type == StreamingContentType.Citation && item.Citation != null) citations.Add(item.Citation);
        await run.Result;
        Assert.AreEqual(1, citations.Count);
        Assert.AreEqual(citations[0].ResponseId, run.Citations.Single().ResponseId);
        Assert.AreEqual(citations[0].StartIndex, run.Citations.Single().StartIndex);
    }

    [TestMethod]
    public async Task InvalidValues_FailAtConfigurationWithoutPoisoningPendingOptions()
    {
        var service = new FeatureService();
        service.WithWebSearch();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.WithReasoning((ReasoningLevel)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.WithReasoning(ReasoningLevel.Low, (CachePreservation)99));
        Assert.Throws<ArgumentException>(() => service.WithWebSearch(new WebSearchOptions { AllowedDomains = new[] { " " } }));
        Assert.Throws<ArgumentException>(() => service.WithFileSearch(Array.Empty<FileSearchStore>()));
        Assert.Throws<ArgumentException>(() => new FileSearchStore("", "store"));
        Assert.Throws<ArgumentException>(() => new FileSearchStore("Test", ""));
        service.ValidatePendingRequestFeatures();
        await service.GetCompletionAsync("still use the previously configured search");
        Assert.IsNotNull(service.Snapshots.Single().WebSearch);
        Assert.IsNull(service.Snapshots.Single().Reasoning);
    }

    [TestMethod]
    public void LegacyServiceWithoutOptionalCapability_RejectsFluentFeaturesBeforeCallingIt()
    {
        var service = new LegacyService();
        Assert.Throws<NotSupportedException>(() => service.WithReasoning(ReasoningLevel.Low));
        Assert.Throws<NotSupportedException>(() => service.WithWebSearch());
        Assert.Throws<NotSupportedException>(() => service.WithFileSearch(new FileSearchStore("Test", "store")));
        Assert.Throws<NotSupportedException>(() => service.GetLastCitations());
        Assert.AreEqual(0, service.Calls);
    }

    public sealed class Answer { public int Value { get; set; } }

    private static AICitation Citation(string response) => new()
    {
        Provider = "Test", Url = "https://example.org/source", FileId = "source_file",
        Title = "Source document", Text = "evidence", ResponseId = response,
        OutputIndex = 0, ContentIndex = 0, StartIndex = 0, EndIndex = 6
    };

    private sealed class FeatureService : AIService
    {
        private readonly Queue<string> _responses;
        public override string Provider => "Test";
        public List<AIRequestFeatures> Snapshots { get; } = new();
        public List<AIRequestFeatures> StreamFeatureChecks { get; } = new();
        public List<string?> Anchors { get; } = new();
        public IReadOnlyList<AICitation> CitationsToRecord { get; set; } = Array.Empty<AICitation>();
        public bool RejectFeatures { get; set; }
        public bool FailNextCompletion { get; set; }
        public int SessionStarts { get; private set; }
        public Action<int>? AfterCompletion { get; set; }
        public Task StreamRelease { get; set; } = Task.CompletedTask;
        public TaskCompletionSource StreamStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FeatureService(params string[] responses) : base("offline", "https://localhost/", new HttpClient())
        {
            _responses = new Queue<string>(responses);
            DefaultPolicy.TimeoutSeconds = 10;
        }

        protected override void ValidateRequestFeatures(AIRequestFeatures features)
        {
            if (RejectFeatures) base.ValidateRequestFeatures(features);
        }

        public override Task<string> GetCompletionAsync(Message message)
        {
            using var scope = BeginRequestFeaturesScope(message);
            Snapshots.Add(CurrentRequestFeatures.Clone());
            Anchors.Add(CurrentFeatureRequestMessage?.Id);
            ActivateChat.Messages.Add(message);
            if (FailNextCompletion)
            {
                FailNextCompletion = false;
                throw new InvalidOperationException("Synthetic provider failure.");
            }
            foreach (var citation in CitationsToRecord) RecordCitation(citation);
            AfterCompletion?.Invoke(Snapshots.Count);
            return Task.FromResult(_responses.Count == 0 ? "answer" : _responses.Dequeue());
        }

        protected override Task<RunSession> CreateRunSessionAsync(Message message, StreamOptions executionOptions,
            AIRequestContext? context, CancellationToken cancellationToken)
        {
            SessionStarts++;
            return base.CreateRunSessionAsync(message, executionOptions, context, cancellationToken);
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(Message message, StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Snapshots.Add(CurrentRequestFeatures.Clone());
            StreamFeatureChecks.Add(CurrentRequestFeatures.Clone());
            StreamStarted.TrySetResult();
            yield return new StreamingContent { Type = StreamingContentType.Text, Content = "ans" };
            await StreamRelease.WaitAsync(cancellationToken);
            StreamFeatureChecks.Add(CurrentRequestFeatures.Clone());
            foreach (var citation in CitationsToRecord)
            {
                RecordCitation(citation);
                yield return new StreamingContent { Type = StreamingContentType.Citation, Citation = citation.Clone() };
            }
            yield return new StreamingContent { Type = StreamingContentType.Text, Content = "wer" };
            yield return new StreamingContent { Type = StreamingContentType.Completion };
        }

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("The common feature tests must not use HTTP.");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("The common feature tests must not use HTTP.");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new FunctionCallBatch());
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }

    private sealed class LegacyService : IAIService
    {
        public int Calls { get; private set; }
        public string Model => "legacy";
        public string Provider => "Test";
        public string SystemMessage { get; set; } = string.Empty;
        public bool StatelessMode { get; set; }
        public ChatBlock ActivateChat { get; } = new();
        public Task<string> GetCompletionAsync(string prompt, AIRequestProfile? profile = null, AIRequestContext? context = null)
        { Calls++; return Task.FromResult("legacy"); }
        public Task<string> GetCompletionAsync(Message message, AIRequestProfile? profile = null, AIRequestContext? context = null)
        { Calls++; return Task.FromResult("legacy"); }
        public IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamAsync(Message message, AIRequestContext? context = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamingContent> StreamAsync(string prompt, StreamOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamingContent> StreamAsync(Message message, StreamOptions options, AIRequestContext? context = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
