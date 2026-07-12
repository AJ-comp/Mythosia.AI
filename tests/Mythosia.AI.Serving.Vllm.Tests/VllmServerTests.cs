using System.Net;

namespace Mythosia.AI.Serving.Vllm.Tests;

[TestClass]
public class VllmServerTests
{
    private const string ModelsJson = """
    {
      "object": "list",
      "data": [
        {
          "id": "cyankiwi/gemma-4-26B-A4B-it-AWQ-4bit",
          "object": "model",
          "created": 1752300000,
          "owned_by": "vllm",
          "root": "Lorbus/Qwen3.6-27B-int4-AutoRound",
          "parent": null,
          "max_model_len": 50000,
          "permission": []
        },
        {
          "id": "my-lora",
          "object": "model",
          "created": 1752300000,
          "owned_by": "vllm",
          "root": "/adapters/my-lora",
          "parent": "cyankiwi/gemma-4-26B-A4B-it-AWQ-4bit",
          "max_model_len": null,
          "permission": []
        }
      ]
    }
    """;

    private static VllmServer CreateServer(
        MockHttpMessageHandler handler,
        string endpoint = "http://localhost:8000",
        string? apiKey = null)
        => new(endpoint, new HttpClient(handler), apiKey);

    #region Endpoint normalization

    [TestMethod]
    [DataRow("http://localhost:8000")]
    [DataRow("http://localhost:8000/")]
    [DataRow("http://localhost:8000/v1")]
    [DataRow("http://localhost:8000/v1/")]
    [DataRow("  http://localhost:8000/v1  ")]
    public void Constructor_NormalizesEndpointToServerRoot(string endpoint)
    {
        var server = CreateServer(new MockHttpMessageHandler(), endpoint);

        Assert.AreEqual("http://localhost:8000/", server.Endpoint.ToString());
    }

    [TestMethod]
    public void Constructor_PreservesSubPathWhenStrippingV1()
    {
        var server = CreateServer(new MockHttpMessageHandler(), "https://gateway.example.com/llm/v1");

        Assert.AreEqual("https://gateway.example.com/llm/", server.Endpoint.ToString());
    }

