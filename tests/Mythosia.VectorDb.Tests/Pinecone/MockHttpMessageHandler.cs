using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.VectorDb.Tests.Pinecone;

/// <summary>
/// A test double for <see cref="HttpMessageHandler"/> that captures requests
/// and returns pre-configured responses.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly List<CapturedRequest> _capturedRequests = new();

    public IReadOnlyList<CapturedRequest> Requests => _capturedRequests;

    /// <summary>Enqueues a response to return for the next request.</summary>
    public void Enqueue(HttpStatusCode statusCode, object? body = null)
    {
        var json = body != null
            ? JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            : "{}";

        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    /// <summary>Enqueues a 200 OK response with the given body.</summary>
    public void EnqueueOk(object? body = null) => Enqueue(HttpStatusCode.OK, body);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? bodyText = null;
        if (request.Content != null)
            bodyText = await request.Content.ReadAsStringAsync();

        _capturedRequests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri!,
            bodyText,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));

        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"No mock responses enqueued. Request: {request.Method} {request.RequestUri}");

        return _responses.Dequeue();
    }
}

internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    Dictionary<string, string> Headers)
{
    /// <summary>Deserializes the request body as JSON.</summary>
    public T BodyAs<T>() => JsonSerializer.Deserialize<T>(Body!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>Checks whether the given header key contains the expected value.</summary>
    public bool HasHeader(string key, string expected)
        => Headers.TryGetValue(key, out var val) && val.Contains(expected);
}
