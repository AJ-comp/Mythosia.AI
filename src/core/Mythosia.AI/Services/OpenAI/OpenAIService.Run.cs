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
using System.Runtime.ExceptionServices;
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
            if (!IsKnownGpt6Model(RequestModel))
                return await base.CreateRunSessionAsync(message, executionOptions, context, cancellationToken).ConfigureAwait(false);

            // Keep the started run's capability tied to its captured model/mode, including
            // while the connection awaits, rather than later mutations of service defaults.
            var canSteer = SupportsGpt6Steering(RequestModel, RequestGpt6ReasoningMode);
            var socket = await ConnectRunWebSocketAsync(cancellationToken).ConfigureAwait(false);
            return new OpenAIRunSession(this, socket, message, executionOptions, context, canSteer, cancellationToken);
        }

        /// <summary>Connects one Responses WebSocket at the configured HTTP API base address. Override for a custom transport.</summary>
        protected virtual async Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
        {
            var endpoint = ResolveRunWebSocketUri(HttpClient.BaseAddress);
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + ApiKey);
            try
            {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        internal static Uri ResolveRunWebSocketUri(Uri? baseAddress)
        {
            if (baseAddress == null || !baseAddress.IsAbsoluteUri ||
                (baseAddress.Scheme != Uri.UriSchemeHttps && baseAddress.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException("OpenAI Run requires an absolute HTTP or HTTPS API base address.");

            // Match HttpClient's relative Responses request resolution, including proxy
            // path prefixes and trailing-slash semantics. Never fall back to another host.
            var responseUri = new Uri(baseAddress, "responses");
            return new UriBuilder(responseUri)
            {
                Scheme = responseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
                Port = responseUri.IsDefaultPort ? -1 : responseUri.Port
            }.Uri;
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
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_speedStreamState.Value != null)
                    _speedStreamState.Value.Observation = BeginProcessingObservation();
                return await base.SendStreamingRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }

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
            private readonly CancellationToken _requestCancellation;
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
            private HashSet<string>? _sendingOutputs;
            private string? _sendingAfterResponseId;
            private readonly Dictionary<string, string> _requiredOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _acceptedInputs = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<string>> _historyInputs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            private readonly Queue<StreamingContent> _toolEvents = new Queue<StreamingContent>();
            private readonly Queue<ProcessingObservation> _pendingProcessing = new Queue<ProcessingObservation>();
            private readonly Dictionary<string, ProcessingObservation> _responseProcessing =
                new Dictionary<string, ProcessingObservation>(StringComparer.Ordinal);
            private ProcessingObservation? _currentProcessing;
            private readonly Task _receiver;
            private TaskCompletionSource<bool>? _pendingAcceptance;
            private string? _pendingInstruction;
            private string? _pendingTarget;
            private string? _currentResponseId;
            private string? _lastRoundResponseId;
            private bool _currentResponseFinished;
            private bool _currentTerminalPublished;
            private bool _currentResponseCompleted;
            private bool _closedAfterFinalResponse;
            private bool _sentInitial;
            private bool _resumeForSteering;
            private bool _disposed;
            private Exception? _failure;
            private Exception? _executionFailure;
            private Exception? _receivedApiFailure;
            private Exception? _cancellationFailure;
            private long _bufferedEventBytes;

            public OpenAIRunSession(OpenAIService service, WebSocket socket, Message message,
                StreamOptions options, AIRequestContext? context, bool canSteer, CancellationToken cancellationToken)
            {
                _service = service;
                _socket = socket;
                _message = message;
                _options = options;
                _context = context;
                CanSteer = canSteer;
                _requestCancellation = cancellationToken;
                _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _receiver = ReceiveAsync();
            }

            public override bool CanSteer { get; }

            public void CaptureProcessing(JsonElement root)
            {
                if (!root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object)
                    return;
                var id = GetString(response, "id");
                lock (_stateLock)
                {
                    ProcessingObservation? observation = null;
                    if (id != null && !_responseProcessing.TryGetValue(id, out observation))
                    {
                        // Accepted steering can start another inference without another client
                        // response.create. Preserve its tier separately from the earlier response.
                        observation = _pendingProcessing.Count > 0
                            ? _pendingProcessing.Dequeue() : _service.BeginProcessingObservation();
                        if (observation != null) _responseProcessing[id] = observation;
                    }
                    observation ??= _currentProcessing;
                    if (observation == null && _pendingProcessing.Count > 0)
                        observation = _pendingProcessing.Peek();
                    if (observation != null) _currentProcessing = observation;
                    OpenAIService.CaptureProcessing(observation, response);
                }
            }

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
                => new ScopedStream(this, cancellationToken);

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
                    TaskCompletionSource<bool> acceptance;
                    // A caller may stop waiting behind another response.create without
                    // submitting an instruction or changing the original run's continuation.
                    await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
                    try
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        string target;
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
                            await SendLockedAsync(new Dictionary<string, object>
                            {
                                ["type"] = "response.steer", ["previous_response_id"] = target, ["input"] = instruction
                            }, linked.Token).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            // Once transmission starts, a failure can mean a partial frame.
                            // Stop the session rather than replaying an uncertain submission.
                            Fail(exception);
                            throw;
                        }
                    }
                    finally { _sendLock.Release(); }
                    // Cancellation after sending cannot retract the input. Keep the acknowledgement
                    // correlated until it arrives; canceling the caller only stops its wait.
                    await WaitWithCancellationAsync(acceptance.Task, linked.Token, preferCompleted: true).ConfigureAwait(false);
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
                    if (_disposed) ThrowIfUnavailable();
                    var automatic = _sentInitial && _lastRoundResponseId != null && _continuationResponses.Contains(_lastRoundResponseId);
                    if (automatic && (!_requiredOutputs.Values.Contains(_lastRoundResponseId!) || HasMissingRequiredOutputs()))
                        // The server owns this continuation; no send is required. Its complete
                        // final response may already be queued even if the peer has since closed.
                        return;
                    ThrowIfUnavailable();
                    if (_sentInitial)
                    {
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

                await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? responseBeforeSend;
                    lock (_stateLock)
                    {
                        responseBeforeSend = _currentResponseId;
                        // A peer can acknowledge these outputs and finish the successor before
                        // the local SendAsync continuation runs. Keep this in-flight delivery
                        // separate from committed outputs so steering still sees required input.
                        if (outputs != null && outputs.Count > 0)
                        {
                            _sendingOutputs = new HashSet<string>(outputs.Select(output => GetString(output, "call_id")!), StringComparer.Ordinal);
                            _sendingAfterResponseId = (string)payload["previous_response_id"];
                        }
                    }
                    try
                    {
                        await SendLockedAsync(payload, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        (exception is WebSocketException || exception is OperationCanceledException ||
                         exception is IOException || exception is ObjectDisposedException) &&
                        CanDrainConfirmedResponse(responseBeforeSend, cancellationToken))
                    {
                        // The peer's completed successor proves that this create was processed.
                        // A normal close can abort its still-pending local send. Commit the same
                        // bookkeeping and drain the response; never send the request again.
                    }
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
                finally
                {
                    lock (_stateLock)
                    {
                        _sendingOutputs = null;
                        _sendingAfterResponseId = null;
                    }
                    _sendLock.Release();
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
                        if (item.ResponseCreated)
                        {
                            // Mutate history only on the common execution path, never in the receiver.
                            // Each event owns only the instructions accepted for its predecessor.
                            foreach (var input in item.HistoryInputs)
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
                var peerClosed = false;
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
                            {
                                peerClosed = true;
                                throw new AIServiceException("OpenAI WebSocket disconnected; pending steering was not replayed.");
                            }
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
                        var historyInputs = Array.Empty<string>();
                        Exception? apiFailure = null;
                        lock (_stateLock)
                        {
                            if (type == "response.created")
                            {
                                if (string.IsNullOrEmpty(responseId)) throw new JsonException("response.created is missing its response ID.");
                                if (_currentResponseId != null && _continuationResponses.Contains(_currentResponseId))
                                {
                                    _steeredResponses.Add(responseId!);
                                    if (_historyInputs.TryGetValue(_currentResponseId, out var accepted))
                                    {
                                        // Freeze this response boundary before later acknowledgements
                                        // arrive, even when the output consumer is temporarily behind.
                                        historyInputs = accepted.ToArray();
                                        _historyInputs.Remove(_currentResponseId);
                                    }
                                }
                                _currentResponseId = responseId;
                                _currentResponseFinished = false;
                                _currentTerminalPublished = false;
                                _currentResponseCompleted = false;
                                _resumeForSteering = false;
                                _resumeRequested = NewCompletion<bool>();
                                var nextResponse = _nextResponse;
                                _nextResponse = NewCompletion<string>();
                                nextResponse.TrySetResult(responseId!);
                                _firstResponse.TrySetResult(responseId!);
                            }
                            else if (type == "response.steer.accepted")
                            {
                                if (_pendingAcceptance == null || _pendingInstruction == null || _pendingTarget == null)
                                    throw new JsonException("An unexpected steering acknowledgement was received.");
                                var steer = root.GetProperty("steer");
                                if (GetString(steer, "previous_response_id") != _pendingTarget)
                                    throw new JsonException("A steering acknowledgement targeted a different response.");
                                var steerId = GetString(steer, "id") ?? throw new JsonException("A steering acknowledgement is missing its ID.");
                                if (_acceptedInputs.ContainsKey(steerId)) throw new JsonException("A steering acknowledgement reused its ID.");
                                _acceptedInputs[steerId] = _pendingInstruction;
                                if (!_historyInputs.TryGetValue(_pendingTarget, out var accepted))
                                    _historyInputs[_pendingTarget] = accepted = new List<string>();
                                accepted.Add(_pendingInstruction);
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
                                _currentResponseCompleted = type == "response.completed" && GetString(response, "status") == "completed";
                                if (type == "response.failed" || type == "error" ||
                                    (type == "response.incomplete" && !IsSteeredTerminal(root)))
                                {
                                    // This parser only builds an error event; unlike the full stream
                                    // parser it does not mutate history, reasoning or tool state.
                                    var failedChunk = new OpenAIStreamChunk();
                                    _service.ParseStreamFailureEvent(root, type!, failedChunk);
                                    var error = failedChunk.Error!;
                                    apiFailure = new AIServiceException(error.Content!,
                                        error.Metadata == null ? string.Empty : JsonSerializer.Serialize(error.Metadata),
                                        _service.Provider);
                                }
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
                            type == "response.created", type == "response.steer.pending", message.Length, historyInputs);
                        if (Interlocked.Add(ref _bufferedEventBytes, message.Length) > 64 * 1024 * 1024 ||
                            !_events.Writer.TryWrite(socketEvent))
                            throw new AIServiceException("OpenAI WebSocket output buffering exceeded its limit; the run was stopped without dropping events.");
                        if (terminal)
                        {
                            lock (_stateLock)
                            {
                                _currentTerminalPublished = true;
                                _receivedApiFailure ??= apiFailure;
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Fail(exception, peerClosed);
                }
            }

            private bool HasFinalResponseLocked()
            {
                var outputsDelivered = _outstandingOutputs.Count == 0 ||
                    (_sendingOutputs != null && _sendingAfterResponseId != _currentResponseId &&
                     _outstandingOutputs.All(_sendingOutputs.Contains));
                return _currentResponseFinished && _currentTerminalPublished && _currentResponseCompleted &&
                    _pendingAcceptance == null && outputsDelivered &&
                    (_currentResponseId == null || !_continuationResponses.Contains(_currentResponseId));
            }

            private bool CanDrainConfirmedResponse(string? responseBeforeSend, CancellationToken cancellationToken)
            {
                lock (_stateLock)
                    return !_disposed && !_requestCancellation.IsCancellationRequested &&
                        !cancellationToken.IsCancellationRequested && _executionFailure == null &&
                        _closedAfterFinalResponse && _currentResponseId != responseBeforeSend &&
                        HasFinalResponseLocked();
            }

            private void Fail(Exception exception, bool peerClosed = false)
            {
                bool cancelExecution;
                lock (_stateLock)
                {
                    if (_disposed) return;
                    // A terminal API failure was received before this transport failure.
                    // Keep its message/metadata even when outstanding tools must be cancelled.
                    _failure ??= _receivedApiFailure ?? exception;
                    // A peer may close after the complete final response has already been
                    // queued. Drain that response normally. Any outstanding tool or steering
                    // continuation still requires a live transport and must be interrupted.
                    var finalResponseReceived = _receivedApiFailure == null && HasFinalResponseLocked();
                    _closedAfterFinalResponse = peerClosed && finalResponseReceived;
                    cancelExecution = !_requestCancellation.IsCancellationRequested && !finalResponseReceived;
                    if (cancelExecution) _executionFailure ??= _failure;
                    _firstResponse.TrySetException(_failure);
                    _nextResponse.TrySetException(_failure);
                    _resumeRequested.TrySetResult(true);
                    _pendingAcceptance?.TrySetException(_failure);
                }
                _events.Writer.TryComplete(exception);
                try { _socket.Abort(); }
                finally
                {
                    if (cancelExecution)
                    {
                        try { _lifetime.Cancel(); }
                        catch (Exception cancellationFailure)
                        {
                            // A user cancellation callback may throw. Still finish the receiver
                            // and drain cooperative work; report this additional cleanup failure.
                            lock (_stateLock) _cancellationFailure ??= cancellationFailure;
                        }
                    }
                }
            }

            private async Task SendLockedAsync(object payload, CancellationToken cancellationToken)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                if (payload is IDictionary<string, object> fields &&
                    fields.TryGetValue("type", out var type) && Equals(type, "response.create"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var observation = _service.BeginProcessingObservation();
                    lock (_stateLock) _pendingProcessing.Enqueue(observation);
                }
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }

            public override async ValueTask DisposeAsync()
            {
                lock (_stateLock)
                {
                    if (_disposed) return;
                    _disposed = true;
                }
                var failures = new List<Exception>();
                try { _lifetime.Cancel(); } catch (Exception exception) { failures.Add(exception); }
                try { _socket.Abort(); } catch (Exception exception) { failures.Add(exception); }
                try { await _receiver.ConfigureAwait(false); } catch (Exception exception) { failures.Add(exception); }
                try { _socket.Dispose(); } catch (Exception exception) { failures.Add(exception); }
                _lifetime.Dispose();
                _events.Writer.TryComplete();
                while (_events.Reader.TryRead(out _)) { }
                Interlocked.Exchange(ref _bufferedEventBytes, 0);
                _historyInputs.Clear();
                _acceptedInputs.Clear();
                _toolEvents.Clear();
                if (_cancellationFailure != null) failures.Add(_cancellationFailure);
                if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
                if (failures.Count > 1) throw new AggregateException("OpenAI Run cleanup failed.", failures);
            }

            private void ThrowIfExecutionFailed(Exception? interruptedFailure = null)
            {
                Exception? failure;
                lock (_stateLock) failure = _executionFailure;
                if (failure == null || ReferenceEquals(failure, interruptedFailure)) return;
                if (interruptedFailure != null && !(interruptedFailure is OperationCanceledException))
                {
                    // Async-scope cleanup can win the race to cancel a tool and throw its
                    // callback error before lifetime cancellation reaches the iterator.
                    // Preserve both causes instead of replacing the transport failure.
                    throw new AggregateException("OpenAI Run failed and execution cleanup also failed.", failure, interruptedFailure);
                }
                ExceptionDispatchInfo.Capture(failure).Throw();
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
                private readonly CancellationToken _cancellationToken;
                public ScopedStream(OpenAIRunSession session, CancellationToken cancellationToken)
                { _session = session; _cancellationToken = cancellationToken; }
                public IAsyncEnumerator<StreamingContent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                {
                    // The common loop and all local tools must observe provider failure as
                    // well as caller cancellation. Merely completing the event queue cannot
                    // interrupt a required handler or a direct wait for an asynchronous result.
                    var execution = CancellationTokenSource.CreateLinkedTokenSource(
                        _cancellationToken, cancellationToken, _session._lifetime.Token);
                    try
                    {
                        var source = _session._service.StreamAsync(_session._message, _session._options,
                            _session._context, execution.Token).GetAsyncEnumerator(execution.Token);
                        return new ScopedEnumerator(_session, source, execution);
                    }
                    catch { execution.Dispose(); throw; }
                }
            }

            private sealed class ScopedEnumerator : IAsyncEnumerator<StreamingContent>
            {
                private readonly OpenAIRunSession _session;
                private readonly IAsyncEnumerator<StreamingContent> _source;
                private readonly CancellationTokenSource _execution;
                public ScopedEnumerator(OpenAIRunSession session, IAsyncEnumerator<StreamingContent> source,
                    CancellationTokenSource execution)
                { _session = session; _source = source; _execution = execution; }
                public StreamingContent Current => _source.Current;
                public async ValueTask<bool> MoveNextAsync()
                {
                    var previous = _session._service._openAIRunSession.Value;
                    _session._service._openAIRunSession.Value = _session;
                    try
                    {
                        _session.ThrowIfExecutionFailed();
                        var moved = await _source.MoveNextAsync().ConfigureAwait(false);
                        _session.ThrowIfExecutionFailed();
                        return moved;
                    }
                    catch (Exception exception)
                    {
                        // Failure cancellation is an internal cleanup mechanism, not a
                        // caller cancellation or policy timeout. Preserve its original cause.
                        _session.ThrowIfExecutionFailed(exception);
                        throw;
                    }
                    finally { _session._service._openAIRunSession.Value = previous; }
                }
                public async ValueTask DisposeAsync()
                {
                    var previous = _session._service._openAIRunSession.Value;
                    _session._service._openAIRunSession.Value = _session;
                    try { await _source.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception exception)
                    {
                        _session.ThrowIfExecutionFailed(exception);
                        throw;
                    }
                    finally
                    {
                        _session._service._openAIRunSession.Value = previous;
                        _execution.Dispose();
                    }
                }
            }

            private static TaskCompletionSource<T> NewCompletion<T>()
                => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken,
                bool preferCompleted = false)
            {
                var canceled = NewCompletion<bool>();
                using (cancellationToken.Register(() => canceled.TrySetResult(true)))
                {
                    await Task.WhenAny(task, canceled.Task).ConfigureAwait(false);
                    // Only the submission's own final acknowledgement may outrun normal
                    // session cleanup. Prerequisite waits must honor cancellation before
                    // they can advance to a new submission, even if the prerequisite settled.
                    if (!preferCompleted || !task.IsCompleted)
                        cancellationToken.ThrowIfCancellationRequested();
                    return await task.ConfigureAwait(false);
                }
            }

            private sealed class SocketEvent
            {
                public SocketEvent(string json, bool terminal, string? responseId, bool responseCreated, bool requiresInput, long byteCount, string[] historyInputs)
                { Json = json; Terminal = terminal; ResponseId = responseId; ResponseCreated = responseCreated; RequiresInput = requiresInput; ByteCount = byteCount; HistoryInputs = historyInputs; }
                public string Json { get; }
                public bool Terminal { get; }
                public string? ResponseId { get; }
                public bool ResponseCreated { get; }
                public bool RequiresInput { get; }
                public long ByteCount { get; }
                public string[] HistoryInputs { get; }
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
