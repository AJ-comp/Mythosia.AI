using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("Streaming")]
public class StreamingTimeoutContractTests
{
    [TestMethod]
    [DataRow("OpenAI")]
    [DataRow("Anthropic")]
    [DataRow("Google")]
    [DataRow("xAI")]
    public async Task PolicyTimeout_CoversSseBodyAfterHeaders(string provider)
    {
        await using var body = new BlockingReadStream();
        using var handler = new ImmediateHeadersHandler(body);
        using var httpClient = new HttpClient(handler);
        var service = CreateService(provider, httpClient);
        service.DefaultPolicy = new FunctionCallingPolicy
        {
            MaxRounds = 3,
            TimeoutSeconds = 1
        };
        using var safetyCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => ConsumeStreamAsync(service, safetyCancellation.Token));

        stopwatch.Stop();
        StringAssert.Contains(exception.Message, "Request timeout after 1 seconds");
        Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsTrue(body.ReadStarted.Task.IsCompleted,
            "The response headers must have completed and the SSE body read must have started.");
        Assert.IsTrue(body.WasDisposed,
            "Cancelling a pending SSE read must dispose the response stream.");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"The policy timeout was not applied promptly: {stopwatch.Elapsed}.");
    }

    [TestMethod]
    [DataRow("OpenAI")]
    [DataRow("Anthropic")]
    [DataRow("Google")]
    [DataRow("xAI")]
    public async Task CallerCancellation_RemainsOperationCanceledWithCallerToken(string provider)
    {
        await using var body = new BlockingReadStream();
        using var handler = new ImmediateHeadersHandler(body);
        using var httpClient = new HttpClient(handler);
        var service = CreateService(provider, httpClient);
        service.DefaultPolicy = new FunctionCallingPolicy
        {
            MaxRounds = 3,
            TimeoutSeconds = null
        };
        using var callerCancellation = new CancellationTokenSource();
        var operation = ConsumeStreamAsync(service, callerCancellation.Token);
        await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        callerCancellation.Cancel();
        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await operation);

        Assert.AreEqual(callerCancellation.Token, exception.CancellationToken);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsTrue(body.WasDisposed,
            "Caller cancellation must dispose the pending SSE response stream.");
    }

    [TestMethod]
    public async Task RoundLoop_ReusesOnePolicyCancellationTokenAcrossRounds()
    {
        using var httpClient = new HttpClient(new ImmediateHeadersHandler(new MemoryStream()));
        var service = new RoundTokenProbeService(httpClient)
        {
            DefaultPolicy = new FunctionCallingPolicy
            {
                MaxRounds = 3,
                TimeoutSeconds = 30
            }
        };

        await ConsumeStreamAsync(service, CancellationToken.None);

        Assert.AreEqual(2, service.RoundTokens.Count);
        Assert.AreEqual(service.RoundTokens[0], service.RoundTokens[1],
            "The policy timeout token must be created once for the complete round loop.");
    }

    private static AIService CreateService(string provider, HttpClient httpClient)
    {
        return provider switch
        {
            "OpenAI" => new OpenAIService("offline-test-key", httpClient),
            "Anthropic" => new AnthropicService("offline-test-key", httpClient),
            "Google" => new GoogleAIService("offline-test-key", httpClient),
            "xAI" => new XAIService("offline-test-key", httpClient),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }

    private static async Task ConsumeStreamAsync(
        AIService service,
        CancellationToken cancellationToken)
    {
        await foreach (var _ in service.StreamAsync(
            "Wait for a response.",
            cancellationToken))
        {
        }
    }

    private sealed class ImmediateHeadersHandler : HttpMessageHandler
    {
        private readonly Stream _body;
        private int _requestCount;

        public ImmediateHeadersHandler(Stream body)
        {
            _body = body;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(_body)
            });
        }
    }

    private sealed class RoundTokenProbeService : OpenAIService
    {
        public RoundTokenProbeService(HttpClient httpClient)
            : base("offline-test-key", httpClient)
        {
        }

        public List<CancellationToken> RoundTokens { get; } = new();

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RoundTokens.Add(cancellationToken);
            await Task.Yield();

            if (RoundTokens.Count == 1)
            {
                yield return new StreamingContent
                {
                    Type = StreamingContentType.FunctionResult
                };
                yield break;
            }

            yield return new StreamingContent
            {
                Type = StreamingContentType.Text,
                Content = "done"
            };
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource<int> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public TaskCompletionSource<bool> ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasDisposed => Volatile.Read(ref _disposed) != 0;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult(true);
            return _completion.Task;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult(true);
            return new ValueTask<int>(_completion.Task);
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _completion.TrySetResult(0);

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
