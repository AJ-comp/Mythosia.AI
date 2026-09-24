using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.DeepSeek;
using SkiaSharp;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Tests.DeepSeek;

[TestClass]
[TestCategory("Live")]
[TestCategory("DeepSeek")]
[TestCategory("DeepSeekCurrentApi")]
[DoNotParallelize]
public class DeepSeekCurrentApiLiveTests
{
    private const string Calculation = "Calculate (17 * 19) - (8 * 19) + 37. Return only the final integer.";

    [TestMethod]
    [DataRow("deepseek-flash", ExecutionMode.Completion)]
    [DataRow("deepseek-flash", ExecutionMode.Stream)]
    [DataRow("deepseek-flash", ExecutionMode.Run)]
    [DataRow("deepseek-v4-pro", ExecutionMode.Completion)]
    [DataRow("deepseek-v4-pro", ExecutionMode.Stream)]
    [DataRow("deepseek-v4-pro", ExecutionMode.Run)]
    public async Task Responses_PublicExecutionPathsCompleteWithRealUsage(string model, ExecutionMode mode)
    {
        using var probe = await Probe.CreateAsync(model, responses: true);
        probe.Service.WithDeepSeekReasoning(DeepSeekReasoning.Low);
        var answer = await Execute(probe.Service, Calculation, mode);
        AssertInteger(answer, 208);
        var request = probe.GenerationRequests.Single();
        Assert.AreEqual("/responses", request.Path);
        Assert.AreEqual(mode != ExecutionMode.Completion, request.Streaming);
        Assert.AreEqual("low", request.Body!["reasoning"]?["effort"]?.GetValue<string>());
        Assert.AreEqual("completed", request.Response()["status"]?.GetValue<string>());
        Assert.IsTrue(request.Response()["usage"]?["input_tokens"]?.GetValue<int>() > 0);
        Assert.IsTrue(request.Response()["usage"]?["output_tokens"]?.GetValue<int>() > 0);
        if (mode != ExecutionMode.Completion)
        {
            Assert.AreEqual(1, request.Frames().Count(frame => frame["type"]?.GetValue<string>() == "response.completed"));
            Assert.IsFalse(Encoding.UTF8.GetString(request.Bytes.ToArray()).Contains("data: [DONE]", StringComparison.Ordinal));
        }
        Assert.AreEqual(answer, probe.Service.ActivateChat.Messages.Last().Content);
    }

    [TestMethod]
    [DataRow(DeepSeekReasoning.Low)]
    [DataRow(DeepSeekReasoning.High)]
    [DataRow(DeepSeekReasoning.Max)]
    public async Task ProChat_EveryNativeReasoningEffortUsesDocumentedTransport(DeepSeekReasoning effort)
    {
        using var probe = await Probe.CreateAsync("deepseek-v4-pro", responses: false);
        probe.Service.WithDeepSeekReasoning(effort);
        if (effort == DeepSeekReasoning.Max) probe.Service.MaxTokens = 16384;
        AssertInteger(await Execute(probe.Service, Calculation, ExecutionMode.Completion), 208);
        var request = probe.GenerationRequests.Single();
        Assert.AreEqual("/chat/completions", request.Path);
        Assert.AreEqual("enabled", request.Body!["thinking"]?["type"]?.GetValue<string>());
        Assert.AreEqual(effort.ToString().ToLowerInvariant(), request.Body["reasoning_effort"]?.GetValue<string>());
        Assert.AreEqual("stop", request.Response()["choices"]?[0]?["finish_reason"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow("deepseek-flash", ExecutionMode.Completion)]
    [DataRow("deepseek-v4-pro", ExecutionMode.Run)]
    public async Task Responses_ToolResultAndAllReasoningSurviveLaterUserTurn(string model, ExecutionMode mode)
    {
        using var probe = await Probe.CreateAsync(model, responses: true);
        probe.Service.WithDeepSeekReasoning(DeepSeekReasoning.Low);
        var nonce = "LOOKUP_" + Guid.NewGuid().ToString("N");
        var invocations = 0;
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "read_test_reference", Description = "Returns the current synthetic reference. Call exactly once to obtain it.",
            Handler = _ => { invocations++; return Task.FromResult(JsonSerializer.Serialize(new { reference = nonce })); }
        });
        var answer = await Execute(probe.Service,
            "Call read_test_reference exactly once. Copy the exact reference returned by that tool into your final answer. Do not guess the reference.", mode);
        StringAssert.Contains(answer, nonce);
        Assert.AreEqual(1, invocations);
        Assert.AreEqual(2, probe.GenerationRequests.Count);
        Assert.IsFalse(probe.GenerationRequests[0].Body!.ToJsonString().Contains(nonce, StringComparison.Ordinal));
        var firstOutput = Output(probe.GenerationRequests[0].Response()).ToArray();
        var call = firstOutput.Single(item => item["type"]?.GetValue<string>() == "function_call");
        var callId = call["call_id"]!.GetValue<string>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(callId));
        Assert.IsTrue(firstOutput.Any(item => item["type"]?.GetValue<string>() == "reasoning" && ContentText(item).Length > 0),
            "The actual tool round must supply reasoning for the replay assertion.");
        var secondInput = Input(probe.GenerationRequests[1].Body!).ToArray();
        Assert.AreEqual(callId, secondInput.Single(item => item["type"]?.GetValue<string>() == "function_call")["call_id"]?.GetValue<string>());
        var toolResult = secondInput.Single(item => item["type"]?.GetValue<string>() == "function_call_output");
        Assert.AreEqual(callId, toolResult["call_id"]?.GetValue<string>());
        StringAssert.Contains(toolResult["output"]!.GetValue<string>(), nonce);

