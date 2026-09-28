using Mythosia.AI.Serving;

namespace Mythosia.AI.Serving.Abstractions.Tests;

[TestClass]
public sealed class ObservationTests
{
    [TestMethod]
    public void Metrics_SnapshotInputCollectionsAndKeepLabelIdentity()
    {
        var labels = new Dictionary<string, string> { ["model"] = "first" };
        var metric = new ServerMetric("requests", 42, labels);
        var source = new List<ServerMetric> { metric };
        var observation = new ServerMetrics(source);
        labels["model"] = "second";
        source.Clear();
        Assert.HasCount(1, observation.Samples);
        Assert.AreEqual("first", observation.Samples[0].Labels["model"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)metric.Labels).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ServerMetric>)observation.Samples).Clear());
    }

    [TestMethod]
    public void Defaults_DoNotTurnMissingObservationsIntoAbsenceOrZero()
    {
        var model = new ServerModel("alias");
        Assert.AreEqual(ModelInstallationState.Unknown, model.InstallationState);
        Assert.AreEqual(ModelLoadState.Unknown, model.LoadState);
        Assert.IsNull(model.IsRemote);
        Assert.IsNull(model.MemoryBytes);
        Assert.IsNull(model.SizeBytes);
        Assert.IsNull(model.ContextLength);
        var progress = new ModelDownloadProgress("alias", "preparing");
        Assert.IsNull(progress.CompletedBytes);
        Assert.IsNull(progress.TotalBytes);
        var capabilities = new ServingCapabilities();
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelDownloading);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void Metrics_PreservePrometheusSpecialValues(double value)
        => Assert.AreEqual(value, new ServerMetric("sample", value).Value);

    [TestMethod]
    public void InvalidObservations_AreRejectedInsteadOfReturningMisleadingValues()
    {
        Assert.Throws<ArgumentException>(() => new ServerModel(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerModel("m", memoryBytes: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelDownloadProgress("m", "pulling", 2, 1));
        Assert.Throws<ArgumentException>(() => new ServerMetrics(new ServerMetric[] { null! }));
        Assert.Throws<ArgumentException>(() => new ServerInfo("runtime", new Uri("relative", UriKind.Relative)));
    }

    [TestMethod]
    public void HttpFailureIsClassifiedButMissingStatusRemainsUnknown()
    {
        Assert.AreEqual(ServingFailureKind.Http, new ServingException("Rejected", 403).FailureKind);
        var unknown = new ServingException("Failed");
        Assert.IsNull(unknown.StatusCode);
        Assert.AreEqual(ServingFailureKind.Unknown, unknown.FailureKind);
        Assert.AreEqual(ServingFailureKind.Timeout,
            new ServingException("Timeout", failureKind: ServingFailureKind.Timeout).FailureKind);
    }
}
