using System.Net;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace Mythosia.AI.Serving.LlamaCpp.Tests;

[TestClass]
public sealed class LlamaCppServerTests
{
    private const string Router = "{\"role\":\"router\",\"build_info\":\"b1234-test\"}";
    private const string Single = "{\"total_slots\":2,\"default_generation_settings\":{\"n_ctx\":4096},\"is_sleeping\":false}";
    private const string Inventory = "{\"data\":[{\"id\":\"owner/model:Q4\",\"extra\":true}]}";

    [TestMethod]
    public async Task RootPrefixAndBearerArePerRequest_WithoutMutatingSharedClient()
    {
        using var handler = new TestHttp();
        handler.Json(Router);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://unrelated.invalid/") };
        client.DefaultRequestHeaders.Add("X-Existing", "keep");
        var timeout = client.Timeout;
        var server = new LlamaCppServer("https://host.invalid/prefix/v1/", client, "private-key");
        var info = await server.GetInfoAsync();
        Assert.AreEqual("https://host.invalid/prefix/", server.Endpoint.AbsoluteUri);
        Assert.AreEqual("/prefix/props", handler.Calls.Single().Path);
        Assert.AreEqual("Bearer private-key", handler.Calls.Single().Authorization);
        Assert.AreEqual("https://unrelated.invalid/", client.BaseAddress.AbsoluteUri);
        Assert.IsNull(client.DefaultRequestHeaders.Authorization);
        Assert.AreEqual(timeout, client.Timeout);
        Assert.AreEqual(ServerMode.Router, info.Mode);
        Assert.AreEqual("b1234-test", info.Version);
    }

    [TestMethod]
    [DataRow("file:///tmp/model")]
    [DataRow("https://name:private-key@host.invalid/")]
    [DataRow("https://host.invalid/?key=private-key")]
    [DataRow("https://host.invalid/#private-key")]
    [DataRow("not-private-key-url")]
    public void InvalidEndpointsFailWithoutDisclosingInput(string endpoint)
    {
        using var client = new HttpClient();
        var ex = Assert.Throws<ArgumentException>(() => new LlamaCppServer(endpoint, client));
        Assert.IsFalse(ex.ToString().Contains("private-key", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ModelInventorySeparatesInstallationLoadingAndUnknownStates()
    {
        using var handler = new TestHttp();
        handler.Json("""
            {"data":[
              {"id":"cached","source":"cache","status":{"value":"unloaded"},"meta":{"size":123,"n_ctx":4096}},
              {"id":"remote-preset","source":"preset","status":{"value":"unloaded"}},
              {"id":"ready","status":{"value":"loaded"}},
              {"id":"sleep","status":{"value":"sleeping"}},
              {"id":"pending","status":{"value":"downloading"}},
              {"id":"broken","status":{"value":"unloaded","failed":true,"exit_code":1}},
              {"id":"new-state","status":{"value":"future-state"},"future":{}}
            ],"extra":"permitted"}
            """);
        handler.Json(Router);
        using var client = new HttpClient(handler);
        var models = await new LlamaCppServer("http://host.invalid", client).GetModelsAsync();
        Assert.AreEqual(7, models.Count);
        Assert.AreEqual(ModelInstallationState.Installed, models[0].InstallationState);
        Assert.AreEqual(ModelLoadState.Unloaded, models[0].LoadState);
        Assert.AreEqual(123L, models[0].SizeBytes);
        Assert.AreEqual(4096, models[0].ContextLength);
        Assert.AreEqual(ModelInstallationState.Unknown, models[1].InstallationState);
        Assert.AreEqual(ModelLoadState.Loaded, models[2].LoadState);
        Assert.AreEqual(ModelLoadState.Sleeping, models[3].LoadState);
        Assert.AreEqual(ModelInstallationState.NotInstalled, models[4].InstallationState);
        Assert.AreEqual(ModelLoadState.Failed, models[5].LoadState);
        Assert.AreEqual(ModelLoadState.Unknown, models[6].LoadState);
        Assert.AreEqual("future-state", models[6].NativeState);
    }

    [TestMethod]
    public async Task SingleModelSleepingIsReadWithoutWakingIt()
    {
        using var handler = new TestHttp();
        handler.Json(Inventory);
        handler.Json(Single.Replace("false", "true", StringComparison.Ordinal));
        using var client = new HttpClient(handler);
        var models = await new LlamaCppServer("http://host.invalid", client).GetModelsAsync();
        Assert.AreEqual(ModelLoadState.Sleeping, models.Single().LoadState);
        Assert.AreEqual(ModelInstallationState.Installed, models.Single().InstallationState);
        Assert.IsTrue(handler.Calls.All(call => call.Method == "GET"));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"data\":null}")]
    [DataRow("{\"data\":{}}")]
    [DataRow("{\"data\":[],\"error\":\"private-error\"}")]
    [DataRow("{\"data\":[null]}")]
    [DataRow("{\"data\":[{\"id\":\"\"}]}")]
    [DataRow("{\"data\":[{\"id\":1}]}")]
    [DataRow("{\"data\":[{\"id\":\"x\"},{\"id\":\"x\"}]}")]
    [DataRow("{\"data\":[{\"id\":\"x\",\"status\":\"loaded\"}]}")]
    [DataRow("{\"data\":[{\"id\":\"x\",\"meta\":{\"size\":-1}}]}")]
    [DataRow("{\"data\":[{\"id\":\"x\",\"meta\":{\"n_ctx\":2147483648}}]}")]
    [DataRow("{\"data\":[{\"id\":\"x\",\"meta\":{\"size\":1.5}}]}")]
    [DataRow("{\"data\":[{\"id\":\"x\",\"meta\":{\"size\":9223372036854775808}}]}")]
    public async Task MalformedInventoryDoesNotSilentlyBecomeEmptyOrDefault(string body)
    {
        using var handler = new TestHttp();
        handler.Json(body);
        handler.Json(Router);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client).GetModelsAsync());
        Assert.AreEqual(ServingFailureKind.InvalidResponse, error.FailureKind);
    }

