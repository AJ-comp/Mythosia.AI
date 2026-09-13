using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Rag.Tests;

internal sealed class PerplexityApiTestTransport : HttpMessageHandler
{
    internal List<JsonObject> Bodies { get; } = [];
    internal List<Uri> Uris { get; } = [];
    internal List<string?> Authorization { get; } = [];
    internal List<TrackedContent> Contents { get; } = [];
    internal Func<JsonObject, string> Reply { get; set; } = _ => "{}";
    internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    internal bool WaitForCancellation { get; set; }
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uris.Add(request.RequestUri!);
        Authorization.Add(request.Headers.Authorization?.ToString());
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        Bodies.Add(body);
        Entered.TrySetResult();
        if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
        var content = new TrackedContent(Reply(body));
        Contents.Add(content);
        return new HttpResponseMessage(Status) { Content = content };
    }

    internal sealed class TrackedContent(string json) : StringContent(json, Encoding.UTF8, "application/json")
    {
        internal bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
