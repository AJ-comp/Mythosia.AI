using System.Net;
using System.Net.Http.Headers;

namespace Mythosia.AI.Serving.LlamaCpp.Tests;

[TestClass]
public sealed class LlamaCppDownloadTests
{
    private const string Router = "{\"role\":\"router\"}";
    private const string Finished = "data: {\"model\":\"target\",\"event\":\"download_finished\"}\n\n";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DownloadSubscribesFirstAndMatchesModelCompletion_WithBothOfficialProgressFormats(bool wrapped)
    {
        const string files = "{\"https://cdn.invalid/a.gguf\":{\"done\":5,\"total\":10,\"extra\":true},\"https://cdn.invalid/b.gguf\":{\"done\":3,\"total\":0}}";
        var progressJson = wrapped ? "{\"progress\":" + files + ",\"future\":true}" : files;
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Text(": heartbeat\n\n" +
            "data: {\"model\":\"other\",\"event\":\"download_finished\"}\n\n" +
            "data: {\"model\":\"other\",\"event\":\"download_failed\"}\n\n" +
            "event: message\nid: 123\nretry: 3000\n" +
            "data: {\"model\":\"target\",\"event\":\"download_progress\",\n" +
            "data: \"data\":" + progressJson + "}\n\n" + Finished);
        handler.Json("{\"success\":true}");
        using var client = new HttpClient(handler);
        var progress = new List<ModelDownloadProgress>();
        await new LlamaCppServer("http://host.invalid", client).DownloadModelAsync("target", new InlineProgress<ModelDownloadProgress>(progress.Add));
        CollectionAssert.AreEqual(new[] { "/props", "/models/sse", "/models" }, handler.Calls.Select(c => c.Path).ToArray());
        CollectionAssert.AreEqual(new[] { "GET", "GET", "POST" }, handler.Calls.Select(c => c.Method).ToArray());
        CollectionAssert.AreEqual(new[] { "accepted", "downloading", "downloading", "completed" }, progress.Select(p => p.Stage).ToArray());
        Assert.AreEqual(5L, progress[1].CompletedBytes);
        Assert.AreEqual(10L, progress[1].TotalBytes);
        Assert.IsNull(progress[2].TotalBytes, "A zero native total means an unknown content length.");
        Assert.IsTrue(progress.All(p => p.ModelId == "target"));
    }

    [TestMethod]
    [DataRow("data: {\"model\":\"other\",\"event\":\"download_finished\"}\n\n")]
    [DataRow("data: {\"model\":\"target\",\"event\":\"model_status\",\"data\":{\"status\":\"loaded\"}}\n\n")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_finished\"}\n")]
    [DataRow("data: {\"model\":\"target\",\"event\":\"download_finished\"}")]
    [DataRow("")]
    public async Task AcceptanceOtherModelsLoadedStateNdjsonAndPartialFramesAreNotDownloadCompletion(string sse)
    {
        using var handler = MakeDownload(sse);
        using var client = new HttpClient(handler);
        var progress = new List<ModelDownloadProgress>();
        await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client)
            .DownloadModelAsync("target", new InlineProgress<ModelDownloadProgress>(progress.Add)));
        Assert.IsFalse(progress.Any(p => p.Stage == "completed"));
    }

    [TestMethod]
    [DataRow("{\"model\":\"target\",\"event\":\"download_failed\",\"data\":{\"message\":\"private-error\"}}")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_progress\",\"data\":null}")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_progress\",\"data\":{\"x\":{\"done\":-1,\"total\":4}}}")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_progress\",\"data\":{\"x\":{\"done\":5,\"total\":4}}}")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_progress\",\"data\":{\"x\":{\"done\":1.5,\"total\":4}}}")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_progress\",\"data\":{\"x\":{\"done\":1}}}")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_progress\",\"data\":{\"x\":{\"done\":1,\"total\":9223372036854775808}}}")]
    [DataRow("{\"model\":\"target\",\"event\":\"download_progress\",\"data\":{}}")]
    [DataRow("{\"model\":\"target\",\"model\":\"other\",\"event\":\"download_finished\"}")]
    [DataRow("{\"model\":1,\"event\":\"download_finished\"}")]
    [DataRow("{\"model\":\"target\",\"event\":4}")]
    [DataRow("private-error")]
    public async Task InvalidEventsFailWithoutLeakingPayload(string payload)
    {
        using var handler = MakeDownload("data: " + payload + "\n\n" + Finished);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client, "private-key").DownloadModelAsync("target"));
        Assert.IsFalse(error.ToString().Contains("private-error", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("private-key", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("host.invalid", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task NonSseResponseCannotStartDownload()
    {
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Text("<html>private-proxy-body</html>", "text/html");
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client).DownloadModelAsync("target"));
        Assert.AreEqual(2, handler.Calls.Count);
        Assert.IsTrue(handler.Calls.All(c => c.Method == "GET"));
        Assert.IsFalse(error.ToString().Contains("private-proxy-body", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CancelDuringObservationDisposesStream_WithoutRemoteUnload()
    {
        using var stream = new BlockingStream();
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Reply(StreamResponse(stream));
        handler.Json("{\"success\":true}");
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var pending = new LlamaCppServer("http://host.invalid", client).DownloadModelAsync("target", cancellationToken: cancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsTrue(stream.WasDisposed);
        Assert.IsFalse(handler.Calls.Any(c => c.Path == "/models/unload"));
    }

    [TestMethod]
    public async Task CancelDuringDownloadPostAlsoDisposesAlreadyOpenedSse()
    {
        using var stream = new BlockingStream();
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Reply(StreamResponse(stream));
        var postStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Reply(async token =>
        {
            postStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var pending = new LlamaCppServer("http://host.invalid", client).DownloadModelAsync("target", cancellationToken: cancellation.Token);
        await postStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsTrue(stream.WasDisposed);
    }

    [TestMethod]
    public async Task StreamBodyTimeoutIsClassifiedAndDisposesTheConnection()
    {
        using var stream = new BlockingStream();
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Reply(StreamResponse(stream));
        handler.Json("{\"success\":true}");
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(150) };
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client)
            .DownloadModelAsync("target").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(ServingFailureKind.Timeout, error.FailureKind);
        Assert.IsTrue(stream.WasDisposed);
    }

    [TestMethod]
    public async Task PostFailureDoesNotLeakBodyAndDisposesTheSseSubscription()
    {
        using var stream = new BlockingStream();
        using var handler = new TestHttp();
        handler.Json(Router);
        handler.Reply(StreamResponse(stream));
        handler.Json("{\"error\":\"private-url private-key\"}", HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client).DownloadModelAsync("target"));
        Assert.AreEqual(400, error.StatusCode);
        Assert.IsFalse(error.ToString().Contains("private-", StringComparison.Ordinal));
        Assert.IsTrue(stream.WasDisposed);
    }

    [TestMethod]
    public async Task OversizedSseFrameFailsBeforeReportedCompletion()
    {
        using var handler = MakeDownload("data: " + new string('x', 1024 * 1024 + 1) + "\n\n" + Finished);
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<ServingException>(() => new LlamaCppServer("http://host.invalid", client).DownloadModelAsync("target"));
        Assert.AreEqual(ServingFailureKind.InvalidResponse, error.FailureKind);
    }

    private static TestHttp MakeDownload(string sse)
    {
        var handler = new TestHttp();
        handler.Json(Router);
        handler.Text(sse);
        handler.Json("{\"success\":true}");
        return handler;
    }

    private static HttpResponseMessage StreamResponse(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return response;
    }
}
