using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Serving
{
    /// <summary>Inspects a running model server without starting inference or changing its state.</summary>
    public interface IModelServer
    {
        Uri Endpoint { get; }
        Task<ServerInfo> GetInfoAsync(CancellationToken cancellationToken = default);
        Task<ServerHealth> GetHealthAsync(CancellationToken cancellationToken = default);
        /// <summary>Returns models exposed by the runtime, not a guarantee that they are loaded or healthy.</summary>
        Task<IReadOnlyList<ServerModel>> GetModelsAsync(CancellationToken cancellationToken = default);
        /// <summary>Observes endpoint support without loading, unloading or downloading a model.</summary>
        Task<ServingCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Optional explicit model lifecycle commands. Completion acknowledges the command; inspect state separately.</summary>
    public interface IModelLifecycle
    {
        Task LoadModelAsync(string modelId, CancellationToken cancellationToken = default);
        Task UnloadModelAsync(string modelId, CancellationToken cancellationToken = default);
    }

    /// <summary>Optional model download. Success requires server-reported completion, not merely HTTP acceptance.</summary>
    /// <remarks>Cancellation stops this client's wait and HTTP work. It does not promise remote rollback or cancellation.</remarks>
    public interface IModelDownloader
    {
        Task DownloadModelAsync(string modelId, IProgress<ModelDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Optional server-wide metrics. Values are observations, not portable resource limits.</summary>
    public interface IModelMetricsProvider
    {
        Task<ServerMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
    }
}
