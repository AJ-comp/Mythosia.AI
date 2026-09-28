using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Mythosia.AI.Serving;
using Newtonsoft.Json.Linq;

namespace Mythosia.AI.Serving.Ollama.Tests;

[TestClass]
public sealed class OllamaServerTests
{
    [TestMethod]
    public async Task Discovery_MergesRegisteredAndRunningWithoutAssumingRemoteResidency()
    {
        using var handler = new StubHandler();
        handler.Json("""{"models":[{"name":"local:latest","model":"local:latest","size":100,"future":{"x":1}},{"model":"cold:latest"},{"model":"remote:cloud","remote_model":"upstream","remote_host":"https://ollama.com"}]}""");
        handler.Json("""{"models":[{"name":"local:latest","model":"local:latest","size":120,"size_vram":80,"context_length":4096},{"model":"appeared:latest","size_vram":10}]}""");
        using var http = new HttpClient(handler);
        var server = new OllamaServer("http://localhost:11434", http);
        var models = await server.GetModelsAsync();
        Assert.HasCount(4, models);
        Assert.AreEqual(ModelInstallationState.Installed, models[0].InstallationState);
        Assert.AreEqual(ModelLoadState.Loaded, models[0].LoadState);
        Assert.AreEqual(100L, models[0].SizeBytes);
        Assert.AreEqual(80L, models[0].MemoryBytes);
        Assert.AreEqual(4096, models[0].ContextLength);
        Assert.AreEqual(ModelLoadState.Unloaded, models[1].LoadState);
        Assert.IsTrue(models[2].IsRemote);
        Assert.AreEqual(ModelLoadState.Unknown, models[2].LoadState);
        Assert.AreEqual(ModelInstallationState.Unknown, models[3].InstallationState);
        Assert.AreEqual(ModelLoadState.Loaded, models[3].LoadState);
        CollectionAssert.AreEqual(new[] { "/api/tags", "/api/ps" }, handler.Requests.Select(x => x.Path).ToArray());
    }