    [TestMethod]
    public async Task MissingPropertiesDoesNotHideAdvertisedModelsOrInventTheirLoadState()
    {
        using var handler = new TestHttp();
        handler.Json(Inventory);
        handler.Json("{\"error\":\"private-body\"}", HttpStatusCode.Forbidden);
        using var client = new HttpClient(handler);
        var models = await new LlamaCppServer("http://host.invalid", client).GetModelsAsync();
        Assert.AreEqual(ModelLoadState.Unknown, models.Single().LoadState);
        Assert.AreEqual(ModelInstallationState.Unknown, models.Single().InstallationState);
    }

    [TestMethod]
    public async Task SingleCapabilitiesOnlyProbeReads_AndRecognizeDisabledMetrics()
    {
        using var handler = new TestHttp();
        handler.Json(Single);
        handler.Json(Inventory);
        handler.Json("{\"error\":{\"type\":\"not_supported_error\"}}", HttpStatusCode.NotImplemented);
        using var client = new HttpClient(handler);
        var caps = await new LlamaCppServer("http://host.invalid", client).GetCapabilitiesAsync();
        Assert.AreEqual(ServingFeatureSupport.Supported, caps.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, caps.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, caps.ModelUnloading);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, caps.ModelDownloading);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, caps.Metrics);
        Assert.IsTrue(handler.Calls.All(call => call.Method == "GET"));
    }

    [TestMethod]
    public async Task RouterCapabilitiesDoNotMistakeSseForProofOfDownloadRoute()
    {
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Json(Inventory);
        handler.Text(": heartbeat\n\n");
        using var client = new HttpClient(handler);
        var caps = await new LlamaCppServer("http://host.invalid", client).GetCapabilitiesAsync();
        Assert.AreEqual(ServingFeatureSupport.Supported, caps.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Supported, caps.ModelUnloading);
        Assert.AreEqual(ServingFeatureSupport.Unknown, caps.ModelDownloading);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, caps.Metrics);
        Assert.IsTrue(handler.Calls.All(call => call.Method == "GET"));
        CollectionAssert.AreEqual(new[] { "/props", "/v1/models", "/models/sse" }, handler.Calls.Select(call => call.Path).ToArray());
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(500)]
    public async Task InconclusiveCapabilityProbesRemainUnknown(int status)
    {
        using var handler = new TestHttp();
        handler.Json("private-body", (HttpStatusCode)status);
        handler.Json("private-body", (HttpStatusCode)status);
        using var client = new HttpClient(handler);
        var caps = await new LlamaCppServer("http://host.invalid", client).GetCapabilitiesAsync();
        Assert.AreEqual(ServingFeatureSupport.Unknown, caps.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Unknown, caps.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Unknown, caps.ModelDownloading);
    }

    [TestMethod]
    [DataRow(200, "{\"status\":\"ok\"}", ServerHealthStatus.Healthy)]
    [DataRow(200, "{}", ServerHealthStatus.Unexpected)]
    [DataRow(200, "{\"status\":\"ok\",\"error\":{\"message\":\"private-error\"}}", ServerHealthStatus.Unexpected)]
    [DataRow(200, "private-body", ServerHealthStatus.Unexpected)]
    [DataRow(503, "private-body", ServerHealthStatus.NotReady)]
    [DataRow(401, "private-body", ServerHealthStatus.Unauthorized)]
    [DataRow(403, "private-body", ServerHealthStatus.Unauthorized)]
    [DataRow(500, "private-body", ServerHealthStatus.Unexpected)]
    public async Task HealthClassifiesStatusAndRejectsInvalidSuccessBodies(int status, string body, ServerHealthStatus expected)
    {
        using var handler = new TestHttp();
        handler.Json(body, (HttpStatusCode)status);
        using var client = new HttpClient(handler);
        var health = await new LlamaCppServer("http://host.invalid", client).GetHealthAsync();
        Assert.AreEqual(expected, health.Status);
        Assert.IsNull(health.Detail);
    }

    [TestMethod]
    public async Task HealthNetworkErrorsAreSanitized()
    {
        using var handler = new TestHttp();
        handler.Reply(_ => throw new HttpRequestException("private-key at private-endpoint"));
        using var client = new HttpClient(handler);
        var health = await new LlamaCppServer("http://host.invalid", client).GetHealthAsync();
        Assert.AreEqual(ServerHealthStatus.Unreachable, health.Status);
        Assert.IsNull(health.Detail);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RouterLifecycleSendsOnlyTheRequestedExplicitCommand(bool load)
    {
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Json("{\"success\":true,\"future\":1}");
        using var client = new HttpClient(handler);
        var server = new LlamaCppServer("http://host.invalid", client, "test-key");
        if (load) await server.LoadModelAsync("owner/model:Q4");
        else await server.UnloadModelAsync("owner/model:Q4");
        Assert.AreEqual("POST", handler.Calls[1].Method);
        Assert.AreEqual(load ? "/models/load" : "/models/unload", handler.Calls[1].Path);
        Assert.AreEqual("owner/model:Q4", (string?)JObject.Parse(handler.Calls[1].Body!)["model"]);
        Assert.AreEqual("Bearer test-key", handler.Calls[1].Authorization);
    }

    [TestMethod]
    [DataRow("{\"success\":false}")]
    [DataRow("{}")]
    [DataRow("{\"success\":\"true\"}")]
    [DataRow("{\"success\":true,\"error\":\"private-error\"}")]
    public async Task LifecycleRequiresTrueAcknowledgement(string body)
    {
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Json(body);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client).LoadModelAsync("model"));
        Assert.IsFalse(error.ToString().Contains("private-error", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SingleAndUnknownModesNeverSendLifecycleMutations()
    {
        using var handler = new TestHttp();
        handler.Json(Single);
        handler.Json("{\"role\":\"future\"}");
        using var client = new HttpClient(handler);
        var server = new LlamaCppServer("http://host.invalid", client);
        await Assert.ThrowsAsync<NotSupportedException>(() => server.LoadModelAsync("model"));
        await Assert.ThrowsAsync<ServingException>(() => server.UnloadModelAsync("model"));
        Assert.IsTrue(handler.Calls.All(call => call.Method == "GET"));
    }

    [TestMethod]
    public async Task CallerCancellationPropagatesItsOriginalToken()
    {
        using var handler = new TestHttp();
        handler.Reply(async token => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var pending = new LlamaCppServer("http://host.invalid", client).GetHealthAsync(cancellation.Token);
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
    }

    [TestMethod]
    public async Task PreCancelledOperationsDoNotSendHttp()
    {
        using var handler = new TestHttp();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var server = new LlamaCppServer("http://host.invalid", client);
        await Assert.ThrowsAsync<OperationCanceledException>(() => server.DownloadModelAsync("model", cancellationToken: cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => server.GetCapabilitiesAsync(cancellation.Token));
        Assert.IsEmpty(handler.Calls);
    }
}