    [TestMethod]
    public void Constructor_RejectsInvalidEndpoint()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CreateServer(new MockHttpMessageHandler(), "   "));
        Assert.ThrowsExactly<ArgumentException>(() => CreateServer(new MockHttpMessageHandler(), "not-a-url"));
    }

    [TestMethod]
    public async Task Routes_ModelsUnderV1_ManagementAtRoot()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ModelsJson);
        handler.Enqueue(HttpStatusCode.OK, """{"version":"0.25.0"}""");
        handler.EnqueueEmpty(HttpStatusCode.OK);
        handler.Enqueue(HttpStatusCode.OK, "", "text/plain");

        // Constructed from the /v1-suffixed chat endpoint — the common consumer situation.
        var server = CreateServer(handler, "http://localhost:8000/v1");
        await server.GetModelsAsync();
        await server.GetVersionAsync();
        await server.GetHealthAsync();
        await server.GetMetricsAsync();

        Assert.AreEqual("http://localhost:8000/v1/models", handler.Calls[0].Url);
        Assert.AreEqual("http://localhost:8000/version", handler.Calls[1].Url);
        Assert.AreEqual("http://localhost:8000/health", handler.Calls[2].Url);
        Assert.AreEqual("http://localhost:8000/metrics", handler.Calls[3].Url);
    }

    #endregion

    #region Authentication

    [TestMethod]
    public async Task ApiKey_IsSentAsBearerOnEveryRequest()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ModelsJson);
        var server = CreateServer(handler, apiKey: "secret-key");

        await server.GetModelsAsync();

        Assert.AreEqual("Bearer secret-key", handler.Calls[0].Authorization);
    }

    [TestMethod]
    public async Task NoApiKey_SendsNoAuthorizationHeader()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ModelsJson);
        var server = CreateServer(handler);

        await server.GetModelsAsync();

        Assert.IsNull(handler.Calls[0].Authorization);
    }

    #endregion

    #region GetModelsAsync / GetModelAsync

    [TestMethod]
    public async Task GetModelsAsync_ParsesModelCards()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ModelsJson);
        var server = CreateServer(handler);

        var models = await server.GetModelsAsync();

        Assert.AreEqual(2, models.Count);

        var baseModel = models[0];
        Assert.AreEqual("cyankiwi/gemma-4-26B-A4B-it-AWQ-4bit", baseModel.Id);
        Assert.AreEqual("Lorbus/Qwen3.6-27B-int4-AutoRound", baseModel.Root);
        Assert.IsNull(baseModel.Parent);
        Assert.AreEqual(50000, baseModel.MaxModelLen);
        Assert.AreEqual("vllm", baseModel.OwnedBy);
        Assert.IsFalse(baseModel.IsLoraAdapter);
        Assert.AreEqual("Lorbus/Qwen3.6-27B-int4-AutoRound", baseModel.DisplayModel);

        var lora = models[1];
        Assert.IsTrue(lora.IsLoraAdapter);
        Assert.AreEqual("cyankiwi/gemma-4-26B-A4B-it-AWQ-4bit", lora.Parent);
        Assert.IsNull(lora.MaxModelLen);
    }

    [TestMethod]
    public async Task DisplayModel_FallsBackToId_WhenRootIsAbsent()
    {
        // A server that stops exposing the undocumented root field.
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"object":"list","data":[{"id":"alias-only","object":"model","created":1,"owned_by":"vllm"}]}""");
        var server = CreateServer(handler);

        var models = await server.GetModelsAsync();

        Assert.IsNull(models[0].Root);
        Assert.AreEqual("alias-only", models[0].DisplayModel);
    }

    [TestMethod]
    public async Task GetModelsAsync_EmptyData_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"object":"list","data":[]}""");
        var server = CreateServer(handler);

        var models = await server.GetModelsAsync();

        Assert.AreEqual(0, models.Count);
    }

    [TestMethod]
    public async Task GetModelAsync_FiltersByExactId()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ModelsJson);
        handler.Enqueue(HttpStatusCode.OK, ModelsJson);
        var server = CreateServer(handler);

        var found = await server.GetModelAsync("cyankiwi/gemma-4-26B-A4B-it-AWQ-4bit");
        var missing = await server.GetModelAsync("no-such-alias");

        Assert.IsNotNull(found);
        Assert.AreEqual("Lorbus/Qwen3.6-27B-int4-AutoRound", found.DisplayModel);
        Assert.IsNull(missing);
    }

    #endregion

    #region GetVersionAsync

    [TestMethod]
    public async Task GetVersionAsync_ReturnsVersion()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"version":"0.25.0"}""");
        var server = CreateServer(handler);

        Assert.AreEqual("0.25.0", await server.GetVersionAsync());
    }

    [TestMethod]
    public async Task GetVersionAsync_ReturnsNull_WhenFieldMissingOrBodyNotJson()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"something":"else"}""");
        handler.Enqueue(HttpStatusCode.OK, "not json at all", "text/plain");
        var server = CreateServer(handler);

        Assert.IsNull(await server.GetVersionAsync());
        Assert.IsNull(await server.GetVersionAsync());
    }

    #endregion

    #region Error handling

    [TestMethod]
    public async Task NonSuccess_ThrowsVllmException_WithParsedErrorBody()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound,
            """{"error":{"message":"The model 'x' does not exist.","type":"NotFoundError","param":null,"code":404}}""");
        var server = CreateServer(handler);

        var ex = await Assert.ThrowsExactlyAsync<VllmException>(() => server.GetModelsAsync());

        Assert.AreEqual(404, ex.StatusCode);
        Assert.AreEqual("NotFoundError", ex.ErrorType);
        Assert.AreEqual("404", ex.ErrorCode);
        StringAssert.Contains(ex.Message, "The model 'x' does not exist.");
    }

    [TestMethod]
    public async Task NonSuccess_NonJsonBody_StillThrowsWithStatusLine()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "<html>proxy error</html>", "text/html");
        var server = CreateServer(handler);

        var ex = await Assert.ThrowsExactlyAsync<VllmException>(() => server.GetVersionAsync());

        Assert.AreEqual(500, ex.StatusCode);
        Assert.IsNull(ex.ErrorType);
        StringAssert.Contains(ex.Message, "500");
        StringAssert.Contains(ex.ResponseBody!, "proxy error");
    }

    #endregion

    #region Health

    [TestMethod]
    [DataRow(HttpStatusCode.OK, VllmHealthStatus.Healthy)]
    [DataRow(HttpStatusCode.ServiceUnavailable, VllmHealthStatus.EngineDead)]
    [DataRow(HttpStatusCode.Unauthorized, VllmHealthStatus.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden, VllmHealthStatus.Unauthorized)]
    [DataRow(HttpStatusCode.NotFound, VllmHealthStatus.Unexpected)]
    public async Task GetHealthAsync_ClassifiesStatusCodes(HttpStatusCode statusCode, VllmHealthStatus expected)
    {
        var handler = new MockHttpMessageHandler();
        handler.EnqueueEmpty(statusCode);
        var server = CreateServer(handler);

        var report = await server.GetHealthAsync();

        Assert.AreEqual(expected, report.Status);
        Assert.AreEqual((int)statusCode, report.StatusCode);
    }

    [TestMethod]
    public async Task GetHealthAsync_ConnectionFailure_ReportsUnreachable()
    {
        var handler = new MockHttpMessageHandler();
        handler.EnqueueThrow(new HttpRequestException("connection refused"));
        var server = CreateServer(handler);

        var report = await server.GetHealthAsync();

        Assert.AreEqual(VllmHealthStatus.Unreachable, report.Status);
        Assert.IsNull(report.StatusCode);
        StringAssert.Contains(report.Detail!, "connection refused");
    }

    [TestMethod]
    public async Task GetHealthAsync_ClientTimeout_ReportsUnreachable()
    {
        // TaskCanceledException WITHOUT caller cancellation = HttpClient.Timeout semantics.
        var handler = new MockHttpMessageHandler();
        handler.EnqueueThrow(new TaskCanceledException("request timed out"));
        var server = CreateServer(handler);

        var report = await server.GetHealthAsync();

        Assert.AreEqual(VllmHealthStatus.Unreachable, report.Status);
    }

    [TestMethod]
    public async Task GetHealthAsync_CallerCancellation_Propagates()
    {
        var handler = new MockHttpMessageHandler();
        handler.EnqueueThrow(new HttpRequestException("should not be reported"));
        var server = CreateServer(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await server.GetHealthAsync(cts.Token);
            Assert.Fail("Expected OperationCanceledException.");
        }
        catch (OperationCanceledException)
        {
            // expected — caller cancellation must never be classified as a health verdict
        }
    }

    [TestMethod]
    public async Task IsHealthyAsync_TrueOnlyOnSuccess()
    {
        var handler = new MockHttpMessageHandler();
        handler.EnqueueEmpty(HttpStatusCode.OK);
        handler.EnqueueEmpty(HttpStatusCode.ServiceUnavailable);
        var server = CreateServer(handler);

        Assert.IsTrue(await server.IsHealthyAsync());
        Assert.IsFalse(await server.IsHealthyAsync());
    }

    #endregion

    #region Metrics

    [TestMethod]
    public async Task GetMetricsAsync_ParsesFamiliesAndKeepsRawText()
    {
        const string exposition = """
        # HELP vllm:num_requests_running Number of requests currently running.
        # TYPE vllm:num_requests_running gauge
        vllm:num_requests_running{model_name="alias-a",engine="0"} 3.0
        vllm:kv_cache_usage_perc{model_name="alias-a",engine="0"} 0.42
        """;

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, exposition, "text/plain");
        var server = CreateServer(handler);

        var metrics = await server.GetMetricsAsync();

        Assert.AreEqual(3.0, metrics.RunningRequests);
        Assert.AreEqual(0.42, metrics.KvCacheUsage);
        Assert.IsNull(metrics.WaitingRequests);
        Assert.AreEqual("alias-a", metrics.Families["vllm:num_requests_running"][0].Labels["model_name"]);
        Assert.AreEqual(exposition, metrics.RawText);
    }

    #endregion
}
