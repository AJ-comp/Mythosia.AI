using System.Collections.Concurrent;
using System.Text.Json;
using Mythosia.AI.Mcp.Transports;

namespace Mythosia.AI.Mcp.Tests;

/// <summary>
/// In-memory MCP transport for unit testing.
/// Responses are only delivered to the read loop AFTER the corresponding request is sent,
/// preventing race conditions where the read loop consumes responses before _pending is populated.
/// </summary>
internal sealed class MockTransport : IMcpTransport
{
    private readonly ConcurrentQueue<string> _outgoing = new();
    private readonly ConcurrentQueue<string> _scheduledResponses = new();
    private readonly BlockingCollection<string> _incoming = new();
    private bool _disposed;

    public bool IsConnected => !_disposed;

    /// <summary>
    /// All JSON messages sent by McpConnection (requests + notifications).
    /// </summary>
    public ConcurrentQueue<string> SentMessages => _outgoing;

    /// <summary>
    /// Enqueue a raw JSON response that will be delivered when the next request is sent.
    /// </summary>
    public void EnqueueResponse(string json) => _scheduledResponses.Enqueue(json);

    /// <summary>
    /// Enqueue a JSON-RPC success response. Delivered when the next request is sent.
    /// </summary>
    public void EnqueueResult(int id, object result)
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            result
        });
        _scheduledResponses.Enqueue(json);
    }

    /// <summary>
    /// Enqueue a JSON-RPC error response. Delivered when the next request is sent.
    /// </summary>
    public void EnqueueError(int id, int code, string message)
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            error = new { code, message }
        });
        _scheduledResponses.Enqueue(json);
    }

    /// <summary>
    /// Enqueue a server-sent notification (no id). Delivered immediately to incoming queue.
    /// </summary>
    public void EnqueueNotification(string method, object? @params = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method,
            @params
        });
        _incoming.Add(json);
    }

    public Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockTransport));
        _outgoing.Enqueue(json);

        // Only deliver a response when a request (with id) is sent.
        // Notifications (no id, e.g. "initialized") should not trigger a response.
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("id", out _))
        {
            if (_scheduledResponses.TryDequeue(out var response))
                _incoming.Add(response);
        }

        return Task.CompletedTask;
    }

    public Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockTransport));

        try
        {
            var item = _incoming.Take(cancellationToken);
            return Task.FromResult<string?>(item);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _incoming.CompleteAdding();
        return default;
    }
}
