using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Perplexity;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

// Full payloads stay in memory for assertions. Logs contain request shape, status, IDs and usage only.
internal sealed class PerplexityAgentLiveProbe : IDisposable
{
    private readonly CaptureHandler _handler = new();
    private readonly HttpClient _http;
    public PerplexityService Service { get; }
    public IReadOnlyList<RequestRecord> Requests => _handler.Requests;

    private PerplexityAgentLiveProbe(string key)
    {
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(8) };
        Service = new PerplexityService(key, _http) { MaxTokens = 4096, StructuredOutputMaxRetries = 0 };
        Service.DefaultPolicy.TimeoutSeconds = 420;
        Service.DefaultPolicy.MaxRounds = 4;
        Service.SystemMessage = "Follow the user's task precisely and keep the final answer concise.";
    }

    public static async Task<PerplexityAgentLiveProbe> CreateAsync()
        => new(await LiveTestSecrets.GetAsync("sonar-secret2"));

    public async Task<ObservedAnswer> ExecuteAsync(string prompt, PerplexityAgentExecutionMode mode)
    {
        var first = Requests.Count;
        var events = new List<StreamingContent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        string result;
        if (mode == PerplexityAgentExecutionMode.Completion)
            result = await Service.GetCompletionAsync(prompt).WaitAsync(timeout.Token);
        else if (mode == PerplexityAgentExecutionMode.Callback)
        {
            var text = new StringBuilder();
            await Service.StreamCompletionAsync(prompt, chunk => { text.Append(chunk); return Task.CompletedTask; }).WaitAsync(timeout.Token);
            result = text.ToString();
        }
        else if (mode == PerplexityAgentExecutionMode.RichStream)
        {
            await foreach (var item in Service.StreamAsync(prompt, StreamOptions.FullOptions, cancellationToken: timeout.Token)) events.Add(item);
            result = string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content));
        }
        else
        {
            var text = new StringBuilder();
            await using var run = await Service.StartRunAsync(prompt, chunk => text.Append(chunk), StreamOptions.FullOptions, cancellationToken: timeout.Token);
            await foreach (var item in run.StreamAsync(timeout.Token)) events.Add(item);
            result = (await run.Result).Text;
            Assert.AreEqual(result, text.ToString());
            Assert.AreEqual(result, string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        }
        var calls = Requests.Skip(first).ToArray();
        Assert.IsNotEmpty(calls);
        Assert.AreEqual(mode == PerplexityAgentExecutionMode.Completion ? calls.Last().AnswerText() : string.Concat(calls.Select(call => call.AnswerText())), result,
            "Public text must preserve the authoritative provider output.");
        if (mode is PerplexityAgentExecutionMode.RichStream or PerplexityAgentExecutionMode.Run)
        {
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
            Assert.AreEqual(calls.Sum(call => call.FunctionCalls().Count), events.Count(item => item.Type == StreamingContentType.FunctionCall));
            var roundUsage = events.Where(item => item.Type == StreamingContentType.RoundUsage).ToArray();
            Assert.AreEqual(calls.Length, roundUsage.Length);
            for (var index = 0; index < calls.Length; index++)
            {
                var native = calls[index].Response()!["usage"]!;
                Assert.AreEqual(native["input_tokens"]!.GetValue<int>(), roundUsage[index].Usage!.InputTokens);
                Assert.AreEqual(native["output_tokens"]!.GetValue<int>(), roundUsage[index].Usage!.OutputTokens);
            }
        }
        AssertTransport();
        return new(result, events);
    }

    public void AssertTransport()
    {
        Assert.IsNotEmpty(Requests);
        foreach (var record in Requests)
        {
            Assert.IsTrue(record.IsHttps && record.HasBearerAuthentication && !record.HasQueryAuthentication);
            Assert.AreEqual("api.perplexity.ai", record.Host);
            Assert.AreEqual("/v1/agent", record.Path);
            Assert.AreEqual(200, record.StatusCode, "Every request must succeed without model substitution or hidden retries.");
            Assert.AreEqual("completed", record.Response()?["status"]?.GetValue<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.Response()?["id"]?.GetValue<string>()));
            Assert.IsNotNull(record.Response()?["usage"]);
            Assert.IsTrue(record.Body["max_output_tokens"]!.GetValue<int>() > 0);
            Assert.IsFalse(record.Body.ContainsKey("messages"));
            Assert.IsFalse(record.Body.ContainsKey("max_tokens"));
        }
    }

    public void Dispose()
    {
        _http.CancelPendingRequests();
        for (var index = 0; index < Requests.Count; index++)
        {
            var request = Requests[index];
            JsonObject? response = null;
            try { response = request.Response(); } catch (JsonException) { }
            Console.WriteLine("LIVE_PERPLEXITY_AGENT_REQUEST " + JsonSerializer.Serialize(new
            {
                index, model = request.Body["model"]?.GetValue<string>(), preset = request.Body["preset"]?.GetValue<string>(),
                request.StatusCode, request.Streaming, responseId = response?["id"]?.GetValue<string>(), status = response?["status"]?.GetValue<string>(),
                maxOutputTokens = request.Body["max_output_tokens"]?.GetValue<int>(),
                reasoningEffort = request.Body["reasoning"]?["effort"]?.GetValue<string>(),
                tools = request.Body["tools"]?.AsArray().Select(tool => tool?["type"]?.GetValue<string>()).ToArray(),
                imageInputs = request.Body["input"]!.AsArray().OfType<JsonObject>().Sum(item => item["content"] is JsonArray parts ? parts.Count(part => part?["type"]?.GetValue<string>() == "input_image") : 0),
                responseFormat = request.Body["response_format"]?["type"]?.GetValue<string>(),
                customCallCount = response?["output"]?.AsArray().Count(item => item?["type"]?.GetValue<string>() == "function_call") ?? 0,
                usage = response?["usage"]?.DeepClone(), recordedResponseBytes = request.Bytes.Length
            }));
            request.Bytes.Dispose();
        }
        _http.Dispose();
    }

    internal sealed record ObservedAnswer(string Text, IReadOnlyList<StreamingContent> Events);

    internal sealed class RequestRecord
    {
        public required JsonObject Body { get; init; }
        public required string Host { get; init; }
        public required string Path { get; init; }
        public bool IsHttps { get; init; }
        public bool HasBearerAuthentication { get; init; }
        public bool HasQueryAuthentication { get; init; }
        public bool Streaming { get; init; }
        public int StatusCode { get; set; }
        public MemoryStream Bytes { get; } = new();

        public IReadOnlyList<JsonObject> Frames()
        {
            var text = Encoding.UTF8.GetString(Bytes.ToArray());
            if (!Streaming || text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                return JsonNode.Parse(text) is JsonObject body ? [body] : [];
            return text.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim())
                .Where(line => line.Length > 0 && line != "[DONE]").Select(line => JsonNode.Parse(line)).OfType<JsonObject>().ToArray();
        }
        public JsonObject? Response() => !Streaming ? Frames().FirstOrDefault() :
            Frames().LastOrDefault(frame => frame["type"]?.GetValue<string>() == "response.completed")?["response"]?.AsObject();
        public string AnswerText() => string.Concat(Response()?["output"]?.AsArray().OfType<JsonObject>()
            .Where(item => item["type"]?.GetValue<string>() == "message").SelectMany(item => item["content"]?.AsArray().OfType<JsonObject>() ?? [])
            .Where(part => part["type"]?.GetValue<string>() == "output_text").Select(part => part["text"]?.GetValue<string>()) ?? []);
        public IReadOnlyList<JsonObject> FunctionCalls() => Response()?["output"]?.AsArray().OfType<JsonObject>()
            .Where(item => item["type"]?.GetValue<string>() == "function_call").ToArray() ?? [];
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public List<RequestRecord> Requests { get; } = [];
        public CaptureHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var record = new RequestRecord
            {
                Body = body, Host = uri.Host, Path = uri.AbsolutePath, IsHttps = uri.Scheme == Uri.UriSchemeHttps,
                HasBearerAuthentication = request.Headers.Authorization?.Scheme == "Bearer" && !string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter),
                HasQueryAuthentication = uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase),
                Streaming = body["stream"]?.GetValue<bool>() == true
            };
            Requests.Add(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.StatusCode = (int)response.StatusCode;
            response.Content = new RecordingContent(response.Content, record.Bytes);
            return response;
        }
    }

    private sealed class RecordingContent(HttpContent inner, MemoryStream capture) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task<Stream> CreateContentReadStreamAsync() => new RecordingStream(await inner.ReadAsStreamAsync(), capture);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var source = new RecordingStream(await inner.ReadAsStreamAsync(), capture);
            await source.CopyToAsync(stream);
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class RecordingStream(Stream inner, MemoryStream capture) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count); capture.Write(buffer, offset, read); return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer, offset, count, cancellationToken); capture.Write(buffer, offset, read); return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken); capture.Write(buffer.Span[..read]); return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
public enum PerplexityAgentExecutionMode { Completion, Callback, RichStream, Run }
