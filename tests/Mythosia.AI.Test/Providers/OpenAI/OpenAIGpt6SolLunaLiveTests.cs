using Mythosia.AI.Builders;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using SkiaSharp;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.OpenAI;

// Paid, opt-in integration checks. Only synthetic prompts/documents/images are sent.
// Every account, protocol, or actual-tier failure remains a failure, never a skip.
[TestClass]
[TestCategory("Live")]
[TestCategory("OpenAI")]
[TestCategory("Gpt6SolLuna")]
[DoNotParallelize]
public class OpenAIGpt6SolLunaLiveTests
{
    private static readonly string[] Models = [AIModels.OpenAI.Gpt6Sol, AIModels.OpenAI.Gpt6Luna];

    public static IEnumerable<object[]> ReasoningCases() => Models.SelectMany(model =>
        new[] { ReasoningLevel.Auto, ReasoningLevel.None, ReasoningLevel.Low, ReasoningLevel.Medium,
            ReasoningLevel.High, ReasoningLevel.XHigh, ReasoningLevel.Max }.Select(level => new object[] { model, level }));

    public static IEnumerable<object[]> SpeedCases() => Models.SelectMany(model => Enum.GetValues<InferenceSpeed>()
        .SelectMany(speed => new[] { false, true }.Select(run => new object[] { model, speed, run })));

    [TestMethod]
    [DynamicData(nameof(ReasoningCases))]
    public async Task ReasoningAndSampling_RealResponsesAcceptEachLevel(string model, ReasoningLevel level)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        var request = probe.Service.CreateRequest("Check 19 + 23 = 42. Reply with exactly LEVEL_OK.")
            .WithReasoning(level).WithTemperature(0.2f).WithTopP(0.8f);
        StringAssert.Contains(await request.GetCompletionAsync(timeout.Token), "LEVEL_OK");
        var body = Assert.ContainsSingle(probe.Requests);
        Assert.AreEqual(level == ReasoningLevel.Auto ? "medium" : level.ToString().ToLowerInvariant(),
            body.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.AreEqual(level == ReasoningLevel.None, body.TryGetProperty("temperature", out _));
        Assert.AreEqual(level == ReasoningLevel.None, body.TryGetProperty("top_p", out _));
    }

