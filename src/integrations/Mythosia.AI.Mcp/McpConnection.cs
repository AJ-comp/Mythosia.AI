using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Mcp.Protocol;
using Mythosia.AI.Mcp.Transports;

namespace Mythosia.AI.Mcp
{
    /// <summary>
    /// Manages a connection to an MCP server. Handles initialization handshake,
    /// tool discovery, and tool invocation over a given transport.
    /// </summary>
    public sealed class McpConnection : IAsyncDisposable
    {
        private readonly IMcpTransport _transport;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonRpcResponse>> _pending
            = new ConcurrentDictionary<int, TaskCompletionSource<JsonRpcResponse>>();
        private readonly CancellationTokenSource _readCts = new CancellationTokenSource();
        private readonly Task _readLoop;
        private readonly object _disposeGate = new object();
        private Task? _disposeTask;
        private bool _readLoopEnded;
        private int _nextId;

        /// <summary>
        /// The server name reported during initialization.
        /// </summary>
        public string? ServerName { get; private set; }

        /// <summary>
        /// The server version reported during initialization.
        /// </summary>
        public string? ServerVersion { get; private set; }

        /// <summary>
        /// Tools discovered from the MCP server after <see cref="InitializeAsync"/>.
        /// </summary>
        public IReadOnlyList<McpToolInfo> Tools { get; private set; } = Array.Empty<McpToolInfo>();

