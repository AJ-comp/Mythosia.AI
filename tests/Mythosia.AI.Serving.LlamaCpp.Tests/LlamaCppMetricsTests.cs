namespace Mythosia.AI.Serving.LlamaCpp.Tests;

[TestClass]
public sealed class LlamaCppMetricsTests
{
    private const string Single = "{\"total_slots\":2,\"default_generation_settings\":{}}";
    private const string Router = "{\"role\":\"router\"}";

    [TestMethod]
    public async Task SingleMetricsRetainLabelsEscapesSpecialValuesAndRawText()
    {
        const string text = """
            # HELP llamacpp:tokens_total Token count
            # TYPE llamacpp:tokens_total counter
            llamacpp:tokens_total{slot="one",model="owner/model"} 12
            llamacpp:tokens_total{slot="two",model="owner/model"} 1.25e2 1710000000000
            llamacpp:info{path="a\\b",quote="a\"b",line="a\nb",} 1
            llamacpp:unknown NaN
            llamacpp:limit +Inf
            llamacpp:other -Inf
            """;
        using var handler = new TestHttp();
        handler.Json(Single);
        handler.Text(text, "text/plain");
        using var client = new HttpClient(handler);
        var metrics = await new LlamaCppServer("http://host.invalid", client).GetMetricsAsync();
        Assert.AreEqual(6, metrics.Samples.Count);
        Assert.AreEqual(text, metrics.RawText);
        Assert.AreEqual("one", metrics.Samples[0].Labels["slot"]);
        Assert.AreEqual("two", metrics.Samples[1].Labels["slot"]);
        Assert.AreEqual(125d, metrics.Samples[1].Value);
        Assert.AreEqual("a\\b", metrics.Samples[2].Labels["path"]);
        Assert.AreEqual("a\"b", metrics.Samples[2].Labels["quote"]);
        Assert.AreEqual("a\nb", metrics.Samples[2].Labels["line"]);
        Assert.IsTrue(double.IsNaN(metrics.Samples[3].Value));
        Assert.IsTrue(double.IsPositiveInfinity(metrics.Samples[4].Value));
        Assert.IsTrue(double.IsNegativeInfinity(metrics.Samples[5].Value));
    }

    [TestMethod]
    public async Task RouterModelMetricsDisableAutoloadAndEncodeModelIdentifier()
    {
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Text("llamacpp:tokens_total 12", "text/plain");
        using var client = new HttpClient(handler);
        var metrics = await new LlamaCppServer("http://host.invalid/prefix", client).GetMetricsAsync("owner/model:Q4&autoload=true");
        Assert.AreEqual(12d, metrics.Samples.Single().Value);
        Assert.AreEqual("/prefix/metrics?model=owner%2Fmodel%3AQ4%26autoload%3Dtrue&autoload=false", handler.Calls[1].Path);
        Assert.IsTrue(handler.Calls.All(call => call.Method == "GET"));
    }

    [TestMethod]
    public async Task RouterDoesNotOfferMisleadingAggregateMetrics()
    {
        using var handler = new TestHttp();
        handler.Json(Router);
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<NotSupportedException>(() => new LlamaCppServer("http://host.invalid", client).GetMetricsAsync());
        Assert.AreEqual(1, handler.Calls.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("# arbitrary proxy response")]
    [DataRow("private-body")]
    [DataRow("x not-a-number")]
    [DataRow("x{a=\"one\",a=\"two\"} 1")]
    [DataRow("x{a=\"unterminated} 1")]
    [DataRow("x{a=\"bad\\t\"} 1")]
    [DataRow("x 1 timestamp-garbage")]
    [DataRow("x 1 2 extra")]
    public async Task MalformedMetricsFailWithoutExposingTheirContent(string text)
    {
        using var handler = new TestHttp();
        handler.Json(Single);
        handler.Text(text, "text/plain");
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client).GetMetricsAsync());
        Assert.AreEqual(ServingFailureKind.InvalidResponse, error.FailureKind);
        if (text.Length != 0) Assert.IsFalse(error.ToString().Contains(text, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MetadataOnlyMetricsAreAValidEmptySnapshot()
    {
        using var handler = new TestHttp();
        handler.Json(Single);
        handler.Text("# HELP llamacpp:tokens_total Tokens\n# TYPE llamacpp:tokens_total counter\n", "text/plain");
        using var client = new HttpClient(handler);
        var metrics = await new LlamaCppServer("http://host.invalid", client).GetMetricsAsync();
        Assert.IsEmpty(metrics.Samples);
    }
}
