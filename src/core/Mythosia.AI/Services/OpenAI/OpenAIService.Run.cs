using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        private readonly AsyncLocal<OpenAIRunSession?> _openAIRunSession = new AsyncLocal<OpenAIRunSession?>();

        protected override async Task<RunSession> CreateRunSessionAsync(
            Message message, StreamOptions executionOptions, AIRequestContext? context,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(Model, "gpt-6-astra", StringComparison.OrdinalIgnoreCase) &&
                !Model.StartsWith("gpt-6-astra-", StringComparison.OrdinalIgnoreCase))
                return await base.CreateRunSessionAsync(message, executionOptions, context, cancellationToken).ConfigureAwait(false);

            var socket = await ConnectRunWebSocketAsync(cancellationToken).ConfigureAwait(false);
            return new OpenAIRunSession(this, socket, message, executionOptions, context, cancellationToken);
        }

        /// <summary>Connects one Responses WebSocket for an Astra run. Override for a custom transport.</summary>
        protected virtual async Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + ApiKey);
            try
            {
                await socket.ConnectAsync(new Uri("wss://api.openai.com/v1/responses"), cancellationToken).ConfigureAwait(false);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        protected override bool HasPendingRunContinuation => _openAIRunSession.Value?.HasPendingContinuation == true;
        protected override bool FunctionResultsRequireContinuation => _openAIRunSession.Value == null;

        protected override Task<IReadOnlyList<FunctionCallResultBatch>> CollectPendingStreamingFunctionResultsAsync(
            CancellationToken cancellationToken) => _openAIRunSession.Value is OpenAIRunSession session
                ? session.WaitForResultOrSteeringAsync(cancellationToken)
                : base.CollectPendingStreamingFunctionResultsAsync(cancellationToken);

        protected override async Task<HttpResponseMessage> SendStreamingRequestAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var session = _openAIRunSession.Value;
            if (session == null)
                return await base.SendStreamingRequestAsync(request, cancellationToken).ConfigureAwait(false);

            await session.SendRoundAsync(request, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        }

        protected override IAsyncEnumerable<string> ReadStreamingResponseLinesAsync(
            HttpResponseMessage response, StreamDiagnostics diagnostics, CancellationToken cancellationToken)
            => _openAIRunSession.Value is OpenAIRunSession session
                ? session.ReadRoundAsync(diagnostics, cancellationToken)
                : base.ReadStreamingResponseLinesAsync(response, diagnostics, cancellationToken);

        private sealed class OpenAIRunSession : RunSession
        {
            private readonly OpenAIService _service;
            private readonly WebSocket _socket;
            private readonly Message _message;
            private readonly StreamOptions _options;
            private readonly AIRequestContext? _context;
            private readonly CancellationTokenSource _lifetime;
            private readonly Channel<SocketEvent> _events = Channel.CreateBounded<SocketEvent>(
                new BoundedChannelOptions(1024) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
            private readonly SemaphoreSlim _steerLock = new SemaphoreSlim(1, 1);
            private readonly object _stateLock = new object();
            private readonly TaskCompletionSource<string> _firstResponse = NewCompletion<string>();
            private TaskCompletionSource<string> _nextResponse = NewCompletion<string>();
            private TaskCompletionSource<bool> _resumeRequested = NewCompletion<bool>();
            private readonly HashSet<string> _continuationResponses = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _steeredResponses = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _sentOutputs = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _outstandingOutputs = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _requiredOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _acceptedInputs = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly List<string> _historyInputs = new List<string>();
            private readonly Queue<StreamingContent> _toolEvents = new Queue<StreamingContent>();
            private readonly Task _receiver;
            private TaskCompletionSource<bool>? _pendingAcceptance;
            private string? _pendingInstruction;
            private string? _pendingTarget;
            private string? _currentResponseId;
            private string? _lastRoundResponseId;
            private bool _currentResponseFinished;
            private bool _sentInitial;
            private bool _resumeForSteering;
            private bool _disposed;
            private Exception? _failure;
            private long _bufferedEventBytes;

            public OpenAIRunSession(OpenAIService service, WebSocket socket, Message message,
                StreamOptions options, AIRequestContext? context, CancellationToken cancellationToken)
            {
                _service = service;
                _socket = socket;
                _message = message;
                _options = options;
                _context = context;
                _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _receiver = ReceiveAsync();
            }

            public override bool CanSteer => true;

            public bool HasPendingContinuation
            {
                get
                {
                    lock (_stateLock)
                        return _resumeForSteering || (_lastRoundResponseId != null && _continuationResponses.Contains(_lastRoundResponseId)) ||
                               _service.ActivateChat.Messages.Where(message => message.FunctionCallResultBatch != null)
                                   .SelectMany(message => message.FunctionCallResultBatch!.Results)
                                   .Any(result => _outstandingOutputs.Contains(result.Call.Id) && !_sentOutputs.Contains(result.Call.Id));
                }
            }

            public bool IsSteeredResponse(string responseId)
            {
                lock (_stateLock) return _steeredResponses.Contains(responseId);
            }

            public override IAsyncEnumerable<StreamingContent> StreamAsync(CancellationToken cancellationToken)
                => new ScopedStream(this, _service.StreamAsync(_message, _options, _context, cancellationToken));

            public override async Task SteerAsync(string instruction, CancellationToken cancellationToken)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                await WaitWithCancellationAsync(_firstResponse.Task, linked.Token).ConfigureAwait(false);
                await _steerLock.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    Task<bool>? previousAcceptance;
                    lock (_stateLock) previousAcceptance = _pendingAcceptance?.Task;
                    if (previousAcceptance != null)
                        await WaitWithCancellationAsync(previousAcceptance, linked.Token).ConfigureAwait(false);
                    while (true)
                    {
                        Task<string>? nextResponse = null;
                        lock (_stateLock)
                        {
                            ThrowIfUnavailable();
                            if (_currentResponseFinished && !_requiredOutputs.Values.Contains(_currentResponseId!))
                            {
                                if (_outstandingOutputs.Count == 0 && !_continuationResponses.Contains(_currentResponseId!))
                                    throw new InvalidOperationException("The response has already finished and cannot accept steering.");
                                nextResponse = _nextResponse.Task;
                                if (!_continuationResponses.Contains(_currentResponseId!))
                                {
                                    // A completed native-async response is no longer steerable.
                                    // Wake the common loop (without canceling tools), then target
                                    // the next response only after its response.created arrives.
                                    _resumeForSteering = true;
                                    _resumeRequested.TrySetResult(true);
                                }
                            }
                        }
                        if (nextResponse == null) break;
                        await WaitWithCancellationAsync(nextResponse, linked.Token).ConfigureAwait(false);
                    }
                    string target;
                    TaskCompletionSource<bool> acceptance;
                    lock (_stateLock)
                    {
                        ThrowIfUnavailable();
                        target = _currentResponseId!;
                        if (_currentResponseFinished && !_requiredOutputs.Values.Contains(target))
                            throw new InvalidOperationException("The response finished before the additional instruction could be sent.");
                        acceptance = NewCompletion<bool>();
                        _pendingAcceptance = acceptance;
                        _pendingInstruction = instruction;
                        _pendingTarget = target;
                        // Record before sending: a racing completed response must not finish the run.
                        _continuationResponses.Add(target);
                    }

                    try
                    {
                        await SendAsync(new Dictionary<string, object>
                        {
                            ["type"] = "response.steer", ["previous_response_id"] = target, ["input"] = instruction
                        }, linked.Token).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        Fail(exception);
                        throw;
                    }
                    // Cancellation after sending cannot retract the input. Keep the acknowledgement
                    // correlated until it arrives; canceling the caller only stops its wait.
                    await WaitWithCancellationAsync(acceptance.Task, linked.Token).ConfigureAwait(false);
                }
                finally
                {
                    _steerLock.Release();
                }
            }

            public async Task SendRoundAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var json = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var payload = document.RootElement.EnumerateObject()
                    .Where(property => property.Name != "stream" && property.Name != "background")
                    .ToDictionary(property => property.Name, property => (object)property.Value.Clone(), StringComparer.Ordinal);
                payload["type"] = "response.create";

                List<JsonElement>? outputs = null;
                lock (_stateLock)
                {
                    ThrowIfUnavailable();
                    if (_sentInitial)
                    {
                        var automatic = _lastRoundResponseId != null && _continuationResponses.Contains(_lastRoundResponseId);
                        if (automatic && (!_requiredOutputs.Values.Contains(_lastRoundResponseId!) || HasMissingRequiredOutputs()))
                            return; // The server owns the accepted steering continuation.

                        outputs = new List<JsonElement>();
                        if (document.RootElement.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in input.EnumerateArray())
                            {
                                if (GetString(item, "type") != "function_call_output") continue;
                                var callId = GetString(item, "call_id");
                                if (callId != null && _outstandingOutputs.Contains(callId) && !_sentOutputs.Contains(callId)) outputs.Add(item.Clone());
                            }
                        }
                        payload["input"] = outputs;
                        payload["previous_response_id"] = _lastRoundResponseId ?? _currentResponseId!;
                    }
                }

                await SendAsync(payload, cancellationToken).ConfigureAwait(false);
                lock (_stateLock)
                {
                    if (!_sentInitial && document.RootElement.TryGetProperty("input", out var initialInput) &&
                        initialInput.ValueKind == JsonValueKind.Array)
                        foreach (var item in initialInput.EnumerateArray())
                            if (GetString(item, "type") == "function_call_output" && GetString(item, "call_id") is string callId)
                                _sentOutputs.Add(callId);
                    _sentInitial = true;
                    if (outputs != null)
                        foreach (var output in outputs)
                        {
                            var callId = GetString(output, "call_id")!;
                            _sentOutputs.Add(callId);
                            _outstandingOutputs.Remove(callId);
                            _requiredOutputs.Remove(callId);
                        }
                }
            }

            public async Task<IReadOnlyList<FunctionCallResultBatch>> WaitForResultOrSteeringAsync(CancellationToken cancellationToken)
            {
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task resume;
                lock (_stateLock) resume = _resumeRequested.Task;
                var results = _service.CollectAsyncFunctionResultsAsync(true, waiting.Token);
                if (await Task.WhenAny(results, resume).ConfigureAwait(false) == results)
                    return await results.ConfigureAwait(false);
                waiting.Cancel();
                try { return await results.ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { return Array.Empty<FunctionCallResultBatch>(); }
            }

            public async IAsyncEnumerable<string> ReadRoundAsync(
                StreamDiagnostics diagnostics, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await foreach (var line in ReadRoundCoreAsync(diagnostics, cancellationToken))
                        yield return line;
                }
                finally
                {
                    diagnostics.Elapsed = stopwatch.Elapsed;
                    try { _service.StreamCompleteCallback?.Invoke(diagnostics); } catch { }
                }
            }

            private async IAsyncEnumerable<string> ReadRoundCoreAsync(
                StreamDiagnostics diagnostics, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                while (await _events.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (_events.Reader.TryRead(out var item))
                    {
                        Interlocked.Add(ref _bufferedEventBytes, -item.ByteCount);
                        if (item.RequiresInput && HasUnsentRequiredOutputs())
                        {
                            var results = new List<FunctionCallResultBatch>();
                            while (HasMissingRequiredOutputs())
                            {
                                var available = await _service.CollectAsyncFunctionResultsAsync(true, cancellationToken).ConfigureAwait(false);
                                results.AddRange(available);
                                if (available.Count == 0 && HasMissingRequiredOutputs())
                                    throw new AIServiceException("OpenAI steering requested a tool result that this run cannot provide.");
                            }
                            foreach (var batch in results)
                                foreach (var result in batch.Results)
                                {
                                    _toolEvents.Enqueue(new StreamingContent
                                    {
                                        Type = StreamingContentType.FunctionResult,
                                        FunctionResult = result.Clone(),
                                        FunctionCallBatchId = batch.FunctionCallBatchId,
                                        Content = result.Content,
                                        Metadata = new Dictionary<string, object>
                                        {
                                            ["function_name"] = result.Call.Name,
                                            ["function_index"] = result.Call.Index,
                                            ["status"] = result.IsError ? "error" : "completed",
                                            ["result"] = result.Content
                                        }
                                    });
                                    yield return "data: {\"type\":\"mythosia.run.function_result\"}";
                                }
                            using var continuationRequest = _service.CreateFunctionMessageRequest();
                            await SendRoundAsync(continuationRequest, cancellationToken).ConfigureAwait(false);
                        }
                        if (item.ResponseCreated && item.ResponseId != null && IsSteeredResponse(item.ResponseId))
                        {
                            // Mutate history only on the common execution path, never in the receiver.
                            string[] inputs;
                            lock (_stateLock)
                            {
                                inputs = _historyInputs.ToArray();
                                _historyInputs.Clear();
                            }
                            foreach (var input in inputs)
                                _service.ActivateChat.Messages.Add(new Message(ActorRole.User, input));
                        }
                        if (item.Terminal)
                        {
                            lock (_stateLock) _lastRoundResponseId = item.ResponseId;
                        }
                        var line = "data: " + item.Json;
                        diagnostics.LinesRead++;
                        diagnostics.LastRawLine = line;
                        try { _service.StreamRawLineCallback?.Invoke(line); } catch { }
                        yield return line;
                        if (item.Terminal) yield break;
                    }
                }
                throw _failure ?? new AIServiceException("OpenAI WebSocket closed before the response finished.");
            }

            private async Task ReceiveAsync()
            {
                try
                {
                    var buffer = new byte[8192];
                    while (!_lifetime.IsCancellationRequested)
                    {
                        using var message = new MemoryStream();
                        WebSocketReceiveResult received;
                        do
                        {
                            received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _lifetime.Token).ConfigureAwait(false);
                            if (received.MessageType == WebSocketMessageType.Close)
                                throw new AIServiceException("OpenAI WebSocket disconnected; pending steering was not replayed.");
                            if (received.MessageType != WebSocketMessageType.Text)
                                throw new AIServiceException("OpenAI WebSocket returned a non-text event.");
                            if (message.Length + received.Count > 16 * 1024 * 1024)
                                throw new AIServiceException("OpenAI WebSocket event exceeded the supported size.");
                            message.Write(buffer, 0, received.Count);
                        } while (!received.EndOfMessage);

                        var json = Encoding.UTF8.GetString(message.ToArray());
                        using var parsed = JsonDocument.Parse(json);
                        var root = parsed.RootElement;
                        var type = GetString(root, "type");
                        var response = root.TryGetProperty("response", out var value) ? value : default;
                        var responseId = GetString(response, "id");
                        var terminal = type == "response.completed" || type == "response.incomplete" || type == "response.failed" || type == "error";
                        lock (_stateLock)
                        {
                            if (type == "response.created")
                            {
                                if (string.IsNullOrEmpty(responseId)) throw new JsonException("response.created is missing its response ID.");
                                if (_currentResponseId != null && _continuationResponses.Contains(_currentResponseId))
                                    _steeredResponses.Add(responseId!);
                                _currentResponseId = responseId;
                                _currentResponseFinished = false;
                                _resumeForSteering = false;
                                _resumeRequested = NewCompletion<bool>();
                                var nextResponse = _nextResponse;
                                _nextResponse = NewCompletion<string>();
                                nextResponse.TrySetResult(responseId!);
                                _firstResponse.TrySetResult(responseId!);
                            }
                            else if (type == "response.steer.accepted")
                            {
                                if (_pendingAcceptance == null || _pendingInstruction == null)
                                    throw new JsonException("An unexpected steering acknowledgement was received.");
                                var steer = root.GetProperty("steer");
                                if (GetString(steer, "previous_response_id") != _pendingTarget)
                                    throw new JsonException("A steering acknowledgement targeted a different response.");
                                var steerId = GetString(steer, "id") ?? throw new JsonException("A steering acknowledgement is missing its ID.");
                                if (_acceptedInputs.ContainsKey(steerId)) throw new JsonException("A steering acknowledgement reused its ID.");
                                _acceptedInputs[steerId] = _pendingInstruction;
                                _historyInputs.Add(_pendingInstruction);
                                _pendingInstruction = null;
                                _pendingTarget = null;
                                _pendingAcceptance.TrySetResult(true);
                                _pendingAcceptance = null;
                            }
                            else if (type == "response.steer.pending")
                            {
                                if (root.TryGetProperty("required_input", out var required) && required.ValueKind == JsonValueKind.Array)
                                    foreach (var input in required.EnumerateArray())
                                    {
                                        if (GetString(input, "type") != "function_call_output")
                                            throw new AIServiceException("OpenAI steering requires an unsupported client approval input.", json, "OpenAI");
                                        var callId = GetString(input, "call_id");
                                        var target = root.TryGetProperty("steer", out var pendingSteer)
                                            ? GetString(pendingSteer, "previous_response_id") : _currentResponseId;
                                        if (callId != null && !_sentOutputs.Contains(callId) && !_outstandingOutputs.Contains(callId))
                                            throw new AIServiceException("OpenAI steering requested an unknown tool-call ID.", json, "OpenAI");
                                        if (callId != null && target != null && !_sentOutputs.Contains(callId)) _requiredOutputs[callId] = target;
                                    }
                            }
                            else if (type == "response.steer.failed")
                                throw new AIServiceException("OpenAI could not apply the additional instruction; it was not replayed.", json, "OpenAI");

                            if (terminal)
                            {
                                if (type != "error" && (responseId == null || responseId != _currentResponseId))
                                    throw new JsonException("A WebSocket terminal event did not match the active response ID.");
                                _currentResponseFinished = true;
                                if (type == "response.incomplete" && IsSteeredTerminal(root) && responseId != null)
                                    _continuationResponses.Add(responseId);
                                if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                                    foreach (var item in output.EnumerateArray())
                                    {
                                        if (GetString(item, "type") != "function_call") continue;
                                        if (type == "response.incomplete" && GetString(item, "status") != "completed") continue;
                                        var callId = GetString(item, "call_id");
                                        if (callId != null && responseId != null && !_sentOutputs.Contains(callId))
                                        {
                                            _outstandingOutputs.Add(callId);
                                            if (!item.TryGetProperty("async", out var asyncFlag) || asyncFlag.ValueKind != JsonValueKind.True)
                                                _requiredOutputs[callId] = responseId;
                                        }
                                    }
                            }
                        }
                        var socketEvent = new SocketEvent(json, terminal, responseId,
                            type == "response.created", type == "response.steer.pending", message.Length);
                        if (Interlocked.Add(ref _bufferedEventBytes, message.Length) > 64 * 1024 * 1024 ||
                            !_events.Writer.TryWrite(socketEvent))
                            throw new AIServiceException("OpenAI WebSocket output buffering exceeded its limit; the run was stopped without dropping events.");
                    }
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }

            private void Fail(Exception exception)
            {
                lock (_stateLock)
                {
                    _failure ??= exception;
                    _firstResponse.TrySetException(exception);
                    _nextResponse.TrySetException(exception);
                    _resumeRequested.TrySetResult(true);
                    _pendingAcceptance?.TrySetException(exception);
                }
                _events.Writer.TryComplete(exception);
                _socket.Abort();
            }

            private async Task SendAsync(object payload, CancellationToken cancellationToken)
            {
                await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                    await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                }
                finally { _sendLock.Release(); }
            }

            public override async ValueTask DisposeAsync()
            {
                lock (_stateLock)
                {
                    if (_disposed) return;
                    _disposed = true;
                }
                _lifetime.Cancel();
                _socket.Abort();
                await _receiver.ConfigureAwait(false);
                _socket.Dispose();
                _lifetime.Dispose();
                _events.Writer.TryComplete();
                while (_events.Reader.TryRead(out _)) { }
                Interlocked.Exchange(ref _bufferedEventBytes, 0);
                _historyInputs.Clear();
                _acceptedInputs.Clear();
                _toolEvents.Clear();
            }

            private void ThrowIfUnavailable()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(OpenAIRunSession));
                if (_failure != null) throw _failure;
            }

            public StreamingContent TakeToolEvent() => _toolEvents.Dequeue();

            private bool HasMissingRequiredOutputs()
            {
                var ready = new HashSet<string>(_service.ActivateChat.Messages
                    .Where(message => message.FunctionCallResultBatch != null)
                    .SelectMany(message => message.FunctionCallResultBatch!.Results)
                    .Select(result => result.Call.Id), StringComparer.Ordinal);
                lock (_stateLock)
                    return _requiredOutputs.Any(pair => pair.Value == _lastRoundResponseId &&
                        !_sentOutputs.Contains(pair.Key) && !ready.Contains(pair.Key));
            }

            private bool HasUnsentRequiredOutputs()
            {
                lock (_stateLock)
                    return _requiredOutputs.Any(pair => pair.Value == _lastRoundResponseId && !_sentOutputs.Contains(pair.Key));
            }

            // Async iterators restore their caller's ExecutionContext after every yield. Scope
            // each MoveNext/Dispose explicitly so later rounds cannot fall back to HTTP.
            private sealed class ScopedStream : IAsyncEnumerable<StreamingContent>
            {
                private readonly OpenAIRunSession _session;
                private readonly IAsyncEnumerable<StreamingContent> _source;
                public ScopedStream(OpenAIRunSession session, IAsyncEnumerable<StreamingContent> source)
                { _session = session; _source = source; }
                public IAsyncEnumerator<StreamingContent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                    => new ScopedEnumerator(_session, _source.GetAsyncEnumerator(cancellationToken));
            }

            private sealed class ScopedEnumerator : IAsyncEnumerator<StreamingContent>
            {
                private readonly OpenAIRunSession _session;
                private readonly IAsyncEnumerator<StreamingContent> _source;
                public ScopedEnumerator(OpenAIRunSession session, IAsyncEnumerator<StreamingContent> source)
                { _session = session; _source = source; }
                public StreamingContent Current => _source.Current;
                public async ValueTask<bool> MoveNextAsync()
                {
                    var previous = _session._service._openAIRunSession.Value;
                    _session._service._openAIRunSession.Value = _session;
                    try { return await _source.MoveNextAsync().ConfigureAwait(false); }
                    finally { _session._service._openAIRunSession.Value = previous; }
                }
                public async ValueTask DisposeAsync()
                {
                    var previous = _session._service._openAIRunSession.Value;
                    _session._service._openAIRunSession.Value = _session;
                    try { await _source.DisposeAsync().ConfigureAwait(false); }
                    finally { _session._service._openAIRunSession.Value = previous; }
                }
            }

            private static TaskCompletionSource<T> NewCompletion<T>()
                => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
            {
                var canceled = NewCompletion<bool>();
                using (cancellationToken.Register(() => canceled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
                        cancellationToken.ThrowIfCancellationRequested();
                    return await task.ConfigureAwait(false);
                }
            }

            private sealed class SocketEvent
            {
                public SocketEvent(string json, bool terminal, string? responseId, bool responseCreated, bool requiresInput, long byteCount)
                { Json = json; Terminal = terminal; ResponseId = responseId; ResponseCreated = responseCreated; RequiresInput = requiresInput; ByteCount = byteCount; }
                public string Json { get; }
                public bool Terminal { get; }
                public string? ResponseId { get; }
                public bool ResponseCreated { get; }
                public bool RequiresInput { get; }
                public long ByteCount { get; }
            }
        }

        private static string? GetString(JsonElement element, string property)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        private static bool IsSteeredTerminal(JsonElement root)
            => root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object &&
               response.TryGetProperty("incomplete_details", out var details) && GetString(details, "reason") == "steered";
    }
}
