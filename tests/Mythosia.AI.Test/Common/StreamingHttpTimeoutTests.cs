using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("Streaming")]
public class StreamingHttpTimeoutTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow("Anthropic", "text")]
    [DataRow("Anthropic", "advanced")]
    [DataRow("Anthropic", "callback")]
    [DataRow("OpenAI", "text")]
    [DataRow("Google", "advanced")]
    [DataRow("xAI", "callback")]
    public async Task HttpTimeout_StreamingEntryPointsPreserveOriginalCause(string provider, string entry)
    {
        var original = HttpTimeout();
        using var fixture = new Fixture(provider, _ => original);

        var operation = ObserveAsync(fixture.Service, entry);
        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => operation.WaitAsync(Deadline));

        AssertHttpTimeout(exception, original);
        Assert.IsTrue(operation.IsFaulted);
        Assert.AreEqual(1, fixture.Handler.Requests);
        AssertNoAssistantOutput(fixture.Service);
    }

    [TestMethod]
    [DataRow("Anthropic", null)]
    [DataRow("OpenAI", 10)]
    [DataRow("Google", null)]
    [DataRow("xAI", 10)]
    public async Task HttpTimeout_RunResultFaultsRetainsOutputErrorAndReleasesActiveSlot(string provider, int? policySeconds)
    {
        var original = HttpTimeout();
        using var fixture = new Fixture(provider, _ => original);
        fixture.Service.DefaultPolicy.TimeoutSeconds = policySeconds;
        using var caller = new CancellationTokenSource();
        var callbackText = new List<string>();
        await using var run = await fixture.Service.StartRunAsync("question", callbackText.Add,
            cancellationToken: caller.Token).WaitAsync(Deadline);

        // Result-only consumers must retain the transport cause without needing an output reader.
        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => run.Result.WaitAsync(Deadline));

        AssertHttpTimeout(exception, original);
        Assert.IsTrue(run.Result.IsFaulted);
        Assert.IsFalse(run.Result.IsCanceled);
        Assert.IsFalse(caller.IsCancellationRequested);
        Assert.AreEqual(0, callbackText.Count);
        AssertNoAssistantOutput(fixture.Service);

        var outputException = await Assert.ThrowsExactlyAsync<AIServiceException>(() => ReadOutputAsync(run).WaitAsync(Deadline));
        Assert.AreSame(exception, outputException);
        AssertHttpTimeout(outputException, original);

        // Start the retry before disposing the failed handle: settled Result promises cleanup.
        await using var retry = await fixture.Service.StartRunAsync("retry after timeout").WaitAsync(Deadline);
        Assert.AreEqual("answer", (await retry.Result.WaitAsync(Deadline)).Text);
        Assert.AreEqual(2, fixture.Handler.Requests);
        Assert.AreEqual(1, fixture.Service.ActivateChat.Messages.Count(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("run")]
    public async Task CallerCancellation_TakesPriorityOverTimeoutShapedTransportException(string entry)
    {
        using var fixture = new Fixture("Anthropic", HttpTimeout, waitForCancellation: true);
        using var caller = new CancellationTokenSource();
        var operation = ObserveAsync(fixture.Service, entry, caller.Token);
        try
        {
            await fixture.Handler.Started.Task.WaitAsync(Deadline);
            caller.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(Deadline));

            Assert.IsTrue(operation.IsCanceled);
            if (entry == "text") Assert.AreEqual(caller.Token, exception.CancellationToken);
            Assert.IsInstanceOfType<TaskCanceledException>(fixture.Handler.Failure);
            Assert.IsInstanceOfType<TimeoutException>(fixture.Handler.Failure.InnerException);
            AssertNoAssistantOutput(fixture.Service);
        }
        finally
        {
            caller.Cancel();
            await DrainAsync(operation);
        }
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("run")]
    public async Task PolicyTimeout_TakesPriorityOverTimeoutShapedTransportException(string entry)
    {
        using var fixture = new Fixture("Anthropic", HttpTimeout, waitForCancellation: true);
        fixture.Service.DefaultPolicy.TimeoutSeconds = 1;
        using var caller = new CancellationTokenSource();
        var operation = ObserveAsync(fixture.Service, entry, caller.Token);
        try
        {
            await fixture.Handler.Started.Task.WaitAsync(Deadline);
            var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => operation.WaitAsync(Deadline));

            Assert.AreEqual("Request timeout after 1 seconds", exception.Message);
            Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
            Assert.IsInstanceOfType<TaskCanceledException>(fixture.Handler.Failure);
            Assert.IsInstanceOfType<TimeoutException>(fixture.Handler.Failure.InnerException);
            Assert.IsTrue(operation.IsFaulted);
            Assert.IsFalse(caller.IsCancellationRequested);
            AssertNoAssistantOutput(fixture.Service);
        }
        finally
        {
            caller.Cancel();
            await DrainAsync(operation);
        }
    }

    [TestMethod]
    [DataRow("text", false)]
    [DataRow("run", false)]
    [DataRow("text", true)]
    [DataRow("run", true)]
    public async Task UnrelatedCancellation_IsNotReclassifiedAsHttpTimeout(string entry, bool timeoutInnerException)
    {
        // Neither an ordinary TaskCanceledException nor an arbitrary OCE with a timeout
        // cause establishes the specific HttpClient timeout shape.
        OperationCanceledException original = timeoutInnerException
            ? new OperationCanceledException("Unrelated cancellation", new TimeoutException("Not an HTTP timeout"))
            : new TaskCanceledException("Unrelated task cancellation");
        using var fixture = new Fixture("Anthropic", _ => original);
        using var caller = new CancellationTokenSource();
        var operation = ObserveAsync(fixture.Service, entry, caller.Token);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(Deadline));

        Assert.IsTrue(operation.IsCanceled);
        Assert.IsFalse(caller.IsCancellationRequested);
        if (entry == "text") Assert.AreSame(original, exception);
        AssertNoAssistantOutput(fixture.Service);
    }

    [TestMethod]
    [DataRow("text")]
    [DataRow("run")]
    public async Task OtherTransportFailure_IsPreserved(string entry)
    {
        var original = new HttpRequestException("Connection reset", new IOException("Transport failure"));
        using var fixture = new Fixture("Anthropic", _ => original);
        var operation = ObserveAsync(fixture.Service, entry);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => operation.WaitAsync(Deadline));

        Assert.AreSame(original, exception);
        Assert.IsTrue(operation.IsFaulted);
        AssertNoAssistantOutput(fixture.Service);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(10)]
    public async Task RealHttpClientTimeout_BeforeHeadersFaultsRunWithTransportCause(int? policySeconds)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cleanup = new CancellationTokenSource(Deadline);
        var accepting = listener.AcceptTcpClientAsync(cleanup.Token).AsTask();
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMilliseconds(500)
        };
        var service = CreateService("Anthropic", client);
        client.BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1/");
        service.DefaultPolicy.TimeoutSeconds = policySeconds;
        using var caller = new CancellationTokenSource();
        var run = await service.StartRunAsync("question", cancellationToken: caller.Token).WaitAsync(Deadline);
        try
        {
            // Keep the accepted TCP connection open without sending any response headers.
            // Only HttpClient.Timeout expires; neither caller nor policy cancellation fires.
            using var peer = await accepting.WaitAsync(Deadline);
            var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => run.Result.WaitAsync(Deadline));

            Assert.AreEqual("The HTTP request timed out.", exception.Message);
            Assert.IsInstanceOfType<TaskCanceledException>(exception.InnerException);
            Assert.IsInstanceOfType<TimeoutException>(exception.InnerException.InnerException);
            Assert.IsTrue(run.Result.IsFaulted);
            Assert.IsFalse(caller.IsCancellationRequested);
            AssertNoAssistantOutput(service);

            var outputException = await Assert.ThrowsExactlyAsync<AIServiceException>(() => ReadOutputAsync(run).WaitAsync(Deadline));
            Assert.AreSame(exception, outputException);
        }
        finally
        {
            caller.Cancel();
            cleanup.Cancel();
            listener.Stop();
            await run.DisposeAsync().AsTask().WaitAsync(Deadline);
        }
    }

    private static TaskCanceledException HttpTimeout(CancellationToken cancellationToken = default) =>
        new("The HTTP request exceeded HttpClient.Timeout.", new TimeoutException("Transport deadline expired."), cancellationToken);

    private static void AssertHttpTimeout(AIServiceException exception, Exception original)
    {
        Assert.AreEqual("The HTTP request timed out.", exception.Message);
        Assert.AreSame(original, exception.InnerException, "The original transport exception must remain available for diagnostics.");
        Assert.IsNotNull(exception.InnerException);
        Assert.IsInstanceOfType<TimeoutException>(exception.InnerException.InnerException);
    }

    private static void AssertNoAssistantOutput(AIService service) =>
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant ||
            message.FunctionCallBatch != null || message.FunctionCallResultBatch != null));

    private static async Task ObserveAsync(AIService service, string entry, CancellationToken cancellationToken = default)
    {
        switch (entry)
        {
            case "text":
                await foreach (var _ in service.StreamAsync("question", cancellationToken)) { }
                break;
            case "advanced":
                await foreach (var _ in service.StreamAsync("question", StreamOptions.TextOnlyOptions, cancellationToken)) { }
                break;
            case "callback":
                await service.StreamCompletionAsync("question", _ => Assert.Fail("A failed request must not produce text."));
                break;
            case "run":
                var run = await service.StartRunAsync("question", cancellationToken: cancellationToken).WaitAsync(Deadline);
                try { await run.Result.WaitAsync(Deadline); }
                finally { await run.DisposeAsync().AsTask().WaitAsync(Deadline); }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entry));
        }
    }

    private static async Task ReadOutputAsync(AIRun run)
    {
        await foreach (var _ in run.StreamAsync()) { }
    }

    private static async Task DrainAsync(Task operation)
    {
        try { await operation.WaitAsync(Deadline); }
        catch (Exception) when (operation.IsCompleted) { }
    }

    private static AIService CreateService(string provider, HttpClient client)
    {
        AIService service = provider switch
        {
            "Anthropic" => new AnthropicService("offline-key", "claude-sonnet-5-5", client),
            "OpenAI" => new OpenAIService("offline-key", AIModels.OpenAI.Gpt4o, client),
            "Google" => new GoogleAIService("offline-key", AIModels.Google.Gemini2_5Flash, client),
            "xAI" => new XAIService("offline-key", client),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 3, EnableLogging = false };
        return service;
    }

    private static string SuccessBody(string provider) => provider switch
    {
        "Anthropic" => Sse(
            """{"type":"message_start","message":{"id":"msg-test","model":"claude-sonnet-5-5","type":"message","role":"assistant","content":[]}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"answer"}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn"}}""",
            """{"type":"message_stop"}"""),
        "Google" => Sse("""{"candidates":[{"content":{"role":"model","parts":[{"text":"answer"}]},"finishReason":"STOP"}]}"""),
        _ => Sse(
            """{"choices":[{"index":0,"delta":{"role":"assistant","content":"answer"},"finish_reason":null}]}""",
            """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]")
    };

    private static string Sse(params string[] events) => string.Concat(events.Select(item => $"data: {item}\n\n"));

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;
        public FailureThenSuccessHandler Handler { get; }
        public AIService Service { get; }

        public Fixture(string provider, Func<CancellationToken, Exception> failureFactory, bool waitForCancellation = false)
        {
            Handler = new FailureThenSuccessHandler(failureFactory, SuccessBody(provider), waitForCancellation);
            _client = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan };
            Service = CreateService(provider, _client);
        }

        public void Dispose() => _client.Dispose();
    }

    private sealed class FailureThenSuccessHandler(
        Func<CancellationToken, Exception> failureFactory, string successBody, bool waitForCancellation) : HttpMessageHandler
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public Exception? Failure { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) > 1)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(successBody, Encoding.UTF8, "text/event-stream")
                };

            Failure = failureFactory(cancellationToken);
            Started.TrySetResult();
            if (waitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            }
            throw Failure;
        }
    }
}
