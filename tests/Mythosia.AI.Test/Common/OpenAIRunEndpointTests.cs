using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("OpenAI")]
[TestCategory("Run")]
public class OpenAIRunEndpointTests
{
    [TestMethod]
    [DataRow("https://api.openai.com/v1/", "wss://api.openai.com/v1/responses")]
    [DataRow("https://eu.api.openai.com/v1/", "wss://eu.api.openai.com/v1/responses")]
    [DataRow("https://proxy.example.test:9443/openai/v1/", "wss://proxy.example.test:9443/openai/v1/responses")]
    [DataRow("https://proxy.example.test/openai/v1", "wss://proxy.example.test/openai/responses")]
    [DataRow("https://proxy.example.test/v1/?route=regional#fragment", "wss://proxy.example.test/v1/responses")]
    [DataRow("https://proxy.example.test:443/v1/", "wss://proxy.example.test/v1/responses")]
    [DataRow("http://127.0.0.1:8181/proxy/v1/", "ws://127.0.0.1:8181/proxy/v1/responses")]
    [DataRow("http://proxy.example.test:80/v1/", "ws://proxy.example.test/v1/responses")]
    [DataRow("http://[::1]:8181/proxy/v1/", "ws://[::1]:8181/proxy/v1/responses")]
    public void ResolveEndpoint_PreservesHttpAuthorityAndRelativePathSemantics(string endpoint, string expected)
    {
        var resolved = OpenAIService.ResolveRunWebSocketUri(new Uri(endpoint));

        Assert.AreEqual(expected, resolved.AbsoluteUri);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("v1/")]
    [DataRow("ftp://proxy.example.test/v1/")]
    [DataRow("wss://proxy.example.test/v1/")]
    [DataRow("file:///private/endpoint")]
    public void ResolveEndpoint_RejectsMissingRelativeAndUnsupportedSchemes(string? endpoint)
    {
        var baseAddress = endpoint == null ? null : new Uri(endpoint, UriKind.RelativeOrAbsolute);

        Assert.Throws<InvalidOperationException>(() => OpenAIService.ResolveRunWebSocketUri(baseAddress));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("ftp://proxy.example.test/v1/")]
    public async Task InvalidBaseAddress_FailsBeforeNetworkAndAllowsAValidRetry(string? endpoint)
    {
        using var handler = new NoHttpHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-endpoint-key", AIModels.OpenAI.Gpt6_1Sol, client);
        client.BaseAddress = endpoint == null ? null : new Uri(endpoint);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartRunAsync("must not connect"));

        Assert.AreEqual(0, handler.Calls);
        Assert.IsEmpty(service.ActivateChat.Messages);

