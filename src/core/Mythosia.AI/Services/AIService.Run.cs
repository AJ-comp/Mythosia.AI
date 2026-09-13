using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        private int _activeRun;
        private string? _runRequestMessageId;
        private AIRequestContext? _runEffectiveRequestContext;

        /// <summary>Starts one request with optional text observation and a separately awaitable result.</summary>
        /// <remarks>
        /// Only one StartRunAsync request may be active on a service instance. Do not mix a running
        /// request with legacy request methods or mutate the service's conversation/configuration.
        /// The text callback is registered before production; it must not depend on the returned
        /// run variable already being assigned. A callback exception fails and cancels the run.
        /// Built-in text, image, and audio payloads are copied at startup. Custom MessageContent
        /// implementations are preserved by reference and must remain immutable until cleanup.
        /// </remarks>
        public Task<AIRun> StartRunAsync(string prompt, Action<string>? onText = null,
            StreamOptions? options = null, AIRequestContext? context = null,
            CancellationToken cancellationToken = default)
        {
            if (prompt == null) throw new ArgumentNullException(nameof(prompt));
            return StartRunAsync(new Message(ActorRole.User, prompt), onText, options, context, cancellationToken);
        }

        /// <summary>Starts a message request; output selection does not disable tool execution.</summary>
        public Task<AIRun> StartRunAsync(Message message, Action<string>? onText = null,
            StreamOptions? options = null, AIRequestContext? context = null,
            CancellationToken cancellationToken = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _activeRun, 1, 0) != 0)
                throw new InvalidOperationException("A run is already active on this service. Await its cleanup before starting another run.");

            try
            {
                using var requestScope = BeginRequestSettingsScope();
                // Capture pending per-call configuration before returning control or starting a worker.
                var policy = GetExecutionPolicy();
                if (policy.MaxRounds <= 0)
                    throw new ArgumentOutOfRangeException(nameof(policy.MaxRounds), "A run requires at least one LLM round.");
                var capturedMessage = CaptureRunMessage(message);
                var capturedFeatures = CaptureRequestFeatures(capturedMessage);
                var capturedContext = context == null ? null : new AIRequestContext
                {
                    SystemMessagePrefix = context.SystemMessagePrefix,
                    SystemMessageSuffix = context.SystemMessageSuffix,
                    RequestMessageOverride = context.RequestMessageOverride == null ? null : CaptureRunMessage(context.RequestMessageOverride),
                    AdditionalMessages = context.AdditionalMessages?.Select(CaptureRunMessage).ToArray()
                };
                var observationOptions = (options ?? StreamOptions.WithFunctions).Clone();
                return StartCapturedRunAsync(capturedMessage, policy, onText, observationOptions,
                    capturedContext, capturedFeatures, cancellationToken);
            }
            catch
            {
                Volatile.Write(ref _activeRun, 0);
                throw;
            }
        }

        private static Message CaptureRunMessage(Message message)
        {
            var captured = message.Clone();
            captured.Content = message.Content;
            captured.Contents = message.Contents.Select(content =>
            {
                // Do not erase custom subclass behavior by converting it to a built-in base type.
                if (content.GetType() == typeof(TextContent))
                    return (MessageContent)new TextContent(((TextContent)content).Text);
                if (content.GetType() == typeof(ImageContent))
                {
                    var image = (ImageContent)content;
                    return new ImageContent(image.Url ?? string.Empty)
                    {
                        Url = image.Url,
                        Data = image.Data == null ? null : (byte[])image.Data.Clone(),
                        MimeType = image.MimeType,
                        IsHighDetail = image.IsHighDetail
                    };
                }
                if (content.GetType() == typeof(AudioContent))
                {
                    var audio = (AudioContent)content;
                    return new AudioContent(Array.Empty<byte>(), audio.MimeType ?? string.Empty)
                    {
                        Data = audio.Data == null ? null : (byte[])audio.Data.Clone(),
                        MimeType = audio.MimeType,
                        Duration = audio.Duration
                    };
                }
                return content;
            }).ToList();
            return captured;
        }

        private async Task<AIRun> StartCapturedRunAsync(Message message, FunctionCallingPolicy policy,
            Action<string>? onText, StreamOptions observationOptions, AIRequestContext? context,
            RequestFeatureExecution features, CancellationToken cancellationToken)
        {
            using var featureScope = UseRequestFeatureExecution(features);
            var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationTokenSource? sessionCancellation = null;
            RunSession? session = null;
            try
            {
                sessionCancellation = CreateRequestTimeoutCts(policy, runCancellation.Token);
                var startupTimeoutSeconds = ResolveRequestTimeoutSeconds(policy);
                var executionOptions = observationOptions.Clone();
                executionOptions.IncludeFunctionCalls = true;
                executionOptions.TextOnly = false;
                executionOptions.IncludeMetadata = true;
                try
                {
                    session = await CreateRunSessionAsync(message, executionOptions, context,
                        sessionCancellation.Token).ConfigureAwait(false);
                    sessionCancellation.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException exception) when (!runCancellation.IsCancellationRequested &&
                    sessionCancellation.IsCancellationRequested && startupTimeoutSeconds.HasValue)
                {
                    throw new AIServiceException($"Request timeout after {startupTimeoutSeconds} seconds", exception);
                }
                // One deadline covers preparation and execution, including providers that replace
                // StreamCoreAsync without creating their own policy timeout source.
                var run = new ServiceRun(this, session, policy, observationOptions, onText,
                    runCancellation, sessionCancellation, message.Id, startupTimeoutSeconds, features);
                run.Start();
                return run;
            }
            catch (Exception startupException)
            {
                var failures = new List<Exception> { startupException };
                try
                {
                    try { runCancellation.Cancel(); }
                    catch (Exception cancellationException) { failures.Add(cancellationException); }

                    if (session != null)
                    {
                        try { await session.DisposeAsync().ConfigureAwait(false); }
                        catch (Exception disposalException) { failures.Add(disposalException); }
                    }
                }
                finally
                {
                    sessionCancellation?.Dispose();
                    runCancellation.Dispose();
                    Volatile.Write(ref _activeRun, 0);
                }
                if (failures.Count > 1)
                    throw new AggregateException("Run startup failed and cleanup also failed.", failures);
                throw;
            }
        }

        /// <summary>Creates a provider session for a run without changing legacy request transports.</summary>
        protected virtual Task<RunSession> CreateRunSessionAsync(Message message,
            StreamOptions executionOptions, AIRequestContext? context, CancellationToken cancellationToken)
            => Task.FromResult<RunSession>(new LegacyRunSession(this, message, executionOptions, context));

        /// <summary>Resolves the explicit model ID sent for the captured run request.</summary>
        /// <remarks>Override when provider options replace the configured model or translate it to a wire ID.
        /// Return null when the request delegates model selection without a single model ID.
        /// Called within the captured request settings and provider-feature scope.</remarks>
        protected virtual string? GetRunRequestedModel() => RequestModel;

        /// <summary>Provider extension point for run output, additional input, and transport cleanup.</summary>
        protected abstract class RunSession : IAsyncDisposable
        {
            public virtual bool CanSteer => false;
            /// <summary>Produces execution events including a final Completion before successful cleanup.</summary>
            /// <remarks>Custom sessions should attach aggregate usage, final response model and reason,
            /// and a one-based final RoundIndex to Completion. Missing data remains unavailable in Result.
            /// If aggregate usage is absent, indexed RoundUsage snapshots are accumulated once per index.</remarks>
            public abstract IAsyncEnumerable<StreamingContent> StreamAsync(CancellationToken cancellationToken);
            public virtual Task SteerAsync(string instruction, CancellationToken cancellationToken)
                => throw new NotSupportedException("This model/provider does not support steering a running request.");
            public virtual ValueTask DisposeAsync() => default;
        }

        private sealed class LegacyRunSession : RunSession
        {
            private readonly AIService _service;
            private readonly Message _message;
            private readonly StreamOptions _options;
            private readonly AIRequestContext? _context;

            public LegacyRunSession(AIService service, Message message, StreamOptions options, AIRequestContext? context)
            {
                _service = service;
                _message = message;
                _options = options;
                _context = context;
            }

            public override IAsyncEnumerable<StreamingContent> StreamAsync(CancellationToken cancellationToken)
                => _service.StreamAsync(_message, _options, _context, cancellationToken);
        }

        private sealed class ServiceRun : AIRun
        {
            private const int ObservationCapacity = 1024;
            private readonly AIService _service;
            private readonly RunSession _session;
            private readonly FunctionCallingPolicy _policy;
            private readonly StreamOptions _options;
            private readonly Action<string>? _onText;
            private readonly CancellationTokenSource _cancellation;
            private readonly CancellationTokenSource _sessionCancellation;
            private readonly string _requestMessageId;
            private readonly int? _timeoutSeconds;
            private readonly string _provider;
            private readonly string? _requestedModel;
            private readonly TaskCompletionSource<AIRunResult> _result = new TaskCompletionSource<AIRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Channel<StreamingContent> _output = Channel.CreateBounded<StreamingContent>(new BoundedChannelOptions(ObservationCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = false
            });
            private readonly SemaphoreSlim _steerGate = new SemaphoreSlim(1, 1);
            private Task _producer = Task.CompletedTask;
            private int _readerClaimed;
            private int _readerStarted;
            private int _observationStopped;
            private int _disposed;
            private int _finished;
            private readonly RequestFeatureExecution _features;

            public ServiceRun(AIService service, RunSession session, FunctionCallingPolicy policy,
                StreamOptions options, Action<string>? onText, CancellationTokenSource cancellation,
                CancellationTokenSource sessionCancellation, string requestMessageId, int? timeoutSeconds,
                RequestFeatureExecution features)
            {
                _service = service;
                _session = session;
                _policy = policy;
                _options = options;
                _onText = onText;
                _cancellation = cancellation;
                _sessionCancellation = sessionCancellation;
                _requestMessageId = requestMessageId;
                _timeoutSeconds = timeoutSeconds;
                _features = features;
                _provider = service.Provider;
                _requestedModel = service.GetRunRequestedModel();
            }

            public override Task<AIRunResult> Result => _result.Task;
            public override IReadOnlyList<AICitation> Citations => _features.Snapshot();
            public override bool CanSteer => _session.CanSteer;

            internal void Start() => _producer = Task.Run(ProduceAsync);

            public override IAsyncEnumerable<StreamingContent> StreamAsync(CancellationToken cancellationToken = default)
            {
                if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AIRun));
                if (Interlocked.CompareExchange(ref _readerClaimed, 1, 0) != 0)
                    throw new InvalidOperationException("A run supports only one output reader.");
                return ReadOutputAsync(cancellationToken);
            }

            private async IAsyncEnumerable<StreamingContent> ReadOutputAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                // An IAsyncEnumerable can create multiple enumerators even when StreamAsync
                // was called once. Reject those before they can consume or stop the first reader.
                if (Interlocked.CompareExchange(ref _readerStarted, 1, 0) != 0)
                    throw new InvalidOperationException("A run supports only one output reader.");
                try
                {
                    await foreach (var item in _output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                        yield return item;
                }
                finally
                {
                    Volatile.Write(ref _observationStopped, 1);
                }
            }

            public override async Task SteerAsync(string instruction, CancellationToken cancellationToken = default)
            {
                if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("An instruction is required.", nameof(instruction));
                if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AIRun));
                if (!CanSteer) throw new NotSupportedException("This model/provider does not support steering a running request.");
                if (Volatile.Read(ref _finished) != 0) throw new InvalidOperationException("The run has already finished.");
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionCancellation.Token);
                await _steerGate.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref _finished) != 0) throw new InvalidOperationException("The run has already finished.");
                    await _session.SteerAsync(instruction, linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    _steerGate.Release();
                }
            }

            public override void Cancel()
            {
                try
                {
                    if (Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _finished) == 0)
                        _cancellation.Cancel();
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
                {
                    // A concurrent DisposeAsync has already cancelled and drained this run.
                }
            }

            public override async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    try
                    {
                        if (Volatile.Read(ref _finished) == 0) _cancellation.Cancel();
                    }
                    finally
                    {
                        try { await _producer.ConfigureAwait(false); }
                        finally { _cancellation.Dispose(); }
                    }
                }
                else
                {
                    await _producer.ConfigureAwait(false);
                }
            }

            private async Task ProduceAsync()
            {
                using var featureScope = _service.UseRequestFeatureExecution(_features);
                var text = new StringBuilder();
                TokenUsage? usage = null;
                var roundUsages = new Dictionary<int, TokenUsage>();
                var roundCount = 0;
                string? responseModel = null;
                string? rawFinishReason = null;
                var finishReason = AIFinishReason.Unknown;
                Exception? failure = null;
                var completed = false;
                _service.SetExecutionSetting(nameof(DefaultPolicy), _policy.Clone());
                _service._runRequestMessageId = _requestMessageId;
                try
                {
                    _sessionCancellation.Token.ThrowIfCancellationRequested();
                    var enumerator = _session.StreamAsync(_sessionCancellation.Token).GetAsyncEnumerator(_sessionCancellation.Token);
                    try
                    {
                        while (await MoveNextWithRunContextAsync(enumerator).ConfigureAwait(false))
                        {
                            var item = enumerator.Current;
                            _sessionCancellation.Token.ThrowIfCancellationRequested();
                            if (item.Type == StreamingContentType.Text && item.Content != null)
                            {
                                text.Append(item.Content);
                                _onText?.Invoke(item.Content);
                            }
                            if (item.Citation != null) _features.Add(item.Citation);
                            if (item.RoundIndex.HasValue)
                                roundCount = Math.Max(roundCount, item.RoundIndex.Value);
                            if (item.Type == StreamingContentType.RoundUsage && item.RoundIndex.HasValue && item.Usage != null)
                                roundUsages[item.RoundIndex.Value] = CopyTokenUsage(item.Usage);
                            if (item.Type == StreamingContentType.Completion)
                            {
                                completed = true;
                                // Completion usage is already aggregated by the round loop. Never
                                // add it to the per-round events (or to repeated completion snapshots).
                                if (item.Usage != null) usage = CopyTokenUsage(item.Usage);
                                responseModel = item.ResponseModel;
                                finishReason = item.FinishReason;
                                rawFinishReason = item.RawFinishReason;
                            }
                            Publish(item);
                            if (item.Type == StreamingContentType.Error)
                                throw new AIServiceException(item.Content ?? "The run failed.",
                                    item.Metadata == null ? string.Empty : System.Text.Json.JsonSerializer.Serialize(item.Metadata), _service.Provider);
                        }
                    }
                    finally
                    {
                        await DisposeWithRunContextAsync(enumerator).ConfigureAwait(false);
                    }
                    _sessionCancellation.Token.ThrowIfCancellationRequested();
                    if (!completed) throw new AIServiceException("The run ended without a completion event.", string.Empty, _service.Provider);
                }
                catch (Exception exception)
                {
                    failure = exception is OperationCanceledException &&
                        !_cancellation.IsCancellationRequested && _sessionCancellation.IsCancellationRequested && _timeoutSeconds.HasValue
                        ? new AIServiceException($"Request timeout after {_timeoutSeconds} seconds", exception)
                        : exception;
                    try { _cancellation.Cancel(); }
                    catch (Exception cancellationException)
                    {
                        failure = new AggregateException(failure, cancellationException);
                    }
                }
                finally
                {
                    Volatile.Write(ref _finished, 1);
                    try
                    {
                        await _session.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                    {
                        failure = failure == null ? cleanupException : new AggregateException(failure, cleanupException);
                    }
                    finally
                    {
                        _sessionCancellation.Dispose();
                        _service._runRequestMessageId = null;
                        _service._runEffectiveRequestContext = null;
                        Volatile.Write(ref _service._activeRun, 0);
                    }
                    AIRunResult? result = null;
                    if (failure == null)
                    {
                        try
                        {
                            result = new AIRunResult(text.ToString(),
                                usage ?? SumRunRoundUsage(roundUsages.Values), _features.Snapshot(),
                                _provider, _requestedModel, responseModel, roundCount, finishReason, rawFinishReason);
                        }
                        catch (Exception resultException)
                        {
                            // Result assembly can fail too, for example when a custom stream's
                            // indexed usage overflows. Always settle Result after cleanup.
                            failure = resultException;
                        }
                    }
                    _output.Writer.TryComplete(failure);
                    if (failure is OperationCanceledException cancelled)
                        _result.TrySetCanceled(cancelled.CancellationToken);
                    else if (failure != null)
                        _result.TrySetException(failure);
                    else
                        _result.TrySetResult(result!);
                }
            }

            private async ValueTask<bool> MoveNextWithRunContextAsync(IAsyncEnumerator<StreamingContent> enumerator)
            {
                // AsyncLocal values set inside an iterator do not flow back through yield return.
                // Reapply the resolved run context on each advancement, without changing legacy APIs.
                var previous = _service._currentRequestContext.Value;
                if (_service._runEffectiveRequestContext != null)
                    _service._currentRequestContext.Value = _service._runEffectiveRequestContext;
                try { return await enumerator.MoveNextAsync().ConfigureAwait(false); }
                finally { _service._currentRequestContext.Value = previous; }
            }

            private async ValueTask DisposeWithRunContextAsync(IAsyncEnumerator<StreamingContent> enumerator)
            {
                var previous = _service._currentRequestContext.Value;
                if (_service._runEffectiveRequestContext != null)
                    _service._currentRequestContext.Value = _service._runEffectiveRequestContext;
                try { await enumerator.DisposeAsync().ConfigureAwait(false); }
                finally { _service._currentRequestContext.Value = previous; }
            }

            private void Publish(StreamingContent item)
            {
                if (Volatile.Read(ref _observationStopped) != 0) return;
                if (_options.TextOnly && item.Type != StreamingContentType.Text) return;
                if (!_options.IncludeFunctionCalls && (item.Type == StreamingContentType.FunctionCall || item.Type == StreamingContentType.FunctionResult)) return;
                if (!_options.IncludeReasoning && item.Type == StreamingContentType.Reasoning) return;
                var observed = new StreamingContent
                {
                    Type = item.Type,
                    Content = item.Content,
                    Metadata = _options.IncludeMetadata && item.Metadata != null ? new Dictionary<string, object>(item.Metadata) : null,
                    Usage = item.Usage == null ? null : CopyTokenUsage(item.Usage),
                    ResponseModel = item.ResponseModel,
                    FinishReason = item.FinishReason,
                    RawFinishReason = item.RawFinishReason,
                    RoundIndex = item.RoundIndex,
                    IsFinalRound = item.IsFinalRound,
                    FunctionCall = item.FunctionCall?.Clone(),
                    FunctionResult = item.FunctionResult?.Clone(),
                    FunctionCallBatchId = item.FunctionCallBatchId,
                    Citation = item.Citation?.Clone()
                };
                if (!_output.Writer.TryWrite(observed))
                {
                    Volatile.Write(ref _observationStopped, 1);
                    _output.Writer.TryComplete(new InvalidOperationException(
                        "The run output buffer exceeded 1,024 unread events. Observe output promptly or use a startup callback. Execution and Result remain available."));
                }
            }

            private static TokenUsage? SumRunRoundUsage(IEnumerable<TokenUsage> rounds)
            {
                TokenUsage? total = null;
                foreach (var round in rounds)
                {
                    total ??= new TokenUsage();
                    checked
                    {
                        total.InputTokens += round.InputTokens;
                        total.OutputTokens += round.OutputTokens;
                        total.TotalTokens += round.TotalTokens;
                        total.CachedInputTokens += round.CachedInputTokens;
                        total.CacheCreationTokens += round.CacheCreationTokens;
                        total.ReasoningTokens += round.ReasoningTokens;
                    }
                }
                return total;
            }
        }
    }
}
