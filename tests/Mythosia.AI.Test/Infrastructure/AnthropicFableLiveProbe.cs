using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

// Records only synthetic test requests in memory. Console output projects protocol metadata;
// neither authentication headers nor prompts, signatures, thinking text, or tool results are logged.
internal sealed class AnthropicFableLiveProbe : IDisposable
{
    internal const string BindingBeta = "thinking-binding-controls-2026-08-01";
    internal const string UpdatesBeta = "thinking-display-updates-2026-08-18";
    internal const string TurnBeta = "mid-conversation-system-clear-at-2026-08-21";
    internal const string EffortBeta = "mid-conversation-output-config-2026-07-01";
    private readonly HttpClient _http;
    private readonly CaptureHandler _handler;
    public AnthropicService Service { get; }
    public IReadOnlyList<RequestRecord> Requests => _handler.Requests;

    private AnthropicFableLiveProbe(string key)
    {
        _handler = new CaptureHandler();
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(5) };
        Service = new AnthropicService(key, AIModels.Anthropic.ClaudeFable5_1, _http)
        {
            MaxTokens = 8192,
            AdaptiveThinkingEffort = ClaudeReasoningEffort.High,
            AdaptiveThinkingDisplay = ClaudeThinkingDisplay.Omitted
        };
        Service.DefaultPolicy.MaxRounds = 8;
        Service.DefaultPolicy.TimeoutSeconds = 240;
        Service.ActivateChat.SystemMessage = "Follow the current task precisely. Keep final answers concise.";
    }

    public static async Task<AnthropicFableLiveProbe> CreateAsync() =>
        new(await LiveTestSecrets.GetAsync("momedit-antropic-secret"));

    public async Task<ObservedAnswer> ExecuteAsync(string prompt, FableExecutionMode mode)
    {
        var events = new List<StreamingContent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        if (mode == FableExecutionMode.Completion)
        {
            var pending = Service.GetCompletionAsync(prompt);
            try { return new(await pending.WaitAsync(timeout.Token), events); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _http.CancelPendingRequests();
                try { await pending; } catch { /* Preserve the original timeout after draining the canceled request. */ }
                throw;
            }
        }
        if (mode == FableExecutionMode.LegacyStream)
        {
            await foreach (var item in Service.StreamAsync(prompt, StreamOptions.FullOptions, cancellationToken: timeout.Token))
                events.Add(item);
            Assert.IsFalse(events.Any(item => item.Type == StreamingContentType.Error), "The live SSE stream returned an error.");
            Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
            return new(string.Concat(events.Where(item => item.Type == StreamingContentType.Text).Select(item => item.Content)), events);
        }
        var callback = new StringBuilder();
        await using var run = await Service.StartRunAsync(prompt, text => callback.Append(text),
            StreamOptions.FullOptions, cancellationToken: timeout.Token);
        await foreach (var item in run.StreamAsync(timeout.Token)) events.Add(item);
        var result = (await run.Result).Text;
        Assert.AreEqual(result, callback.ToString());
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
        return new(result, events);
    }

    public void AssertTransport(FableExecutionMode mode, params string[] requiredBetas)
    {
        Assert.IsTrue(Requests.Count > 0);
        foreach (var request in Requests)
        {
            Assert.AreEqual("api.anthropic.com", request.Host);
            Assert.AreEqual("/v1/messages", request.Path);
            Assert.AreEqual(AIModels.Anthropic.ClaudeFable5_1, request.Body["model"]!.GetValue<string>());
            Assert.AreEqual(mode != FableExecutionMode.Completion, request.Streaming);
            foreach (var beta in requiredBetas)
                Assert.IsTrue(request.Betas.Contains(beta, StringComparer.Ordinal), "The required feature beta was not sent: " + beta);
        }
    }

    public void Dispose()
    {
        for (var index = 0; index < Requests.Count; index++)
        {
            var request = Requests[index];
            Console.WriteLine("LIVE_FABLE_REQUEST " + JsonSerializer.Serialize(new
            {
                index, request.StatusCode, request.RequestId, request.Streaming, request.Betas,
                model = request.Body["model"]?.GetValue<string>(),
                toolChoice = request.Body["tool_choice"]?["type"]?.GetValue<string>(),
                responseTypes = request.ResponseBlocks().Select(block => block["type"]?.GetValue<string>()).ToArray(),
                stopReasons = request.ProtocolValues("stop_reason"),
                transformations = request.Transformations().Count,
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
        public required string[] Betas { get; init; }
        public bool Streaming { get; init; }
        public int StatusCode { get; set; }
        public string? RequestId { get; set; }
        public MemoryStream Bytes { get; } = new();

        public IReadOnlyList<JsonObject> Frames()
        {
            var text = Encoding.UTF8.GetString(Bytes.ToArray());
            try
            {
                if (!Streaming || text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    return JsonNode.Parse(text) is JsonObject body ? new[] { body } : [];
                return text.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
                    .Select(line => JsonNode.Parse(line[6..]) as JsonObject).OfType<JsonObject>().ToArray();
            }
            catch (JsonException) { return []; } // Production parsing still owns malformed-response failures.
        }

        public IReadOnlyList<JsonObject> ResponseBlocks()
        {
            if (!Streaming)
                return Frames().FirstOrDefault()?["content"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
            var blocks = new SortedDictionary<int, JsonObject>();
            foreach (var frame in Frames())
            {
                var type = frame["type"]?.GetValue<string>();
                if (type == "content_block_start")
                    blocks[frame["index"]!.GetValue<int>()] = (JsonObject)frame["content_block"]!.DeepClone();
                else if (type == "content_block_delta" && blocks.TryGetValue(frame["index"]!.GetValue<int>(), out var block))
                {
                    var delta = frame["delta"]!;
                    var field = delta["type"]?.GetValue<string>() switch
                    {
                        "thinking_delta" => "thinking", "signature_delta" => "signature", "text_delta" => "text", _ => null
                    };
                    if (field != null) block[field] = (block[field]?.GetValue<string>() ?? "") + delta[field]!.GetValue<string>();
                }
            }
            return blocks.Values.ToArray();
        }

        public IReadOnlyList<JsonObject> Transformations() => Frames().SelectMany(frame =>
            (frame["input_transformations"] ?? frame["message"]?["input_transformations"])?
                .AsArray().OfType<JsonObject>() ?? []).ToArray();

        public string[] ProtocolValues(string name) => Frames().Select(frame =>
            (frame[name] ?? frame["delta"]?[name])?.GetValue<string>()).OfType<string>().Distinct().ToArray();
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public List<RequestRecord> Requests { get; } = new();
        public CaptureHandler() : base(new HttpClientHandler()) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var record = new RequestRecord
            {
                Body = body, Host = request.RequestUri!.Host, Path = request.RequestUri.AbsolutePath,
                Streaming = body["stream"]?.GetValue<bool>() == true,
                Betas = request.Headers.TryGetValues("anthropic-beta", out var betas)
                    ? string.Join(",", betas).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : []
            };
            Requests.Add(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.StatusCode = (int)response.StatusCode;
            record.RequestId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;
            response.Content = new RecordingContent(response.Content, record.Bytes);
            return response;
        }
    }

    // Tee actual reads; do not buffer an SSE response ahead of the production parser.
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

public enum FableExecutionMode { Completion, Run, LegacyStream }
