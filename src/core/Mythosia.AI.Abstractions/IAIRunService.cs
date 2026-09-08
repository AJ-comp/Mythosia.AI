using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services
{
    /// <summary>Optional capability for starting observable, controllable requests.</summary>
    /// <remarks>
    /// Existing IAIService implementations do not need to implement this interface.
    /// Streaming options select observed output and do not disable registered tool execution.
    /// </remarks>
    public interface IAIRunService
    {
        /// <summary>Starts execution and returns its handle before the final result is available.</summary>
        Task<AIRun> StartRunAsync(string prompt, Action<string>? onText = null,
            StreamOptions? options = null, AIRequestContext? context = null,
            CancellationToken cancellationToken = default);

        /// <summary>Starts a message request and returns its handle before the final result is available.</summary>
        Task<AIRun> StartRunAsync(Message message, Action<string>? onText = null,
            StreamOptions? options = null, AIRequestContext? context = null,
            CancellationToken cancellationToken = default);
    }
}
