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
        private int _nextId;
        private bool _disposed;

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
                return $"Error: {errorText}";
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
            var id = Interlocked.Increment(ref _nextId);
            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = parameters
            };

            var tcs = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                var json = JsonSerializer.Serialize(request);
                await _transport.SendAsync(json, cancellationToken).ConfigureAwait(false);
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        private async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
        {
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
                        if (_pending.TryRemove(id, out var tcs))
                        {
                            var response = JsonSerializer.Deserialize<JsonRpcResponse>(line);
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
                foreach (var kvp in _pending)
                {
                    kvp.Value.TrySetCanceled();
                }
                _pending.Clear();
            }
        }

        #endregion

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _readCts.Cancel();

            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch
            {
                // Ignore read loop exit exceptions
            }

            await _transport.DisposeAsync().ConfigureAwait(false);
            _readCts.Dispose();
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
