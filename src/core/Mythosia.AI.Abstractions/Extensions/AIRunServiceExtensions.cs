using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Runs;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Extensions
{
    /// <summary>Access to optional run support through an IAIService reference.</summary>
    public static class AIRunServiceExtensions
    {
        /// <summary>Starts a run, or rejects a service that does not implement IAIRunService.</summary>
        public static Task<AIRun> StartRunAsync(this IAIService service, string prompt,
            Action<string>? onText = null, StreamOptions? options = null,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
            => RequireRunService(service).StartRunAsync(prompt, onText, options, context, cancellationToken);

        /// <summary>Starts a message run, or rejects a service that does not implement IAIRunService.</summary>
        public static Task<AIRun> StartRunAsync(this IAIService service, Message message,
            Action<string>? onText = null, StreamOptions? options = null,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
            => RequireRunService(service).StartRunAsync(message, onText, options, context, cancellationToken);

        private static IAIRunService RequireRunService(IAIService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            return service as IAIRunService ?? throw new NotSupportedException(
                "This AI service does not implement the optional IAIRunService capability.");
        }
    }
}
