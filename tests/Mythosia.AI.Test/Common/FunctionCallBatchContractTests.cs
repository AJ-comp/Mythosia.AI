using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Protocols;
using Mythosia.AI.Services.Base;
using System.Net.Http;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class FunctionCallBatchContractTests
{
    private sealed class BatchProbeService : AIService
    {
        public BatchProbeService()
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public Task<FunctionCallResultBatch> ExecuteAsync(
            FunctionCallBatch calls,
            CancellationToken cancellationToken = default)
            => ProcessFunctionCallsAsync(
                calls,
                FunctionCallingPolicy.Default,
                cancellationToken);

        public Task<FunctionCallResultBatch> ExecuteAsync(
            FunctionCallBatch calls,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken = default)
            => ProcessFunctionCallsAsync(calls, policy, cancellationToken);

        public void SaveRound(FunctionCallBatch calls, FunctionCallResultBatch results)
        {
            AddFunctionCallBatchToHistory(string.Empty, calls);
            AddFunctionResultBatchToHistory(results);
        }

        public Message SaveCalls(FunctionCallBatch calls)
            => AddFunctionCallBatchToHistory(string.Empty, calls);

        public Message SaveResults(FunctionCallResultBatch results)
            => AddFunctionResultBatchToHistory(results);

        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult(string.Empty);
        protected override HttpRequestMessage CreateMessageRequest() => new(HttpMethod.Post, "https://localhost/");
        protected override string ExtractResponseContent(string responseContent) => responseContent;
        protected override string StreamParseJson(string jsonData) => jsonData;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;
        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response)
            => (response, new FunctionCallBatch());
    }

    [TestMethod]
    public async Task BatchExecutor_ExecutesSequentiallyInProviderOrder()
    {
        Assert.AreEqual(
            FunctionExecutionMode.Sequential,
            FunctionCallingPolicy.Default.ExecutionMode);

        var service = new BatchProbeService();
        var executionOrder = new List<string>();
        var activeHandlers = 0;
        var maximumConcurrency = 0;

        service.Functions.Add(CreateFunction("first", async () =>
        {
            executionOrder.Add("first");
            var active = Interlocked.Increment(ref activeHandlers);
            maximumConcurrency = Math.Max(maximumConcurrency, active);
            await Task.Delay(20);
            Interlocked.Decrement(ref activeHandlers);
            return "first-result";
        }));
        service.Functions.Add(CreateFunction("second", async () =>
        {
            executionOrder.Add("second");
            var active = Interlocked.Increment(ref activeHandlers);
            maximumConcurrency = Math.Max(maximumConcurrency, active);
            await Task.Delay(5);
            Interlocked.Decrement(ref activeHandlers);
            return "second-result";
        }));

        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "first", 0),
            CreateCall("call-b", "second", 1)
        });

        var results = await service.ExecuteAsync(calls);

        CollectionAssert.AreEqual(new[] { "first", "second" }, executionOrder);
        Assert.AreEqual(1, maximumConcurrency);
        CollectionAssert.AreEqual(
            new[] { "call-a", "call-b" },
            results.Results.Select(result => result.Call.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first-result", "second-result" },
            results.Results.Select(result => result.Content).ToArray());

        service.SaveRound(calls, results);
        Assert.AreEqual(2, service.ActivateChat.Messages.Count);
        Assert.AreEqual(2, service.ActivateChat.Messages[0].FunctionCallBatch?.Calls.Count);
        Assert.AreEqual(2, service.ActivateChat.Messages[1].FunctionCallResultBatch?.Results.Count);
    }

    [TestMethod]
    public async Task BatchExecutor_ExplicitSequentialModePreservesCurrentBehavior()
    {
        var service = new BatchProbeService();
        var executionOrder = new List<string>();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        service.Functions.Add(CreateFunction("first", async () =>
        {
            executionOrder.Add("first:start");
            firstStarted.TrySetResult(true);
            await firstRelease.Task;
            executionOrder.Add("first:end");
            return "first-result";
        }));
        service.Functions.Add(CreateFunction("second", () =>
        {
            executionOrder.Add("second");
            return Task.FromResult("second-result");
        }));

        var execution = service.ExecuteAsync(
            new FunctionCallBatch(new[]
            {
                CreateCall("call-a", "first", 0),
                CreateCall("call-b", "second", 1)
            }),
            new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Sequential,
                MaxConcurrency = 10
            });

        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(new[] { "first:start" }, executionOrder);
        }
        finally
        {
            firstRelease.TrySetResult(true);
        }

        var results = await execution;
        CollectionAssert.AreEqual(
            new[] { "first:start", "first:end", "second" },
            executionOrder);
        CollectionAssert.AreEqual(
            new[] { "call-a", "call-b" },
            results.Results.Select(result => result.Call.Id).ToArray());
    }

    [TestMethod]
    public async Task BatchExecutor_ParallelModeHonorsConcurrencyLimit()
    {
        var service = new BatchProbeService();
        var releaseHandlers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWaveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedHandlers = 0;
        var activeHandlers = 0;
        var maximumConcurrency = 0;

        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Handler = async arguments =>
            {
                Interlocked.Increment(ref startedHandlers);
                var active = Interlocked.Increment(ref activeHandlers);
                UpdateMaximum(ref maximumConcurrency, active);
                if (active == 2)
                    firstWaveStarted.TrySetResult(true);

                await releaseHandlers.Task;
                Interlocked.Decrement(ref activeHandlers);
                return $"result-{arguments["number"]}";
            }
        });

        var calls = new FunctionCallBatch(Enumerable.Range(0, 5).Select(index =>
        {
            var call = CreateCall($"call-{index}", "lookup", index);
            call.Arguments["number"] = index;
            return call;
        }));

        var execution = service.ExecuteAsync(
            calls,
            new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Parallel,
                MaxConcurrency = 2
            });

        try
        {
            await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, Volatile.Read(ref startedHandlers));
            Assert.AreEqual(2, Volatile.Read(ref maximumConcurrency));
            Assert.IsFalse(execution.IsCompleted);
        }
        finally
        {
            releaseHandlers.TrySetResult(true);
        }

        var results = await execution;
        Assert.AreEqual(5, startedHandlers);
        Assert.AreEqual(2, maximumConcurrency);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 5).Select(index => $"call-{index}").ToArray(),
            results.Results.Select(result => result.Call.Id).ToArray());
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 5).Select(index => $"result-{index}").ToArray(),
            results.Results.Select(result => result.Content).ToArray());
    }

    [TestMethod]
    public async Task BatchExecutor_ParallelModePreservesProviderOrderAfterReverseCompletion()
    {
        var service = new BatchProbeService();
        var bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var started = 0;

        void MarkStarted()
        {
            if (Interlocked.Increment(ref started) == 2)
                bothStarted.TrySetResult(true);
        }

        service.Functions.Add(CreateFunction("first", async () =>
        {
            MarkStarted();
            await bothStarted.Task;
            await secondCompleted.Task;
            completed.Enqueue("first");
            return "first-result";
        }));
        service.Functions.Add(CreateFunction("second", async () =>
        {
            MarkStarted();
            await bothStarted.Task;
            completed.Enqueue("second");
            secondCompleted.TrySetResult(true);
            return "second-result";
        }));

        var results = await service.ExecuteAsync(
            new FunctionCallBatch(new[]
            {
                CreateCall("call-a", "first", 0),
                CreateCall("call-b", "second", 1)
            }),
            new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Parallel,
                MaxConcurrency = 2
            });

        CollectionAssert.AreEqual(new[] { "second", "first" }, completed.ToArray());
        CollectionAssert.AreEqual(
            new[] { "call-a", "call-b" },
            results.Results.Select(result => result.Call.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first-result", "second-result" },
            results.Results.Select(result => result.Content).ToArray());
    }

    [TestMethod]
    public async Task BatchExecutor_ParallelModeRejectsInvalidConcurrencyBeforeExecution()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("lookup", () =>
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult("result");
        }));
        var call = CreateCall(string.Empty, "lookup", 0);
        call.Arguments = null!;
        var calls = new FunctionCallBatch(new[] { call }) { Id = string.Empty };

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.ExecuteAsync(
            calls,
            new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Parallel,
                MaxConcurrency = 0
            }));

        Assert.AreEqual(0, executions);
        Assert.AreEqual(string.Empty, calls.Id);
        Assert.AreEqual(string.Empty, call.Id);
        Assert.IsNull(call.Arguments);
    }

    [TestMethod]
    public async Task BatchExecutor_UnknownExecutionModeIsRejectedBeforeExecution()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("lookup", () =>
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult("result");
        }));
        var call = CreateCall(string.Empty, "lookup", 0);
        var calls = new FunctionCallBatch(new[] { call }) { Id = string.Empty };

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.ExecuteAsync(
            calls,
            new FunctionCallingPolicy
            {
                ExecutionMode = (FunctionExecutionMode)int.MaxValue
            }));

        Assert.AreEqual(0, executions);
        Assert.AreEqual(string.Empty, calls.Id);
        Assert.AreEqual(string.Empty, call.Id);
    }

    [TestMethod]
    public async Task BatchExecutor_ParallelModeIsolatesHandlerFailures()
    {
        var service = new BatchProbeService();
        var bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        async Task WaitForBothHandlersAsync()
        {
            if (Interlocked.Increment(ref started) == 2)
                bothStarted.TrySetResult(true);
            await bothStarted.Task;
        }

        service.Functions.Add(CreateFunction("failing", async () =>
        {
            await WaitForBothHandlersAsync();
            throw new InvalidOperationException("expected parallel failure");
        }));
        service.Functions.Add(CreateFunction("successful", async () =>
        {
            await WaitForBothHandlersAsync();
            return "success";
        }));

        var results = await service.ExecuteAsync(
            new FunctionCallBatch(new[]
            {
                CreateCall("call-a", "failing", 0),
                CreateCall("call-b", "successful", 1)
            }),
            new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Parallel,
                MaxConcurrency = 2
            });

        Assert.AreEqual(2, results.Results.Count);
        Assert.IsTrue(results.Results[0].IsError);
        StringAssert.Contains(results.Results[0].Content, "expected parallel failure");
        Assert.IsFalse(results.Results[1].IsError);
        Assert.AreEqual("success", results.Results[1].Content);
    }

    [TestMethod]
    public async Task BatchExecutor_ParallelModePreCancelledTokenExecutesNoHandlers()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("lookup", () =>
        {
            Interlocked.Increment(ref executions);
            return Task.FromResult("result");
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ExecuteAsync(
            new FunctionCallBatch(new[] { CreateCall("call-a", "lookup", 0) }),
            new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Parallel,
                MaxConcurrency = 2
            },
            cancellation.Token));

        Assert.AreEqual(0, executions);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public void FunctionPolicyCloneAndConvenienceOverridesPreserveExecutionSettings()
    {
        var service = new BatchProbeService
        {
            DefaultPolicy = new FunctionCallingPolicy
            {
                ExecutionMode = FunctionExecutionMode.Parallel,
                MaxConcurrency = 7,
                MaxRounds = 11,
                TimeoutSeconds = 120,
                EnableLogging = true
            }
        };

        var clone = service.DefaultPolicy.Clone();
        Assert.AreEqual(FunctionExecutionMode.Parallel, clone.ExecutionMode);
        Assert.AreEqual(7, clone.MaxConcurrency);

        service.WithTimeout(30);
        Assert.IsNotNull(service.CurrentPolicy);
        Assert.AreEqual(FunctionExecutionMode.Parallel, service.CurrentPolicy.ExecutionMode);
        Assert.AreEqual(7, service.CurrentPolicy.MaxConcurrency);
        Assert.AreEqual(11, service.CurrentPolicy.MaxRounds);
        Assert.IsTrue(service.CurrentPolicy.EnableLogging);
        Assert.AreEqual(30, service.CurrentPolicy.TimeoutSeconds);

        service.WithMaxRounds(4);
        Assert.AreEqual(FunctionExecutionMode.Parallel, service.CurrentPolicy.ExecutionMode);
        Assert.AreEqual(7, service.CurrentPolicy.MaxConcurrency);
        Assert.AreEqual(30, service.CurrentPolicy.TimeoutSeconds);
        Assert.IsTrue(service.CurrentPolicy.EnableLogging);
        Assert.AreEqual(4, service.CurrentPolicy.MaxRounds);
    }

    [TestMethod]
    public async Task BatchExecutor_InvalidSecondCall_ExecutesNoHandlers()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("valid", () =>
        {
            executions++;
            return Task.FromResult("ok");
        }));

        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "valid", 0),
            CreateCall("call-b", string.Empty, 1)
        });

        await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.ExecuteAsync(calls));
        Assert.AreEqual(0, executions);
    }

    [TestMethod]
    public async Task BatchExecutor_InvalidLaterCallDoesNotPartiallyNormalizeEarlierCalls()
    {
        var service = new BatchProbeService();
        var first = CreateCall(string.Empty, "valid", 0);
        first.Arguments = null!;
        var calls = new FunctionCallBatch(new[]
        {
            first,
            CreateCall("call-b", string.Empty, 1)
        })
        {
            Id = string.Empty
        };

        await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.ExecuteAsync(calls));

        Assert.AreEqual(string.Empty, calls.Id);
        Assert.AreEqual(string.Empty, first.Id);
        Assert.IsNull(first.Arguments);
    }

    [TestMethod]
    public async Task BatchExecutor_DuplicateCallId_ExecutesNoHandlers()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("first", () =>
        {
            executions++;
            return Task.FromResult("first-result");
        }));
        service.Functions.Add(CreateFunction("second", () =>
        {
            executions++;
            return Task.FromResult("second-result");
        }));

        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("duplicate", "first", 3),
            CreateCall("duplicate", "second", 7)
        });

        await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.ExecuteAsync(calls));
        Assert.AreEqual(0, executions);
    }

    [TestMethod]
    public async Task BatchExecutor_DuplicateProviderIndex_ExecutesNoHandlers()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("first", () =>
        {
            executions++;
            return Task.FromResult("first-result");
        }));
        service.Functions.Add(CreateFunction("second", () =>
        {
            executions++;
            return Task.FromResult("second-result");
        }));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "first", 4),
            CreateCall("call-b", "second", 4)
        });

        await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.ExecuteAsync(calls));

        Assert.AreEqual(0, executions);
    }

    [TestMethod]
    public async Task BatchExecutor_EmptyBatchAndNullElementsAreRejectedBeforeExecution()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("valid", () =>
        {
            executions++;
            return Task.FromResult("ok");
        }));

        await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.ExecuteAsync(new FunctionCallBatch()));
        await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.ExecuteAsync(new FunctionCallBatch { Calls = null! }));
        await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.ExecuteAsync(new FunctionCallBatch(new FunctionCall[] { null! })));

        Assert.AreEqual(0, executions);
    }

    [TestMethod]
    public async Task BatchExecutor_BlankBatchAndCallIdsAreNormalizedUniquely()
    {
        var service = new BatchProbeService();
        service.Functions.Add(CreateFunction("first", () => Task.FromResult("first-result")));
        service.Functions.Add(CreateFunction("second", () => Task.FromResult("second-result")));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall(string.Empty, "first", 0),
            CreateCall("   ", "second", 1)
        })
        {
            Id = string.Empty
        };

        var results = await service.ExecuteAsync(calls);

        Assert.IsFalse(string.IsNullOrWhiteSpace(calls.Id));
        Assert.IsTrue(calls.Calls.All(call => !string.IsNullOrWhiteSpace(call.Id)));
        Assert.AreNotEqual(calls.Calls[0].Id, calls.Calls[1].Id);
        Assert.AreEqual(calls.Id, results.FunctionCallBatchId);
    }

    [TestMethod]
    public async Task BatchExecutor_PreservesProviderIndexesAcrossResults()
    {
        var service = new BatchProbeService();
        service.Functions.Add(CreateFunction("first", () => Task.FromResult("first-result")));
        service.Functions.Add(CreateFunction("second", () => Task.FromResult("second-result")));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "first", 2),
            CreateCall("call-b", "second", 5)
        });

        var results = await service.ExecuteAsync(calls);

        CollectionAssert.AreEqual(new[] { 2, 5 }, calls.Calls.Select(call => call.Index).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 5 }, results.Results.Select(result => result.Call.Index).ToArray());
    }

    [TestMethod]
    public async Task BatchExecutor_FirstHandlerFailureStillProducesEveryOrderedResult()
    {
        var service = new BatchProbeService();
        var executionOrder = new List<string>();
        service.Functions.Add(CreateFunction("first", () =>
        {
            executionOrder.Add("first");
            throw new InvalidOperationException("expected failure");
        }));
        service.Functions.Add(CreateFunction("second", () =>
        {
            executionOrder.Add("second");
            return Task.FromResult("second-result");
        }));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "first", 0),
            CreateCall("call-b", "second", 1)
        });

        var results = await service.ExecuteAsync(calls);

        CollectionAssert.AreEqual(new[] { "first", "second" }, executionOrder);
        Assert.AreEqual(2, results.Results.Count);
        Assert.IsTrue(results.Results[0].IsError);
        StringAssert.Contains(results.Results[0].Content, "expected failure");
        Assert.IsFalse(results.Results[1].IsError);
        Assert.AreEqual("second-result", results.Results[1].Content);
    }

    [TestMethod]
    public async Task BatchExecutor_HandlerOperationCanceledExceptionIsAnOrderedErrorResult()
    {
        var service = new BatchProbeService();
        var executionOrder = new List<string>();
        service.Functions.Add(CreateFunction("cancelled", () =>
        {
            executionOrder.Add("cancelled");
            throw new OperationCanceledException("handler cancelled itself");
        }));
        service.Functions.Add(CreateFunction("next", () =>
        {
            executionOrder.Add("next");
            return Task.FromResult("next-result");
        }));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "cancelled", 0),
            CreateCall("call-b", "next", 1)
        });

        var results = await service.ExecuteAsync(calls);

        CollectionAssert.AreEqual(new[] { "cancelled", "next" }, executionOrder);
        Assert.IsTrue(results.Results[0].IsError);
        StringAssert.Contains(results.Results[0].Content, "handler cancelled itself");
        Assert.IsFalse(results.Results[1].IsError);
    }

    [TestMethod]
    public async Task BatchExecutor_PreCancelledTokenExecutesNoHandlers()
    {
        var service = new BatchProbeService();
        var executions = 0;
        service.Functions.Add(CreateFunction("valid", () =>
        {
            executions++;
            return Task.FromResult("ok");
        }));
        var calls = new FunctionCallBatch(new[] { CreateCall("call-a", "valid", 0) });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(calls, cancellation.Token));

        Assert.AreEqual(0, executions);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task BatchExecutor_CancellationAfterStartFinishesValidatedBatch()
    {
        var service = new BatchProbeService();
        var cancellation = new CancellationTokenSource();
        var executionOrder = new List<string>();
        service.Functions.Add(CreateFunction("first", () =>
        {
            executionOrder.Add("first");
            cancellation.Cancel();
            return Task.FromResult("first-result");
        }));
        service.Functions.Add(CreateFunction("second", () =>
        {
            executionOrder.Add("second");
            return Task.FromResult("second-result");
        }));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "first", 0),
            CreateCall("call-b", "second", 1)
        });

        var results = await service.ExecuteAsync(calls, cancellation.Token);

        CollectionAssert.AreEqual(new[] { "first", "second" }, executionOrder);
        Assert.AreEqual(2, results.Results.Count);
    }

    [TestMethod]
    public async Task BatchExecutor_HandlerCannotMutateCallsOrHistoryThroughArguments()
    {
        var service = new BatchProbeService();
        service.Functions.Add(new FunctionDefinition
        {
            Name = "mutator",
            Handler = arguments =>
            {
                ((Dictionary<string, object>)arguments["nested"])["value"] = "corrupted";
                arguments.Clear();
                return Task.FromResult("done");
            }
        });
        var call = CreateCall("call-a", "mutator", 0);
        call.Arguments["top"] = "original";
        call.Arguments["nested"] = new Dictionary<string, object> { ["value"] = "original" };
        var calls = new FunctionCallBatch(new[] { call });

        var results = await service.ExecuteAsync(calls);
        service.SaveRound(calls, results);

        Assert.AreEqual("original", call.Arguments["top"]);
        Assert.AreEqual(
            "original",
            ((Dictionary<string, object>)call.Arguments["nested"])["value"]);
        Assert.AreEqual("original", results.Results[0].Call.Arguments["top"]);
        Assert.AreEqual(
            "original",
            ((Dictionary<string, object>)service.ActivateChat.Messages[0]
                .FunctionCallBatch!.Calls[0].Arguments["nested"])["value"]);
    }

    [TestMethod]
    public async Task HistorySnapshotsCannotBeMutatedThroughCallerOwnedBatchOrResults()
    {
        var service = new BatchProbeService();
        service.Functions.Add(CreateFunction("lookup", () => Task.FromResult("original-result")));
        var call = CreateCall("call-a", "lookup", 0);
        call.Arguments["value"] = "original-argument";
        var calls = new FunctionCallBatch(new[] { call });
        var results = await service.ExecuteAsync(calls);

        service.SaveRound(calls, results);
        calls.Calls[0].Name = "corrupted-name";
        calls.Calls[0].Arguments["value"] = "corrupted-argument";
        results.Results[0].Content = "corrupted-result";
        results.Results[0].Call.Name = "corrupted-result-name";

        var savedCalls = service.ActivateChat.Messages[0].FunctionCallBatch!;
        var savedResults = service.ActivateChat.Messages[1].FunctionCallResultBatch!;
        Assert.AreEqual("lookup", savedCalls.Calls[0].Name);
        Assert.AreEqual("original-argument", savedCalls.Calls[0].Arguments["value"]);
        Assert.AreEqual("lookup", savedResults.Results[0].Call.Name);
        Assert.AreEqual("original-result", savedResults.Results[0].Content);
    }

    [TestMethod]
    public async Task HistoryRejectsMismatchedResultBatchWithoutAppendingIt()
    {
        var service = new BatchProbeService();
        service.Functions.Add(CreateFunction("lookup", () => Task.FromResult("result")));
        var calls = new FunctionCallBatch(new[] { CreateCall("call-a", "lookup", 0) });
        var results = await service.ExecuteAsync(calls);
        service.SaveCalls(calls);
        results.FunctionCallBatchId = "different-batch";

        Assert.ThrowsExactly<AIServiceException>(() => service.SaveResults(results));

        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
        Assert.IsNotNull(service.ActivateChat.Messages[0].FunctionCallBatch);
    }

    [TestMethod]
    public async Task HistoryRejectsReorderedResultsAndDuplicateBatchMessages()
    {
        var service = new BatchProbeService();
        service.Functions.Add(CreateFunction("first", () => Task.FromResult("first-result")));
        service.Functions.Add(CreateFunction("second", () => Task.FromResult("second-result")));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "first", 0),
            CreateCall("call-b", "second", 1)
        });
        var results = await service.ExecuteAsync(calls);
        service.SaveCalls(calls);
        var reordered = new FunctionCallResultBatch(
            results.FunctionCallBatchId,
            results.Results.Reverse());

        Assert.ThrowsExactly<AIServiceException>(() => service.SaveResults(reordered));
        Assert.ThrowsExactly<AIServiceException>(() => service.SaveCalls(calls));

        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task HistoryRejectsNullOrEmptyResultsWithoutAppendingThem()
    {
        var service = new BatchProbeService();
        service.Functions.Add(CreateFunction("lookup", () => Task.FromResult("result")));
        var calls = new FunctionCallBatch(new[] { CreateCall("call-a", "lookup", 0) });
        service.SaveCalls(calls);

        Assert.ThrowsExactly<AIServiceException>(() => service.SaveResults(
            new FunctionCallResultBatch(calls.Id, Array.Empty<FunctionCallResult>())));
        Assert.ThrowsExactly<AIServiceException>(() => service.SaveResults(
            new FunctionCallResultBatch(calls.Id, Array.Empty<FunctionCallResult>())
            {
                Results = null!
            }));

        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task HistoryInvalidLaterResultDoesNotNormalizeEarlierResults()
    {
        var service = new BatchProbeService();
        service.Functions.Add(CreateFunction("first", () => Task.FromResult("first-result")));
        service.Functions.Add(CreateFunction("second", () => Task.FromResult("second-result")));
        var calls = new FunctionCallBatch(new[]
        {
            CreateCall("call-a", "first", 0),
            CreateCall("call-b", "second", 1)
        });
        var results = await service.ExecuteAsync(calls);
        service.SaveCalls(calls);
        results.Results[0].Content = null!;
        results.Results[1].Call.Name = "tampered";

        Assert.ThrowsExactly<AIServiceException>(() => service.SaveResults(results));

        Assert.IsNull(results.Results[0].Content);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public void MessageTokenEstimate_IncludesFunctionArguments()
    {
        var small = new Message(ActorRole.Assistant, string.Empty)
        {
            FunctionCallBatch = new FunctionCallBatch(new[]
            {
                CreateCall("call-a", "lookup", 0)
            })
        };
        var largeCall = CreateCall("call-a", "lookup", 0);
        largeCall.Arguments["payload"] = new string('x', 4_000);
        var large = new Message(ActorRole.Assistant, string.Empty)
        {
            FunctionCallBatch = new FunctionCallBatch(new[] { largeCall })
        };

        Assert.IsTrue(small.EstimateTokens() > 0u);
        Assert.IsTrue(large.EstimateTokens() > small.EstimateTokens() + 900u);
    }

    [TestMethod]
    public void MessageClone_DeepCopiesFunctionGraphsAndTokenEstimateToleratesCycles()
    {
        var recursive = new Dictionary<string, object>();
        recursive["self"] = recursive;
        var call = CreateCall("call-a", "lookup", 0);
        call.Arguments["nested"] = new Dictionary<string, object> { ["value"] = "original" };
        call.Arguments["recursive"] = recursive;
        call.Metadata = new Dictionary<string, object>
        {
            ["items"] = new List<object> { "original" }
        };
        var message = new Message(ActorRole.Assistant, "content")
        {
            FunctionCallBatch = new FunctionCallBatch(new[] { call })
        };

        var clone = message.Clone();
        ((Dictionary<string, object>)call.Arguments["nested"])["value"] = "corrupted";
        ((List<object>)call.Metadata["items"])[0] = "corrupted";

        var clonedCall = clone.FunctionCallBatch!.Calls[0];
        Assert.AreEqual(
            "original",
            ((Dictionary<string, object>)clonedCall.Arguments["nested"])["value"]);
        Assert.AreEqual("original", ((List<object>)clonedCall.Metadata!["items"])[0]);
        Assert.IsTrue(message.EstimateTokens() > 0u);
    }

    [TestMethod]
    public void ChatCompletionsProtocol_ParsesAndSerializesEveryCallInOrder()
    {
        const string response = """
        {
          "choices": [{
            "finish_reason": "tool_calls",
            "message": {
              "content": null,
              "tool_calls": [
                { "id": "call-a", "type": "function", "function": { "name": "first", "arguments": "{\"value\":1}" } },
                { "id": "call-b", "type": "function", "function": { "name": "second", "arguments": "{\"value\":2}" } }
              ]
            }
          }]
        }
        """;

        var protocol = ChatCompletionsProtocol.Instance;
        var (_, calls) = protocol.ExtractFunctionCalls(response);

        Assert.AreEqual(2, calls.Calls.Count);
        CollectionAssert.AreEqual(
            new[] { "call-a", "call-b" },
            calls.Calls.Select(call => call.Id).ToArray());

        var results = new FunctionCallResultBatch(calls.Id, calls.Calls.Select(call =>
            new FunctionCallResult { Call = call, Content = $"{call.Name}-result" }));
        var messages = new[]
        {
            new Message(ActorRole.Assistant, string.Empty) { FunctionCallBatch = calls },
            new Message(ActorRole.Function, string.Empty) { FunctionCallResultBatch = results }
        };
        var body = protocol.BuildFunctionRequestBody(
            new ProtocolRequestParams { Model = "test-model", Messages = messages },
            Array.Empty<FunctionDefinition>(),
            FunctionCallMode.Auto);

        using var request = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var serializedMessages = request.RootElement.GetProperty("messages");
        Assert.AreEqual(3, serializedMessages.GetArrayLength());
        Assert.AreEqual(2, serializedMessages[0].GetProperty("tool_calls").GetArrayLength());
        Assert.AreEqual("call-a", serializedMessages[1].GetProperty("tool_call_id").GetString());
        Assert.AreEqual("call-b", serializedMessages[2].GetProperty("tool_call_id").GetString());
    }

    private static FunctionDefinition CreateFunction(string name, Func<Task<string>> handler)
    {
        return new FunctionDefinition
        {
            Name = name,
            Handler = _ => handler()
        };
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var observed = Volatile.Read(ref maximum);
        while (candidate > observed)
        {
            var previous = Interlocked.CompareExchange(ref maximum, candidate, observed);
            if (previous == observed)
                return;

            observed = previous;
        }
    }

    private static FunctionCall CreateCall(string id, string name, int index)
    {
        return new FunctionCall
        {
            Id = id,
            Name = name,
            Source = IdSource.OpenAI,
            Index = index,
            Arguments = new Dictionary<string, object>()
        };
    }
}
