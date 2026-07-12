using System.Net;
using System.Text;

namespace Mythosia.AI.Serving.Vllm.Tests;

/// <summary>
/// Queue-based HttpMessageHandler stub. Captures the URL and Authorization header of every
/// request at send time (the client disposes requests after use, so nothing else is retained).
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<(string Url, string? Authorization)> Calls { get; } = new();

    public void Enqueue(HttpStatusCode statusCode, string content, string mediaType = "application/json")
        => _responders.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType),
        });

    public void EnqueueEmpty(HttpStatusCode statusCode)
        => _responders.Enqueue(_ => new HttpResponseMessage(statusCode));

    public void EnqueueThrow(Exception exception)
        => _responders.Enqueue(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));

        if (_responders.Count == 0)
            throw new InvalidOperationException("No mock response enqueued.");

        return Task.FromResult(_responders.Dequeue()(request));
    }
}
