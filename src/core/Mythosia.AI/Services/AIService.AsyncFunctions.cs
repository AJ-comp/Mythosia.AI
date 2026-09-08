using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        private const string AsyncFunctionCallIdsMetadataKey = "mythosia_async_function_call_ids";
        private AsyncFunctionScope? _asyncFunctionScope;

        /// <summary>Whether this provider and model accept native asynchronous function calls.</summary>
        protected virtual bool SupportsAsyncFunctionCalls => false;

        protected bool HasPendingAsyncFunctions =>
            _asyncFunctionScope?.Work.Any(work => !work.Delivered) == true;

        protected bool UsedAsyncFunctions => _asyncFunctionScope?.UsedAsync == true;

        /// <summary>
        /// Owns deferred handlers for one request. Cleanup completes started work before the
        /// request releases its chat, including on failure, cancellation, or stream disposal.
        /// </summary>
        protected Func<Task> BeginAsyncFunctionScope(bool useFunctions = true)
        {
            var previous = _asyncFunctionScope;
            var scope = useFunctions && SupportsAsyncFunctionCalls && Functions.Any(function => function.AllowAsync)
                ? new AsyncFunctionScope()
                : null;
            _asyncFunctionScope = scope;
            return async () =>
            {
                try
                {
                    if (scope != null)
                    {
                        await Task.WhenAll(scope.Work.Select(work => work.Task)).ConfigureAwait(false);
                        await CollectAsyncFunctionResultsAsync(false, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    scope?.ConcurrencyGate?.Dispose();
                    _asyncFunctionScope = previous;
                }
            };
        }

        /// <summary>
        /// Records a validated call batch and runs its required calls. Eligible asynchronous
        /// calls may finish in later rounds; every result is recorded exactly once.
        /// </summary>
        protected async Task<IReadOnlyList<FunctionCallResultBatch>> ProcessFunctionBatchForRoundAsync(
            string content,
            FunctionCallBatch calls,
            Dictionary<string, object>? metadata,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken)
        {
            NormalizeAndValidateFunctionCalls(calls);
            var scope = _asyncFunctionScope;
            var deferredCalls = scope == null
                ? Array.Empty<FunctionCall>()
                : calls.Calls.Where(call => call.IsAsync &&
                    Functions.Any(function => function.Name == call.Name && function.AllowAsync)).ToArray();

            // Keep the existing batch extension points and execution semantics for ordinary tools.
            if (deferredCalls.Length == 0 && !UsedAsyncFunctions)
            {
                var results = await ProcessFunctionCallsAsync(calls, policy, cancellationToken).ConfigureAwait(false);
                AddFunctionCallBatchToHistory(content, calls, metadata);
                AddFunctionResultBatchToHistory(results, new Dictionary<string, object> { ["model"] = Model });
                return new[] { results };
            }

            if (policy.ExecutionMode != FunctionExecutionMode.Sequential && policy.ExecutionMode != FunctionExecutionMode.Parallel)
                throw new ArgumentOutOfRangeException(nameof(policy.ExecutionMode), "Unsupported function execution mode.");
            if (policy.MaxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(policy.MaxConcurrency), "MaxConcurrency must be greater than zero for asynchronous execution.");
            cancellationToken.ThrowIfCancellationRequested();

            // Reject reused provider IDs before starting any handler, even across response rounds.
            var previousIds = new HashSet<string>(ActivateChat.Messages
                .Where(message => message.FunctionCallBatch != null)
                .SelectMany(message => message.FunctionCallBatch!.Calls)
                .Select(call => call.Id), StringComparer.Ordinal);
            foreach (var call in calls.Calls)
            {
                if (previousIds.Contains(call.Id))
                    throw new AIServiceException($"The provider reused function-call ID '{call.Id}' in conversation history.");
            }

            var executionBatch = calls.Clone();
            var deferredIds = new HashSet<string>(deferredCalls.Select(call => call.Id), StringComparer.Ordinal);
            if (deferredIds.Count > 0)
            {
                executionBatch.Metadata ??= new Dictionary<string, object>();
                executionBatch.Metadata[AsyncFunctionCallIdsMetadataKey] = deferredIds.ToArray();
                scope!.UsedAsync = true;
                scope.ConcurrencyGate ??= new SemaphoreSlim(policy.MaxConcurrency, policy.MaxConcurrency);
            }
            AddFunctionCallBatchToHistory(content, executionBatch, metadata);

            // Schedule all deferred calls before running required calls, including when a
            // required call awaits work appearing later in this same provider response.
            foreach (var call in executionBatch.Calls.Where(call => deferredIds.Contains(call.Id)))
            {
                var task = Task.Run(() => ExecuteScopedFunctionAsync(call, policy, scope!.ConcurrencyGate));
                scope!.Work.Add(new AsyncFunctionWork(executionBatch.Id, task));
            }

            var requiredCalls = executionBatch.Calls.Where(call => !deferredIds.Contains(call.Id)).ToArray();
            if (requiredCalls.Length > 0)
            {
                // Required calls retain their normal sequential/parallel policy and extension hook.
                // Once a batch starts, handlers have no cancellation token and must finish.
                var requiredBatch = new FunctionCallBatch(requiredCalls) { Id = executionBatch.Id };
                var requiredTask = ProcessRequiredScopedFunctionsAsync(requiredBatch, policy);
                var requiredWork = new List<Task<FunctionCallResult>>();
                foreach (var call in requiredCalls)
                {
                    var task = GetRequiredFunctionResultAsync(requiredTask, call);
                    requiredWork.Add(task);
                    scope!.Work.Add(new AsyncFunctionWork(executionBatch.Id, task));
                }
                await Task.WhenAll(requiredWork).ConfigureAwait(false);
            }

            return await CollectAsyncFunctionResultsAsync(false, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Delivers completed results, optionally waiting until at least one is ready.</summary>
        protected async Task<IReadOnlyList<FunctionCallResultBatch>> CollectAsyncFunctionResultsAsync(
            bool waitForResult,
            CancellationToken cancellationToken)
        {
            var pending = _asyncFunctionScope?.Work.Where(work => !work.Delivered).ToArray()
                ?? Array.Empty<AsyncFunctionWork>();
            if (pending.Length == 0)
                return Array.Empty<FunctionCallResultBatch>();

            if (waitForResult && !pending.Any(work => work.Task.IsCompleted))
            {
                var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancellation.TrySetCanceled(cancellationToken)))
                {
                    var completed = await Task.WhenAny(
                        Task.WhenAny(pending.Select(work => work.Task)), cancellation.Task).ConfigureAwait(false);
                    await completed.ConfigureAwait(false);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();

            var batches = new List<FunctionCallResultBatch>();
            foreach (var group in pending.Where(work => work.Task.IsCompleted).GroupBy(work => work.BatchId))
            {
                // Work is registered async-first so dependent wait results follow the work they
                // waited for. Correlation uses original IDs and indexes, never delivery position.
                var ready = group.ToArray();
                var results = await Task.WhenAll(ready.Select(work => work.Task)).ConfigureAwait(false);
                var batch = new FunctionCallResultBatch(group.Key, results);
                AddFunctionResultBatchToHistory(batch, new Dictionary<string, object> { ["model"] = Model });
                foreach (var work in ready)
                    work.Delivered = true;
                batches.Add(batch);
            }
            return batches;
        }

        private async Task<FunctionCallResult> ExecuteScopedFunctionAsync(
            FunctionCall call, FunctionCallingPolicy policy, SemaphoreSlim? gate)
        {
            if (gate != null)
                await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (policy.EnableLogging)
                    Console.WriteLine($"  Executing asynchronous function: {call.Name}");
                return await ProcessFunctionCallAsync(call).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return FailedScopedFunction(call, exception);
            }
            finally
            {
                gate?.Release();
            }
        }

        private async Task<FunctionCallResultBatch> ProcessRequiredScopedFunctionsAsync(
            FunctionCallBatch calls, FunctionCallingPolicy policy)
        {
            try
            {
                return await ProcessFunctionCallsAsync(calls, policy, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                return new FunctionCallResultBatch(calls.Id,
                    calls.Calls.Select(call => FailedScopedFunction(call, exception)));
            }
        }

        private static async Task<FunctionCallResult> GetRequiredFunctionResultAsync(
            Task<FunctionCallResultBatch> task, FunctionCall call)
        {
            var results = await task.ConfigureAwait(false);
            return results.Results.Single(result => result.Call.Id == call.Id);
        }

        private static FunctionCallResult FailedScopedFunction(FunctionCall call, Exception exception) =>
            new FunctionCallResult
            {
                Call = call.Clone(),
                Content = $"Error executing function: {exception.Message}",
                IsError = true
            };

        private sealed class AsyncFunctionScope
        {
            public bool UsedAsync { get; set; }
            public List<AsyncFunctionWork> Work { get; } = new List<AsyncFunctionWork>();
            public SemaphoreSlim? ConcurrencyGate { get; set; }
        }

        private sealed class AsyncFunctionWork
        {
            public AsyncFunctionWork(string batchId, Task<FunctionCallResult> task)
            {
                BatchId = batchId;
                Task = task;
            }

            public string BatchId { get; }
            public Task<FunctionCallResult> Task { get; }
            public bool Delivered { get; set; }
        }
    }
}
