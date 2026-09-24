using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Services.Perplexity;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mythosia.AI.Tests.Perplexity;

[TestClass]
[TestCategory("Live")]
[TestCategory("Perplexity")]
[TestCategory("PerplexityPlatform")]
[DoNotParallelize]
public class PerplexityPlatformLiveTests
{
    // Saved profiles, custom skills and connected accounts require user-owned resource IDs.
    // They have a separate opt-in PerplexityResourceLiveTests suite and runner.
    [TestMethod]
    public async Task Background_SubmitPollAndRetrievePreserveSnapshotWithoutChatMutation()
    {
        await using var probe = await PlatformProbe.CreateAsync("background");
        var expected = "BG_" + Guid.NewGuid().ToString("N");
        probe.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Earlier synthetic context."));
        var before = probe.Service.ActivateChat.Messages.ToArray();
        var job = await probe.SubmitAsync("Return only this exact identifier: " + expected);
        Assert.IsFalse(string.IsNullOrWhiteSpace(job.Id));
        var completed = await job.WaitForCompletionAsync(TimeSpan.FromSeconds(2), probe.Token);
        AssertCompleted(completed);
        Assert.IsTrue(completed.Text.Trim() == expected, "The background result must preserve the requested identifier.");
        var retrieved = await probe.Service.GetAgentResponseAsync(job.Id, probe.Token);
        Assert.AreEqual(job.Id, retrieved.Id);
        Assert.IsTrue(completed.Text == retrieved.Text, "Polling and retrieval must return identical final text.");
        CollectionAssert.AreEqual(before, probe.Service.ActivateChat.Messages.ToArray());
        Assert.AreEqual(job.Id, probe.Service.LastResponseId);
        var body = probe.Submissions.Single().Body!;
        Assert.IsTrue(body["background"]!.GetValue<bool>());
        Assert.IsTrue(body["stream"]!.GetValue<bool>());
        Assert.AreEqual(2, body["input"]!.AsArray().Count);
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task Background_SseReconnectUsesSavedCursorAndPreservesFullText()
    {
        await using var probe = await PlatformProbe.CreateAsync("replay");
        var expected = "CURSOR_" + Guid.NewGuid().ToString("N");
        var job = await probe.SubmitAsync("Return only this exact identifier: " + expected);
        var first = new List<PerplexityAgentEvent>();
        await foreach (var item in job.StreamAsync(cancellationToken: probe.Token))
        {
            first.Add(item);
            // Consume real text from the first GET, beyond the buffered submission snapshot.
            if (item.SequenceNumber.HasValue && item.TextDelta?.Length > 0 && item.Response?.IsTerminal != true) break;
        }
        Assert.IsNotEmpty(first);
        var cursor = first.Last().SequenceNumber;
        Assert.IsNotNull(cursor, "A durable stream must expose a sequence cursor.");
        Assert.IsFalse(first.Last().Response?.IsTerminal == true, "Reconnect must start before observing the terminal event.");
        Assert.AreEqual(cursor, job.LastSequenceNumber);
        var resumed = probe.Service.ResumeBackgroundRun(job.Id);
        var rest = new List<PerplexityAgentEvent>();
        await foreach (var item in resumed.StreamAsync(cursor, probe.Token)) rest.Add(item);
        Assert.IsNotEmpty(rest);
        Assert.IsTrue(rest.Where(item => item.SequenceNumber.HasValue).All(item => item.SequenceNumber > cursor));
        var sequences = first.Concat(rest).Where(item => item.SequenceNumber.HasValue).Select(item => item.SequenceNumber!.Value).ToArray();
        Assert.AreEqual(sequences.Length, sequences.Distinct().Count());
        Assert.IsTrue(sequences.Zip(sequences.Skip(1), (left, right) => left < right).All(value => value));
        var completed = resumed.LastResponse!;
        AssertCompleted(completed);
        Assert.IsTrue(completed.Text.Trim() == expected, "The resumed response must preserve its output.");
        Assert.IsTrue(string.Concat(first.Concat(rest).Select(item => item.TextDelta)) == completed.Text,
            "Joining both real stream segments must equal the terminal provider text.");
        Assert.IsTrue(probe.Requests.Any(request => request.StartingAfter == cursor));
        await job.GetResponseAsync(probe.Token);
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task Background_ImmediateCancelReachesCancelledState()
    {
        await using var probe = await PlatformProbe.CreateAsync("cancel");
        probe.Service.AgentOptions.Tools = [PerplexityHostedTools.Sandbox()];
        var job = await probe.SubmitAsync(
            "Use the sandbox once to run Python that sleeps for 45 seconds and then prints DONE. Do not use the network or create files.");
        Assert.IsTrue(job.LastResponse!.Status is "queued" or "in_progress",
            "The cancellation probe requires an active job; a completed job does not prove cancellation.");
        var cancelled = await job.CancelAsync(probe.Token);
        Assert.AreEqual("cancelled", cancelled.Status);
        Assert.IsTrue(probe.Requests.Any(request => request.Operation == "cancel" && request.Method == "POST"),
            "A real server cancellation request must have been sent.");
        Assert.AreEqual("cancelled", (await job.GetResponseAsync(probe.Token)).Status);
        Assert.HasCount(0, probe.Service.ActivateChat.Messages);
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task StoredContinuation_RecallsNonceUsingOnlyNewInput()
    {
        await using var probe = await PlatformProbe.CreateAsync("continuation");
        probe.Service.StatelessMode = true;
        probe.Service.AgentOptions.Store = true;
        var nonce = "MEMORY_" + Guid.NewGuid().ToString("N");
        await probe.Service.GetCompletionAsync("Remember the exact synthetic reference " + nonce + ". Reply only ACK.").WaitAsync(probe.Token);
        var firstId = probe.Service.LastResponseId;
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstId));
        var stored = await probe.Service.GetAgentResponseAsync(firstId!, probe.Token);
        AssertCompleted(stored);
        probe.Service.AgentOptions.PreviousResponseId = firstId;
        var answer = await probe.Service.GetCompletionAsync("Return only the exact synthetic reference I gave in the preceding response.").WaitAsync(probe.Token);
        Assert.IsTrue(answer.Trim() == nonce, "Server continuation must recall the reference without client history replay.");
        Assert.HasCount(2, probe.Submissions);
        var request = probe.Submissions[1].Body!;
        Assert.AreEqual(firstId, request["previous_response_id"]!.GetValue<string>());
        Assert.HasCount(1, request["input"]!.AsArray());
        Assert.IsFalse(request["input"]!.ToJsonString().Contains(nonce, StringComparison.Ordinal));
        Assert.HasCount(0, probe.Service.ActivateChat.Messages);
        Assert.IsTrue(answer == probe.Submissions[1].Response()!["output"]!.AsArray().OfType<JsonObject>()
            .Where(item => item["type"]?.GetValue<string>() == "message")
            .SelectMany(item => item["content"]!.AsArray()).Select(item => item!["text"]?.GetValue<string>()).Aggregate("", (left, right) => left + right));
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task ModelList_SelectsOneOfTheValidConfiguredModels()
    {
        await using var probe = await PlatformProbe.CreateAsync("model-list");
        string[] models = ["openai/gpt-5.6-luna", "perplexity/sonar"];
        probe.Service.AgentOptions.Models = models;
        var answer = await probe.Service.GetCompletionAsync("Reply with only MODEL_LIST_OK.").WaitAsync(probe.Token);
        Assert.IsTrue(answer.Trim() == "MODEL_LIST_OK");
        var request = probe.Submissions.Single();
        Assert.IsFalse(request.Body!.ContainsKey("model"));
        CollectionAssert.AreEqual(models, request.Body["models"]!.AsArray().Select(model => model!.GetValue<string>()).ToArray());
        CollectionAssert.Contains(models, request.Response()!["model"]!.GetValue<string>());
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task FetchUrl_ReturnsExtractedContentForTheExactRequestedUrl()
    {
        await using var probe = await PlatformProbe.CreateAsync("fetch");
        const string url = "https://docs.perplexity.ai/docs/agent-api/tools/fetch-url-content";
        var tool = PerplexityHostedTools.FetchUrl();
        tool.Parameters["max_urls"] = 1;
        probe.Service.AgentOptions.Tools = [tool];
        var answer = await probe.Service.GetCompletionAsync(
            "Use fetch_url to read exactly " + url + " and state the documented maximum allowed max_urls value. Do not search or fetch other URLs.").WaitAsync(probe.Token);
        Assert.IsFalse(string.IsNullOrWhiteSpace(answer));
        var response = probe.Submissions.Single().Response()!;
        var contents = Output(response, "fetch_url_results").SelectMany(item => item["contents"]!.AsArray()).ToArray();
        Assert.IsTrue(contents.Any(item => item!["url"]?.GetValue<string>() == url &&
            item["snippet"]?.GetValue<string>()?.Contains("max_urls", StringComparison.Ordinal) == true),
            "A successful exact-URL extraction must appear in the provider tool trace.");
        AssertToolInvocation(response, "fetch_url");
        probe.AssertTransport();
    }

    [TestMethod]
    [DataRow("finance")]
    [DataRow("people")]
    public async Task HostedSearch_ReturnsActualToolResults(string kind)
    {
        await using var probe = await PlatformProbe.CreateAsync(kind);
        probe.Service.AgentOptions.MaxSteps = 4;
        if (kind == "finance")
            probe.Service.AgentOptions.Tools = [PerplexityHostedTools.FinanceSearch()];
        else
        {
            var people = PerplexityHostedTools.PeopleSearch();
            people.Parameters["max_tokens"] = 2000;
            people.Parameters["max_tokens_per_page"] = 500;
            probe.Service.AgentOptions.Tools = [people];
        }
        var prompt = kind == "finance"
            ? "Use finance_search to retrieve Microsoft's latest available stock quote. Report the symbol and price in one sentence; no investment advice."
            : "Use people_search to find the public professional profile of Notion cofounder Ivan Zhao. Give only his name and company; do not seek personal contact information.";
        var answer = await probe.Service.GetCompletionAsync(prompt).WaitAsync(probe.Token);
        Assert.IsFalse(string.IsNullOrWhiteSpace(answer));
        var response = probe.Submissions.Single().Response()!;
        var results = Output(response, kind == "finance" ? "finance_results" : "people_search_results")
            .SelectMany(item => item["results"]!.AsArray()).ToArray();
        Assert.IsNotEmpty(results, "The provider must return actual hosted search results.");
        if (kind == "finance")
            Assert.IsTrue(results.Any(item => !string.IsNullOrWhiteSpace(item!["content"]?.GetValue<string>())));
        else
            Assert.IsTrue(results.Any(item => Uri.TryCreate(item!["url"]?.GetValue<string>(), UriKind.Absolute, out _)));
        AssertToolInvocation(response, kind == "finance" ? "finance_search" : "search_people");
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task PublicMcp_ExecutesOnlyTheReadOnlyDeepWikiAllowlist()
    {
        await using var probe = await PlatformProbe.CreateAsync("mcp");
        probe.Service.AgentOptions.Tools = [PerplexityHostedTools.Mcp("deepwiki", new Uri("https://mcp.deepwiki.com/mcp"), ["ask_wiki_question"])];
        var answer = await probe.Service.GetCompletionAsync(
            "Use DeepWiki ask_wiki_question exactly once to ask which license the public fastapi/fastapi repository uses. Report only the license name.").WaitAsync(probe.Token);
        Assert.IsTrue(answer.Contains("MIT", StringComparison.OrdinalIgnoreCase));
        var response = probe.Submissions.Single().Response()!;
        var calls = Output(response, "mcp_call").ToArray();
        Assert.IsNotEmpty(calls);
        foreach (var call in calls)
        {
            Assert.AreEqual("deepwiki", call["server_label"]!.GetValue<string>());
            Assert.AreEqual("ask_wiki_question", call["name"]!.GetValue<string>());
            Assert.IsNull(call["error"]);
            Assert.IsFalse(string.IsNullOrWhiteSpace(call["output"]?.GetValue<string>()));
        }
        Assert.AreEqual("ask_wiki_question", probe.Submissions.Single().Body!["tools"]![0]!["allowed_tools"]![0]!.GetValue<string>());
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task InlineSkill_IsLoadedAndItsPrivateInstructionRunsInSandbox()
    {
        await using var probe = await PlatformProbe.CreateAsync("inline-skill");
        var marker = "SKILL_" + Guid.NewGuid().ToString("N");
        probe.Service.AgentOptions.MaxSteps = 6;
        probe.Service.AgentOptions.Skills = [new PerplexitySkill
        {
            Type = PerplexitySkillType.Inline, Name = "synthetic-check",
            Description = "Load this skill whenever the user requests the synthetic verification procedure.",
            Instructions = "For the synthetic verification procedure, execute Python in the sandbox to print the exact string " + marker +
                ". Do not use a network, install packages or create files. Reply with only that exact string after observing stdout."
        }];
        var answer = await probe.Service.GetCompletionAsync(
            "Load synthetic-check and perform its synthetic verification procedure. The full procedure is available only in that skill.").WaitAsync(probe.Token);
        Assert.IsTrue(answer.Trim() == marker, "The hidden skill instruction must control the final answer.");
        var response = probe.Submissions.Single().Response()!;
        var executions = SuccessfulSandboxExecutions(response);
        Assert.IsTrue(executions.Any(item => item["stdout"]?.GetValue<string>()?.Contains(marker, StringComparison.Ordinal) == true),
            "The marker must be observed in an actual successful sandbox execution.");
        Assert.IsFalse(probe.Submissions.Single().Body!["input"]!.ToJsonString().Contains(marker, StringComparison.Ordinal));
        AssertToolInvocation(response, "sandbox");
        probe.AssertTransport();
    }

    [TestMethod]
    public async Task BuiltinXlsxSkill_GeneratesSharesAndDownloadsAValidWorkbook()
    {
        await using var probe = await PlatformProbe.CreateAsync("xlsx");
        probe.Service.MaxTokens = 4096;
        probe.Service.AgentOptions.MaxSteps = 10;
        probe.Service.AgentOptions.Skills = [new PerplexitySkill { Type = PerplexitySkillType.Builtin, Name = "office/xlsx" }];
        var job = await probe.SubmitAsync(
            "Use the office/xlsx skill to create and share a minimal workbook named smoke.xlsx. Use one sheet named Data containing exactly A1=Item, B1=Count, A2=apple, B2=2. Do not add charts or extra data. Validate the workbook and deliver the file with share_file. Do not access external websites.");
        var completed = await job.WaitForCompletionAsync(TimeSpan.FromSeconds(3), probe.Token);
        AssertCompleted(completed);
        var native = JsonNode.Parse(completed.OutputJson)!.AsArray();
        Assert.IsTrue(native.Any(item => item?["type"]?.GetValue<string>() == "share_file"),
            "The provider must explicitly share the generated artifact.");
        var files = await job.ListFilesAsync(probe.Token);
        var file = files.Single(item => item.FileName.Equals("smoke.xlsx", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(file.Bytes > 0);
        var bytes = await probe.Service.GetResponseFileContentAsync(job.Id, file.Id, probe.Token);
        Assert.AreEqual(file.Bytes, bytes.LongLength);
        using var memory = new MemoryStream(bytes);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.IsNotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.IsNotNull(archive.GetEntry("xl/workbook.xml"));
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.IsNotNull(sheet);
        var strings = archive.GetEntry("xl/sharedStrings.xml");
        var shared = strings == null ? [] : ReadXml(strings).Descendants().Where(node => node.Name.LocalName == "si")
            .Select(node => string.Concat(node.Descendants().Where(child => child.Name.LocalName == "t").Select(child => child.Value))).ToArray();
        var cells = ReadXml(sheet).Descendants().Where(node => node.Name.LocalName == "c").ToDictionary(
            node => node.Attribute("r")!.Value,
            node => node.Attribute("t")?.Value == "s"
                ? shared[int.Parse(node.Elements().Single(child => child.Name.LocalName == "v").Value, System.Globalization.CultureInfo.InvariantCulture)]
                : string.Concat(node.Descendants().Where(child => child.Name.LocalName is "v" or "t").Select(child => child.Value)));
        Assert.IsTrue(cells.GetValueOrDefault("A1") == "Item" && cells.GetValueOrDefault("B1") == "Count" &&
            cells.GetValueOrDefault("A2") == "apple" && cells.GetValueOrDefault("B2") == "2",
            "The downloaded OOXML must contain the requested cells.");
        Assert.AreEqual(2, ReadXml(sheet).Descendants().Count(node => node.Name.LocalName == "row"));
        Assert.IsTrue(probe.Requests.Any(request => request.Operation == "files"));
        Assert.IsTrue(probe.Requests.Any(request => request.Operation == "file-content" && request.Capture.Length == bytes.Length));
        probe.AssertTransport();
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void AssertCompleted(PerplexityAgentResponse response)
    {
        Assert.AreEqual("completed", response.Status);
        Assert.IsTrue(response.IsTerminal);
        Assert.IsNull(response.Error);
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.Id));
        Assert.IsNotNull(response.Usage);
    }

    private static IEnumerable<JsonObject> Output(JsonObject response, string type)
        => response["output"]!.AsArray().OfType<JsonObject>().Where(item => item["type"]?.GetValue<string>() == type);

    private static JsonObject[] SuccessfulSandboxExecutions(JsonObject response)
    {
        var calls = Output(response, "sandbox_results").ToArray();
        Assert.IsNotEmpty(calls, "A sandbox result must prove that code actually ran.");
        foreach (var call in calls)
        {
            Assert.AreEqual("completed", call["status"]!.GetValue<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(call["container_id"]?.GetValue<string>()));
        }
        var executions = calls.SelectMany(call => call["results"]!.AsArray().OfType<JsonObject>()).ToArray();
        Assert.IsNotEmpty(executions);
        Assert.IsTrue(executions.All(item => item["status"]?.GetValue<string>() == "completed" && item["exit_code"]?.GetValue<int>() == 0));
        return executions;
    }

    private static void AssertToolInvocation(JsonObject response, string name)
        => Assert.IsTrue(response["usage"]?["tool_calls_details"]?[name]?["invocation"]?.GetValue<int>() > 0,
            "Provider usage must record the hosted tool invocation.");

    internal sealed class PlatformProbe : IAsyncDisposable
    {
        private readonly CaptureHandler _handler;
        private readonly HttpClient _http;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromMinutes(8));
        private readonly List<PerplexityBackgroundRun> _jobs = [];
        private readonly string _scenario;
        public PerplexityService Service { get; }
        public CancellationToken Token => _timeout.Token;
        public IReadOnlyList<Record> Requests => _handler.Records;
        public IReadOnlyList<Record> Submissions => Requests.Where(record => record.Operation == "submit").ToArray();

        private PlatformProbe(string key, string scenario)
        {
            _scenario = scenario;
            _handler = new CaptureHandler();
            _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(8) };
            Service = new PerplexityService(key, _http) { MaxTokens = 2048, StructuredOutputMaxRetries = 0 };
            Service.ChangeModel("openai/gpt-5.6-luna");
            Service.DefaultPolicy.TimeoutSeconds = 420;
            Service.AgentOptions.DisableWebSearch = true;
            Service.AgentOptions.Store = true;
            Service.AgentOptions.MaxSteps = 4;
        }

        public static async Task<PlatformProbe> CreateAsync(string scenario)
            => new(await LiveTestSecrets.GetAsync("sonar-secret2"), scenario);

        public async Task<PerplexityBackgroundRun> SubmitAsync(string prompt)
        {
            var job = await Service.StartBackgroundAsync(prompt, cancellationToken: Token);
            _jobs.Add(job);
            return job;
        }

        public void AssertTransport()
        {
            Assert.IsNotEmpty(Requests);
            foreach (var request in Requests)
            {
                Assert.IsTrue(request.SecureTransport);
                Assert.IsTrue(request.StatusCode is >= 200 and < 300 || IsRecoveredThrottle(request),
                    "Only a throttled GET or cancellation followed by the same operation's successful retry may recover.");
                if (request.Operation == "submit")
                {
                    Assert.IsTrue(request.Body!["max_output_tokens"]!.GetValue<int>() is > 0 and <= 4096);
                    Assert.IsFalse(request.Body.ContainsKey("messages"));
                }
            }
        }

        private bool IsRecoveredThrottle(Record request)
        {
            if (request.StatusCode != 429 || string.IsNullOrEmpty(request.RequestResponseId) ||
                !(request.Method == "GET" || request.Method == "POST" && request.Operation == "cancel")) return false;
            return Requests.SkipWhile(candidate => !ReferenceEquals(candidate, request)).Skip(1).Any(candidate =>
                candidate.RequestResponseId == request.RequestResponseId && candidate.Operation == request.Operation &&
                candidate.Method == request.Method && candidate.StartingAfter == request.StartingAfter &&
                candidate.StatusCode is >= 200 and < 300);
        }

        public async ValueTask DisposeAsync()
        {
            var cleanupFailed = false;
            foreach (var job in _jobs.Where(job => job.LastResponse?.IsTerminal != true))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await job.CancelAsync(timeout.Token); }
                catch { cleanupFailed = true; }
            }
            _http.CancelPendingRequests();
            foreach (var request in Requests)
            {
                JsonObject? response = null;
                try { response = request.Response(); } catch (JsonException) { }
                var error = request.StatusCode >= 400 ? response?["error"] as JsonObject ?? response : null;
                var message = TextField(error, "message") ?? TextField(error, "detail") ?? TextField(response, "error");
                Console.WriteLine("LIVE_PERPLEXITY_PLATFORM_REQUEST " + JsonSerializer.Serialize(new
                {
                    scenario = _scenario, request.Operation, request.Method, request.StatusCode, request.StartingAfter,
                    request.RequestResponseId, request.RetryAfter, recovered429 = IsRecoveredThrottle(request),
                    errorCode = SafeErrorToken(TextField(error, "code")), errorType = SafeErrorToken(TextField(error, "type")),
                    errorMessage = request.StatusCode >= 400 ? SafeErrorMessage(message) : null,
                    model = request.Body?["model"]?.GetValue<string>(), models = request.Body?["models"]?.DeepClone(),
                    background = request.Body?["background"]?.GetValue<bool>(), maxOutputTokens = request.Body?["max_output_tokens"]?.GetValue<int>(),
                    previousResponseId = request.Body?["previous_response_id"]?.GetValue<string>(),
                    tools = request.Body?["tools"]?.AsArray().Select(tool => tool?["type"]?.GetValue<string>()).ToArray(),
                    skillTypes = request.Body?["skills"]?.AsArray().Select(skill => skill?["type"]?.GetValue<string>()).ToArray(),
                    responseId = response?["id"]?.GetValue<string>(), status = response?["status"]?.GetValue<string>(),
                    outputTypes = response?["output"]?.AsArray().Select(item => item?["type"]?.GetValue<string>()).ToArray(),
                    usage = response?["usage"]?.DeepClone(), bytes = request.Capture.Length, cleanupFailed
                }));
                request.Capture.Dispose();
            }
            _timeout.Dispose();
            _http.Dispose();
            Assert.IsFalse(cleanupFailed, "A known background job could not be cleaned up; inspect its logged response ID.");
        }

        private static string? TextField(JsonObject? value, string property)
            => value?[property] is JsonValue child && child.TryGetValue<string>(out var text) ? text : null;

        private static string? SafeErrorToken(string? value)
            => value == null ? null : Regex.Replace(value, "[^a-zA-Z0-9_.:-]", "")[..Math.Min(80, Regex.Replace(value, "[^a-zA-Z0-9_.:-]", "").Length)];

        private static string? SafeErrorMessage(string? value)
        {
            if (value == null) return null;
            if (!Regex.IsMatch(value, "rate|limit|quota|capacity|model|unsupported|invalid|validation|permission|denied|not found|unavailable|too many|retry|credit|budget|concurrent", RegexOptions.IgnoreCase))
                return "[Unclassified provider message withheld]";
            var safe = Regex.Replace(value, "(?i)bearer\\s+\\S+|(?:sk|pplx)-[a-zA-Z0-9_-]+|data:[^\\s]+", "[redacted]");
            safe = Regex.Replace(safe, "\"[^\"]*\"|'[^']*'|`[^`]*`", "[quoted value]");
            safe = Regex.Replace(safe, "[\\r\\n\\t]+", " ");
            return safe[..Math.Min(safe.Length, 384)];
        }
    }

    internal sealed class Record
    {
        public required string Operation { get; init; }
        public required string Method { get; init; }
        public JsonObject? Body { get; init; }
        public bool SecureTransport { get; init; }
        public long? StartingAfter { get; init; }
        public string? RequestResponseId { get; init; }
        public string? RetryAfter { get; set; }
        public int StatusCode { get; set; }
        public MemoryStream Capture { get; } = new();
        public JsonObject? Response()
        {
            if (Operation == "file-content" || Capture.Length == 0) return null;
            var text = Encoding.UTF8.GetString(Capture.ToArray());
            if (text.TrimStart().StartsWith("{", StringComparison.Ordinal)) return JsonNode.Parse(text) as JsonObject;
            return text.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line[5..].Trim()).Where(line => line.Length > 0 && line != "[DONE]")
                .Select(line => JsonNode.Parse(line)).OfType<JsonObject>()
                .LastOrDefault(item => item["response"] is JsonObject)?["response"]?.AsObject();
        }
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        private static readonly SemaphoreSlim RateGate = new(1, 1);
        private static DateTimeOffset _nextRequestTime;
        public List<Record> Records { get; } = [];
        public CaptureHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await RateGate.WaitAsync(cancellationToken);
            try
            {
                var delay = _nextRequestTime - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            var uri = request.RequestUri!;
            var operation = uri.AbsolutePath == "/v1/agent" ? "submit" :
                uri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal) ? "cancel" :
                uri.AbsolutePath.EndsWith("/content", StringComparison.Ordinal) ? "file-content" :
                uri.AbsolutePath.EndsWith("/files", StringComparison.Ordinal) ? "files" :
                uri.Query.Contains("stream=true", StringComparison.Ordinal) ? "replay" : "get";
            var cursor = uri.Query.TrimStart('?').Split('&').FirstOrDefault(part => part.StartsWith("starting_after=", StringComparison.Ordinal));
            var record = new Record
            {
                Operation = operation, Method = request.Method.Method,
                RequestResponseId = operation == "submit" ? null : uri.AbsolutePath.Split('/')[3],
                Body = request.Content == null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject(),
                SecureTransport = uri.Scheme == "https" && uri.Host == "api.perplexity.ai" &&
                    request.Headers.Authorization?.Scheme == "Bearer" && !string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter) &&
                    !uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase),
                StartingAfter = cursor == null ? null : long.Parse(cursor["starting_after=".Length..], System.Globalization.CultureInfo.InvariantCulture)
            };
            Records.Add(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.StatusCode = (int)response.StatusCode;
            record.RetryAfter = response.Headers.RetryAfter?.ToString();
            response.Content = new RecordingContent(response.Content, record.Capture);
            return response;
            }
            finally
            {
                // Space requests after response headers too: a long submit can finish well
                // after its original start time, immediately before the next test submits.
                _nextRequestTime = DateTimeOffset.UtcNow.AddMilliseconds(1500);
                RateGate.Release();
            }
        }
    }

    private sealed class RecordingContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly MemoryStream _capture;
        public RecordingContent(HttpContent inner, MemoryStream capture)
        {
            _inner = inner;
            _capture = capture;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task<Stream> CreateContentReadStreamAsync() => new RecordingStream(await _inner.ReadAsStreamAsync(), _capture);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var source = new RecordingStream(await _inner.ReadAsStreamAsync(), _capture);
            await source.CopyToAsync(stream);
        }
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class RecordingStream(Stream inner, MemoryStream capture) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count); capture.Write(buffer, offset, read); return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer, offset, count, cancellationToken); capture.Write(buffer, offset, read); return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken); capture.Write(buffer.Span[..read]); return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
