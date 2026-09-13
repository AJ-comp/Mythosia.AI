using Mythosia.AI.Attributes;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System.Text.Json;
using System.Threading.Tasks.Sources;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class ToolReturnContractTests
{
    [TestMethod]
    [DataRow("sync_dto")]
    [DataRow("task_dto")]
    [DataRow("value_task_dto")]
    public async Task ReflectedObjectResultsAreSerializedAfterCompletion(string name)
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(name);

        Assert.IsFalse(result.IsError);
        using var json = JsonDocument.Parse(result.Content);
        Assert.AreEqual("Seoul", json.RootElement.GetProperty("City").GetString());
        Assert.AreEqual(23, json.RootElement.GetProperty("Temperature").GetInt32());
    }

    [TestMethod]
    [DataRow("sync_string")]
    [DataRow("task_string")]
    [DataRow("value_task_string")]
    public async Task ReflectedStringsRemainRawText(string name)
    {
        var result = await CreateService().ExecuteAsync(name);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual("raw \"text\"\nwith newline", result.Content);
    }

    [TestMethod]
    public async Task ReflectedTaskDeclaredWithoutResultPreservesARuntimeStringResult()
    {
        var result = await CreateService().ExecuteAsync("erased_task_string");

        Assert.IsFalse(result.IsError);
        Assert.AreEqual("actual output", result.Content);
    }

    [TestMethod]
    public async Task ReflectedObjectDeclaredTaskStringIsAwaitedAndReturnsRawText()
    {
        var functions = new ReturnTools();
        var service = CreateService(functions);
        var execution = Task.Run(() => service.ExecuteAsync("erased_object_string"));
        await functions.ErasedTaskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsFalse(execution.IsCompleted);
        functions.ErasedStringGate.SetResult("actual output");

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(result.IsError);
        Assert.AreEqual("actual output", result.Content);
    }

    [TestMethod]
    public async Task ReflectedObjectDeclaredTaskWithoutResultIsAwaited()
    {
        var functions = new ReturnTools();
        var service = CreateService(functions);
        var execution = Task.Run(() => service.ExecuteAsync("erased_object_task"));
        await functions.ErasedTaskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsFalse(execution.IsCompleted);
        functions.ErasedNoResultGate.SetResult();

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(result.IsError);
        Assert.AreEqual("Success", result.Content);
        Assert.AreEqual(1, functions.NoResultCompletions);
    }

    [TestMethod]
    [DataRow("sync_null")]
    [DataRow("task_null")]
    [DataRow("value_task_null")]
    [DataRow("sync_null_string")]
    [DataRow("task_null_string")]
    [DataRow("value_task_null_string")]
    public async Task ReflectedNullResultsUseTheSameCompletionMarker(string name)
    {
        var result = await CreateService().ExecuteAsync(name);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual("Done", result.Content);
    }

    [TestMethod]
    [DataRow("sync_void", "Done")]
    [DataRow("task_void", "Success")]
    [DataRow("value_task_void", "Success")]
    public async Task ReflectedNoResultFunctionsCompleteBeforeReportingSuccess(string name, string expected)
    {
        var functions = new ReturnTools();
        var service = CreateService(functions);

        var result = await service.ExecuteAsync(name);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(expected, result.Content);
        Assert.AreEqual(1, functions.NoResultCompletions);
    }

    [TestMethod]
    [DataRow("sync_number")]
    [DataRow("task_number")]
    [DataRow("value_task_number")]
    public async Task ReflectedValueTypeResultsAreSerializedConsistently(string name)
    {
        var result = await CreateService().ExecuteAsync(name);

        Assert.IsFalse(result.IsError);
        Assert.AreEqual("23", result.Content);
    }

    [TestMethod]
    [DataRow("task_gated")]
    [DataRow("value_task_gated")]
    public async Task ReflectedAsyncObjectResultWaitsForTheUnderlyingOperation(string name)
    {
        var functions = new ReturnTools();
        var execution = CreateService(functions).ExecuteAsync(name);

        Assert.IsFalse(execution.IsCompleted);
        functions.ResultGate.SetResult(new WeatherResult("Busan", 25));

        var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(result.IsError);
        using var json = JsonDocument.Parse(result.Content);
        Assert.AreEqual("Busan", json.RootElement.GetProperty("City").GetString());
    }

    [TestMethod]
    public async Task ReflectedValueTaskConsumesAnIValueTaskSourceOnlyOnce()
    {
        var functions = new SourceBackedTools();
        var service = new ProbeService();
        service.WithFunctions(functions);

        var result = await service.ExecuteAsync("source_result");

        Assert.IsFalse(result.IsError);
        Assert.AreEqual(1, functions.Source.ResultReads);
        using var json = JsonDocument.Parse(result.Content);
        Assert.AreEqual("Seoul", json.RootElement.GetProperty("City").GetString());
    }

    [TestMethod]
    [DataRow("sync_failure")]
    [DataRow("task_failure")]
    [DataRow("value_task_failure")]
    public async Task ReflectedExceptionsBecomeFailedCallsWithTheOriginalError(string name)
    {
        var result = await CreateService().ExecuteAsync(name);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.Content, "weather lookup failed");
        Assert.IsFalse(result.Content.Contains("target of an invocation", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task LegacyHandlerViewPropagatesTheOriginalReflectedException()
    {
        var service = CreateService();
        var definition = service.Functions.Single(function => function.Name == "sync_failure");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            definition.Handler!(new Dictionary<string, object>()));

        Assert.AreEqual("weather lookup failed", exception.Message);
        StringAssert.Contains(exception.StackTrace!, nameof(ReturnTools.SyncFailure));
    }

    [TestMethod]
    public async Task ReflectedArgumentConversionErrorsAreFailedCalls()
    {
        var result = await CreateService().ExecuteAsync("with_number", new Dictionary<string, object>
        {
            ["number"] = "not a number"
        });

        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    public async Task ReflectedSerializationErrorsAreFailedCalls()
    {
        var result = await CreateService().ExecuteAsync("serialization_failure");

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.Content, "cycle");
    }

    [TestMethod]
    [DataRow("null_task")]
    [DataRow("null_generic_task")]
    public async Task ReturningANullTaskIsAFailureRatherThanASuccessfulEmptyResult(string name)
    {
        var result = await CreateService().ExecuteAsync(name);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(result.Content, "null task");
    }

    [TestMethod]
    public async Task CancellationTokenIsInjectedAndNeverExposedAsAModelArgument()
    {
        var functions = new TokenTools();
        var service = new ProbeService();
        service.WithFunctions(functions);
        var definition = service.Functions.Single();
        using var cancellation = new CancellationTokenSource();
        var arguments = new Dictionary<string, object>
        {
            ["city_name"] = "Seoul",
            ["token_from_model"] = "must be ignored"
        };

        CollectionAssert.AreEqual(new[] { "city_name" }, definition.Parameters.Properties.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "city_name" }, definition.Parameters.Required);
        Assert.AreEqual("Seoul", await definition.HandlerWithCancellation!(arguments, cancellation.Token));
        Assert.AreEqual(cancellation.Token, functions.ObservedToken);

        Assert.AreEqual("Seoul", await definition.Handler!(arguments));
        Assert.AreEqual(CancellationToken.None, functions.ObservedToken);
    }

    [TestMethod]
    public async Task ReflectedTokenCanCancelAnAlreadyRunningOperation()
    {
        var functions = new CancellableTools();
        var service = new ProbeService();
        service.WithFunctions(functions);
        var definition = service.Functions.Single();
        using var cancellation = new CancellationTokenSource();
        var execution = definition.HandlerWithCancellation!(new Dictionary<string, object>(), cancellation.Token);
        await functions.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
    }

    [TestMethod]
    public async Task StaticRegistrationSupportsObjectResultsAndInjectedTokens()
    {
        var service = new ProbeService();
        service.WithStaticFunctions<StaticTools>();
        var definition = service.Functions.Single();
        using var cancellation = new CancellationTokenSource();

        Assert.AreEqual(0, definition.Parameters.Properties.Count);
        var result = await definition.HandlerWithCancellation!(new Dictionary<string, object>(), cancellation.Token);

        Assert.AreEqual("true", result);
    }

    [TestMethod]
    public async Task RequestBuilderRegistrationUsesTheSameObjectReturnAdapter()
    {
        var service = new ProbeService { CompletionFunctionName = "task_dto" };

        var result = await service.CreateRequest("weather").WithFunctions(new ReturnTools()).GetCompletionAsync();

        Assert.AreEqual(0, service.Functions.Count);
        using var json = JsonDocument.Parse(result);
        Assert.AreEqual("Seoul", json.RootElement.GetProperty("City").GetString());
    }

    [TestMethod]
    public void AsyncVoidFunctionsAreRejectedBeforeExecution()
    {
        var functions = new AsyncVoidTools();
        var service = new ProbeService();

        var exception = Assert.ThrowsExactly<ArgumentException>(() => service.WithFunctions(functions));

        StringAssert.Contains(exception.Message, "async void");
        StringAssert.Contains(exception.Message, "Task or ValueTask");
        Assert.IsFalse(functions.WasCalled);
        Assert.AreEqual(0, service.Functions.Count);
    }

    [TestMethod]
    public async Task FunctionBuilderCancellationHandlersRetainALegacyCallableView()
    {
        var observedToken = CancellationToken.None;
        var definition = FunctionBuilder.Create("cancellable")
            .WithHandler((arguments, token) =>
            {
                observedToken = token;
                return Task.FromResult((string)arguments["city"]);
            }).Build();
        using var cancellation = new CancellationTokenSource();
        var arguments = new Dictionary<string, object> { ["city"] = "Seoul" };

        Assert.AreEqual("Seoul", await definition.HandlerWithCancellation!(arguments, cancellation.Token));
        Assert.AreEqual(cancellation.Token, observedToken);
        Assert.AreEqual("Seoul", await definition.Handler!(arguments));
        Assert.AreEqual(CancellationToken.None, observedToken);
    }

    [TestMethod]
    public async Task FunctionBuilderSynchronousCancellationHandlerReceivesTheToken()
    {
        var definition = FunctionBuilder.Create("cancellable")
            .WithHandler((arguments, token) => token.CanBeCanceled ? "cancellable" : "none")
            .Build();
        using var cancellation = new CancellationTokenSource();

        Assert.AreEqual("cancellable", await definition.HandlerWithCancellation!(new(), cancellation.Token));
        Assert.AreEqual("none", await definition.Handler!(new()));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SettingEitherHandlerPropertyReplacesBothCallableViews(bool replaceWithCancellation)
    {
        var definition = new FunctionDefinition();
        using var cancellation = new CancellationTokenSource();
        if (replaceWithCancellation)
        {
            definition.Handler = _ => Task.FromResult("old");
            definition.HandlerWithCancellation = (_, token) => Task.FromResult(token.CanBeCanceled ? "new-token" : "new-none");
        }
        else
        {
            definition.HandlerWithCancellation = (_, token) => Task.FromResult("old");
            definition.Handler = _ => Task.FromResult("new");
        }

        Assert.AreEqual(replaceWithCancellation ? "new-token" : "new",
            await definition.HandlerWithCancellation!(new(), cancellation.Token));
        Assert.AreEqual(replaceWithCancellation ? "new-none" : "new",
            await definition.Handler!(new()));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ClearingEitherHandlerPropertyClearsBothCallableViews(bool clearCancellationHandler)
    {
        var definition = new FunctionDefinition
        {
            HandlerWithCancellation = (_, token) => Task.FromResult("old")
        };

        if (clearCancellationHandler)
            definition.HandlerWithCancellation = null;
        else
            definition.Handler = null;

        Assert.IsNull(definition.Handler);
        Assert.IsNull(definition.HandlerWithCancellation);
    }

    private static ProbeService CreateService(ReturnTools? functions = null)
    {
        var service = new ProbeService();
        service.WithFunctions(functions ?? new ReturnTools());
        return service;
    }

    public sealed record WeatherResult(string City, int Temperature);

    public sealed class ReturnTools
    {
        public int NoResultCompletions { get; private set; }
        public TaskCompletionSource<WeatherResult> ResultGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> ErasedStringGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ErasedNoResultGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ErasedTaskStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [AiFunction("sync_dto", "Test function.")] public WeatherResult SyncDto() => new("Seoul", 23);
        [AiFunction("task_dto", "Test function.")] public async Task<WeatherResult> TaskDto() { await Task.Yield(); return SyncDto(); }
        [AiFunction("value_task_dto", "Test function.")] public async ValueTask<WeatherResult> ValueTaskDto() { await Task.Yield(); return SyncDto(); }
        [AiFunction("sync_string", "Test function.")] public string SyncString() => "raw \"text\"\nwith newline";
        [AiFunction("task_string", "Test function.")] public async Task<string> TaskString() { await Task.Yield(); return SyncString(); }
        [AiFunction("value_task_string", "Test function.")] public async ValueTask<string> ValueTaskString() { await Task.Yield(); return SyncString(); }
        [AiFunction("erased_task_string", "Test function.")] public Task ErasedTaskString() => Task.FromResult("actual output");
        [AiFunction("erased_object_string", "Test function.")] public object ErasedObjectString() { ErasedTaskStarted.TrySetResult(); return ErasedStringGate.Task; }
        [AiFunction("erased_object_task", "Test function.")] public object ErasedObjectTask() => CompleteErasedTaskAsync();
        [AiFunction("sync_null", "Test function.")] public WeatherResult? SyncNull() => null;
        [AiFunction("task_null", "Test function.")] public Task<WeatherResult?> TaskNull() => Task.FromResult<WeatherResult?>(null);
        [AiFunction("value_task_null", "Test function.")] public ValueTask<WeatherResult?> ValueTaskNull() => new((WeatherResult?)null);
        [AiFunction("sync_null_string", "Test function.")] public string? SyncNullString() => null;
        [AiFunction("task_null_string", "Test function.")] public Task<string?> TaskNullString() => Task.FromResult<string?>(null);
        [AiFunction("value_task_null_string", "Test function.")] public ValueTask<string?> ValueTaskNullString() => new((string?)null);
        [AiFunction("sync_void", "Test function.")] public void SyncVoid() => NoResultCompletions++;
        [AiFunction("task_void", "Test function.")] public async Task TaskVoid() { await Task.Yield(); NoResultCompletions++; }
        [AiFunction("value_task_void", "Test function.")] public async ValueTask ValueTaskVoid() { await Task.Yield(); NoResultCompletions++; }
        [AiFunction("sync_number", "Test function.")] public int SyncNumber() => 23;
        [AiFunction("task_number", "Test function.")] public Task<int> TaskNumber() => Task.FromResult(23);
        [AiFunction("value_task_number", "Test function.")] public ValueTask<int> ValueTaskNumber() => new(23);
        [AiFunction("task_gated", "Test function.")] public Task<WeatherResult> TaskGated() => ResultGate.Task;
        [AiFunction("value_task_gated", "Test function.")] public ValueTask<WeatherResult> ValueTaskGated() => new(ResultGate.Task);
        [AiFunction("sync_failure", "Test function.")] public WeatherResult SyncFailure() => throw new InvalidOperationException("weather lookup failed");
        [AiFunction("task_failure", "Test function.")] public async Task<WeatherResult> TaskFailure() { await Task.Yield(); return SyncFailure(); }
        [AiFunction("value_task_failure", "Test function.")] public async ValueTask<WeatherResult> ValueTaskFailure() { await Task.Yield(); return SyncFailure(); }
        [AiFunction("with_number", "Test function.")] public int WithNumber(int number) => number;
        [AiFunction("serialization_failure", "Test function.")] public object SerializationFailure() { var cycle = new Dictionary<string, object>(); cycle["self"] = cycle; return cycle; }
        [AiFunction("null_task", "Test function.")] public Task NullTask() => null!;
        [AiFunction("null_generic_task", "Test function.")] public Task<WeatherResult> NullGenericTask() => null!;

        private async Task CompleteErasedTaskAsync()
        {
            ErasedTaskStarted.TrySetResult();
            await ErasedNoResultGate.Task;
            NoResultCompletions++;
        }
    }

    public sealed class TokenTools
    {
        public CancellationToken ObservedToken { get; private set; }

        [AiFunction("with_token", "Test function.")]
        public Task<string> WithToken(
            [AiParameter(Name = "city_name", Required = true)] string city,
            [AiParameter(Name = "token_from_model")] CancellationToken cancellationToken = default)
        {
            ObservedToken = cancellationToken;
            return Task.FromResult(city);
        }
    }

    public sealed class CancellableTools
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [AiFunction("cancellable", "Test function.")]
        public async Task<WeatherResult> Cancellable(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new WeatherResult("Seoul", 23);
        }
    }

    public sealed class StaticTools
    {
        [AiFunction("static_token", "Test function.")]
        public static ValueTask<bool> WithToken(CancellationToken cancellationToken)
            => new(cancellationToken.CanBeCanceled);
    }

    public sealed class AsyncVoidTools
    {
        public bool WasCalled { get; private set; }

        [AiFunction("async_void", "Test function.")]
        public async void Invalid() { WasCalled = true; await Task.Yield(); }
    }

    public sealed class SourceBackedTools
    {
        public SingleUseResultSource Source { get; } = new();

        [AiFunction("source_result", "Test function.")]
        public ValueTask<WeatherResult> GetResult() => new(Source, 0);
    }

    public sealed class SingleUseResultSource : IValueTaskSource<WeatherResult>
    {
        public int ResultReads { get; private set; }
        public WeatherResult GetResult(short token)
        {
            if (++ResultReads != 1)
                throw new InvalidOperationException("ValueTask source was consumed more than once.");
            return new WeatherResult("Seoul", 23);
        }
        public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => throw new InvalidOperationException("The source has already completed.");
    }

    private sealed class ProbeService : AIService
    {
        public ProbeService() : base("fake-key", "https://localhost/", new HttpClient()) => AddNewChat();
        public override string Provider => nameof(AIProvider.OpenAI);
        public string CompletionFunctionName { get; set; } = "sync_dto";

        public Task<FunctionCallResult> ExecuteAsync(string name, Dictionary<string, object>? arguments = null)
            => ProcessFunctionCallAsync(new FunctionCall
            {
                Id = "call-test", Name = name, Arguments = arguments ?? new Dictionary<string, object>()
            });

        public override async Task<string> GetCompletionAsync(Message message)
            => (await ExecuteAsync(CompletionFunctionName)).Content;
        protected override HttpRequestMessage CreateMessageRequest() => throw new NotSupportedException();
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => throw new NotSupportedException();
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new NotSupportedException();
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
            => throw new NotSupportedException();
    }
}
