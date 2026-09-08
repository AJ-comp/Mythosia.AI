using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Models.Runs
{
    /// <summary>A single running request with independent result and output observation.</summary>
    public abstract class AIRun : IAsyncDisposable
    {
        /// <summary>
        /// Completes after execution and cleanup, with the concatenated text emitted by this run.
        /// Intermediate text, including text emitted before steering, is not rolled back.
        /// This task does not depend on consuming the output stream.
        /// </summary>
        public abstract Task<string> Result { get; }

        /// <summary>Provider-supplied source references, retained independently of output observation.</summary>
        public virtual IReadOnlyList<AICitation> Citations => Array.Empty<AICitation>();

        /// <summary>Whether this run's provider session supports additional instructions.</summary>
        public abstract bool CanSteer { get; }

        /// <summary>Observes output without starting another request.</summary>
        /// <remarks>
        /// Only one stream reader is supported. A startup text callback may also observe the same run.
        /// The core implementation buffers at most 1,024 unread events. Exceeding that limit faults
        /// observation explicitly while execution and Result continue. Read promptly or use a callback
        /// for long streams. Stopping or cancelling observation does not cancel execution.
        /// </remarks>
        public abstract IAsyncEnumerable<StreamingContent> StreamAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an additional instruction to this running request. Completion acknowledges acceptance,
        /// not that the model has already applied it. Unsupported providers throw NotSupportedException.
        /// </summary>
        public abstract Task SteerAsync(string instruction, CancellationToken cancellationToken = default);

        /// <summary>Requests cancellation of execution. Await Result or DisposeAsync for cleanup.</summary>
        public abstract void Cancel();

        /// <summary>Cancels unfinished execution and waits for its cleanup.</summary>
        public abstract ValueTask DisposeAsync();
    }
}