        var followUp = await Execute(probe.Service,
            "Repeat the exact reference you already obtained. Use the stored conversation result and make no further tool call.", mode);
        StringAssert.Contains(followUp, nonce);
        Assert.AreEqual(1, invocations);
        Assert.AreEqual(3, probe.GenerationRequests.Count);
        for (var index = 1; index < 3; index++)
        {
            var expectedReasoning = probe.GenerationRequests.Take(index).SelectMany(request => Output(request.Response()))
                .Where(item => item["type"]?.GetValue<string>() == "reasoning").Select(ContentText).ToArray();
            var replayedReasoning = Input(probe.GenerationRequests[index].Body!)
                .Where(item => item["type"]?.GetValue<string>() == "reasoning").Select(ContentText).ToArray();
            CollectionAssert.AreEqual(expectedReasoning, replayedReasoning);
            Assert.IsFalse(probe.GenerationRequests[index].Body!.ContainsKey("previous_response_id"));
        }
    }

    [TestMethod]
    [DataRow("deepseek-flash")]
    [DataRow("deepseek-v4-pro")]
    public async Task Responses_TypedSchemaReturnsObjectWithoutRepair(string model)
    {
        using var probe = await Probe.CreateAsync(model, responses: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var answer = await probe.Service.GetCompletionAsync<ArithmeticResult>(
            "Return JSON with Value equal to 17 times 19 and City equal to Seoul.", timeout.Token);
        Assert.AreEqual(323, answer.Value);
        Assert.AreEqual("Seoul", answer.City);
        var request = probe.GenerationRequests.Single();
        Assert.AreEqual("json_schema", request.Body!["text"]?["format"]?["type"]?.GetValue<string>());
        Assert.IsNotNull(request.Body["text"]?["format"]?["schema"]);
    }

    [TestMethod]
    public async Task FlashFiles_UploadInspectUseBothTransportsAndDelete()
    {
        using var probe = await Probe.CreateAsync("deepseek-flash", responses: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        using var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var data = new MemoryStream(encoded.ToArray(), writable: false);
        string? fileId = null;
        try
        {
            var uploaded = await probe.Service.UploadFileAsync(data, "synthetic-blue.png", expiresAfterSeconds: 3600, cancellationToken: timeout.Token);
            fileId = uploaded.Id;
            Assert.AreEqual(encoded.Size, uploaded.Bytes);
            var metadata = await probe.Service.GetFileAsync(fileId, timeout.Token);
            Assert.AreEqual(fileId, metadata.Id);
            Assert.AreEqual("synthetic-blue.png", metadata.Filename);
            var page = await probe.Service.ListFilesAsync(new DeepSeekFileListOptions { Limit = 100, Order = DeepSeekFileOrder.Descending }, timeout.Token);
            Assert.IsTrue(page.Data.Any(item => item.Id == fileId), "The newly uploaded synthetic image must appear in the first descending page.");
            foreach (var responses in new[] { false, true })
            {
                probe.Service.UseResponsesApi = responses;
                probe.Service.ActivateChat.Messages.Clear();
                var message = new Message(ActorRole.User, new List<MessageContent>
                {
                    new TextContent("What is the dominant color? Reply with its English color name."),
                    new DeepSeekImageFileContent(fileId)
                });
                string answer;
                if (responses)
                {
                    await using var run = await probe.Service.StartRunAsync(message, cancellationToken: timeout.Token);
                    answer = (await run.Result).Text;
                }
                else answer = await probe.Service.GetCompletionAsync(message, cancellationToken: timeout.Token);
                StringAssert.Contains(answer.ToLowerInvariant(), "blue");
                var request = probe.GenerationRequests.Last();
                Assert.AreEqual(responses ? "/responses" : "/chat/completions", request.Path);
                var messages = responses ? Input(request.Body!) : request.Body!["messages"]!.AsArray().OfType<JsonObject>();
                var content = messages.Single(item => item["role"]?.GetValue<string>() == "user")["content"]!.AsArray().OfType<JsonObject>().ToArray();
                var filePart = content.Single(item => item["file_id"] != null);
                Assert.AreEqual(fileId, filePart["file_id"]?.GetValue<string>());
                Assert.AreEqual(responses ? "input_image" : "file", filePart["type"]?.GetValue<string>());
            }
        }
        finally
        {
            if (fileId != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                var deletion = await probe.Service.DeleteFileAsync(fileId, cleanup.Token);
                Assert.IsTrue(deletion.Deleted);
                Assert.AreEqual(fileId, deletion.Id);
            }
        }
        Assert.AreEqual(2, probe.GenerationRequests.Count);
        Assert.AreEqual(6, probe.Requests.Count);
    }

    public enum ExecutionMode { Completion, Stream, Run }
    public sealed class ArithmeticResult { public int Value { get; set; } public string City { get; set; } = ""; }

    private static void AssertInteger(string text, int expected)
    {
        var numbers = Regex.Matches(text, @"-?\d+").Select(match => int.Parse(match.Value)).ToArray();
        CollectionAssert.AreEqual(new[] { expected }, numbers, "The visible answer must contain only the requested integer value.");
    }

    private static async Task<string> Execute(DeepSeekService service, string prompt, ExecutionMode mode)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        if (mode == ExecutionMode.Completion) return await service.GetCompletionAsync(prompt, cancellationToken: timeout.Token);
        if (mode == ExecutionMode.Run)
        {
            await using var run = await service.StartRunAsync(prompt, options: StreamOptions.FullOptions, cancellationToken: timeout.Token);
            var observed = new StringBuilder();
            await foreach (var item in run.StreamAsync(timeout.Token))
            {
                Assert.AreNotEqual(StreamingContentType.Error, item.Type);
                if (item.Type == StreamingContentType.Text) observed.Append(item.Content);
            }
            var result = await run.Result;
            Assert.AreEqual(result.Text, observed.ToString());
            return result.Text;
        }
        var output = new StringBuilder();
        var completionCount = 0;
        await foreach (var item in service.StreamAsync(prompt, StreamOptions.FullOptions, cancellationToken: timeout.Token))
        {
            Assert.AreNotEqual(StreamingContentType.Error, item.Type);
            if (item.Type == StreamingContentType.Text) output.Append(item.Content);
            if (item.Type == StreamingContentType.Completion) completionCount++;
        }
        Assert.AreEqual(1, completionCount);
        return output.ToString();
    }

    private static IEnumerable<JsonObject> Input(JsonObject request) => request["input"]!.AsArray().OfType<JsonObject>();
    private static IEnumerable<JsonObject> Output(JsonObject response) => response["output"]!.AsArray().OfType<JsonObject>();
    private static string ContentText(JsonObject item) => string.Concat(item["content"]!.AsArray().OfType<JsonObject>().Select(part => part["text"]?.GetValue<string>()));

    // Tee actual production reads. Raw prompts, reasoning, file data, tool values, and credentials
    // remain in memory; the report contains only transport shape, status, and token counts.
    private sealed class Probe : IDisposable
    {
        private readonly CaptureHandler _handler = new();
        private readonly HttpClient _http;
        public DeepSeekService Service { get; }
        public IReadOnlyList<RequestRecord> Requests => _handler.Requests;
        public IReadOnlyList<RequestRecord> GenerationRequests => Requests.Where(item => item.Body != null).ToArray();
        private Probe(string key, string model, bool responses)
        {
            _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(4) };
            Service = new DeepSeekService(key, model, _http) { UseResponsesApi = responses, MaxTokens = 4096, StructuredOutputMaxRetries = 0 };
            Service.DefaultPolicy.TimeoutSeconds = 210;
            Service.DefaultPolicy.MaxRounds = 2;
            Service.SystemMessage = "Follow the task precisely and keep the final answer concise.";
        }
        public static async Task<Probe> CreateAsync(string model, bool responses)
            => new(await LiveTestSecrets.GetAsync("deepseek-secret"), model, responses);
        public void Dispose()
        {
            _http.CancelPendingRequests();
            foreach (var request in Requests)
            {
                JsonObject? usage = null;
                string? terminal = null;
                var nativeCalls = 0;
                try
                {
                    if (request.Body != null)
                    {
                        var response = request.Response();
                        usage = response["usage"] as JsonObject;
                        terminal = response["status"]?.GetValue<string>() ?? response["choices"]?[0]?["finish_reason"]?.GetValue<string>();
                        nativeCalls = response["output"]?.AsArray().OfType<JsonObject>().Count(item => item["type"]?.GetValue<string>() == "function_call") ?? 0;
                    }
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException) { terminal = "unparsed"; }
                Console.WriteLine("LIVE_DEEPSEEK_CURRENT_REQUEST " + JsonSerializer.Serialize(new
                {
                    request.Method, path = request.Path.StartsWith("/files/", StringComparison.Ordinal) ? "/files/{id}" : request.Path,
                    request.StatusCode, request.Streaming, model = request.Body?["model"]?.GetValue<string>(), terminal, nativeCalls,
                    effort = request.Body?["reasoning"]?["effort"]?.GetValue<string>() ?? request.Body?["reasoning_effort"]?.GetValue<string>(),
                    maxTokens = request.Body?["max_output_tokens"]?.GetValue<int>() ?? request.Body?["max_tokens"]?.GetValue<int>(),
                    inputTokens = usage?["input_tokens"]?.GetValue<int>() ?? usage?["prompt_tokens"]?.GetValue<int>(),
                    outputTokens = usage?["output_tokens"]?.GetValue<int>() ?? usage?["completion_tokens"]?.GetValue<int>(),
                    cachedTokens = usage?["input_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? usage?["prompt_cache_hit_tokens"]?.GetValue<int>(),
                    recordedResponseBytes = request.Bytes.Length
                }));
                request.Bytes.Dispose();
            }
            _http.Dispose();
        }
    }

    private sealed class RequestRecord
    {
        public required string Path { get; init; }
        public required string Method { get; init; }
        public JsonObject? Body { get; init; }
        public bool Streaming => Body?["stream"]?.GetValue<bool>() == true;
        public int StatusCode { get; set; }
        public MemoryStream Bytes { get; } = new();
        public IReadOnlyList<JsonObject> Frames()
        {
            var text = Encoding.UTF8.GetString(Bytes.ToArray());
            if (!Streaming || text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                return JsonNode.Parse(text) is JsonObject response ? [response] : [];
            return text.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line[5..].Trim()).Where(line => line.Length > 0 && line != "[DONE]")
                .Select(line => JsonNode.Parse(line) as JsonObject).OfType<JsonObject>().ToArray();
        }
        public JsonObject Response() => Streaming
            ? Frames().Last(frame => frame["type"]?.GetValue<string>() is "response.completed" or "response.incomplete" or "response.failed")["response"]!.AsObject()
            : Frames().Single();
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public List<RequestRecord> Requests { get; } = new();
        public CaptureHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            Assert.AreEqual("https", uri.Scheme);
            Assert.AreEqual("api.deepseek.com", uri.Host);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.IsFalse(string.IsNullOrWhiteSpace(request.Headers.Authorization?.Parameter));
            Assert.IsFalse(uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase));
            var json = request.Content?.Headers.ContentType?.MediaType == "application/json"
                ? JsonNode.Parse(await request.Content.ReadAsStringAsync(token))!.AsObject() : null;
            var record = new RequestRecord { Path = uri.AbsolutePath, Method = request.Method.Method, Body = json };
            Requests.Add(record);
            var response = await base.SendAsync(request, token);
            record.StatusCode = (int)response.StatusCode;
            response.Content = new RecordingContent(response.Content, record.Bytes);
            return response;
        }
    }

    private sealed class RecordingContent : HttpContent
    {
        private readonly HttpContent inner;
        private readonly MemoryStream capture;
        public RecordingContent(HttpContent content, MemoryStream bytes)
        {
            inner = content;
            capture = bytes;
            foreach (var header in content.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
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
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            var read = await inner.ReadAsync(buffer, offset, count, token); capture.Write(buffer, offset, read); return read;
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