    [TestMethod]
    [DynamicData(nameof(SpeedCases))]
    public async Task Speed_CompletionAndRunReportActualTier(string model, InferenceSpeed speed, bool useRun)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        var request = probe.Service.CreateRequest("Reply with exactly SPEED_OK.")
            .WithReasoning(ReasoningLevel.None).WithSpeed(speed);
        var answer = await ExecuteAsync(request, useRun, timeout.Token);
        StringAssert.Contains(answer, "SPEED_OK");
        Assert.IsTrue(probe.Service.LastProcessing.Count > 0);
        foreach (var actual in probe.Service.LastProcessing)
        {
            Assert.AreEqual(speed, actual.RequestedSpeed);
            Assert.IsNotNull(actual.AppliedSpeed, "Server must identify the actual processing tier.");
            if (speed != InferenceSpeed.ProviderDefault) Assert.AreEqual(speed, actual.AppliedSpeed.Value,
                "An observable downgrade is not proof that Fast execution succeeded.");
            Console.WriteLine($"GPT6_LIVE_SPEED model={model} run={useRun} requested={speed} actual={actual.RawAppliedMode}");
        }
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, Gpt6Reasoning.None)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, Gpt6Reasoning.Medium)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, Gpt6Reasoning.None)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, Gpt6Reasoning.Medium)]
    public async Task ProMode_KeepsSelectedModelAndEffort(string model, Gpt6Reasoning level)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        probe.Service.Gpt6ReasoningMode = Gpt6ReasoningMode.Pro;
        probe.Service.Gpt6ReasoningEffort = level;
        StringAssert.Contains(await probe.Service.CreateRequest("Reply with exactly PRO_OK.")
            .GetCompletionAsync(timeout.Token), "PRO_OK");
        var body = Assert.ContainsSingle(probe.Requests);
        Assert.AreEqual(model, body.GetProperty("model").GetString());
        Assert.AreEqual("pro", body.GetProperty("reasoning").GetProperty("mode").GetString());
        Assert.AreEqual(level.ToString().ToLowerInvariant(), body.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, false)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, true)]
    public Task AsyncTools_ContinueBeforeResultAndDeliverOriginalCall(string model, bool streaming)
        => OpenAIAsyncToolCallingLiveTests.VerifyAsync(model, allowAsync: true, expectNativeAsync: true, streaming);

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, false)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, true)]
    public async Task NoneReasoning_FunctionContinuationReturnsActualResult(string model, bool useRun)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        var token = "NONE_TOOL_" + Guid.NewGuid().ToString("N");
        int invocations = 0;
        var request = probe.Service.CreateRequest("Call read_none_token exactly once and reply with only its returned token. Never invent it.")
            .WithReasoning(ReasoningLevel.None)
            .WithFunctions(new FunctionDefinition
            {
                Name = "read_none_token", Description = "Get the required verification token. Takes no arguments.",
                Handler = _ => { Interlocked.Increment(ref invocations); return Task.FromResult(token); }
            });
        StringAssert.Contains(await ExecuteAsync(request, useRun, timeout.Token), token);
        Assert.AreEqual(1, invocations);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public Task Steering_UpdatesActualRunningResponse(string model)
        => OpenAIRunLiveTests.VerifyTextSteeringAsync(model);

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, false)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, true)]
    public Task Steering_PreservesPendingToolsAndTheirResults(string model, bool nativeAsync)
        => OpenAIRunLiveTests.VerifyToolSteeringAsync(model, nativeAsync);

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, false)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, true)]
    public Task CachePreservingReasoning_TransitionsThroughNone(string model, bool useRun)
        => VerifyCacheTransitionAsync(model, useRun, initiallyNone: false);

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, false)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, true)]
    public Task CachePreservingReasoning_EnablesReasoningFromNoneAndRetainsIt(string model, bool useRun)
        => VerifyCacheTransitionAsync(model, useRun, initiallyNone: true);

    private static async Task VerifyCacheTransitionAsync(string model, bool useRun, bool initiallyNone)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        if (initiallyNone) probe.Service.Gpt6ReasoningEffort = Gpt6Reasoning.None;
        probe.Service.StreamRawLineCallback = line =>
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) return;
            using var document = JsonDocument.Parse(line[6..]);
            if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "error")
                Console.WriteLine("GPT6_LIVE_CACHE server_error_event");
        };
        StringAssert.Contains(await ExecuteAsync(probe.Service.CreateRequest("17 * 19? Reply with only the integer.")
            .WithReasoning(initiallyNone ? ReasoningLevel.None : ReasoningLevel.Medium), useRun, timeout.Token), "323");
        StringAssert.Contains(await ExecuteAsync(probe.Service.CreateRequest("18 * 19? Reply with only the integer.")
            .WithReasoning(initiallyNone ? ReasoningLevel.High : ReasoningLevel.None, CachePreservation.Required), useRun, timeout.Token), "342");
        var third = probe.Service.CreateRequest("19 * 19? Reply with only the integer.");
        if (!initiallyNone) third = third.WithReasoning(ReasoningLevel.Low, CachePreservation.Required);
        StringAssert.Contains(await ExecuteAsync(third, useRun, timeout.Token), "361");
        if (!useRun)
        {
            Assert.AreEqual(3, probe.Requests.Count);
            foreach (var body in probe.Requests.Skip(1))
            {
                Assert.AreEqual(initiallyNone ? "none" : "medium", body.GetProperty("reasoning").GetProperty("effort").GetString());
                Assert.IsTrue(body.GetProperty("input").EnumerateArray().Any(item =>
                    item.TryGetProperty("type", out var type) && type.GetString() == "configuration_update"));
            }
        }
        Console.WriteLine($"GPT6_LIVE_CACHE model={model} run={useRun} initially_none={initiallyNone} accepted=true cache_hit=not_asserted");
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task StructuredOutput_ReturnsTypedResultWithoutRepair(string model)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        probe.Service.Gpt6ReasoningEffort = Gpt6Reasoning.None;
        probe.Service.StructuredOutputMaxRetries = 0;
        var result = await probe.Service.GetCompletionAsync<Answer>("Set Value to STRUCTURED_OK and Count to 42.", timeout.Token);
        Assert.AreEqual("STRUCTURED_OK", result.Value);
        Assert.AreEqual(42, result.Count);
        var body = Assert.ContainsSingle(probe.Requests);
        Assert.AreEqual("json_schema", body.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task ImageInput_KeepsModelAndRecognizesSyntheticImage(string model)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        probe.Service.Gpt6ReasoningEffort = Gpt6Reasoning.None;
        var imagePath = Path.Combine(Path.GetTempPath(), "mythosia-gpt6-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using var bitmap = new SKBitmap(128, 128);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Blue);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(imagePath, png.ToArray(), timeout.Token);
            var text = await probe.Service.GetCompletionWithImageAsync("What color fills this image? Reply with only the English color name.", imagePath, timeout.Token);
            StringAssert.Contains(text.ToLowerInvariant(), "blue");
            Assert.AreEqual(model, Assert.ContainsSingle(probe.Requests).GetProperty("model").GetString());
        }
        finally { if (File.Exists(imagePath)) File.Delete(imagePath); }
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task WebSearch_ReturnsActualProviderCitations(string model)
    {
        using var probe = await Probe.CreateAsync(model);
        using var timeout = Timeout();
        var text = await probe.Service.CreateRequest("Use web search to find NASA's official Mars exploration website. Reply with one sentence and cite the source.")
            .WithWebSearch().WithReasoning(ReasoningLevel.Low).GetCompletionAsync(timeout.Token);
        Assert.IsFalse(string.IsNullOrWhiteSpace(text));
        Assert.IsTrue(probe.Service.LastCitations.Any(item =>
            item.Provider == "OpenAI" && Uri.TryCreate(item.Url, UriKind.Absolute, out _)));
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task FileSearch_RetrievesUnpredictableTokenAndCitation(string model)
    {
        var key = await LiveTestSecrets.GetAsync("momedit-openai-secret");
        using var timeout = Timeout();
        await using var fixture = await HostedSearchLiveFixture.CreateAsync("OpenAI", key, timeout.Token);
        using var probe = await Probe.CreateAsync(model);
        var text = await probe.Service.CreateRequest(fixture.Query).WithFileSearch(fixture.Store)
            .WithReasoning(ReasoningLevel.Low).GetCompletionAsync(timeout.Token);
        StringAssert.Contains(text, fixture.VerificationToken);
        Assert.IsTrue(probe.Service.LastCitations.Any(item => item.Provider == "OpenAI" &&
            (item.FileId == fixture.FileId || (item.Title?.Contains(fixture.DocumentTitle, StringComparison.Ordinal) ?? false))));
    }

    public sealed class Answer { public string Value { get; set; } = ""; public int Count { get; set; } }

    private static CancellationTokenSource Timeout() => new(TimeSpan.FromMinutes(4));

    private static async Task<string> ExecuteAsync(AIRequestBuilder request, bool useRun, CancellationToken cancellationToken)
    {
        if (!useRun) return await request.GetCompletionAsync(cancellationToken);
        var observed = new StringBuilder();
        await using var run = await request.StartRunAsync(text => observed.Append(text), cancellationToken: cancellationToken);
        Assert.IsTrue(run.CanSteer, "Both new models must use the native GPT-6 Run transport.");
        int completions = 0;
        await foreach (var item in run.StreamAsync(cancellationToken))
        {
            Assert.AreNotEqual(StreamingContentType.Error, item.Type);
            if (item.Type == StreamingContentType.Completion) completions++;
        }
        var result = await run.Result;
        Assert.AreEqual(1, completions);
        Assert.AreEqual(result.Text, observed.ToString());
        return result.Text;
    }

    private sealed class Probe : DelegatingHandler
    {
        private readonly HttpClient _http;
        public OpenAIService Service { get; }
        public List<JsonElement> Requests { get; } = [];

        private Probe(string key, string model) : base(new HttpClientHandler())
        {
            _http = new HttpClient(this, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(4) };
            Service = new OpenAIService(key, model, _http)
            {
                MaxTokens = 4096, Gpt6ReasoningSummary = null, Gpt6Verbosity = Verbosity.Low,
                Gpt6ReasoningEffort = Gpt6Reasoning.Auto,
                DefaultPolicy = new() { MaxRounds = 6, TimeoutSeconds = 210 }
            };
        }

        public static async Task<Probe> CreateAsync(string model)
            => new(await LiveTestSecrets.GetAsync("momedit-openai-secret"), model);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(body.RootElement.Clone());
            var response = await base.SendAsync(request, cancellationToken);
            // Only identifiers/status are logged. Never keys, request headers, or document content.
            Console.WriteLine($"GPT6_LIVE_HTTP model={Service.Model} status={(int)response.StatusCode} request_id=" +
                (response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : "missing"));
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _http.Dispose();
            base.Dispose(disposing);
        }
    }
}
