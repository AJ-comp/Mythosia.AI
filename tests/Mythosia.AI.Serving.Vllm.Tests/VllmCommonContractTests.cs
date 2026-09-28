using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Mythosia.AI.Serving.Vllm.Tests;

[TestClass]
[TestCategory("Unit")]
public class VllmCommonContractTests
{
    private const string Models = """
        {"data":[
          {"id":"alias-a","root":"org/base","max_model_len":32768},
          {"id":"alias-b","root":"org/base","max_model_len":32768},
          {"id":"adapter","root":"/adapters/one","parent":"alias-a"},
          {"id":"alias-only"}
        ]}
        """;

    private const string Metrics = """
        # TYPE vllm:num_requests_running gauge
        vllm:num_requests_running{model_name="alias-a",engine="0"} 2
        vllm:num_requests_running{model_name="alias-b",engine="1"} 3
        vllm:renamed_metric{model_name="alias-a"} NaN
        """;

    [TestMethod]
    public async Task CommonModels_PreserveAliasesWithoutInferringInstalledOrLoaded()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(Models)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://unrelated.example/") };
        client.DefaultRequestHeaders.Add("X-Shared", "unchanged");
        var concrete = new VllmServer("https://server.example/proxy/v1", client, "test-key");
        IModelServer common = concrete;

        IReadOnlyList<VllmModelCard> legacy = await concrete.GetModelsAsync();
        var models = await common.GetModelsAsync();

