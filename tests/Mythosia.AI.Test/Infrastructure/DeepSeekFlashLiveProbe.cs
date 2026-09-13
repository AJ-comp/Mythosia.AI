using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

// Synthetic requests and responses stay in memory. Diagnostics contain protocol counts and
// configuration only; authentication, prompts, reasoning text, and handler values are never logged.
internal sealed class DeepSeekFlashLiveProbe : IDisposable
{
    private readonly CaptureHandler _handler = new();
    private readonly HttpClient _http;
    public DeepSeekService Service { get; }
    public IReadOnlyList<RequestRecord> Requests => _handler.Requests;

    private DeepSeekFlashLiveProbe(string key)
    {
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(4) };
        Service = new DeepSeekService(key, _http)
        {
            MaxTokens = 4096,
            StructuredOutputMaxRetries = 0
        };
        Service.DefaultPolicy.TimeoutSeconds = 210;
        Service.DefaultPolicy.MaxRounds = 4;
        Service.ActivateChat.SystemMessage = "Follow the task precisely. Keep final answers concise.";
    }

    public static async Task<DeepSeekFlashLiveProbe> CreateAsync() => new(await LiveTestSecrets.GetAsync("deepseek-secret"));

    public async Task<ObservedAnswer> ExecuteAsync(string prompt, DeepSeekFlashExecutionMode mode)
    {
        var firstRequest = Requests.Count;
        var events = new List<StreamingContent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        if (mode == DeepSeekFlashExecutionMode.Completion)
            return new(await Service.GetCompletionAsync(prompt).WaitAsync(timeout.Token), events);
        var text = new StringBuilder();
        if (mode == DeepSeekFlashExecutionMode.LegacyCallback)
        {
            await Service.StreamCompletionAsync(prompt, chunk =>
            {
                text.Append(chunk);
                return Task.CompletedTask;
            }).WaitAsync(timeout.Token);
            return new(text.ToString(), events);
        }
        if (mode == DeepSeekFlashExecutionMode.RichStream)
        {
            await foreach (var item in Service.StreamAsync(prompt, StreamOptions.FullOptions, cancellationToken: timeout.Token))
                events.Add(item);
            text.Append(string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        }
        else
        {
            await using var run = await Service.StartRunAsync(prompt, chunk => text.Append(chunk),
                StreamOptions.FullOptions, cancellationToken: timeout.Token);
            await foreach (var item in run.StreamAsync(timeout.Token)) events.Add(item);
            var result = (await run.Result).Text;
            Assert.AreEqual(result, text.ToString(), "The Run callback and result must receive identical text.");
            Assert.AreEqual(result, string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        }
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
        Assert.AreEqual(string.Concat(Requests.Skip(firstRequest).Select(request => request.AnswerText())), text.ToString(),
            "Run and rich streaming must preserve all provider text, including progress emitted between tool calls.");
        var providerReasoning = string.Concat(Requests.Skip(firstRequest).SelectMany(request => request.Choices()).Select(choice =>
            choice["delta"]?["reasoning_content"]?.GetValue<string>()));
        var publicReasoning = string.Concat(events.Where(item => item.Type == StreamingContentType.Reasoning).Select(item => item.Content));
        Assert.AreEqual(providerReasoning, publicReasoning,
            "Every reasoning delta actually emitted by DeepSeek must reach the requested public event stream.");
        Console.WriteLine($"LIVE_DEEPSEEK_FLASH_EVENTS mode={mode} reasoning={events.Count(item => item.Type == StreamingContentType.Reasoning)}");
        return new(text.ToString(), events);
    }

    public void AssertTransport(bool? streaming = null, bool queryRewriteFirst = false)
    {
        Assert.IsTrue(Requests.Count > 0);
        for (var index = 0; index < Requests.Count; index++)
        {
            var request = Requests[index];
            Assert.IsTrue(request.IsHttps);
            Assert.AreEqual("api.deepseek.com", request.Host);
            Assert.AreEqual("/chat/completions", request.Path);
            Assert.IsTrue(request.HasBearerAuthentication);
            Assert.IsFalse(request.HasQueryAuthentication);
            Assert.AreEqual(AIModels.DeepSeek.Flash, request.Body["model"]?.GetValue<string>());
            Assert.AreEqual(200, request.StatusCode, "Every real request must succeed without model substitution or hidden retries.");
            if (streaming.HasValue) Assert.AreEqual(streaming.Value, request.Streaming);
            Assert.AreEqual(queryRewriteFirst && index == 0 ? 512 : (int)Service.MaxTokens, request.Body["max_tokens"]?.GetValue<int>(),
                "The explicit output budget must reach every request, including both client-function rounds.");
            Assert.IsFalse(request.Body.ContainsKey("frequency_penalty"));
            Assert.IsFalse(request.Body.ContainsKey("presence_penalty"));
            var thinking = request.Body["thinking"]?["type"]?.GetValue<string>();
            Assert.IsTrue(thinking is "enabled" or "disabled", "Library defaults must explicitly select the provider thinking mode.");
            var effort = request.Body["reasoning_effort"]?.GetValue<string>();
            Assert.IsTrue(effort is null or "low" or "high" or "max");
            if (thinking == "enabled")
            {
                Assert.IsFalse(request.Body.ContainsKey("temperature"));
                Assert.IsTrue(request.Body["top_p"]!.GetValue<float>() >= 0.95f);
            }
            else
            {
                Assert.IsFalse(request.Body.ContainsKey("top_p"));
                Assert.IsFalse(request.Body.ContainsKey("reasoning_effort"));
            }
            var finishes = request.Choices().Select(choice => choice["finish_reason"]?.GetValue<string>()).OfType<string>().ToArray();
            Assert.IsTrue(finishes.Length > 0, "The actual response must include a terminal finish reason.");
            Assert.IsTrue(finishes.All(reason => reason is "stop" or "tool_calls"),
                "Truncated, refused, or filtered responses do not qualify as successful completion.");
        }
    }

    public void Dispose()
    {
        _http.CancelPendingRequests();
        for (var index = 0; index < Requests.Count; index++)
        {
            var request = Requests[index];
            Console.WriteLine("LIVE_DEEPSEEK_FLASH_REQUEST " + JsonSerializer.Serialize(new
            {
                index, model = request.Body["model"]?.GetValue<string>(), request.StatusCode, request.Streaming,
                maxTokens = request.Body["max_tokens"]?.GetValue<int>(),
                topP = request.Body["top_p"]?.GetValue<float>(),
                thinking = request.Body["thinking"]?["type"]?.GetValue<string>(),
                reasoningEffort = request.Body["reasoning_effort"]?.GetValue<string>(),
                imageInputs = request.Body["messages"]!.AsArray().OfType<JsonObject>().Sum(message => message["content"] is JsonArray parts ? parts.OfType<JsonObject>().Count(part => part["type"]?.GetValue<string>() == "image_url") : 0),
                responseFormat = request.Body["response_format"]?["type"]?.GetValue<string>(),
                nativeCallIds = request.NativeCallIds().Count,
                reasoningParts = request.Choices().Count(choice =>
                    !string.IsNullOrWhiteSpace((choice["delta"] ?? choice["message"])?["reasoning_content"]?.GetValue<string>())),
                recordedResponseBytes = request.Bytes.Length
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
            try
            {
                if (!Streaming || text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    return JsonNode.Parse(text) is JsonObject body ? new[] { body } : [];
                return text.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                    .Select(line => line[5..].Trim()).Where(line => line.Length > 0 && line != "[DONE]")
                    .Select(line => JsonNode.Parse(line) as JsonObject).OfType<JsonObject>().ToArray();
            }
            catch (JsonException) { return []; }
        }
        public IReadOnlyList<JsonObject> Choices() => Frames().SelectMany(frame => frame["choices"]?.AsArray().OfType<JsonObject>() ?? []).ToArray();
        public string Reasoning() => string.Concat(Choices().Select(choice =>
            (choice["delta"] ?? choice["message"])?["reasoning_content"]?.GetValue<string>()));
        public string AnswerText() => string.Concat(Choices().Select(choice =>
            (choice["delta"] ?? choice["message"])?["content"]?.GetValue<string>()));
        public IReadOnlyList<string> NativeCallIds() => Choices().SelectMany(choice =>
            (choice["delta"] ?? choice["message"])?["tool_calls"]?.AsArray().OfType<JsonObject>() ?? [])
            .Select(call => call["id"]?.GetValue<string>()).OfType<string>().Where(id => id.Length > 0).Distinct().ToArray();
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public List<RequestRecord> Requests { get; } = new();
        public CaptureHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var record = new RequestRecord
            {
                Body = body, Host = uri.Host, Path = uri.AbsolutePath, IsHttps = uri.Scheme == Uri.UriSchemeHttps,
                HasBearerAuthentication = request.Headers.Authorization?.Scheme == "Bearer" &&
                    !string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter),
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

    // Tee production reads rather than pre-buffering an SSE response.
    private sealed class RecordingContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly MemoryStream _capture;
        public RecordingContent(HttpContent inner, MemoryStream capture)
        {
            _inner = inner; _capture = capture;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task<Stream> CreateContentReadStreamAsync() => new RecordingStream(await _inner.ReadAsStreamAsync(), _capture);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var source = new RecordingStream(await _inner.ReadAsStreamAsync(), _capture);
            await source.CopyToAsync(stream);
        }
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
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

public enum DeepSeekFlashExecutionMode { Completion, LegacyCallback, RichStream, Run }