        public McpConnection(IMcpTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token));
        }

        /// <summary>
        /// Performs the MCP initialize handshake and discovers available tools.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            // 1. Send initialize request
            var initParams = new McpInitializeParams();
            var initResponse = await SendRequestAsync(McpProtocolConstants.Initialize, initParams, cancellationToken)
                .ConfigureAwait(false);

            if (initResponse.Error != null)
                throw new McpException($"MCP initialize failed: {initResponse.Error.Message}");

            if (initResponse.Result?.ServerInfo != null)
            {
                ServerName = initResponse.Result.ServerInfo.Name;
                ServerVersion = initResponse.Result.ServerInfo.Version;
            }

            // 2. Send initialized notification
            await SendNotificationAsync(McpProtocolConstants.Initialized, cancellationToken)
                .ConfigureAwait(false);

            // 3. Discover tools
            await RefreshToolsAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Re-fetches the tool list from the server.
        /// </summary>
        public async Task RefreshToolsAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendRequestAsync(McpProtocolConstants.ToolsList, null, cancellationToken)
                .ConfigureAwait(false);

            if (response.Error != null)
                throw new McpException($"tools/list failed: {response.Error.Message}");

            var tools = new List<McpToolInfo>();
            if (response.Result?.Tools != null)
            {
                foreach (var t in response.Result.Tools)
                {
                    tools.Add(new McpToolInfo(t.Name, t.Description, t.InputSchema));
                }
            }

            Tools = tools;
        }

        /// <summary>
        /// Calls a tool on the MCP server with the given arguments.
        /// </summary>
        /// <exception cref="McpException">The server reports a protocol or tool execution error.</exception>
        public async Task<string> CallToolAsync(
            string toolName,
            Dictionary<string, object>? arguments = null,
            CancellationToken cancellationToken = default)
        {
            var callParams = new McpToolCallParams
            {
                Name = toolName,
                Arguments = arguments
            };

            var response = await SendRequestAsync(McpProtocolConstants.ToolsCall, callParams, cancellationToken)
                .ConfigureAwait(false);

            if (response.Error != null)
                throw new McpException($"tools/call '{toolName}' failed: {response.Error.Message}");

            if (response.Result?.IsError == true)
            {
                var errorText = response.Result.Content?
                    .Where(c => c.Type == "text")
                    .Select(c => c.Text)
                    .FirstOrDefault() ?? "Unknown MCP tool error";
                throw new McpException($"MCP tool '{toolName}' failed: {errorText}");
            }

            // Concatenate all text content blocks
            if (response.Result?.Content != null)
            {
                var texts = response.Result.Content
                    .Where(c => c.Type == "text" && c.Text != null)
                    .Select(c => c.Text);
                return string.Join("\n", texts);
            }

            return string.Empty;
        }

        #region JSON-RPC Transport

        private async Task<JsonRpcResponse> SendRequestAsync(
            string method, object? parameters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Interlocked.Increment(ref _nextId);
            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = parameters
            };

            var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_disposeGate)
            {
                // Publish requests under the same gate that starts disposal. Once cleanup
                // begins, the read loop may already have drained its last pending call.
                if (_disposeTask != null) throw new ObjectDisposedException(nameof(McpConnection));
                if (_readLoopEnded) throw new McpException("The MCP connection is closed. Create a new connection before sending requests.");
                _pending[id] = tcs;
            }

            try
            {
                using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                {
                    var json = JsonSerializer.Serialize(request);
                    await _transport.SendAsync(json, cancellationToken).ConfigureAwait(false);
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                // Cancelled requests may never receive a response from the server.
                _pending.TryRemove(id, out _);
            }
        }

        private async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
        {
            lock (_disposeGate)
            {
                if (_disposeTask != null) throw new ObjectDisposedException(nameof(McpConnection));
                if (_readLoopEnded) throw new McpException("The MCP connection is closed. Create a new connection before sending notifications.");
            }
            var notification = new JsonRpcNotification { Method = method };
            var json = JsonSerializer.Serialize(notification);
            await _transport.SendAsync(json, cancellationToken).ConfigureAwait(false);
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _transport.IsConnected)
                {
                    var line = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null) break; // EOF

                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);

                        // Skip server-sent notifications (no "id" field)
                        if (!doc.RootElement.TryGetProperty("id", out var idProp)
                            || idProp.ValueKind == JsonValueKind.Null)
                            continue;

                        var id = idProp.GetInt32();
                        // A malformed body must not remove its pending call: a later valid
                        // response or connection cleanup still needs to settle that task.
                        var response = JsonSerializer.Deserialize<JsonRpcResponse>(line);
                        if (_pending.TryRemove(id, out var tcs))
                        {
                            if (response != null)
                                tcs.TrySetResult(response);
                            else
                                tcs.TrySetCanceled();
                        }
                    }
                    catch (JsonException)
                    {
                        // Malformed message — skip
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch
            {
                // Transport error — cancel all pending
            }
            finally
            {
                // EOF and transport failure also close the receiving side. Seal registration
                // before draining so a late request cannot appear after the final cleanup.
                lock (_disposeGate) _readLoopEnded = true;
                foreach (var kvp in _pending)
                {
                    kvp.Value.TrySetCanceled();
                }
                _pending.Clear();
            }
        }

        #endregion

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (_disposeGate)
            {
                if (_disposeTask != null)
                    return _disposeTask.IsCompleted ? default : new ValueTask(_disposeTask);

                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                // Publish before cancellation can reenter DisposeAsync through a callback.
                _disposeTask = completion.Task;
            }

            _ = CompleteDisposalAsync(completion);
            return new ValueTask(completion.Task);
        }

        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            var failures = new List<Exception>();
            try
            {
                try { _readCts.Cancel(); }
                catch (Exception cancellationException) { failures.Add(cancellationException); }

                // Closing the transport must be able to unblock a read whose underlying
                // I/O cannot stop through its cancellation token alone.
                try { await _transport.DisposeAsync().ConfigureAwait(false); }
                catch (Exception disposalException) { failures.Add(disposalException); }

                try
                {
                    await _readLoop.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore read loop exit exceptions
                }
            }
            finally
            {
                try { _readCts.Dispose(); }
                catch (Exception disposalException) { failures.Add(disposalException); }
            }

            if (failures.Count == 0)
                completion.TrySetResult(true);
            else if (failures.Count == 1)
                completion.TrySetException(failures[0]);
            else
                completion.TrySetException(new AggregateException("MCP connection cleanup failed.", failures));
        }
    }

    /// <summary>
    /// Describes an MCP tool discovered from a server.
    /// </summary>
    public sealed class McpToolInfo
    {
        /// <summary>Tool name.</summary>
        public string Name { get; }

        /// <summary>Human-readable description.</summary>
        public string? Description { get; }

        /// <summary>Raw JSON Schema for the tool's input parameters.</summary>
        public JsonElement? InputSchema { get; }

        internal McpToolInfo(string name, string? description, JsonElement? inputSchema)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
        }
    }

    /// <summary>
    /// Exception thrown when an MCP protocol error occurs.
    /// </summary>
    public class McpException : Exception
    {
        public McpException(string message) : base(message) { }
        public McpException(string message, Exception innerException) : base(message, innerException) { }
    }
}