    [TestMethod]
    public async Task Discovery_EmptyListsAreValid_AndModelSuffixDoesNotDetermineRemoteState()
    {
        using var handler = new StubHandler();
        handler.Json("""{"models":[]}""");
        handler.Json("""{"models":[]}""");
        handler.Json("""{"models":[{"name":"custom:cloud"}]}""");
        handler.Json("""{"models":[]}""");
        using var http = new HttpClient(handler);
        var server = new OllamaServer("http://localhost", http);
        Assert.HasCount(0, await server.GetModelsAsync());
        Assert.IsNull((await server.GetModelsAsync())[0].IsRemote);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"models\":null}")]
    [DataRow("{\"models\":[null]}")]
    [DataRow("{\"models\":[{}]}")]
    [DataRow("{\"models\":[{\"model\":\" \"}]}")]
    [DataRow("{\"models\":[{\"model\":42}]}")]
    [DataRow("{\"models\":[{\"model\":\"same\"},{\"name\":\"same\"}]}")]
    [DataRow("{\"models\":[{\"model\":\"one\",\"size\":-1}]}")]
    [DataRow("{\"models\":[{\"model\":\"one\",\"size\":1.5}]}")]
    [DataRow("{\"models\":[{\"model\":\"one\",\"size\":9223372036854775808}]}")]
    [DataRow("{\"models\":[{\"model\":\"one\",\"remote_host\":false}]}")]
    public async Task Discovery_RejectsMalformedRegisteredModels(string invalid)
    {
        using var handler = new StubHandler();
        handler.Json(invalid);
        handler.Json("""{"models":[]}""");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ServingException>(() => new OllamaServer("http://localhost", http).GetModelsAsync());
    }

    [TestMethod]
    [DataRow("{\"models\":[{\"model\":\"one\",\"context_length\":\"4096\"}]}")]
    [DataRow("{\"models\":[{\"model\":\"one\",\"context_length\":2147483648}]}")]
    [DataRow("{\"models\":[{\"model\":\"one\",\"size_vram\":-1}]}")]
    [DataRow("{\"models\":[{\"model\":\"one\"},{\"model\":\"one\"}]}")]
    public async Task Discovery_RejectsMalformedRunningModels(string invalid)
    {
        using var handler = new StubHandler();
        handler.Json("""{"models":[]}""");
        handler.Json(invalid);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ServingException>(() => new OllamaServer("http://localhost", http).GetModelsAsync());
    }

    [TestMethod]
    [DataRow("http://localhost:11434", "/api/version")]
    [DataRow("http://localhost:11434/api/", "/api/version")]
    [DataRow("http://localhost:11434/v1", "/api/version")]
    [DataRow("http://localhost:11434/proxy/ollama/v1", "/proxy/ollama/api/version")]
    public async Task Connection_NormalizesEndpointAndLeavesSharedClientUnchanged(string endpoint, string expectedPath)
    {
        using var handler = new StubHandler();
        handler.Json("""{"version":"0.20.0","future":true}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://original/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "original");
        var server = new OllamaServer(endpoint, http, "request-secret");
        Assert.AreEqual("0.20.0", await server.GetVersionAsync());
        Assert.AreEqual(expectedPath, handler.Requests.Single().Path);
        Assert.AreEqual("Bearer request-secret", handler.Requests.Single().Authorization);
        Assert.AreEqual("http://original/", http.BaseAddress.ToString());
        Assert.AreEqual("original", http.DefaultRequestHeaders.Authorization.Parameter);
    }

    [TestMethod]
    public async Task Connection_ConcurrentClientsDoNotExchangeKeys()
    {
        using var handler = new StubHandler();
        handler.Json("""{"version":"1"}""");
        handler.Json("""{"version":"1"}""");
        using var http = new HttpClient(handler);
        await Task.WhenAll(new OllamaServer("http://first", http, "first-secret").GetInfoAsync(),
            new OllamaServer("http://second", http, "second-secret").GetInfoAsync());
        Assert.AreEqual("Bearer first-secret", handler.Requests.Single(x => x.Host == "first").Authorization);
        Assert.AreEqual("Bearer second-secret", handler.Requests.Single(x => x.Host == "second").Authorization);
        Assert.IsNull(http.DefaultRequestHeaders.Authorization);
    }

    [TestMethod]
    public async Task HealthAndCapabilities_DoNotStartInference()
    {
        using var handler = new StubHandler();
        handler.Json("""{"version":"1.0"}""");
        handler.Json("""{"models":[]}""");
        handler.Json("""{"models":[]}""");
        handler.Json("""{"version":"1.0"}""");
        using var http = new HttpClient(handler);
        var server = new OllamaServer("http://localhost", http);
        var capabilities = await server.GetCapabilitiesAsync();
        Assert.AreEqual(ServingFeatureSupport.Supported, capabilities.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Supported, capabilities.ModelDownloading);
        Assert.AreEqual(ServingFeatureSupport.Unsupported, capabilities.Metrics);
        Assert.HasCount(3, handler.Requests);
        Assert.AreEqual(ServerHealthStatus.Healthy, (await server.GetHealthAsync()).Status);
        Assert.IsTrue(handler.Requests.All(x => x.Method == "GET"));
        CollectionAssert.AreEqual(new[] { "/api/version", "/api/tags", "/api/ps", "/api/version" }, handler.Requests.Select(x => x.Path).ToArray());
    }

    [TestMethod]
    [DataRow(401, "{\"error\":\"secret\"}")]
    [DataRow(404, "{}")]
    [DataRow(200, "{\"version\":42}")]
    [DataRow(200, "malformed")]
    public async Task Capabilities_UnknownUntilOllamaIdentityIsObserved(int code, string version)
    {
        using var handler = new StubHandler();
        handler.Json(version, code);
        using var http = new HttpClient(handler);
        var capabilities = await new OllamaServer("http://localhost", http).GetCapabilitiesAsync();
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelUnloading);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelDownloading);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.Metrics);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow(404, "{}", ServingFeatureSupport.Unsupported)]
    [DataRow(403, "{}", ServingFeatureSupport.Unknown)]
    [DataRow(200, "{}", ServingFeatureSupport.Unknown)]
    public async Task Capabilities_MissingInventoryDiffersFromUnverifiedInventory(int code, string inventory, ServingFeatureSupport expected)
    {
        using var handler = new StubHandler();
        handler.Json("""{"version":"1.0"}""");
        handler.Json("""{"models":[]}""");
        handler.Json(inventory, code);
        using var http = new HttpClient(handler);
        var capabilities = await new OllamaServer("http://localhost", http).GetCapabilitiesAsync();
        Assert.AreEqual(expected, capabilities.ModelListing);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelLoading);
        Assert.AreEqual(ServingFeatureSupport.Unknown, capabilities.ModelDownloading);
        Assert.IsTrue(handler.Requests.All(x => x.Method == "GET"));
    }

    [TestMethod]
    public async Task UnreachableServer_HealthAndCapabilitiesPreserveUncertainty()
    {
        using var handler = new StubHandler();
        handler.Enqueue((_, _) => throw new HttpRequestException("private-network-info"));
        handler.Enqueue((_, _) => throw new HttpRequestException("private-network-info"));
        using var http = new HttpClient(handler);
        var server = new OllamaServer("http://localhost", http);
        var health = await server.GetHealthAsync();
        Assert.AreEqual(ServerHealthStatus.Unreachable, health.Status);
        Assert.IsFalse(health.Detail?.Contains("private-network-info") ?? false);
        Assert.AreEqual(ServingFeatureSupport.Unknown, (await server.GetCapabilitiesAsync()).ModelListing);
    }

    [TestMethod]
    public async Task EmbeddingOnlyPreloadRejection_IsNotRetriedAsInferenceOrAnotherEndpoint()
    {
        using var handler = new StubHandler();
        handler.Json("""{"error":"model does not support generate"}""", 400);
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<ServingException>(() => new OllamaServer("http://localhost", http).LoadModelAsync("embedding-model"));
        Assert.AreEqual(400, ex.StatusCode);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual("", JObject.Parse(handler.Requests.Single().Body!).Value<string>("prompt"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Timeout_CoversBodyReadsAfterHeaders(bool download)
    {
        using var body = new BlockingStream();
        using var handler = new StubHandler();
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var server = new OllamaServer("http://localhost", http);
        Task pending = download ? server.DownloadModelAsync("model") : server.GetVersionAsync();
        var ex = await Assert.ThrowsAsync<ServingException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(ServingFailureKind.Timeout, ex.FailureKind);
        Assert.IsTrue(body.CancellationObserved);
    }

    [TestMethod]
    [DataRow(401, ServerHealthStatus.Unauthorized)]
    [DataRow(403, ServerHealthStatus.Unauthorized)]
    [DataRow(503, ServerHealthStatus.NotReady)]
    [DataRow(500, ServerHealthStatus.Unexpected)]
    [DataRow(200, ServerHealthStatus.Unexpected)]
    public async Task Health_ClassifiesErrorsWithoutLeakingServerText(int code, ServerHealthStatus expected)
    {
        using var handler = new StubHandler();
        handler.Json("{\"error\":\"server-secret\"}", code);
        using var http = new HttpClient(handler);
        var health = await new OllamaServer("http://localhost", http).GetHealthAsync();
        Assert.AreEqual(expected, health.Status);
        Assert.IsFalse(health.Detail?.Contains("server-secret") ?? false);
    }

    [TestMethod]
    public async Task Lifecycle_ExplicitRequestsDoNotGenerateTextOrDeleteModels()
    {
        using var handler = new StubHandler();
        handler.Json("""{"model":"model:latest","done":true,"done_reason":"load","response":""}""");
        handler.Json("""{"model":"model:latest","done":true,"done_reason":"unload"}""");
        using var http = new HttpClient(handler);
        IModelLifecycle lifecycle = new OllamaServer("http://localhost", http);
        await lifecycle.LoadModelAsync("model:latest");
        await lifecycle.UnloadModelAsync("model:latest");
        Assert.HasCount(2, handler.Requests);
        foreach (var request in handler.Requests)
        {
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("/api/generate", request.Path);
            var body = JObject.Parse(request.Body!);
            Assert.AreEqual("", body.Value<string>("prompt"));
            Assert.IsFalse(body.Value<bool>("stream"));
            Assert.AreEqual("model:latest", body.Value<string>("model"));
        }
        Assert.IsNull(JObject.Parse(handler.Requests[0].Body!)["keep_alive"]);
        Assert.AreEqual(0, JObject.Parse(handler.Requests[1].Body!).Value<int>("keep_alive"));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"model\":\"m\",\"done\":false}")]
    [DataRow("{\"done\":true}")]
    [DataRow("{\"model\":\"m\",\"done\":true,\"response\":\"unexpected inference\"}")]
    [DataRow("{\"model\":\"m\",\"done\":true,\"done_reason\":\"unload\"}")]
    [DataRow("{\"model\":\"m\",\"done\":true,\"error\":\"secret\"}")]
    public async Task Lifecycle_RejectsUnacknowledgedOrErrorResponse(string response)
    {
        using var handler = new StubHandler();
        handler.Json(response);
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<ServingException>(() => new OllamaServer("http://localhost", http).LoadModelAsync("m"));
        Assert.IsFalse(ex.ToString().Contains("secret"));
        Assert.IsFalse(ex.ToString().Contains("unexpected inference"));
    }

    [TestMethod]
    public async Task Download_ReportsEachArtifactAndRequiresTerminalSuccess()
    {
        using var handler = new StubHandler();
        handler.Json("{\"status\":\"pulling manifest\"}\n{\"status\":\"pulling a\",\"digest\":\"sha256:a\",\"total\":100,\"completed\":20,\"future\":true}\r\n{\"status\":\"pulling b\",\"digest\":\"sha256:b\",\"total\":50}\n{\"status\":\"success\"}\n");
        using var http = new HttpClient(handler);
        var progress = new CapturingProgress();
        await new OllamaServer("http://localhost", http).DownloadModelAsync("model", progress);
        Assert.HasCount(4, progress.Items);
        Assert.AreEqual("success", progress.Items.Last().Stage);
        Assert.AreEqual(20L, progress.Items[1].CompletedBytes);
        Assert.AreEqual("sha256:a", progress.Items[1].Artifact);
        Assert.IsNull(progress.Items[2].CompletedBytes);
        Assert.AreEqual("/api/pull", handler.Requests.Single().Path);
        Assert.IsTrue(JObject.Parse(handler.Requests.Single().Body!).Value<bool>("stream"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("{\"status\":\"pulling manifest\"}\n")]
    [DataRow("{\"status\":\"success\"}\n{\"status\":\"more\"}")]
    [DataRow("{\"error\":\"private-server-detail\"}")]
    [DataRow("{\"status\":\"pulling\"}\n{\"error\":\"private-server-detail\"}")]
    [DataRow("{\"status\":\"success\"}\n{\"error\":\"private-server-detail\"}")]
    [DataRow("{\"private-server-detail\":")]
    [DataRow("{\"status\":\"pulling\",\"status\":\"success\"}")]
    [DataRow("{\"status\":\"pulling\",\"completed\":-1}")]
    [DataRow("{\"status\":\"pulling\",\"completed\":101,\"total\":100}")]
    [DataRow("{\"status\":\"pulling\",\"total\":1.5}")]
    [DataRow("{\"status\":false}")]
    public async Task Download_RejectsInterruptedMalformedOrErrorStreams(string response)
    {
        using var handler = new StubHandler();
        handler.Json(response);
        using var http = new HttpClient(handler);
        var progress = new CapturingProgress();
        var ex = await Assert.ThrowsAsync<ServingException>(() => new OllamaServer("http://localhost", http).DownloadModelAsync("model", progress));
        Assert.IsFalse(ex.ToString().Contains("private-server-detail"));
        Assert.IsFalse(progress.Items.Any(x => x.Stage == "success"));
    }

    [TestMethod]
    public async Task FailedHttpResponse_DoesNotExposeBodyOrKey()
    {
        using var handler = new StubHandler();
        handler.Json("{\"error\":\"private-server-detail key-secret\"}", 500);
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<ServingException>(() => new OllamaServer("http://localhost", http, "key-secret").DownloadModelAsync("model"));
        Assert.IsFalse(ex.ToString().Contains("private-server-detail"));
        Assert.IsFalse(ex.ToString().Contains("key-secret"));
        Assert.AreEqual(500, ex.StatusCode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_ReachesOngoingHttpRequest_AndProbesDoNotHideIt(bool capabilities)
    {
        using var handler = new StubHandler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Enqueue(async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var server = new OllamaServer("http://localhost", http);
        Task pending = capabilities ? server.GetCapabilitiesAsync(cancellation.Token) : server.GetHealthAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_ReachesResponseBodyReads(bool download)
    {
        using var body = new BlockingStream();
        using var handler = new StubHandler();
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var server = new OllamaServer("http://localhost", http);
        Task pending = download ? server.DownloadModelAsync("model", cancellationToken: cancellation.Token) : server.GetVersionAsync(cancellation.Token);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(body.CancellationObserved);
    }

    [TestMethod]
    public async Task InvalidModelIdsAndPreCanceledCalls_DoNotSendRequests()
    {
        using var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var server = new OllamaServer("http://localhost", http);
        await Assert.ThrowsAsync<ArgumentException>(() => server.LoadModelAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => server.UnloadModelAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => server.DownloadModelAsync(null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => server.GetModelsAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => server.GetCapabilitiesAsync(cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => server.DownloadModelAsync("model", cancellationToken: cancellation.Token));
        Assert.HasCount(0, handler.Requests);
    }

    private sealed class CapturingProgress : IProgress<ModelDownloadProgress>
    {
        public List<ModelDownloadProgress> Items { get; } = new();
        public void Report(ModelDownloadProgress value) => Items.Add(value);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();
        public List<(string Host, string Path, string Method, string? Authorization, string? Body)> Requests { get; } = new();
        public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) => _responses.Enqueue(response);
        public void Json(string json, int status = 200) => Enqueue((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.Host, request.RequestUri.AbsolutePath, request.Method.Method, request.Headers.Authorization?.ToString(), body));
            return await _responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed class BlockingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { CancellationObserved = cancellationToken.IsCancellationRequested; throw; }
            return 0;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
