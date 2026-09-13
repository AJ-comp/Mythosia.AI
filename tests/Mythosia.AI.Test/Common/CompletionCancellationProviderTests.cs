using Mythosia.AI.Builders;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class CompletionCancellationProviderTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    public static IEnumerable<object[]> Providers => new[]
    {
        "OpenAI Chat", "OpenAI Responses", "Anthropic", "Google", "xAI", "DeepSeek",
        "Perplexity", "Qwen", "vLLM", "Ollama"
    }.Select(provider => new object[] { provider });

    public static IEnumerable<object[]> ProviderBodies => Providers.SelectMany(provider =>
        new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }.Select(status => new[] { provider[0], status }));

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task PreCancelledRequest_DoesNotSendOrMutateConversation(string provider)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Reply(provider)));
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.GetCompletionAsync("question", cancellationToken: cancellation.Token));

        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(0, handler.Requests);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task HeaderCancellation_ReachesTransportAndNextRequestCanComplete(string provider)
    {
        var started = Signal();
        CancellationToken transportToken = default;
        using var handler = new Handler(async (number, token) =>
        {
            if (number > 1) return Reply(provider);
            transportToken = token;
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply(provider);
        });
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetCompletionAsync("question", cancellationToken: cancellation.Token);
        try
        {
            await started.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));

            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(transportToken.IsCancellationRequested);
            Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
            Assert.AreEqual("answer", await service.GetCompletionAsync("try again").WaitAsync(Deadline));
            Assert.AreEqual(2, handler.Requests);
        }
        finally
        {
            cancellation.Cancel();
            await DrainAsync(pending);
        }
    }

    [TestMethod]
    [DynamicData(nameof(ProviderBodies))]
    public async Task BodyCancellation_AbortsAndDisposesStalledSuccessOrErrorBody(string provider, HttpStatusCode status)
    {
        using var body = new StalledBody();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StreamContent(body)
        }));
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetCompletionAsync("question", cancellationToken: cancellation.Token);
        try
        {
            await body.Started.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));

            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(body.IsDisposed, "Cancellation must release the response stream, including error bodies.");
            Assert.IsTrue(body.ReadToken.IsCancellationRequested);
            Assert.AreEqual(1, handler.Requests);
            Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
        }
        finally
        {
            cancellation.Cancel();
            body.Dispose();
            await DrainAsync(pending);
        }
    }

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task PolicyTimeout_RemainsTimeoutRatherThanCallerCancellation(string provider)
    {
        using var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply(provider);
        });
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        service.DefaultPolicy.TimeoutSeconds = 0;
        using var caller = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<AIServiceException>(() =>
            service.GetCompletionAsync("question", cancellationToken: caller.Token).WaitAsync(Deadline));

        StringAssert.Contains(exception.Message, "timeout");
        Assert.IsFalse(caller.IsCancellationRequested);
    }

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task HttpClientTimeout_IsReportedAsHttpTimeoutWithoutInventingPolicyDuration(string provider)
    {
        var timeout = new TaskCanceledException("HttpClient timeout", new TimeoutException("Configured HTTP timeout elapsed."));
        using var handler = new Handler((_, _) => Task.FromException<HttpResponseMessage>(timeout));
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        using var caller = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<AIServiceException>(() =>
            service.GetCompletionAsync("question", cancellationToken: caller.Token));

        Assert.AreEqual("The HTTP request timed out.", exception.Message);
        Assert.AreSame(timeout, exception.InnerException);
        Assert.IsFalse(caller.IsCancellationRequested);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task UnrelatedTransportCancellation_IsNotMisreportedAsTimeout(string provider)
    {
        using var handler = new Handler((_, _) => Task.FromException<HttpResponseMessage>(
            new OperationCanceledException("Independent transport cancellation.")));
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        using var caller = new CancellationTokenSource();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.GetCompletionAsync("question", cancellationToken: caller.Token));

        Assert.AreEqual("Independent transport cancellation.", exception.Message);
        Assert.IsFalse(caller.IsCancellationRequested);
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task CompletedBody_PreservesDeclaredCharsetAndDisposesContent(string provider)
    {
        var content = new TrackedContent(Payload(provider, "응답 café"), Encoding.Unicode);
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        }));
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        using var caller = new CancellationTokenSource();

        Assert.AreEqual("응답 café", await service.GetCompletionAsync("question", cancellationToken: caller.Token));
        Assert.IsTrue(content.IsDisposed);
    }

    [TestMethod]
    [DynamicData(nameof(Providers))]
    public async Task ToolCancellation_InFinalAllowedRoundRecordsResultAndSkipsNextRequest(string provider)
    {
        var started = Signal();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ToolPayload(provider), Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        service.DefaultPolicy.MaxRounds = 1;
        CancellationToken toolToken = default;
        service.Functions.Add(FunctionBuilder.Create("lookup").WithHandler(async (_, token) =>
        {
            toolToken = token;
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return "unexpected";
        }).Build());
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetCompletionAsync("question", cancellationToken: cancellation.Token);
        try
        {
            await started.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));

            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(toolToken.IsCancellationRequested);
            Assert.AreEqual(1, handler.Requests);
            var calls = service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null).FunctionCallBatch!;
            var results = service.ActivateChat.Messages.Single(message => message.FunctionCallResultBatch != null).FunctionCallResultBatch!;
            Assert.AreEqual(calls.Id, results.FunctionCallBatchId);
            Assert.AreEqual(calls.Calls.Single().Id, results.Results.Single().Call.Id);
            Assert.IsTrue(results.Results.Single().IsCancelled);
            Assert.IsTrue(results.Results.Single().IsError);
        }
        finally
        {
            cancellation.Cancel();
            await DrainAsync(pending);
        }
    }

    [TestMethod]
    public async Task GoogleImageUrlCompletion_HeaderCancellationPreservesCallerTokenAndSkipsModelRequest()
    {
        var started = Signal();
        CancellationToken transportToken = default;
        using var handler = new Handler(async (_, token) =>
        {
            transportToken = token;
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Reply("Google");
        });
        using var client = new HttpClient(handler);
        var service = new GoogleAIService("offline-key", client);
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetCompletionWithImageUrlAsync("describe", "https://offline.invalid/image.png", cancellation.Token);
        try
        {
            await started.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));

            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(transportToken.IsCancellationRequested);
            Assert.AreEqual(1, handler.Requests, "Do not submit the model request after the image download is canceled.");
            Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        }
        finally
        {
            cancellation.Cancel();
            await DrainAsync(pending);
        }
    }

    [TestMethod]
    public async Task GoogleImageUrlCompletion_CancelsImageDownloadBeforeModelRequest()
    {
        using var body = new StalledBody();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body)
        }));
        using var client = new HttpClient(handler);
        var service = new GoogleAIService("offline-key", client);
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetCompletionWithImageUrlAsync("describe", "https://offline.invalid/image.png", cancellation.Token);
        try
        {
            await body.Started.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));
            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(body.IsDisposed);
            Assert.IsTrue(body.ReadToken.IsCancellationRequested);
            Assert.AreEqual(1, handler.Requests, "Do not submit the model request after the image download is canceled.");
            Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        }
        finally
        {
            cancellation.Cancel();
            body.Dispose();
            await DrainAsync(pending);
        }
    }

    [TestMethod]
    public async Task NativeDeferredTool_CallerCancellationDrainsToolAndRestoresNextCompletion()
    {
        var toolStarted = Signal();
        var toolStopped = Signal();
        var continuationStarted = Signal();
        CancellationToken continuationToken = default;
        using var handler = new Handler(async (number, token) =>
        {
            if (number == 1)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ToolPayload("OpenAI Responses")
                        .Replace("\"name\":\"lookup\"", "\"name\":\"lookup\",\"async\":true"), Encoding.UTF8, "application/json")
                };
            if (number == 2)
            {
                continuationToken = token;
                continuationStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return Reply("OpenAI Responses");
        });
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-key", AIModels.OpenAI.Gpt6Astra, client)
        {
            DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 4, EnableLogging = false }
        };
        service.Functions.Add(FunctionBuilder.Create("lookup").WithAsync().WithHandler(async (_, token) =>
        {
            toolStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "unexpected";
            }
            finally { toolStopped.TrySetResult(); }
        }).Build());
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetCompletionAsync("question", cancellationToken: cancellation.Token);
        try
        {
            await toolStarted.Task.WaitAsync(Deadline);
            await continuationStarted.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));

            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(continuationToken.IsCancellationRequested);
            Assert.IsTrue(toolStopped.Task.IsCompletedSuccessfully, "The completion must drain the deferred tool before returning cancellation.");
            Assert.AreEqual(2, handler.Requests);
            var calls = service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null).FunctionCallBatch!;
            var results = service.ActivateChat.Messages.Single(message => message.FunctionCallResultBatch != null).FunctionCallResultBatch!;
            Assert.AreEqual(calls.Id, results.FunctionCallBatchId);
            Assert.AreEqual(calls.Calls.Single().Id, results.Results.Single().Call.Id);
            Assert.IsTrue(results.Results.Single().IsError && results.Results.Single().IsCancelled);
            Assert.AreEqual("answer", await service.GetCompletionAsync("try again").WaitAsync(Deadline));
            Assert.AreEqual(3, handler.Requests);
        }
        finally
        {
            cancellation.Cancel();
            await DrainAsync(pending);
        }
    }

    [TestMethod]
    public async Task NoncooperativeStartedTool_CancellationWaitsAndRecordsActualResult()
    {
        var started = Signal();
        var cancellationObserved = Signal();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ToolPayload("OpenAI Chat"), Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(handler);
        var service = CreateService("OpenAI Chat", client);
        service.DefaultPolicy.MaxRounds = 1;
        service.Functions.Add(FunctionBuilder.Create("lookup").WithHandler(async (_, token) =>
        {
            using var observed = token.Register(() => cancellationObserved.TrySetResult());
            started.TrySetResult();
            // This tool observes the signal but its already-started work cannot be interrupted.
            return await release.Task;
        }).Build());
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetCompletionAsync("question", cancellationToken: cancellation.Token);
        try
        {
            await started.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            await cancellationObserved.Task.WaitAsync(Deadline);
            Assert.IsFalse(pending.IsCompleted, "Do not abandon already-started work or invent its result.");
            release.TrySetResult("actual completed result");
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(Deadline));

            Assert.AreEqual(1, handler.Requests);
            var calls = service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null).FunctionCallBatch!;
            var results = service.ActivateChat.Messages.Single(message => message.FunctionCallResultBatch != null).FunctionCallResultBatch!;
            Assert.AreEqual(calls.Id, results.FunctionCallBatchId);
            Assert.AreEqual(calls.Calls.Single().Id, results.Results.Single().Call.Id);
            var result = results.Results.Single();
            Assert.AreEqual("actual completed result", result.Content);
            Assert.IsFalse(result.IsError);
            Assert.IsFalse(result.IsCancelled);
        }
        finally
        {
            release.TrySetResult("cleanup result");
            cancellation.Cancel();
            await DrainAsync(pending);
        }
    }

    private static AIService CreateService(string provider, HttpClient client)
    {
        AIService service = provider switch
        {
            "OpenAI Chat" => new OpenAIService("offline-key", AIModels.OpenAI.Gpt4o, client),
            "OpenAI Responses" => new OpenAIService("offline-key", AIModels.OpenAI.Gpt4_1, client),
            "Anthropic" => new AnthropicService("offline-key", client),
            "Google" => new GoogleAIService("offline-key", AIModels.Google.Gemini2_5Flash, client),
            "xAI" => new XAIService("offline-key", client),
            "DeepSeek" => new DeepSeekService("offline-key", client),
            "Perplexity" => new PerplexityService("offline-key", client),
            "Qwen" => new QwenService("offline-key", client),
            "vLLM" => new QwenService("https://offline.invalid/", EndpointPlatform.Vllm, client),
            "Ollama" => new QwenService("https://offline.invalid/", EndpointPlatform.Ollama, client),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = null, MaxRounds = 3, EnableLogging = false };
        return service;
    }

    private static HttpResponseMessage Reply(string provider) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(Payload(provider), Encoding.UTF8, "application/json")
    };

    private static string Payload(string provider, string answer = "answer") => provider switch
    {
        "OpenAI Responses" or "Perplexity" => """{"id":"resp-test","status":"completed","output":[{"type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"ANSWER"}]}]}""".Replace("ANSWER", answer),
        "Anthropic" => """{"id":"msg-test","type":"message","role":"assistant","content":[{"type":"text","text":"ANSWER"}],"stop_reason":"end_turn"}""".Replace("ANSWER", answer),
        "Google" => """{"candidates":[{"content":{"role":"model","parts":[{"text":"ANSWER"}]},"finishReason":"STOP"}]}""".Replace("ANSWER", answer),
        _ => """{"choices":[{"message":{"role":"assistant","content":"ANSWER"},"finish_reason":"stop"}]}""".Replace("ANSWER", answer)
    };

    private static string ToolPayload(string provider) => provider switch
    {
        "OpenAI Responses" or "Perplexity" => """{"id":"resp-test","status":"completed","output":[{"id":"item-test","type":"function_call","status":"completed","call_id":"call-test","name":"lookup","arguments":"{}"}]}""",
        "Anthropic" => """{"id":"msg-test","type":"message","role":"assistant","content":[{"type":"tool_use","id":"call-test","name":"lookup","input":{}}],"stop_reason":"tool_use"}""",
        "Google" => """{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"id":"call-test","name":"lookup","args":{}}}]},"finishReason":"STOP"}]}""",
        _ => """{"choices":[{"message":{"role":"assistant","content":"","tool_calls":[{"id":"call-test","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}"""
    };

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task DrainAsync(Task task)
    {
        try { await task.WaitAsync(Deadline); }
        catch (OperationCanceledException) { }
    }

    private sealed class Handler(Func<int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(++Requests, cancellationToken);
    }

    private sealed class TrackedContent(string content, Encoding encoding) : StringContent(content, encoding, "application/json")
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>Models a pending network read that only aborts when its owning response is disposed.</summary>
    private sealed class StalledBody : Stream
    {
        private readonly TaskCompletionSource _disposed = Signal();
        public TaskCompletionSource Started { get; } = Signal();
        public bool IsDisposed { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ReadToken = cancellationToken;
            Started.TrySetResult();
            await _disposed.Task;
            throw new ObjectDisposedException(nameof(StalledBody));
        }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            _disposed.TrySetResult();
            base.Dispose(disposing);
        }
    }
}
