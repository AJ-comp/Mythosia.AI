using Mythosia.AI.Models.Functions;
using System;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        #region Function Calling Support

        /// <summary>
        /// Process function call
        /// </summary>
        protected virtual async Task<FunctionCallResult> ProcessFunctionCallAsync(FunctionCall functionCall)
        {
            var callSnapshot = functionCall.Clone();
            var function = Functions
                .FirstOrDefault(f => f.Name == callSnapshot.Name);

            if (function?.Handler == null)
            {
                return new FunctionCallResult
                {
                    Call = callSnapshot,
                    Content = $"Error: Function '{callSnapshot.Name}' not found",
                    IsError = true
                };
            }

            try
            {
                var handlerArguments = ObjectGraphSnapshot.CloneDictionary(callSnapshot.Arguments);
                var content = await function.Handler(handlerArguments);
                if (string.IsNullOrEmpty(content))
                    content = "Function executed successfully";

                return new FunctionCallResult
                {
                    Call = callSnapshot,
                    Content = content
                };
            }
            catch (Exception ex)
            {
                return new FunctionCallResult
                {
                    Call = callSnapshot,
                    Content = $"Error executing function: {ex.Message}",
                    IsError = true
                };
            }
        }

        /// <summary>
        /// Executes one validated provider batch using the configured execution mode.
        /// </summary>
        protected virtual Task<FunctionCallResultBatch> ProcessFunctionCallsAsync(
            FunctionCallBatch functionCalls,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            var executionMode = policy.ExecutionMode;
            return executionMode switch
            {
                FunctionExecutionMode.Sequential => ProcessFunctionCallsSequentiallyAsync(
                    functionCalls,
                    policy,
                    cancellationToken),
                FunctionExecutionMode.Parallel => ProcessFunctionCallsInParallelAsync(
                    functionCalls,
                    policy,
                    cancellationToken),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(policy.ExecutionMode),
                    policy.ExecutionMode,
                    "Unsupported function execution mode.")
            };
        }

        /// <summary>
        /// Validates the complete provider batch, then executes each call in provider order.
        /// Parallel execution is intentionally not part of this contract.
        /// </summary>
        protected virtual async Task<FunctionCallResultBatch> ProcessFunctionCallsSequentiallyAsync(
            FunctionCallBatch functionCalls,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            NormalizeAndValidateFunctionCalls(functionCalls);
            // A provider turn is one execution unit. Honour cancellation before any handler
            // starts, then finish the validated batch so history never contains tool calls
            // without their corresponding result batch.
            cancellationToken.ThrowIfCancellationRequested();
            var executionBatch = functionCalls.Clone();
            var results = new List<FunctionCallResult>(executionBatch.Calls.Count);
            var enableLogging = policy.EnableLogging;

            foreach (var functionCall in executionBatch.Calls)
            {
                if (enableLogging)
                    Console.WriteLine($"  Executing function: {functionCall.Name}");

                results.Add(await ProcessFunctionCallAsync(functionCall));
            }

            return new FunctionCallResultBatch(executionBatch.Id, results);
        }

        /// <summary>
        /// Validates the complete provider batch, then executes calls concurrently while
        /// preserving provider order in the returned result batch.
        /// </summary>
        protected virtual async Task<FunctionCallResultBatch> ProcessFunctionCallsInParallelAsync(
            FunctionCallBatch functionCalls,
            FunctionCallingPolicy policy,
            CancellationToken cancellationToken = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            var maxConcurrency = policy.MaxConcurrency;
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(policy.MaxConcurrency),
                    maxConcurrency,
                    "MaxConcurrency must be greater than zero for parallel execution.");

            NormalizeAndValidateFunctionCalls(functionCalls);
            // Match the sequential batch contract: cancellation is honoured before any
            // handler starts, then the validated batch is completed so call/result history
            // cannot be left partially populated. Function handlers do not currently accept
            // a CancellationToken.
            cancellationToken.ThrowIfCancellationRequested();
            var executionBatch = functionCalls.Clone();
            var enableLogging = policy.EnableLogging;
            using var concurrencyGate = new SemaphoreSlim(
                maxConcurrency,
                maxConcurrency);

            var tasks = executionBatch.Calls.Select(async functionCall =>
            {
                await concurrencyGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (enableLogging)
                        Console.WriteLine($"  Executing function: {functionCall.Name}");

                    return await ProcessFunctionCallAsync(functionCall).ConfigureAwait(false);
                }
                finally
                {
                    concurrencyGate.Release();
                }
            }).ToArray();

            // Task.WhenAll returns results in the same order as the input task array even when
            // handlers finish out of order, preserving provider call/result correlation.
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return new FunctionCallResultBatch(executionBatch.Id, results);
        }

        protected Message AddFunctionCallBatchToHistory(
            string content,
            FunctionCallBatch functionCalls,
            Dictionary<string, object>? metadata = null)
        {
            NormalizeAndValidateFunctionCalls(functionCalls);
            if (ActivateChat.Messages.Any(existing =>
                existing.FunctionCallBatch?.Id == functionCalls.Id))
            {
                throw new AIServiceException(
                    $"Function-call batch '{functionCalls.Id}' already exists in conversation history.");
            }

            var snapshot = functionCalls.Clone();

            var message = new Message(ActorRole.Assistant, content ?? string.Empty)
            {
                FunctionCallBatch = snapshot,
                Metadata = metadata == null
                    ? new Dictionary<string, object>()
                    : ObjectGraphSnapshot.CloneDictionary(metadata)
            };

            PopulateFunctionCallMetadata(message.Metadata, snapshot);
            ActivateChat.Messages.Add(message);
            return message;
        }

        protected Message AddFunctionResultBatchToHistory(
            FunctionCallResultBatch functionResults,
            Dictionary<string, object>? metadata = null)
        {
            ValidateFunctionResultsAgainstHistory(functionResults);
            var snapshot = functionResults.Clone();
            foreach (var result in snapshot.Results)
                result.Content ??= string.Empty;

            var displayContent = snapshot.Results.Count == 1
                ? snapshot.Results[0].Content
                : string.Join(Environment.NewLine, snapshot.Results.Select(
                    result => $"{result.Call.Name}: {result.Content}"));

            var message = new Message(ActorRole.Function, displayContent)
            {
                FunctionCallResultBatch = snapshot,
                Metadata = metadata == null
                    ? new Dictionary<string, object>()
                    : ObjectGraphSnapshot.CloneDictionary(metadata)
            };

            PopulateFunctionResultMetadata(message.Metadata, snapshot);
            ActivateChat.Messages.Add(message);
            return message;
        }

        private static void NormalizeAndValidateFunctionCalls(FunctionCallBatch functionCalls)
        {
            if (functionCalls == null)
                throw new ArgumentNullException(nameof(functionCalls));

            if (functionCalls.Calls == null)
                throw new AIServiceException("The provider returned a null function-call collection.");

            if (functionCalls.Calls.Count == 0)
                throw new AIServiceException("A function-call execution batch cannot be empty.");

            var callIds = new HashSet<string>(StringComparer.Ordinal);
            var callIndexes = new HashSet<int>();
            for (var index = 0; index < functionCalls.Calls.Count; index++)
            {
                var functionCall = functionCalls.Calls[index]
                    ?? throw new AIServiceException($"The provider returned a null function call at index {index}.");

                if (string.IsNullOrWhiteSpace(functionCall.Name))
                    throw new AIServiceException($"The provider returned a function call without a name at index {index}.");

                if (functionCall.Index < 0)
                {
                    throw new AIServiceException(
                        $"The provider returned a negative function-call index {functionCall.Index} at position {index}.");
                }

                if (!callIndexes.Add(functionCall.Index))
                {
                    throw new AIServiceException(
                        $"The provider returned duplicate function-call index {functionCall.Index} at position {index}.");
                }

                if (!string.IsNullOrWhiteSpace(functionCall.Id) &&
                    !callIds.Add(functionCall.Id))
                {
                    throw new AIServiceException(
                        $"The provider returned duplicate function-call ID '{functionCall.Id}' at index {index}.");
                }
            }

            // Normalize only after the complete batch has passed validation. A malformed later
            // call must not partially rewrite earlier caller/provider objects.
            if (string.IsNullOrWhiteSpace(functionCalls.Id))
                functionCalls.Id = Guid.NewGuid().ToString();

            foreach (var functionCall in functionCalls.Calls)
            {
                functionCall.Arguments ??= new Dictionary<string, object>();
                if (!string.IsNullOrWhiteSpace(functionCall.Id))
                    continue;

                string generatedId;
                do
                {
                    generatedId = $"call_{Guid.NewGuid():N}";
                }
                while (!callIds.Add(generatedId));

                functionCall.Id = generatedId;
            }
        }

        private void ValidateFunctionResultsAgainstHistory(FunctionCallResultBatch functionResults)
        {
            if (functionResults == null)
                throw new ArgumentNullException(nameof(functionResults));

            if (string.IsNullOrWhiteSpace(functionResults.FunctionCallBatchId))
                throw new AIServiceException("A function-result batch must identify its function-call batch.");

            if (functionResults.Results == null || functionResults.Results.Count == 0)
                throw new AIServiceException("A function-result batch cannot be null or empty.");

            var expectedBatch = ActivateChat.Messages
                .Select(message => message.FunctionCallBatch)
                .LastOrDefault(batch => batch?.Id == functionResults.FunctionCallBatchId);
            if (expectedBatch == null)
            {
                throw new AIServiceException(
                    $"Function-result batch '{functionResults.FunctionCallBatchId}' has no matching call batch in history.");
            }

            if (ActivateChat.Messages.Any(message =>
                message.FunctionCallResultBatch?.FunctionCallBatchId == functionResults.FunctionCallBatchId))
            {
                throw new AIServiceException(
                    $"Function-result batch '{functionResults.FunctionCallBatchId}' already exists in conversation history.");
            }

            if (expectedBatch.Calls.Count != functionResults.Results.Count)
            {
                throw new AIServiceException(
                    $"Function-result batch '{functionResults.FunctionCallBatchId}' contains " +
                    $"{functionResults.Results.Count} results for {expectedBatch.Calls.Count} calls.");
            }

            for (var index = 0; index < functionResults.Results.Count; index++)
            {
                var result = functionResults.Results[index]
                    ?? throw new AIServiceException($"The function-result batch contains a null result at index {index}.");
                var actualCall = result.Call
                    ?? throw new AIServiceException($"The function result at index {index} has no function call.");
                var expectedCall = expectedBatch.Calls[index];

                if (!string.Equals(expectedCall.Id, actualCall.Id, StringComparison.Ordinal) ||
                    !string.Equals(expectedCall.Name, actualCall.Name, StringComparison.Ordinal) ||
                    expectedCall.Index != actualCall.Index ||
                    expectedCall.Source != actualCall.Source)
                {
                    throw new AIServiceException(
                        $"Function result at index {index} does not match its ordered call batch.");
                }
            }
        }

        private static void PopulateFunctionCallMetadata(
            Dictionary<string, object> metadata,
            FunctionCallBatch functionCalls)
        {
            metadata[MessageMetadataKeys.MessageType] = "function_call";
            metadata[MessageMetadataKeys.FunctionBatchId] = functionCalls.Id;
            metadata[MessageMetadataKeys.FunctionCount] = functionCalls.Calls.Count;

            if (functionCalls.Calls.Count == 0)
                return;

            var firstCall = functionCalls.Calls[0];
            metadata[MessageMetadataKeys.FunctionId] = firstCall.Id;
            metadata[MessageMetadataKeys.FunctionSource] = firstCall.Source;
            metadata[MessageMetadataKeys.FunctionName] = firstCall.Name;
            metadata[MessageMetadataKeys.FunctionArguments] = JsonSerializer.Serialize(firstCall.Arguments);
        }

        private static void PopulateFunctionResultMetadata(
            Dictionary<string, object> metadata,
            FunctionCallResultBatch functionResults)
        {
            metadata[MessageMetadataKeys.MessageType] = "function_result";
            metadata[MessageMetadataKeys.FunctionBatchId] = functionResults.FunctionCallBatchId;
            metadata[MessageMetadataKeys.FunctionCount] = functionResults.Results.Count;

            if (functionResults.Results.Count == 0)
                return;

            var firstCall = functionResults.Results[0].Call;
            metadata[MessageMetadataKeys.FunctionId] = firstCall.Id;
            metadata[MessageMetadataKeys.FunctionSource] = firstCall.Source;
            metadata[MessageMetadataKeys.FunctionName] = firstCall.Name;
        }

        /// <summary>
        /// Creates HTTP request with function definitions
        /// </summary>
        protected abstract HttpRequestMessage CreateFunctionMessageRequest();

        /// <summary>
        /// Extracts every function call from one API response.
        /// </summary>
        protected abstract (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response);

        #endregion
    }
}
