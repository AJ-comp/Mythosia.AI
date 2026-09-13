using Mythosia.AI.Attributes;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks.Sources;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialToolCancellationTests
{
    [TestMethod]
    [DataRow("object_task_dto")]
    [DataRow("base_task_dto")]
    [DataRow("object_value_task_dto")]
    public async Task ErasedAsyncObjectReturnsExposeTheCompletedValue(string name)
    {
        var service = new ProbeService();
        service.WithFunctions(new ErasedTools());
        var result = await service.ExecuteAsync(name);
        Assert.IsFalse(result.IsError, result.Content);
        using var json = JsonDocument.Parse(result.Content);
        Assert.AreEqual("Seoul", json.RootElement.GetProperty("City").GetString());
        Assert.AreEqual(23, json.RootElement.GetProperty("Temperature").GetInt32());
    }

    [TestMethod]
    public async Task BoxedValueTaskWithoutResultMustAwaitItsOperation()
    {
        var tools = new ErasedTools();
        var service = new ProbeService();
        service.WithFunctions(tools);
        var execution = service.ExecuteAsync("object_value_task_void");
        try
        {
            Assert.IsTrue(tools.Invoked);
            Assert.IsFalse(execution.IsCompleted, "Serializing a boxed ValueTask must not report completion before its work finishes.");
            tools.VoidGate.SetResult();
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("Success", result.Content);
            Assert.IsFalse(result.IsError);
        }
        finally { tools.VoidGate.TrySetResult(); await execution; }
    }

    [TestMethod]
    public async Task ErasedValueTaskSourceIsAwaitedAndConsumedExactlyOnce()
    {
        var tools = new ErasedTools();
        var service = new ProbeService();
        service.WithFunctions(tools);
        var result = await service.ExecuteAsync("object_source_result");
        Assert.IsFalse(result.IsError, result.Content);
        Assert.AreEqual(1, tools.Source.Reads);
        using var json = JsonDocument.Parse(result.Content);
        Assert.AreEqual("Seoul", json.RootElement.GetProperty("City").GetString());
    }

    [TestMethod]
    [DataRow("object_task_error")]
    [DataRow("object_value_task_error")]
    public async Task ErasedAwaitableErrorsKeepTheOriginalException(string name)
    {
        var service = new ProbeService();
        service.WithFunctions(new ErasedTools());
        var result = await service.ExecuteAsync(name);
        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.Content, "original tool error");
        Assert.IsFalse(result.Content.Contains("target of an invocation", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DataRow("object_task_cancel")]
    [DataRow("object_value_task_cancel")]
    public async Task ErasedAwaitableCancellationIsMarkedCancelled(string name)
    {
        var service = new ProbeService();
        service.WithFunctions(new ErasedTools());
        var result = await service.ExecuteAsync(name);
        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.IsCancelled, result.Content);
    }

    [TestMethod]
    [DataRow("object_async_void")]
    [DataRow("base_async_void")]
    public async Task ErasedCompilerGeneratedNoResultTasksRemainNoResult(string name)
    {
        var service = new ProbeService();
        service.WithFunctions(new ErasedTools());
        var result = await service.ExecuteAsync(name);
        Assert.IsFalse(result.IsError, result.Content);
        Assert.AreEqual("Success", result.Content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StartupFailureStillDisposesSessionWhenCancellationCallbackThrows(bool disposeAlsoThrows)
    {
        var service = new StartupProbeService { DisposeAlsoThrows = disposeAlsoThrows };
        var exception = await Assert.ThrowsAsync<AggregateException>(() => service.StartRunAsync("fail during startup"));
        Assert.AreEqual(1, service.Disposals, "A throwing cancellation callback cannot skip disposal of a successfully created session.");
        var failures = exception.Flatten().InnerExceptions;
        Assert.IsTrue(failures.Any(error => error.Message == "requested model failed"));
        Assert.IsTrue(failures.Any(error => error.Message == "cancellation callback failed"));
        Assert.AreEqual(disposeAlsoThrows, failures.Any(error => error.Message == "session disposal failed"));
        service.FailModelResolution = false;
        service.DisposeAlsoThrows = false;
        await using var next = await service.StartRunAsync("next request");
        await next.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, service.Disposals);
    }

    public sealed record Weather(string City, int Temperature);
    public sealed class ErasedTools
    {
        public TaskCompletionSource VoidGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OneReadSource Source { get; } = new();
        public bool Invoked { get; private set; }
        [AiFunction("object_task_dto")] public object ObjectTaskDto() => Task.FromResult(new Weather("Seoul", 23));
        [AiFunction("base_task_dto")] public Task BaseTaskDto() => Task.FromResult(new Weather("Seoul", 23));
        [AiFunction("object_value_task_dto")] public object ObjectValueTaskDto() => new ValueTask<Weather>(new Weather("Seoul", 23));
        [AiFunction("object_value_task_void")] public object ObjectValueTaskVoid() { Invoked = true; return new ValueTask(VoidGate.Task); }
        [AiFunction("object_source_result")] public object ObjectSourceResult() => new ValueTask<Weather>(Source, 0);
        [AiFunction("object_task_error")] public object ObjectTaskError() => Task.FromException<Weather>(new InvalidOperationException("original tool error"));
        [AiFunction("object_value_task_error")] public object ObjectValueTaskError() => new ValueTask<Weather>(Task.FromException<Weather>(new InvalidOperationException("original tool error")));
        [AiFunction("object_task_cancel")] public object ObjectTaskCancel() => Task.FromCanceled<Weather>(new CancellationToken(true));
        [AiFunction("object_value_task_cancel")] public object ObjectValueTaskCancel() => new ValueTask<Weather>(Task.FromCanceled<Weather>(new CancellationToken(true)));
        [AiFunction("object_async_void")] public object ObjectAsyncVoid() => NoResultAsync();
        [AiFunction("base_async_void")] public Task BaseAsyncVoid() => NoResultAsync();
        private static async Task NoResultAsync() { await Task.Yield(); }
    }
    public sealed class OneReadSource : IValueTaskSource<Weather>
    {
        public int Reads { get; private set; }
        public Weather GetResult(short token)
        {
            if (++Reads != 1) throw new InvalidOperationException("Source consumed twice");
            return new Weather("Seoul", 23);
        }
        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => throw new InvalidOperationException();
    }
    private class ProbeService : AIService
    {
        public ProbeService() : base("offline", "https://localhost/", new HttpClient()) { }
        public override string Provider => "Probe";
        public Task<FunctionCallResult> ExecuteAsync(string name) => ProcessFunctionCallAsync(new FunctionCall { Id = "call", Name = name });
        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult("answer");
        protected override HttpRequestMessage CreateMessageRequest() => throw new NotSupportedException();
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new NotSupportedException();
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => throw new NotSupportedException();
    }
    private sealed class StartupProbeService : ProbeService
    {
        public bool FailModelResolution { get; set; } = true;
        public bool DisposeAlsoThrows { get; set; }
        public int Disposals { get; private set; }
        protected override string? GetRunRequestedModel() => FailModelResolution ? throw new InvalidOperationException("requested model failed") : "model";
        protected override Task<RunSession> CreateRunSessionAsync(Message message, StreamOptions executionOptions, AIRequestContext? context, CancellationToken cancellationToken)
            => Task.FromResult<RunSession>(new Session(this, cancellationToken));
        private sealed class Session : RunSession
        {
            private readonly StartupProbeService _owner;
            private readonly CancellationTokenRegistration _registration;
            public Session(StartupProbeService owner, CancellationToken token)
            {
                _owner = owner;
                _registration = token.Register(() => throw new InvalidOperationException("cancellation callback failed"));
            }
            public override async IAsyncEnumerable<StreamingContent> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return new StreamingContent { Type = StreamingContentType.Completion };
            }
            public override ValueTask DisposeAsync()
            {
                _owner.Disposals++;
                _registration.Dispose();
                if (_owner.DisposeAlsoThrows) throw new InvalidOperationException("session disposal failed");
                return default;
            }
        }
    }
}
