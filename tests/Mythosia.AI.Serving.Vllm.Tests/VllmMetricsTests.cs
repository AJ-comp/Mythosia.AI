namespace Mythosia.AI.Serving.Vllm.Tests;

[TestClass]
public class VllmMetricsTests
{
    private static VllmMetrics FromExposition(string exposition)
        => new(PrometheusTextParser.Parse(exposition), exposition);

    [TestMethod]
    public void TypedGetters_SumAcrossEngines()
    {
        var metrics = FromExposition("""
            vllm:num_requests_running{engine="0"} 1
            vllm:num_requests_running{engine="1"} 4
            vllm:prompt_tokens_total{engine="0"} 1000
            vllm:prompt_tokens_total{engine="1"} 250
            """);

        Assert.AreEqual(5.0, metrics.RunningRequests);
        Assert.AreEqual(1250.0, metrics.PromptTokensTotal);
    }

    [TestMethod]
    public void KvCacheUsage_AveragesAcrossSamples()
    {
        var metrics = FromExposition("""
            vllm:kv_cache_usage_perc{engine="0"} 0.2
            vllm:kv_cache_usage_perc{engine="1"} 0.6
            """);

        Assert.AreEqual(0.4, metrics.KvCacheUsage!.Value, 1e-9);
    }

    [TestMethod]
    public void MissingFamily_ReturnsNull()
    {
        // Simulates a vLLM version that renamed the metric — getters go null, Families still has the data.
        var metrics = FromExposition("""vllm:gpu_cache_usage_perc{engine="0"} 0.5""");

        Assert.IsNull(metrics.KvCacheUsage);
        Assert.IsNull(metrics.RunningRequests);
        Assert.AreEqual(0.5, metrics.Families["vllm:gpu_cache_usage_perc"][0].Value);
    }

    [TestMethod]
    public void RawText_IsPassedThroughVerbatim()
    {
        const string exposition = "vllm:num_requests_waiting 0";
        var metrics = FromExposition(exposition);

        Assert.AreEqual(exposition, metrics.RawText);
        Assert.AreEqual(0.0, metrics.WaitingRequests);
    }
}
