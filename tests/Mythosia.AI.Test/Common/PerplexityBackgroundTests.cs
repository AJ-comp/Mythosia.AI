using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Perplexity;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class PerplexityBackgroundTests
{
    [TestMethod]
    public async Task SubmitAndPoll_PreservesHistoryAndReturnsActualTerminalSnapshot()
    {
        using var handler = new Handler(Snapshot("queued"), Snapshot("completed", "answer"));
        using var http = new HttpClient(handler);
        var service = new PerplexityService("test-key", http);
        var run = await service.UsePreset(PerplexityPreset.High).StartBackgroundAsync("research");
        Assert.AreEqual("resp_test", run.Id);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        var request = handler.Requests[0];
        Assert.IsTrue(request.Body!["background"]!.GetValue<bool>());
        Assert.IsTrue(request.Body["stream"]!.GetValue<bool>());
        Assert.AreEqual("high", request.Body["preset"]!.GetValue<string>());
        var result = await run.WaitForCompletionAsync(TimeSpan.FromMilliseconds(100));
        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual("completed", result.Status);
        Assert.AreEqual(10, result.Usage!.InputTokens);
        Assert.AreEqual("https://api.perplexity.ai/v1/agent/resp_test", handler.Requests[1].Uri);
        Assert.AreEqual("Bearer test-key", handler.Requests[1].Authorization);
        Assert.AreEqual("resp_test", service.LastResponseId);
    }

    [TestMethod]
    [DataRow("failed")]
    [DataRow("cancelled")]
    [DataRow("incomplete")]
    public async Task FailedTerminalStates_AreNeverPresentedAsCompleted(string status)
    {
        using var handler = new Handler(Snapshot(status, "partial"));
        using var http = new HttpClient(handler);
        var run = new PerplexityService("key", http).ResumeBackgroundRun("resp_test");
        var result = await run.WaitForCompletionAsync();
        Assert.IsTrue(result.IsTerminal);
        Assert.AreEqual(status, result.Status);
        Assert.AreNotEqual("completed", result.Status);
    }

    [TestMethod]
    public async Task Cancellation_CallsServerAndWaitsForCancelled()
    {
        using var handler = new Handler(Snapshot("in_progress"), "{\"response_id\":\"resp_test\",\"status\":\"cancelling\"}", Snapshot("cancelled"));
        using var http = new HttpClient(handler);
        var result = await new PerplexityService("key", http).ResumeBackgroundRun("resp_test").CancelAsync();
        Assert.AreEqual("cancelled", result.Status);
        Assert.AreEqual("POST", handler.Requests[1].Method);
        Assert.IsTrue(handler.Requests[1].Uri.EndsWith("/resp_test/cancel", StringComparison.Ordinal));
        Assert.AreEqual(3, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ResumeStream_UsesCursorAndDeduplicatesReplayedEvents()
    {
        var terminal = Snapshot("completed", "ok");
        var sse = "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":4,\"delta\":\"old\"}\n\n" +
                  "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":5,\"delta\":\"ok\"}\n\n" +
                  $"data: {{\"type\":\"response.completed\",\"sequence_number\":6,\"response\":{terminal}}}\n\n";
        using var handler = new Handler(sse);
        using var http = new HttpClient(handler);
        var run = new PerplexityService("key", http).ResumeBackgroundRun("resp_test");
        var events = new List<PerplexityAgentEvent>();
        await foreach (var item in run.StreamAsync(4)) events.Add(item);
        Assert.AreEqual("ok", string.Concat(events.Select(item => item.TextDelta)));
        Assert.AreEqual(2, events.Count);
        Assert.AreEqual(6L, run.LastSequenceNumber);
        Assert.AreEqual("completed", run.LastResponse!.Status);
        StringAssert.EndsWith(handler.Requests[0].Uri, "?stream=true&starting_after=4");
    }

    [TestMethod]
    public async Task StreamWithoutTerminal_FailsAndPreservesReconnectCursor()
    {
        using var handler = new Handler("data: {\"type\":\"response.output_text.delta\",\"sequence_number\":2,\"delta\":\"partial\"}\n\n");
        using var http = new HttpClient(handler);
        var run = new PerplexityService("key", http).ResumeBackgroundRun("resp_test");
        await Assert.ThrowsAsync<AIServiceException>(async () => { await foreach (var _ in run.StreamAsync()) { } });
        Assert.AreEqual(2L, run.LastSequenceNumber);
        Assert.IsNull(run.LastResponse);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("\"sequence_number\":-1,")]
    [DataRow("\"sequence_number\":\"3\",")]
    public async Task ResumeStream_InvalidSequenceFailsBeforeDeliveryAndPreservesLastValidCursor(string sequenceProperty)
    {
        const string prefix = "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":2,\"delta\":\"prefix \"}\n\n";
        var malformed = "data: {\"type\":\"response.output_text.delta\"," + sequenceProperty + "\"delta\":\"invalid\"}\n\n";
        var remaining = "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":3,\"delta\":\"tail\"}\n\n" +
            "data: {\"type\":\"response.completed\",\"sequence_number\":4,\"response\":" + Snapshot("completed", "prefix tail") + "}\n\n";
        using var handler = new Handler(prefix + malformed, remaining);
        using var http = new HttpClient(handler);
        var run = new PerplexityService("key", http).ResumeBackgroundRun("resp_test");
        var text = new StringBuilder();
        var error = await Assert.ThrowsAsync<AIServiceException>(async () =>
        {
            await foreach (var item in run.StreamAsync()) text.Append(item.TextDelta);
        });
        StringAssert.Contains(error.Message, "nonnegative sequence number");
        Assert.AreEqual("prefix ", text.ToString(), "An event without a usable cursor must never be delivered.");
        Assert.AreEqual(2L, run.LastSequenceNumber);
        Assert.IsNull(run.LastResponse);
        await foreach (var item in run.StreamAsync(run.LastSequenceNumber)) text.Append(item.TextDelta);
        Assert.AreEqual("prefix tail", text.ToString());
        Assert.AreEqual(4L, run.LastSequenceNumber);
        StringAssert.EndsWith(handler.Requests[1].Uri, "?stream=true&starting_after=2");
    }

    [TestMethod]
    public async Task Files_AreRetrievedFromOwningResponseAndReturnUnmodifiedBytes()
    {
        using var handler = new Handler("{\"data\":[{\"id\":\"file_test\",\"filename\":\"result.csv\",\"bytes\":5}]}", "a,b\n1");
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http);
        var files = await service.GetResponseFilesAsync("resp_test");
        Assert.AreEqual("result.csv", files.Single().FileName);
        var bytes = await service.GetResponseFileContentAsync("resp_test", files.Single().Id);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("a,b\n1"), bytes);
        StringAssert.EndsWith(handler.Requests[1].Uri, "/resp_test/files/file_test/content");
    }

    [TestMethod]
    [DataRow("../private")]
    [DataRow("resp_test?stream=true")]
    [DataRow("https://example.org")]
    public void ResumeRejectsInvalidIds(string id)
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new PerplexityService("key", http).ResumeBackgroundRun(id));
    }

    [TestMethod]
    public async Task ContinuationRequiresStatelessAndDoesNotReplayThePriorConversation()
    {
        using var handler = new Handler(Snapshot("completed", "followup"));
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http) { StatelessMode = true };
        service.AgentOptions.PreviousResponseId = "resp_parent";
        var answer = await service.GetCompletionAsync("new turn");
        Assert.AreEqual("followup", answer);
        var body = handler.Requests.Single().Body!;
        Assert.AreEqual("resp_parent", body["previous_response_id"]!.GetValue<string>());
        Assert.AreEqual(1, body["input"]!.AsArray().Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        service.StatelessMode = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCompletionAsync("bad"));
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ProfilesFallbackAndSkills_ReachTheAgentWireAndAreCopied()
    {
        using var handler = new Handler(Snapshot("queued"));
        using var http = new HttpClient(handler);
        var skill = new PerplexitySkill { Type = PerplexitySkillType.Inline, Name = "review", Description = "Review documents", Instructions = "Check accuracy." };
        var profile = new PerplexityProfile { Id = "profile_test", Version = "3" };
        var models = new[] { "openai/gpt-5.6-luna", "perplexity/sonar" };
        var service = new PerplexityService("key", http).WithPerplexityOptions(new PerplexityAgentOptions
        {
            Profile = profile, Models = models, Skills = [skill], ServiceTier = PerplexityServiceTier.Priority,
            Store = true, LanguagePreference = "ko", PromptCacheKey = "research"
        });
        skill.Instructions = "changed";
        profile.Version = "4";
        models[0] = "invalid";
        await service.StartBackgroundAsync("question");
        var body = handler.Requests.Single().Body!;
        Assert.AreEqual("3", body["profile"]!["version"]!.GetValue<string>());
        Assert.AreEqual("Check accuracy.", body["skills"]![0]!["instructions"]!.GetValue<string>());
        Assert.AreEqual("openai/gpt-5.6-luna", body["models"]![0]!.GetValue<string>());
        Assert.IsFalse(body.ContainsKey("model"));
        Assert.AreEqual("priority", body["service_tier"]!.GetValue<string>());
        Assert.AreEqual("ko", body["language_preference"]!.GetValue<string>());
        Assert.AreEqual("research", body["prompt_cache_key"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task ProfileAndPresetConflictFailsBeforeHttp()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http).UsePreset(PerplexityPreset.Fast);
        service.AgentOptions.Profile = new PerplexityProfile { Id = "profile_test" };
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartBackgroundAsync("bad"));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task MetadataIncludesHostedSearchAndTextAnnotations()
    {
        var snapshot = JsonNode.Parse(Snapshot("completed", "answer"))!;
        snapshot["output"]!.AsArray().Insert(0, JsonNode.Parse("{\"type\":\"search_results\",\"results\":[{\"url\":\"https://example.org/source\",\"title\":\"Source\",\"snippet\":\"Evidence\"}]}"));
        snapshot["output"]![1]!["content"]![0]!["annotations"] = JsonNode.Parse("[{\"type\":\"url_citation\",\"url\":\"https://example.org/source\",\"title\":\"Source\",\"start_index\":0,\"end_index\":6}]");
        using var handler = new Handler(snapshot.ToJsonString());
        using var http = new HttpClient(handler);
        var result = await new PerplexityService("key", http).GetAgentResponseAsync("resp_test");
        Assert.AreEqual(2, result.Citations.Count);
        Assert.AreEqual("Evidence", result.Citations[0].Text);
        Assert.AreEqual(0, result.Citations[1].StartIndex);
        Assert.AreEqual(1, result.Citations[1].OutputIndex);
        StringAssert.Contains(result.OutputJson, "search_results");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FetchedSourcesAndGeneratedFileCitations_AreConsistentAcrossExecutionModes(bool background)
    {
        var snapshot = JsonNode.Parse(Snapshot("completed", "answer"))!;
        snapshot["output"]!.AsArray().Insert(0, JsonNode.Parse("{\"type\":\"fetch_url_results\",\"contents\":[{\"url\":\"https://example.org/source\",\"title\":\"Fetched page\"}]}"));
        snapshot["output"]![1]!["content"]![0]!["annotations"] = JsonNode.Parse("[{\"type\":\"file_citation\",\"file_id\":\"file_result\",\"filename\":\"result.xlsx\"}]");
        using var handler = new Handler(snapshot.ToJsonString());
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http);
        IReadOnlyList<AICitation> citations;
        if (background) citations = (await service.GetAgentResponseAsync("resp_test")).Citations;
        else { await service.GetCompletionAsync("question"); citations = service.LastCitations; }
        Assert.AreEqual(2, citations.Count);
        Assert.AreEqual("https://example.org/source", citations[0].Url);
        Assert.AreEqual(0, citations[0].OutputIndex);
        Assert.AreEqual("file_result", citations[1].FileId);
        Assert.AreEqual("result.xlsx", citations[1].Title);
    }

    [TestMethod]
    public async Task CompletedSnapshotWithoutOutput_IsRejected()
    {
        using var handler = new Handler("{\"id\":\"resp_test\",\"status\":\"completed\"}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AIServiceException>(() => new PerplexityService("key", http).GetAgentResponseAsync("resp_test"));
    }

    [TestMethod]
    [DataRow("retention")]
    [DataRow("penalty")]
    [DataRow("sampling")]
    [DataRow("language")]
    [DataRow("models")]
    [DataRow("skill-name")]
    [DataRow("skill-bytes")]
    public async Task InvalidBackgroundOptions_FailBeforeAnyRequestOrHistoryMutation(string invalid)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http);
        switch (invalid)
        {
            case "retention": service.AgentOptions.Store = false; break;
            case "penalty": service.PresencePenalty = 1; break;
            case "sampling": service.Temperature = float.NaN; break;
            case "language": service.AgentOptions.LanguagePreference = "ko-KR"; break;
            case "models": service.AgentOptions.Models = ["sonar-pro"]; break;
            case "skill-name": service.AgentOptions.Skills = [new() { Type = PerplexitySkillType.Inline, Name = "Invalid", Description = "Read", Instructions = "Read the context." }]; break;
            case "skill-bytes": service.AgentOptions.Skills = [new() { Type = PerplexitySkillType.Inline, Name = "valid", Description = new string('가', 400), Instructions = "Read." }]; break;
        }
        var error = await Assert.ThrowsAsync<Exception>(() => service.StartBackgroundAsync("question"));
        Assert.IsTrue(error is ArgumentException or NotSupportedException);
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task CustomSkillAndConnectorConfiguration_IsCopiedWithoutLosingVersionOrAllowlist()
    {
        using var handler = new Handler(Snapshot("queued"));
        using var http = new HttpClient(handler);
        var allowed = new[] { "get_issue" };
        var service = new PerplexityService("key", http);
        var options = new PerplexityAgentOptions { Skills = [new() { Type = PerplexitySkillType.Custom, Id = "skill_review", Version = "7" }] };
        options.Tools.Add(PerplexityHostedTools.Connector("connector_github", "github", allowed));
        service.WithPerplexityOptions(options);
        allowed[0] = "delete_issue";
        await service.StartBackgroundAsync("question");
        var body = handler.Requests.Single().Body!;
        Assert.AreEqual("7", body["skills"]![0]!["version"]!.GetValue<string>());
        Assert.AreEqual("connector_github", body["tools"]![0]!["id"]!.GetValue<string>());
        Assert.AreEqual("get_issue", body["tools"]![0]!["allowed_tools"]![0]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ContextOverride_OnlyReplacesNewInputAndPreservesExistingAssistant(bool stateless)
    {
        using var handler = new Handler(Snapshot("queued"));
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http) { StatelessMode = stateless };
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "old"));
        service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "old-answer"));
        await service.StartBackgroundAsync("new", new AIRequestContext {
            RequestMessageOverride = new Message(ActorRole.User, "enriched"),
            AdditionalMessages = [new Message(ActorRole.System, "reference")]
        });
        var input = handler.Requests.Single().Body!["input"]!.AsArray();
        CollectionAssert.AreEqual(stateless ? new[] { "enriched", "reference" } : new[] { "old", "old-answer", "enriched", "reference" },
            input.Select(item => item!["content"]!.GetValue<string>()).ToArray());
        Assert.AreEqual("old-answer", service.ActivateChat.Messages.Last().Content);
        Assert.AreEqual(2, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task StreamingSubmission_ReturnsAfterIdBeforeBodyCompletesAndDisposesPostConnection()
    {
        var eventJson = "data: {\"type\":\"response.created\",\"sequence_number\":0,\"response\":" + Snapshot("queued") + "}\n\n";
        using var stream = new PrefixThenPendingStream(eventJson);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        using var handler = new InitialResponseHandler(content);
        using var http = new HttpClient(handler);
        var service = new PerplexityService("test-key", http);
        var job = await service.StartBackgroundAsync("synthetic task").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("resp_test", job.Id);
        Assert.AreEqual("queued", job.LastResponse!.Status);
        Assert.IsNull(job.LastSequenceNumber, "No stream event has been delivered to the caller during submission.");
        Assert.IsTrue(stream.WasDisposed, "Closing the initial local connection must not wait for the server job to finish.");
        Assert.IsFalse(stream.WaitingForMore.Task.IsCompleted);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual("POST", handler.Requests[0].Method);
        Assert.IsTrue(handler.Requests[0].Body!["background"]!.GetValue<bool>());
        Assert.IsTrue(handler.Requests[0].Body!["stream"]!.GetValue<bool>());
        Assert.IsEmpty(service.ActivateChat.Messages);
    }

    [TestMethod]
    public async Task StreamingSubmission_PreservesInitialEventsBeforeReconnecting_ExplicitCursorResumes()
    {
        const string early = "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":0,\"delta\":\"prefix \"}\n\n";
        var created = "data: {\"type\":\"response.created\",\"sequence_number\":1,\"response\":" + Snapshot("in_progress") + "}\n\n";
        const string later = "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":2,\"delta\":\"tail\"}\n\n";
        var completed = "data: {\"type\":\"response.completed\",\"sequence_number\":3,\"response\":" + Snapshot("completed", "prefix tail") + "}\n\n";
        using var initial = new StringContent(early + created, Encoding.UTF8, "text/event-stream");
        using var handler = new InitialResponseHandler(initial, early + created + later + completed, early + created + later + completed);
        using var http = new HttpClient(handler);
        var job = await new PerplexityService("key", http).StartBackgroundAsync("task");
        Assert.IsNull(job.LastSequenceNumber);
        var first = new List<PerplexityAgentEvent>();
        await foreach (var item in job.StreamAsync()) first.Add(item);
        Assert.AreEqual("prefix tail", string.Concat(first.Select(item => item.TextDelta)));
        Assert.AreEqual("prefix tail", job.LastResponse!.Text);
        CollectionAssert.AreEqual(new long?[] { 0, 1, 2, 3 }, first.Select(item => item.SequenceNumber).ToArray());
        Assert.AreEqual("https://api.perplexity.ai/v1/agent/resp_test?stream=true&starting_after=1", handler.Requests[1].Uri);
        var resumed = new List<PerplexityAgentEvent>();
        await foreach (var item in job.StreamAsync(1)) resumed.Add(item);
        Assert.AreEqual("tail", string.Concat(resumed.Select(item => item.TextDelta)));
        CollectionAssert.AreEqual(new long?[] { 2, 3 }, resumed.Select(item => item.SequenceNumber).ToArray());
        Assert.AreEqual(3L, job.LastSequenceNumber);
        StringAssert.EndsWith(handler.Requests[2].Uri, "?stream=true&starting_after=1");
        Assert.HasCount(1, handler.Requests.Where(request => request.Method == "POST"));
    }

    [TestMethod]
    [DataRow("queued")]
    [DataRow("in_progress")]
    [DataRow("completed")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    [DataRow("incomplete")]
    public async Task StreamingSubmission_PreservesInitialSnapshotStatus(string status)
    {
        // Split JSON across data lines to exercise SSE event framing, including keepalive comments.
        var data = ": keepalive\n\nevent: response.created\ndata: {\"type\":\"response.created\",\ndata: \"sequence_number\":0,\"response\":" + Snapshot(status, "partial") + "}\n\n";
        using var content = new StringContent(data, Encoding.UTF8, "text/event-stream");
        using var handler = new InitialResponseHandler(content);
        using var http = new HttpClient(handler);
        var job = await new PerplexityService("key", http).StartBackgroundAsync("task");
        Assert.AreEqual("resp_test", job.Id);
        Assert.AreEqual(status, job.LastResponse!.Status);
        Assert.AreEqual(status is "completed" or "failed" or "cancelled" or "incomplete", job.LastResponse.IsTerminal);
        Assert.IsNull(job.LastSequenceNumber);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(": keepalive\n\n")]
    [DataRow("data: [DONE]\n\n")]
    [DataRow("data: {\"type\":\"response.created\",\"response\":{\"status\":\"queued\"}}\n\n")]
    [DataRow("data: {\"type\":\"response.created\",\"response\":null}\n\n")]
    [DataRow("data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_test\",\"status\":\"queued\"}}\n\n")]
    [DataRow("data: {\"type\":\"response.created\",\"sequence_number\":-1,\"response\":{\"id\":\"resp_test\",\"status\":\"queued\"}}\n\n")]
    [DataRow("data: {\"type\":\"response.created\",\"sequence_number\":\"invalid\",\"response\":{\"id\":\"resp_test\",\"status\":\"queued\"}}\n\n")]
    [DataRow("data: {invalid-json}\n\n")]
    [DataRow("data: {\"type\":\"error\",\"message\":\"rejected\"}\n\n")]
    public async Task StreamingSubmission_RequiresAnActualResponseIdAndNeverRetries(string data)
    {
        using var content = new StringContent(data, Encoding.UTF8, "text/event-stream");
        using var handler = new InitialResponseHandler(content);
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http);
        await Assert.ThrowsAsync<AIServiceException>(() => service.StartBackgroundAsync("task"));
        Assert.HasCount(1, handler.Requests);
        Assert.IsNull(service.LastResponseId);
        Assert.IsEmpty(service.ActivateChat.Messages);
    }

    [TestMethod]
    public async Task StreamingSubmission_CancellationBeforeIdDisposesConnectionWithoutServerCancellation()
    {
        using var stream = new PrefixThenPendingStream(": keepalive\n\n");
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
        using var handler = new InitialResponseHandler(content);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var service = new PerplexityService("key", http);
        var pending = service.StartBackgroundAsync("task", cancellationToken: cancellation.Token);
        await stream.WaitingForMore.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(stream.WasDisposed);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual("POST", handler.Requests[0].Method);
        Assert.IsFalse(handler.Requests[0].Uri.EndsWith("/cancel", StringComparison.Ordinal));
        Assert.IsNull(service.LastResponseId);
    }

    [TestMethod]
    public async Task StreamingSubmission_BufferedPrefixCanBeInterruptedAndResumedWithoutLosingText()
    {
        const string prefix = "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":0,\"delta\":\"prefix \"}\n\n";
        var created = "data: {\"type\":\"response.created\",\"sequence_number\":1,\"response\":" + Snapshot("in_progress") + "}\n\n";
        var tail = "data: {\"type\":\"response.output_text.delta\",\"sequence_number\":2,\"delta\":\"tail\"}\n\n" +
            "data: {\"type\":\"response.completed\",\"sequence_number\":3,\"response\":" + Snapshot("completed", "prefix tail") + "}\n\n";
        using var content = new StringContent(prefix + created, Encoding.UTF8, "text/event-stream");
        using var handler = new InitialResponseHandler(content, tail);
        using var http = new HttpClient(handler);
        var job = await new PerplexityService("key", http).StartBackgroundAsync("task");
        var text = new StringBuilder();
        await foreach (var item in job.StreamAsync())
        {
            text.Append(item.TextDelta);
            break;
        }
        Assert.AreEqual("prefix ", text.ToString());
        Assert.AreEqual(0L, job.LastSequenceNumber);
        Assert.HasCount(1, handler.Requests, "A buffered event does not need an observation GET.");
        await foreach (var item in job.StreamAsync(job.LastSequenceNumber)) text.Append(item.TextDelta);
        Assert.AreEqual("prefix tail", text.ToString());
        Assert.AreEqual("prefix tail", job.LastResponse!.Text);
        StringAssert.EndsWith(handler.Requests[1].Uri, "?stream=true&starting_after=1");
    }

    [TestMethod]
    [DataRow("completed")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    [DataRow("incomplete")]
    public async Task StreamingSubmission_BufferedTerminalEventNeedsNoObservationRequest(string status)
    {
        var data = "data: {\"type\":\"response." + status + "\",\"sequence_number\":0,\"response\":" + Snapshot(status, "answer") + "}\n\n";
        using var content = new StringContent(data, Encoding.UTF8, "text/event-stream");
        using var handler = new InitialResponseHandler(content);
        using var http = new HttpClient(handler);
        var job = await new PerplexityService("key", http).StartBackgroundAsync("task");
        var events = new List<PerplexityAgentEvent>();
        await foreach (var item in job.StreamAsync()) events.Add(item);
        Assert.HasCount(1, events);
        Assert.AreEqual(status, events[0].Response!.Status);
        Assert.AreEqual(0L, job.LastSequenceNumber);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task StreamingSubmission_CancellationAfterBufferedEventDoesNotOpenReplayConnection()
    {
        var data = "data: {\"type\":\"response.created\",\"sequence_number\":0,\"response\":" + Snapshot("queued") + "}\n\n";
        using var content = new StringContent(data, Encoding.UTF8, "text/event-stream");
        using var handler = new InitialResponseHandler(content);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var job = await new PerplexityService("key", http).StartBackgroundAsync("task");
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in job.StreamAsync(cancellationToken: cancellation.Token)) cancellation.Cancel();
        });
        Assert.AreEqual(0L, job.LastSequenceNumber);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StreamWithoutSubmissionEvents_AlwaysSendsRequiredStartingAfterZero(bool jsonSubmission)
    {
        var complete = "data: {\"type\":\"response.completed\",\"sequence_number\":2,\"response\":" + Snapshot("completed", "answer") + "}\n\n";
        using var handler = new Handler(jsonSubmission ? [Snapshot("queued"), complete] : [complete]);
        using var http = new HttpClient(handler);
        var service = new PerplexityService("key", http);
        var job = jsonSubmission ? await service.StartBackgroundAsync("task") : service.ResumeBackgroundRun("resp_test");
        await foreach (var _ in job.StreamAsync()) { }
        StringAssert.EndsWith(handler.Requests.Last().Uri, "?stream=true&starting_after=0");
        Assert.AreEqual("answer", job.LastResponse!.Text);
    }

    [TestMethod]
    public async Task ObservationRateLimit_RetriesTheSameIdWithoutResubmittingWork()
    {
        using var handler = new RateHandler((index, _) => index == 0 ? Limited(0) : Ok(Snapshot("completed", "answer")));
        using var http = new HttpClient(handler);
        var result = await new PerplexityService("key", http).ResumeBackgroundRun("resp_test").GetResponseAsync();
        Assert.AreEqual("answer", result.Text);
        Assert.AreEqual(2, handler.Requests.Count);
        Assert.IsTrue(handler.Requests.All(item => item == "GET /v1/agent/resp_test"));
    }

    [TestMethod]
    public async Task SubmissionRateLimit_DoesNotCreateDuplicateJobs()
    {
        using var handler = new RateHandler((_, _) => Limited(0));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AIServiceException>(() => new PerplexityService("key", http).StartBackgroundAsync("question"));
        CollectionAssert.AreEqual(new[] { "POST /v1/agent" }, handler.Requests);
    }

    [TestMethod]
    public async Task RepeatedRateLimit_IsBoundedAndCancellationInterruptsCooldown()
    {
        using var handler = new RateHandler((_, _) => Limited(0));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<AIServiceException>(() => new PerplexityService("key", http).GetAgentResponseAsync("resp_test"));
        Assert.AreEqual(4, handler.Requests.Count);
        using var slow = new RateHandler((_, _) => Limited(60));
        using var slowHttp = new HttpClient(slow);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => new PerplexityService("key", slowHttp).GetAgentResponseAsync("resp_test", cancel.Token));
        Assert.AreEqual(1, slow.Requests.Count);
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static HttpResponseMessage Limited(int seconds)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{\"error\":\"rate limit\"}") };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }
    private sealed class RateHandler(Func<int, HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return Task.FromResult(send(Requests.Count - 1, request));
        }
    }

    private static string Snapshot(string status, string text = "") => JsonSerializer.Serialize(new
    {
        id = "resp_test", status, model = "perplexity/sonar",
        output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } } } },
        usage = new { input_tokens = 10, output_tokens = 2, total_tokens = 12 }
    });

    private sealed class Handler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);
        public List<(string Uri, string Method, string? Authorization, JsonObject? Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Method.Method, request.Headers.Authorization?.ToString(),
                request.Content == null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject()));
            if (_responses.Count == 0) throw new InvalidOperationException("Unexpected request.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class InitialResponseHandler(HttpContent initial, params string[] replay) : HttpMessageHandler
    {
        private readonly Queue<string> _replay = new(replay);
        public List<(string Uri, string Method, JsonObject? Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Method.Method,
                request.Content == null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject()));
            if (Requests.Count == 1) return new HttpResponseMessage(HttpStatusCode.OK) { Content = initial };
            if (_replay.Count == 0) throw new InvalidOperationException("Unexpected extra request.");
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(_replay.Dequeue(), Encoding.UTF8, "text/event-stream") };
        }
    }

    private sealed class PrefixThenPendingStream(string prefix) : Stream
    {
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes(prefix);
        private readonly CancellationTokenSource _disposed = new();
        private int _position;
        public bool WasDisposed { get; private set; }
        public TaskCompletionSource WaitingForMore { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => !WasDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < _prefix.Length)
            {
                var count = Math.Min(buffer.Length, _prefix.Length - _position);
                _prefix.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }
            WaitingForMore.TrySetResult();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);
            await Task.Delay(Timeout.Infinite, linked.Token);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        protected override void Dispose(bool disposing)
        {
            if (!WasDisposed)
            {
                WasDisposed = true;
                if (disposing) _disposed.Cancel();
            }
            base.Dispose(disposing);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
