using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Mcp.Transports
{
    /// <summary>
    /// MCP transport over stdio — launches a child process and communicates via stdin/stdout.
    /// </summary>
    public sealed class StdioTransport : IMcpTransport
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        /// <inheritdoc/>
        public bool IsConnected => !_disposed && !_process.HasExited;

        /// <summary>
        /// Creates and starts a stdio transport with the given command and arguments.
        /// </summary>
        /// <param name="command">The executable to launch (e.g., "npx", "python", "node").</param>
        /// <param name="args">Arguments to pass to the executable.</param>
        /// <param name="workingDirectory">Optional working directory for the process.</param>
        /// <param name="environmentVariables">Optional additional environment variables.</param>
        public StdioTransport(
            string command,
            string? args = null,
            string? workingDirectory = null,
            System.Collections.Generic.Dictionary<string, string>? environmentVariables = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            if (environmentVariables != null)
            {
                foreach (var kvp in environmentVariables)
                    startInfo.Environment[kvp.Key] = kvp.Value;
            }

            _process = new Process { StartInfo = startInfo };
            _process.Start();

            _writer = _process.StandardInput;
            _writer.AutoFlush = true;

            _reader = _process.StandardOutput;
        }

        /// <inheritdoc/>
        public async Task SendAsync(string json, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StdioTransport));

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(json).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StdioTransport));

            // ReadLineAsync doesn't accept CancellationToken on netstandard2.1,
            // so we use Task.Run with cancellation check
            var readTask = _reader.ReadLineAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);

            var completed = await Task.WhenAny(readTask, cancelTask).ConfigureAwait(false);
            if (completed == cancelTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await readTask.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (_disposed) return default;
            _disposed = true;

            try
            {
                _writer.Dispose();
                _reader.Dispose();

                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(3000);
                }

                _process.Dispose();
            }
            catch
            {
                // Best-effort cleanup
            }

            _writeLock.Dispose();
            return default;
        }
    }
}
