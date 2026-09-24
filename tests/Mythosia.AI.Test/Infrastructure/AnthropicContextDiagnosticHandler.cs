using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

// Used only by ContextManagementTest's six synthetic requests. No authentication headers,
// signatures or thinking blocks are recorded, and the original response content is retained.
internal sealed class AnthropicContextDiagnosticHandler : DelegatingHandler
{
    private int _requestIndex;

    public AnthropicContextDiagnosticHandler(HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler()) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _requestIndex);
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        Console.WriteLine("LIVE_CLAUDE_CONTEXT_REQUEST " + JsonSerializer.Serialize(new
        {
            index,
            host = request.RequestUri?.Host,
            path = request.RequestUri?.AbsolutePath,
            model = body["model"]?.GetValue<string>(),
            stream = body["stream"]?.GetValue<bool>(),
            maxTokens = body["max_tokens"]?.GetValue<int>(),
            thinking = body["thinking"],
            outputConfig = body["output_config"],
            systemPresent = body.ContainsKey("system"),
            messages = body["messages"]!.AsArray().Select(message => new
            {
                role = message!["role"]?.GetValue<string>(),
                text = VisibleText(message["content"]),
                blockTypes = BlockTypes(message["content"])
            }).ToArray()
        }));

        var response = await base.SendAsync(request, cancellationToken);
        string raw;
        try
        {
            // This handler is never attached to a streaming fixture. Reading buffers the
            // content for the production parser; it does not replace or rewrite the body.
            raw = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }
        try
        {
            var result = JsonNode.Parse(raw) as JsonObject
                ?? throw new InvalidOperationException("The diagnostic response is not a JSON object.");
            Console.WriteLine("LIVE_CLAUDE_CONTEXT_RESPONSE " + JsonSerializer.Serialize(new
            {
                index,
                status = (int)response.StatusCode,
                requestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null,
                model = result["model"]?.GetValue<string>(),
                stopReason = result["stop_reason"]?.GetValue<string>(),
                stopDetails = result["stop_details"],
                text = VisibleText(result["content"]),
                blockTypes = BlockTypes(result["content"]),
                errorType = result["error"]?["type"]?.GetValue<string>(),
                errorMessage = result["error"]?["message"]?.GetValue<string>()
            }));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            // Diagnostic projection must not hide the production parser's treatment of
            // malformed JSON, unexpected root shapes, or fields with unexpected types.
            Console.WriteLine($"LIVE_CLAUDE_CONTEXT_UNPARSED index={index} status={(int)response.StatusCode}");
        }
        return response;
    }

    private static string? VisibleText(JsonNode? content) => content switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonArray blocks => string.Concat(blocks.OfType<JsonObject>()
            .Where(block => block["type"]?.GetValue<string>() == "text")
            .Select(block => block["text"]?.GetValue<string>())),
        _ => null
    };

    private static string[] BlockTypes(JsonNode? content) => content is JsonArray blocks
        ? blocks.OfType<JsonObject>().Select(block => block["type"]?.GetValue<string>() ?? "unknown").ToArray()
        : [];
}