        // A rejected startup must release the service's active-Run guard so callers can
        // correct configuration and retry without constructing a different service.
        await using var server = new LoopbackResponsesServer();
        client.BaseAddress = new Uri(server.Origin, "v1/");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var retry = await service.StartRunAsync("retry locally", cancellationToken: cancellation.Token);
        Assert.AreEqual("local answer", (await retry.Result.WaitAsync(cancellation.Token)).Text);
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("/proxy/openai/v1/", "/proxy/openai/v1/responses")]
    [DataRow("/proxy/openai/v1", "/proxy/openai/responses")]
    public async Task DefaultConnector_UsesConfiguredLoopbackPortPathAndAuthorization(string basePath, string expectedPath)
    {
        await using var server = new LoopbackResponsesServer();
        using var handler = new NoHttpHandler();
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-endpoint-key", AIModels.OpenAI.Gpt6_1Sol, client);
        client.BaseAddress = new Uri(server.Origin, basePath);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var run = await service.StartRunAsync("local endpoint", cancellationToken: cancellation.Token);
        var result = await run.Result.WaitAsync(cancellation.Token);
        await server.ResponseSent.WaitAsync(cancellation.Token);

        Assert.AreEqual("local answer", result.Text);
        Assert.AreEqual($"GET {expectedPath} HTTP/1.1", server.RequestLine);
        Assert.AreEqual(server.Origin.Authority, server.Headers["Host"]);
        Assert.AreEqual("Bearer offline-endpoint-key", server.Headers["Authorization"]);
        Assert.AreEqual("response.create", server.RequestBody.GetProperty("type").GetString());
        Assert.AreEqual(AIModels.OpenAI.Gpt6_1Sol, server.RequestBody.GetProperty("model").GetString());
        Assert.AreEqual(0, handler.Calls, "The default Run connector uses its configured WebSocket, not the HTTP message handler.");
    }

    [TestMethod]
    public async Task ProtectedConnectorOverride_RemainsInControlOfCustomTransport()
    {
        using var handler = new NoHttpHandler();
        using var client = new HttpClient(handler);
        var service = new OverrideProbe(client);
        client.BaseAddress = null;

        await Assert.ThrowsExactlyAsync<CustomTransportReachedException>(() => service.StartRunAsync("custom endpoint"));

        Assert.AreEqual(1, service.Connections);
        Assert.AreEqual(0, handler.Calls);
    }

    private sealed class OverrideProbe(HttpClient client)
        : OpenAIService("offline-endpoint-key", AIModels.OpenAI.Gpt6_1Sol, client)
    {
        public int Connections { get; private set; }
        protected override Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
        {
            Connections++;
            throw new CustomTransportReachedException();
        }
    }

    private sealed class CustomTransportReachedException : Exception { }

    private sealed class NoHttpHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new AssertFailedException("No HTTP request is expected during native Run endpoint validation.");
        }
    }

    // A raw loopback listener avoids platform-specific HttpListener URL reservations.
    // Only an ephemeral local port is opened; no provider connection or real key is used.
    private sealed class LoopbackResponsesServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _responseSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serving;

        public Uri Origin { get; }
        public string? RequestLine { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public JsonElement RequestBody { get; private set; }
        public Task ResponseSent => _responseSent.Task;

        public LoopbackResponsesServer()
        {
            _listener.Start();
            Origin = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
            _serving = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                await using var stream = client.GetStream();
                var request = new StringBuilder();
                var buffer = new byte[16_384];
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, _stopping.Token);
                    if (read == 0) throw new IOException("WebSocket request ended before the handshake.");
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    if (request.Length > 16_384) throw new IOException("Unexpectedly large WebSocket handshake.");
                }
                var lines = request.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                RequestLine = lines[0];
                foreach (var line in lines.Skip(1))
                {
                    var separator = line.IndexOf(':');
                    if (separator > 0) Headers.Add(line[..separator], line[(separator + 1)..].Trim());
                }
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
                    Headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var handshake = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\n" +
                    "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                await stream.WriteAsync(handshake, _stopping.Token);
                using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
                using var message = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stopping.Token);
                    if (received.MessageType != WebSocketMessageType.Text) throw new IOException("Expected a JSON response.create message.");
                    message.Write(buffer, 0, received.Count);
                    if (message.Length > 65_536) throw new IOException("Unexpectedly large response.create message.");
                } while (!received.EndOfMessage);
                RequestBody = JsonSerializer.Deserialize<JsonElement>(message.ToArray());
                foreach (var frame in new[]
                {
                    """{"type":"response.created","response":{"id":"resp_local","status":"in_progress"}}""",
                    """{"type":"response.output_text.delta","delta":"local answer"}""",
                    """{"type":"response.completed","response":{"id":"resp_local","status":"completed","output":[]}}"""
                })
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(frame)), WebSocketMessageType.Text, true, _stopping.Token);
                _responseSent.TrySetResult();
                await _release.Task.WaitAsync(_stopping.Token);
            }
            catch (Exception exception) when (_stopping.IsCancellationRequested &&
                exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException or WebSocketException)
            { }
            catch (Exception exception)
            {
                _responseSent.TrySetException(exception);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _release.TrySetResult();
            await _stopping.CancelAsync();
            _listener.Stop();
            try { await _serving.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { _stopping.Dispose(); }
        }
    }
}
