using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mythosia.AI.Serving
{
    public enum ServerMode { Unknown, SingleModel, Router }
    public enum ServerHealthStatus { Healthy, NotReady, Unauthorized, Unreachable, Unexpected }
    public enum ServingFeatureSupport { Unknown, Supported, Unsupported }
    public enum ModelInstallationState { Unknown, Installed, NotInstalled }
    public enum ModelLoadState { Unknown, Unloaded, Loading, Loaded, Unloading, Sleeping, Downloading, Failed }

    /// <summary>Runtime identity and optional version/mode reported by the server.</summary>
    public sealed class ServerInfo
    {
        public string Runtime { get; }
        public Uri Endpoint { get; }
        public string? Version { get; }
        public ServerMode Mode { get; }
        public ServerInfo(string runtime, Uri endpoint, string? version = null, ServerMode mode = ServerMode.Unknown)
        {
            Runtime = Required(runtime, nameof(runtime));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            if (!endpoint.IsAbsoluteUri) throw new ArgumentException("An absolute endpoint is required.", nameof(endpoint));
            Version = version;
            Mode = mode;
        }

        internal static string Required(string value, string name) =>
            !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("A non-empty value is required.", name);
    }

    /// <summary>A probe result. Healthy means the probe succeeded; individual models can still be unavailable.</summary>
    public sealed class ServerHealth
    {
        public ServerHealthStatus Status { get; }
        public int? StatusCode { get; }
        public string? Detail { get; }
        public ServerHealth(ServerHealthStatus status, int? statusCode = null, string? detail = null)
        { Status = status; StatusCode = statusCode; Detail = detail; }
    }

    /// <summary>Model inventory observation. Unknown installation/loading must never be interpreted as absence.</summary>
    public sealed class ServerModel
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ModelInstallationState InstallationState { get; }
        public ModelLoadState LoadState { get; }
        public long? SizeBytes { get; }
        public long? MemoryBytes { get; }
        public int? ContextLength { get; }
        public string? NativeState { get; }
        /// <summary>True for a known remote model reference; null when the server does not report locality.</summary>
        public bool? IsRemote { get; }
        public ServerModel(string id, string? displayName = null,
            ModelInstallationState installationState = ModelInstallationState.Unknown,
            ModelLoadState loadState = ModelLoadState.Unknown, long? sizeBytes = null,
            long? memoryBytes = null, int? contextLength = null, string? nativeState = null, bool? isRemote = null)
        {
            Id = ServerInfo.Required(id, nameof(id));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName!;
            if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            if (memoryBytes < 0) throw new ArgumentOutOfRangeException(nameof(memoryBytes));
            if (contextLength < 0) throw new ArgumentOutOfRangeException(nameof(contextLength));
            InstallationState = installationState; LoadState = loadState;
            SizeBytes = sizeBytes; MemoryBytes = memoryBytes; ContextLength = contextLength;
            NativeState = nativeState; IsRemote = isRemote;
        }
    }

    /// <summary>Endpoint support at observation time. Supported is not authorization or a promise for every model.</summary>
    public sealed class ServingCapabilities
    {
        public ServingFeatureSupport ModelListing { get; }
        public ServingFeatureSupport ModelLoading { get; }
        public ServingFeatureSupport ModelUnloading { get; }
        public ServingFeatureSupport ModelDownloading { get; }
        public ServingFeatureSupport Metrics { get; }
        public ServingCapabilities(ServingFeatureSupport modelListing = ServingFeatureSupport.Unknown,
            ServingFeatureSupport modelLoading = ServingFeatureSupport.Unknown,
            ServingFeatureSupport modelUnloading = ServingFeatureSupport.Unknown,
            ServingFeatureSupport modelDownloading = ServingFeatureSupport.Unknown,
            ServingFeatureSupport metrics = ServingFeatureSupport.Unknown)
        {
            ModelListing = modelListing; ModelLoading = modelLoading; ModelUnloading = modelUnloading;
            ModelDownloading = modelDownloading; Metrics = metrics;
        }
    }

    /// <summary>Progress for an individual download stage/artifact. Nullable byte counts are not zero.</summary>
    public sealed class ModelDownloadProgress
    {
        public string ModelId { get; }
        public string Stage { get; }
        public long? CompletedBytes { get; }
        public long? TotalBytes { get; }
        public string? Artifact { get; }
        public ModelDownloadProgress(string modelId, string stage, long? completedBytes = null,
            long? totalBytes = null, string? artifact = null)
        {
            ModelId = ServerInfo.Required(modelId, nameof(modelId));
            Stage = ServerInfo.Required(stage, nameof(stage));
            if (completedBytes < 0) throw new ArgumentOutOfRangeException(nameof(completedBytes));
            if (totalBytes < 0 || completedBytes > totalBytes) throw new ArgumentOutOfRangeException(nameof(totalBytes));
            CompletedBytes = completedBytes; TotalBytes = totalBytes; Artifact = artifact;
        }
    }

    /// <summary>A metric sample. Labels are retained; differently labelled samples must not be blindly summed.</summary>
    public sealed class ServerMetric
    {
        public string Name { get; }
        /// <summary>Prometheus values may include NaN or infinity. Check before displaying or aggregating.</summary>
        public double Value { get; }
        public IReadOnlyDictionary<string, string> Labels { get; }
        public ServerMetric(string name, double value, IReadOnlyDictionary<string, string>? labels = null)
        {
            Name = ServerInfo.Required(name, nameof(name)); Value = value;
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            if (labels != null) foreach (var entry in labels) copy.Add(entry.Key, entry.Value);
            Labels = new ReadOnlyDictionary<string, string>(copy);
        }
    }

    /// <summary>Immutable metric observations and, when exposed, the original text representation.</summary>
    public sealed class ServerMetrics
    {
        public IReadOnlyList<ServerMetric> Samples { get; }
        public string? RawText { get; }
        public ServerMetrics(IEnumerable<ServerMetric> samples, string? rawText = null)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var copy = samples.ToArray();
            if (copy.Any(sample => sample == null)) throw new ArgumentException("Metric samples cannot be null.", nameof(samples));
            Samples = Array.AsReadOnly(copy); RawText = rawText;
        }
    }
}