        Assert.AreEqual(4, legacy.Count);
        CollectionAssert.AreEqual(new[] { "alias-a", "alias-b", "adapter", "alias-only" }, models.Select(m => m.Id).ToArray());
        Assert.AreEqual("org/base", models[0].DisplayName);
        Assert.AreEqual(32768, models[0].ContextLength);
        Assert.AreEqual("alias-only", models[3].DisplayName);
        foreach (var model in models)
        {
            Assert.AreEqual(ModelInstallationState.Unknown, model.InstallationState);
            Assert.AreEqual(ModelLoadState.Unknown, model.LoadState);
            Assert.IsNull(model.SizeBytes);
            Assert.IsNull(model.MemoryBytes);
            Assert.IsNull(model.IsRemote);
            Assert.IsNull(model.NativeState);
        }
        Assert.AreEqual(new Uri("https://server.example/proxy/"), common.Endpoint);
        Assert.IsTrue(handler.Calls.All(call => call.Url == "https://server.example/proxy/v1/models"));
        Assert.IsTrue(handler.Calls.All(call => call.Authorization == "Bearer test-key"));
        Assert.AreEqual(new Uri("https://unrelated.example/"), client.BaseAddress);
        Assert.IsNull(client.DefaultRequestHeaders.Authorization);
        Assert.AreEqual("unchanged", client.DefaultRequestHeaders.GetValues("X-Shared").Single());
    }

    [TestMethod]
    public async Task LegacyMethods_RetainConcreteReturnTypesAndPermissiveParsing()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");
        handler.Enqueue(HttpStatusCode.OK, "not json", "text/plain");
        handler.Enqueue(HttpStatusCode.OK, "", "text/plain");
        handler.EnqueueEmpty(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example/", client);

        Task<IReadOnlyList<VllmModelCard>> models = server.GetModelsAsync();
        Task<string?> version = server.GetVersionAsync();
        Task<VllmMetrics> metrics = server.GetMetricsAsync();
        Task<VllmHealthReport> health = server.GetHealthAsync();

        Assert.AreEqual(0, (await models).Count);
        Assert.IsNull(await version);
        Assert.AreEqual(0, (await metrics).Families.Count);
        Assert.AreEqual(VllmHealthStatus.Healthy, (await health).Status);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.OK, ServerHealthStatus.Healthy)]
    [DataRow(HttpStatusCode.ServiceUnavailable, ServerHealthStatus.NotReady)]
    [DataRow(HttpStatusCode.Unauthorized, ServerHealthStatus.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden, ServerHealthStatus.Unauthorized)]
    [DataRow(HttpStatusCode.NotFound, ServerHealthStatus.Unexpected)]
    [DataRow(HttpStatusCode.InternalServerError, ServerHealthStatus.Unexpected)]
    public async Task CommonHealth_MapsEvidenceWithoutChangingLegacyStatuses(HttpStatusCode status, ServerHealthStatus expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response("", status)));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);

        var health = await server.GetHealthAsync();

        Assert.AreEqual(expected, health.Status);
        Assert.AreEqual((int)status, health.StatusCode);
        Assert.AreEqual("https://server.example/health", handler.Calls.Single().Url);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CommonHealth_TransportFailureOrClientTimeoutIsUnreachable(bool timeout)
    {
        using var handler = new Handler((_, _) => throw (timeout
            ? new TaskCanceledException("timeout")
            : new HttpRequestException("offline")));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);

        var health = await server.GetHealthAsync();

        Assert.AreEqual(ServerHealthStatus.Unreachable, health.Status);
        Assert.IsNull(health.StatusCode);
    }

    [TestMethod]
    public async Task GetInfo_ReportsVersionWithoutGuessingMode()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response("""{"version":"0.25.0"}""")));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example/v1", client);

        var info = await server.GetInfoAsync();

        Assert.AreEqual("vllm", info.Runtime);
        Assert.AreEqual("0.25.0", info.Version);
        Assert.AreEqual(server.Endpoint, info.Endpoint);
        Assert.AreEqual(ServerMode.Unknown, info.Mode);
        Assert.AreEqual("https://server.example/version", handler.Calls.Single().Url);
    }

    [TestMethod]
    [DataRow("<html>login page</html>")]
    [DataRow("[]")]
    [DataRow("{\"version\":{\"unexpected\":\"value\"}}")]
    public async Task GetInfo_RejectsMalformedVersionResponse(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(body)));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);

        var error = await Assert.ThrowsExactlyAsync<VllmException>(() => server.GetInfoAsync());

        Assert.AreEqual(200, error.StatusCode);
        Assert.IsNull(error.ResponseBody);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"data\":{}}")]
    [DataRow("{\"data\":[null]}")]
    [DataRow("{\"data\":[{\"id\":7}]}")]
    [DataRow("{\"data\":[{\"id\":\" \"}]}")]
    [DataRow("{\"data\":[{\"id\":\"alias\",\"root\":{}}]}")]
    [DataRow("{\"data\":[{\"id\":\"alias\",\"max_model_len\":-1}]}")]
    [DataRow("{\"data\":[{\"id\":\"alias\",\"max_model_len\":2147483648}]}")]
    public async Task CommonModels_RejectMalformedInventoryRatherThanClaimEmptySuccess(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(body)));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);

        var error = await Assert.ThrowsExactlyAsync<VllmException>(() => server.GetModelsAsync());

        Assert.AreEqual(200, error.StatusCode);
        Assert.IsInstanceOfType<ServingException>(error);
        Assert.AreEqual(200, ((ServingException)error).StatusCode);
        Assert.IsNull(error.ResponseBody);
    }

    [TestMethod]
    public async Task CommonMetrics_PreserveEverySampleLabelAndRawExposition()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(Metrics)));
        using var client = new HttpClient(handler);
        IModelMetricsProvider server = new VllmServer("https://server.example", client);

        var metrics = await server.GetMetricsAsync();

        Assert.AreEqual(3, metrics.Samples.Count);
        CollectionAssert.AreEqual(new[] { 2.0, 3.0 }, metrics.Samples.Take(2).Select(s => s.Value).ToArray());
        Assert.AreEqual("alias-a", metrics.Samples[0].Labels["model_name"]);
        Assert.AreEqual("1", metrics.Samples[1].Labels["engine"]);
        Assert.IsTrue(double.IsNaN(metrics.Samples[2].Value));
        Assert.AreEqual(Metrics, metrics.RawText);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, string>)metrics.Samples[0].Labels).Add("changed", "value"));
    }

    [TestMethod]
    public async Task Capabilities_ObserveBothEndpointsWithoutAdvertisingLifecycle()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Response(
            request.RequestUri!.AbsolutePath == "/v1/models" ? "{\"data\":[]}" : Metrics)));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);

        var capabilities = await server.GetCapabilitiesAsync();

        Assert.AreEqual(ServingFeatureSupport.Supported, capabilities.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Supported, capabilities.Metrics);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, capabilities.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, capabilities.ModelUnloading);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, capabilities.ModelDownloading);
        Assert.IsFalse(server is IModelLifecycle);
        Assert.IsFalse(server is IModelDownloader);
        CollectionAssert.AreEqual(new[] { "https://server.example/v1/models", "https://server.example/metrics" }, handler.Calls.Select(c => c.Url).ToArray());
    }

    [TestMethod]
    [DataRow(HttpStatusCode.NotFound, ServingFeatureSupport.Unsupported)]
    [DataRow(HttpStatusCode.MethodNotAllowed, ServingFeatureSupport.Unsupported)]
    [DataRow(HttpStatusCode.NotImplemented, ServingFeatureSupport.Unsupported)]
    [DataRow(HttpStatusCode.Unauthorized, ServingFeatureSupport.Unknown)]
    [DataRow(HttpStatusCode.Forbidden, ServingFeatureSupport.Unknown)]
    [DataRow(HttpStatusCode.InternalServerError, ServingFeatureSupport.Unknown)]
    [DataRow(HttpStatusCode.ServiceUnavailable, ServingFeatureSupport.Unknown)]
    public async Task Capabilities_DistinguishMissingRoutesFromInconclusiveFailures(HttpStatusCode status, ServingFeatureSupport expected)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response("{}", status)));
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example", client);

        var capabilities = await server.GetCapabilitiesAsync();

        Assert.AreEqual(expected, capabilities.ModelListing);
        Assert.AreEqual(expected, capabilities.Metrics);
    }

    [TestMethod]
    public async Task Capabilities_AuthFailureDoesNotHideIndependentMetricEvidence()
    {
        using var handler = new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v1/models"
            ? Response("{}", HttpStatusCode.Unauthorized) : Response(Metrics)));
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example", client);

        var capabilities = await server.GetCapabilitiesAsync();

        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Supported, capabilities.Metrics);
    }

    [TestMethod]
    public async Task Capabilities_ValidStatusWithWrongBodiesDoesNotProveSupport()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response("<html>login page</html>")));
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example", client);

        var capabilities = await server.GetCapabilitiesAsync();

        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.Metrics);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Capabilities_ConnectionFailureOrTimeoutRemainUnknown(bool timeout)
    {
        using var handler = new Handler((_, _) => throw (timeout
            ? new TaskCanceledException("timeout") : new HttpRequestException("offline")));
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example", client);

        var capabilities = await server.GetCapabilitiesAsync();

        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.Metrics);
        Assert.AreEqual(2, handler.Calls.Count);
    }

    [TestMethod]
    public async Task PreCancelledToken_PerformsNoHttpWork()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(Models)));
        using var client = new HttpClient(handler);
        var concrete = new VllmServer("https://server.example", client);
        IModelServer common = concrete;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => concrete.GetModelsAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => common.GetModelsAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => common.GetHealthAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => common.GetInfoAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => common.GetCapabilitiesAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => ((IModelMetricsProvider)concrete).GetMetricsAsync(cancellation.Token));
        Assert.AreEqual(0, handler.Calls.Count);
    }

    [TestMethod]
    [DataRow("legacy-models")]
    [DataRow("legacy-version")]
    [DataRow("legacy-metrics")]
    [DataRow("common-models")]
    [DataRow("common-info")]
    [DataRow("common-metrics")]
    [DataRow("capabilities")]
    public async Task CallerCancellation_InterruptsBodyReadsAndDisposesResponse(string operation)
    {
        using var stream = new BlockingReadStream();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        using var client = new HttpClient(handler);
        var concrete = new VllmServer("https://server.example", client);
        IModelServer common = concrete;
        using var cancellation = new CancellationTokenSource();
        var task = operation switch
        {
            "legacy-models" => (Task)concrete.GetModelsAsync(cancellation.Token),
            "legacy-version" => concrete.GetVersionAsync(cancellation.Token),
            "legacy-metrics" => concrete.GetMetricsAsync(cancellation.Token),
            "common-models" => common.GetModelsAsync(cancellation.Token),
            "common-info" => common.GetInfoAsync(cancellation.Token),
            "common-metrics" => ((IModelMetricsProvider)concrete).GetMetricsAsync(cancellation.Token),
            _ => common.GetCapabilitiesAsync(cancellation.Token)
        };
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsTrue(stream.Disposed);
        Assert.AreEqual(1, handler.Calls.Count);
    }

    [TestMethod]
    public async Task BodyRead_PreservesDeclaredEncoding()
    {
        using var handler = new Handler((_, _) =>
        {
            var content = new ByteArrayContent(Encoding.Latin1.GetBytes("{\"data\":[{\"id\":\"café\"}]}"));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "iso-8859-1" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example", client);

        Assert.AreEqual("café", (await server.GetModelsAsync()).Single().Id);
    }

    [TestMethod]
    public async Task ExistingVllmExceptionAlsoHasCommonStatusCode()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(
            "{\"error\":{\"message\":\"denied\",\"type\":\"auth\",\"code\":403}}", HttpStatusCode.Forbidden)));
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example", client);

        var error = await Assert.ThrowsExactlyAsync<VllmException>(() => server.GetModelsAsync());

        int legacyStatus = error.StatusCode;
        ServingException common = error;
        Assert.AreEqual(403, legacyStatus);
        Assert.AreEqual(403, common.StatusCode);
        Assert.AreEqual("auth", error.ErrorType);
        Assert.AreEqual("403", error.ErrorCode);
    }

    [TestMethod]
    public async Task CommonErrors_DoNotForwardServerPayloadsOrTransportDetails()
    {
        const string sensitive = "do-not-forward-test-value";
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/version") throw new HttpRequestException(sensitive);
            if (request.RequestUri.AbsolutePath == "/health")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = sensitive });
            return Task.FromResult(Response($"{{\"error\":{{\"message\":\"{sensitive}\"}}}}", HttpStatusCode.Forbidden));
        });
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);

        var inventoryError = await Assert.ThrowsExactlyAsync<VllmException>(() => server.GetModelsAsync());
        var metricError = await Assert.ThrowsExactlyAsync<VllmException>(() => ((IModelMetricsProvider)server).GetMetricsAsync());
        var infoError = await Assert.ThrowsExactlyAsync<ServingException>(() => server.GetInfoAsync());
        var health = await server.GetHealthAsync();

        Assert.AreEqual(403, inventoryError.StatusCode);
        Assert.IsNull(inventoryError.ResponseBody);
        Assert.IsNull(metricError.ResponseBody);
        Assert.IsFalse(inventoryError.ToString().Contains(sensitive));
        Assert.IsFalse(metricError.ToString().Contains(sensitive));
        Assert.IsFalse(infoError.ToString().Contains(sensitive));
        Assert.IsFalse(health.Detail!.Contains(sensitive));
    }

    [TestMethod]
    [DataRow("{\"data\":[{\"id\":\"same\"},{\"id\":\"same\"}]}")]
    [DataRow("{\"data\":[{\"id\":\"first\",\"id\":\"replacement\"}]}")]
    [DataRow("{\"data\":[],\"error\":\"private-error\"}")]
    public async Task CommonInventory_RejectsAmbiguousIdentifiersAndErrorEnvelopes(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(json)));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);
        var error = await Assert.ThrowsAsync<ServingException>(() => server.GetModelsAsync());
        Assert.AreEqual(ServingFailureKind.InvalidResponse, error.FailureKind);
        Assert.IsFalse(error.ToString().Contains("private-error", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("{\"version\":\"first\",\"version\":\"replacement\"}")]
    [DataRow("{\"error\":\"private-error\"}")]
    public async Task CommonInfo_RejectsDuplicateFieldsAndErrorEnvelopes(string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(json)));
        using var client = new HttpClient(handler);
        IModelServer server = new VllmServer("https://server.example", client);
        var error = await Assert.ThrowsAsync<ServingException>(() => server.GetInfoAsync());
        Assert.AreEqual(ServingFailureKind.InvalidResponse, error.FailureKind);
    }

    [TestMethod]
    [DataRow("valid_count 1\nmalformed_count not-a-number")]
    [DataRow("valid_count{model=\"first\",model=\"second\"} 1")]
    public async Task CommonMetrics_RejectsPartialOrAmbiguousSamples_WhileLegacyStaysTolerant(string text)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(text)));
        using var client = new HttpClient(handler);
        var server = new VllmServer("https://server.example", client);
        Assert.AreEqual(1, (await server.GetMetricsAsync()).Families.Count);
        var error = await Assert.ThrowsAsync<ServingException>(() => ((IModelMetricsProvider)server).GetMetricsAsync());
        Assert.AreEqual(ServingFailureKind.InvalidResponse, error.FailureKind);
        var caps = await server.GetCapabilitiesAsync();
        Assert.AreEqual(ServingFeatureSupport.Unknown, caps.Metrics);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.OK, ServingFailureKind.Timeout)]
    [DataRow(HttpStatusCode.Forbidden, ServingFailureKind.Http)]
    public async Task CommonBodyTimeoutAndHttpErrorStatusDoNotWaitIndefinitely(HttpStatusCode status, ServingFailureKind expectedKind)
    {
        using var stream = new BlockingReadStream();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StreamContent(stream) }));
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(150) };
        IModelServer server = new VllmServer("https://server.example", client);
        var pending = server.GetModelsAsync();
        var error = await Assert.ThrowsAsync<ServingException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(expectedKind, error.FailureKind);
        Assert.IsTrue(stream.Disposed);
    }

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(string Url, string? Authorization)> Calls { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            return respond(request, cancellationToken);
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Deliberately ignore the token: response disposal must also unblock old/custom streams.
            Started.TrySetResult();
            return new ValueTask<int>(_completion.Task);
        }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _completion.TrySetException(new ObjectDisposedException(nameof(BlockingReadStream)));
            base.Dispose(disposing);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
