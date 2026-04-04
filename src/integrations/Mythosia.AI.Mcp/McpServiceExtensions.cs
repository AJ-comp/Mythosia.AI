using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Mcp.Transports;
using Mythosia.AI.Services.Base;

namespace Mythosia.AI.Mcp
{
    /// <summary>
    /// Extension methods that integrate MCP servers with <see cref="AIService"/>.
    /// </summary>
    public static class McpServiceExtensions
    {
        /// <summary>
        /// Connects to an MCP server via stdio transport, discovers its tools,
        /// and registers them as <see cref="Mythosia.AI.Models.Functions.FunctionDefinition"/>s on this service.
        /// Dispose the returned <see cref="McpConnection"/> to shut down the server process.
        /// </summary>
        /// <param name="service">The AI service to register tools on.</param>
        /// <param name="command">The executable to launch (e.g., "npx", "python", "node").</param>
        /// <param name="args">Arguments to pass to the executable.</param>
        /// <param name="toolFilter">Optional filter — return true for tool names to include.</param>
        /// <param name="namePrefix">Optional prefix to prepend to each tool name.</param>
        /// <param name="workingDirectory">Optional working directory for the server process.</param>
        /// <param name="environmentVariables">Optional environment variables for the server process.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The initialized <see cref="McpConnection"/>. Caller must dispose it when done.</returns>
        public static async Task<McpConnection> WithMcpServerAsync(
            this AIService service,
            string command,
            string? args = null,
            Func<string, bool>? toolFilter = null,
            string? namePrefix = null,
            string? workingDirectory = null,
            Dictionary<string, string>? environmentVariables = null,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrEmpty(command)) throw new ArgumentException("Command cannot be empty.", nameof(command));

            var transport = new StdioTransport(command, args, workingDirectory, environmentVariables);
            var connection = new McpConnection(transport);

            try
            {
                await connection.InitializeAsync(cancellationToken).ConfigureAwait(false);

                var functions = McpToolAdapter.ToFunctionDefinitions(connection, toolFilter, namePrefix);
                foreach (var func in functions)
                {
                    service.Functions.Add(func);
                }

                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Connects to an MCP server using a pre-built transport, discovers its tools,
        /// and registers them on this service.
        /// </summary>
        /// <param name="service">The AI service to register tools on.</param>
        /// <param name="transport">A ready-to-use MCP transport.</param>
        /// <param name="toolFilter">Optional filter — return true for tool names to include.</param>
        /// <param name="namePrefix">Optional prefix to prepend to each tool name.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The initialized <see cref="McpConnection"/>. Caller must dispose it when done.</returns>
        public static async Task<McpConnection> WithMcpServerAsync(
            this AIService service,
            IMcpTransport transport,
            Func<string, bool>? toolFilter = null,
            string? namePrefix = null,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (transport == null) throw new ArgumentNullException(nameof(transport));

            var connection = new McpConnection(transport);

            try
            {
                await connection.InitializeAsync(cancellationToken).ConfigureAwait(false);

                var functions = McpToolAdapter.ToFunctionDefinitions(connection, toolFilter, namePrefix);
                foreach (var func in functions)
                {
                    service.Functions.Add(func);
                }

                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
