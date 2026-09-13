using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

// Capture synthetic protocol data only in memory. Never log authentication, request URLs,
// prompts, image bytes, provider signatures, thought text, or opaque tool/document values.
internal sealed class GeminiFlashLiveProbe : IDisposable
{
    private readonly CaptureHandler _handler = new();
    private readonly HttpClient _http;
    public GoogleAIService Service { get; }
    public IReadOnlyList<RequestRecord> Requests => _handler.Requests;

    private GeminiFlashLiveProbe(string key, string model)
    {
        _http = new HttpClient(_handler);
        Service = new GoogleAIService(key, model, _http)
        {
            MaxTokens = 4096,
            StructuredOutputMaxRetries = 0,
            ThinkingLevel = GeminiThinkingLevel.Low
        };
        Service.DefaultPolicy.TimeoutSeconds = 210;
        Service.DefaultPolicy.MaxRounds = 4;
        Service.ActivateChat.SystemMessage = "Follow the task precisely. Keep final answers concise.";
    }

    public static async Task<GeminiFlashLiveProbe> CreateAsync(string model) =>
        new(await LiveTestSecrets.GetAsync("gemini-secret"), model);

    public Task<ObservedAnswer> ExecuteAsync(string prompt, GeminiFlashExecutionMode mode) =>
        ExecuteAsync(new Message(ActorRole.User, prompt), mode);

    public async Task<ObservedAnswer> ExecuteAsync(Message message, GeminiFlashExecutionMode mode)
    {
        var events = new List<StreamingContent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        if (mode == GeminiFlashExecutionMode.Completion)
            return new(await Service.GetCompletionAsync(message).WaitAsync(timeout.Token), events, Service.LastCitations.ToArray());
        var text = new StringBuilder();
        if (mode == GeminiFlashExecutionMode.LegacyCallback)
        {
            await Service.StreamCompletionAsync(message, chunk =>
            {
                text.Append(chunk);
                return Task.CompletedTask;
            }).WaitAsync(timeout.Token);
            return new(text.ToString(), events, Service.LastCitations.ToArray());
        }
        await using var run = await Service.StartRunAsync(message, chunk => text.Append(chunk),
            StreamOptions.FullOptions, cancellationToken: timeout.Token);
        await foreach (var item in run.StreamAsync(timeout.Token)) events.Add(item);
        var result = (await run.Result).Text;
        Assert.AreEqual(result, text.ToString(), "The Run callback and result must receive identical text.");
        Assert.AreEqual(result, string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)));
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
        Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error));
        Console.WriteLine($"LIVE_GOOGLE_FLASH_EVENTS model={Service.Model} reasoning={events.Count(item => item.Type == StreamingContentType.Reasoning)}");
        return new(result, events, run.Citations.ToArray());
    }

    public void AssertTransport(bool? streaming = null)
    {
        Assert.IsTrue(Requests.Count > 0);
        foreach (var request in Requests)
        {
            Assert.AreEqual("generativelanguage.googleapis.com", request.Host);
            Assert.IsTrue(request.IsHttps);
            Assert.AreEqual($"/v1beta/models/{Service.Model}:" +
                (request.Streaming ? "streamGenerateContent" : "generateContent"), request.Path);
            Assert.AreEqual(200, request.StatusCode, "Every model request must succeed without fallback or retries.");
            Assert.IsTrue(request.HasHeaderAuthentication);
            Assert.IsFalse(request.HasQueryAuthentication);
            if (streaming.HasValue) Assert.AreEqual(streaming.Value, request.Streaming);
            var config = request.Body["generationConfig"]!.AsObject();
            Assert.IsFalse(config.ContainsKey("temperature"));
            Assert.IsFalse(config.ContainsKey("topP"));
            Assert.IsFalse(config.ContainsKey("topK"));
            Assert.IsFalse(config["thinkingConfig"]?.AsObject().ContainsKey("thinkingBudget") == true);
            var thinking = config["thinkingConfig"]?["thinkingLevel"]?.GetValue<string>();
            Assert.IsTrue(thinking is "LOW" or "MEDIUM" or "HIGH", "Only the documented supported thinking levels may reach these models.");
            Assert.IsTrue(request.Frames().Count > 0, "Actual provider response content must have been consumed.");
            Assert.IsTrue(request.Frames().Any(frame => frame["candidates"]?.AsArray().OfType<JsonObject>()
                .Any(candidate => candidate["finishReason"]?.GetValue<string>() == "STOP") == true),
                "The provider must report a successful terminal STOP, rather than a truncated answer.");
        }
    }

    public void Dispose()
    {
        _http.CancelPendingRequests();
        for (var index = 0; index < Requests.Count; index++)
        {
            var request = Requests[index];
            Console.WriteLine("LIVE_GOOGLE_FLASH_REQUEST " + JsonSerializer.Serialize(new
            {
                index, model = Service.Model, request.StatusCode, request.Streaming,
                thinkingLevel = request.Body["generationConfig"]?["thinkingConfig"]?["thinkingLevel"]?.GetValue<string>(),
                includeThoughts = request.Body["generationConfig"]?["thinkingConfig"]?["includeThoughts"]?.GetValue<bool>() == true,
                responseMimeType = request.Body["generationConfig"]?["responseFormat"]?["text"]?["mimeType"]?.GetValue<string>(),
                functionCalls = request.ResponseParts().Count(part => part["functionCall"] != null),
                signedParts = request.ResponseParts().Count(part => part["thoughtSignature"] != null),
                thoughtParts = request.ResponseParts().Count(part => part["thought"]?.GetValue<bool>() == true),
                recordedResponseBytes = request.Bytes.Length
            }));
            request.Bytes.Dispose();
        }
        _http.Dispose();
    }

    internal sealed record ObservedAnswer(string Text, IReadOnlyList<StreamingContent> Events, IReadOnlyList<AICitation> Citations);

    internal sealed class RequestRecord
    {
        public required JsonObject Body { get; init; }
        public required string Host { get; init; }
        public required string Path { get; init; }
        public bool IsHttps { get; init; }
        public bool HasHeaderAuthentication { get; init; }
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

        public IReadOnlyList<JsonObject> ResponseParts() => Frames().SelectMany(frame =>
            frame["candidates"]?.AsArray().OfType<JsonObject>() ?? []).SelectMany(candidate =>
                candidate["content"]?["parts"]?.AsArray().OfType<JsonObject>() ?? []).ToArray();
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public List<RequestRecord> Requests { get; } = new();
        public CaptureHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var record = new RequestRecord
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject(),
                Host = uri.Host, Path = uri.AbsolutePath, IsHttps = uri.Scheme == Uri.UriSchemeHttps,
                HasHeaderAuthentication = request.Headers.Contains("x-goog-api-key"),
                HasQueryAuthentication = uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase),
                Streaming = uri.AbsolutePath.EndsWith(":streamGenerateContent", StringComparison.Ordinal)
            };
            Requests.Add(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.StatusCode = (int)response.StatusCode;
            response.Content = new RecordingContent(response.Content, record.Bytes);
            return response;
        }
    }

    // Tee production reads instead of pre-buffering SSE, so the live suite observes real streaming.
    private sealed class RecordingContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly MemoryStream _capture;
        public RecordingContent(HttpContent inner, MemoryStream capture)
        {
            _inner = inner;
            _capture = capture;
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

public enum GeminiFlashExecutionMode { Completion, LegacyCallback, Run }
