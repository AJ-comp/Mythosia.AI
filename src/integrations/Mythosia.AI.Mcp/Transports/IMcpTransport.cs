using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Mcp.Transports
{
    /// <summary>
    /// Abstraction for MCP transport layer (stdio, SSE, etc.)
    /// </summary>
    public interface IMcpTransport : IAsyncDisposable
    {
        /// <summary>
        /// Sends a JSON message to the MCP server
        /// </summary>
        Task SendAsync(string json, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the next JSON message from the MCP server
        /// </summary>
        Task<string?> ReceiveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether the transport is connected and operational
        /// </summary>
        bool IsConnected { get; }
    }
}
