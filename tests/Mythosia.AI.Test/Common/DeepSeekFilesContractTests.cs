using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.DeepSeek;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class DeepSeekFilesContractTests
{
    private const string FileId = "file-api-synthetic_01";
    private const string NextFileId = "file-api-synthetic_02";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=");

    [TestMethod]
    [DataRow(null)]
    [DataRow(3600)]
    [DataRow(2592000)]
    public async Task Upload_UsesImageContentAndPurpose_LeavesCallerStreamOpen_DisposesHttpContent(int? expiration)
    {
        using var fixture = new Fixture();
        var response = new TrackingContent(FileJson(expiration: expiration));
        HttpContent? sentContent = null;
        fixture.Handler.Respond = async (request, token) =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://api.deepseek.com/files", request.RequestUri!.AbsoluteUri);
            sentContent = request.Content;
            var parts = ((MultipartFormDataContent)request.Content!).ToDictionary(
                part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.AreEqual("user_data", await parts["purpose"].ReadAsStringAsync(token));
            var image = parts["file"];
            Assert.AreEqual("image/png", image.Headers.ContentType!.MediaType);
            Assert.AreEqual("renamed.bin", image.Headers.ContentDisposition!.FileName!.Trim('"'));
            CollectionAssert.AreEqual(Png, await image.ReadAsByteArrayAsync(token));
            if (expiration.HasValue)
            {
                Assert.AreEqual("created_at", await parts["expires_after[anchor]"].ReadAsStringAsync(token));
                Assert.AreEqual(expiration.Value.ToString(), await parts["expires_after[seconds]"].ReadAsStringAsync(token));
            }
            else Assert.AreEqual(2, parts.Count);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = response };
        };
        using var source = new MemoryStream(Png);
        var file = await fixture.Service.UploadFileAsync(source, "renamed.bin", expiration);
        Assert.AreEqual(FileId, file.Id);
        Assert.AreEqual((long)Png.Length, file.Bytes);
        Assert.AreEqual("user_data", file.Purpose);
        Assert.AreEqual(expiration.HasValue ? 1700000000L + expiration : null, file.ExpiresAt);
        Assert.IsTrue(source.CanRead, "The caller owns the source stream.");
        Assert.IsTrue(response.Disposed);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => sentContent!.ReadAsStringAsync());
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task Upload_FromCurrentStreamPosition_DetectsSupportedFormats()
    {
        using var fixture = new Fixture();
        foreach (var sample in new[]
        {
            (Bytes: Png, Mime: "image/png"),
            (Bytes: new byte[] { 255, 216, 255, 224 }, Mime: "image/jpeg"),
            (Bytes: Encoding.ASCII.GetBytes("GIF89a"), Mime: "image/gif"),
            (Bytes: Encoding.ASCII.GetBytes("RIFFxxxxWEBP"), Mime: "image/webp")
        })
        {
            fixture.Handler.Respond = async (request, token) =>
            {
                var image = ((MultipartFormDataContent)request.Content!).Single(part => part.Headers.ContentDisposition!.Name!.Trim('"') == "file");
                Assert.AreEqual(sample.Mime, image.Headers.ContentType!.MediaType);
                CollectionAssert.AreEqual(sample.Bytes, await image.ReadAsByteArrayAsync(token));
                return JsonResponse(FileJson());
            };
            using var source = new MemoryStream(new byte[] { 0, 0 }.Concat(sample.Bytes).ToArray());
            source.Position = 2;
            await fixture.Service.UploadFileAsync(source, "image.dat");
            Assert.IsTrue(source.CanRead);
        }
    }

    [TestMethod]
    public async Task FileMetadata_ListPagination_AndDeleteUseTypedResponses()
    {
        using var fixture = new Fixture();
        fixture.Handler.Respond = (request, _) => Task.FromResult(fixture.Handler.RequestCount switch
        {
            1 => JsonResponse(FileJson()),
            2 => JsonResponse(ListJson(FileId, hasMore: true)),
            3 => JsonResponse(ListJson(NextFileId, hasMore: false)),
            _ => JsonResponse($"{{\"id\":\"{FileId}\",\"object\":\"file\",\"deleted\":true}}")
        });
        var info = await fixture.Service.GetFileAsync(FileId);
        Assert.AreEqual("image.png", info.Filename);
        Assert.AreEqual(1700000000L, info.CreatedAt);
        Assert.IsNull(info.ExpiresAt);
        var first = await fixture.Service.ListFilesAsync(new DeepSeekFileListOptions { Limit = 1, Order = DeepSeekFileOrder.Descending });
        Assert.IsTrue(first.HasMore);
        Assert.AreEqual(FileId, first.FirstId);
        Assert.AreEqual(FileId, first.LastId);
        var second = await fixture.Service.ListFilesAsync(new DeepSeekFileListOptions { After = first.LastId, Limit = 1, Order = DeepSeekFileOrder.Descending });
        Assert.IsFalse(second.HasMore);
        Assert.AreEqual(NextFileId, second.Data.Single().Id);
        var deletion = await fixture.Service.DeleteFileAsync(FileId);
        Assert.IsTrue(deletion.Deleted);
        Assert.AreEqual(FileId, deletion.Id);
        CollectionAssert.AreEqual(new[]
        {
            "GET /files/" + FileId,
            "GET /files?purpose=user_data&order=desc&limit=1",
            "GET /files?purpose=user_data&order=desc&after=" + FileId + "&limit=1",
            "DELETE /files/" + FileId
        }, fixture.Handler.Requests);
        Assert.IsTrue(fixture.Handler.Responses.All(response => response.Disposed));
    }

    [TestMethod]
    public async Task EmptyFileList_AllowsMissingOrNullCursors()
    {
        using var fixture = new Fixture();
        foreach (var json in new[]
        {
            "{\"object\":\"list\",\"data\":[],\"has_more\":false}",
            "{\"object\":\"list\",\"data\":[],\"has_more\":false,\"first_id\":null,\"last_id\":null}"
        })
        {
            fixture.Handler.Respond = (_, _) => Task.FromResult(JsonResponse(json));
            var files = await fixture.Service.ListFilesAsync();
            Assert.AreEqual(0, files.Data.Count);
            Assert.IsFalse(files.HasMore);
            Assert.IsNull(files.FirstId);
            Assert.IsNull(files.LastId);
        }
    }

    [TestMethod]
    public async Task PermanentMetadata_AllowsNullExpiration()
    {
        using var fixture = new Fixture();
        var json = JsonNode.Parse(FileJson())!.AsObject();
        json["expires_at"] = null;
        fixture.Handler.Respond = (_, _) => Task.FromResult(JsonResponse(json.ToJsonString()));
        Assert.IsNull((await fixture.Service.GetFileAsync(FileId)).ExpiresAt);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("file-api-")]
    [DataRow("../balance")]
    [DataRow("file-api-../balance")]
    [DataRow("file-api-%2e%2e%2fsecret")]
    [DataRow("file-api-id?query=1")]
    [DataRow("file-api-id#fragment")]
    [DataRow("file-api-id\\path")]
    public async Task InvalidFileIds_AreRejectedBeforeTransport(string id)
    {
        using var fixture = new Fixture();
        Assert.ThrowsExactly<ArgumentException>(() => new DeepSeekImageFileContent(id));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.GetFileAsync(id));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.DeleteFileAsync(id));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.ListFilesAsync(new DeepSeekFileListOptions { After = id }));
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    public async Task UploadAndListValidation_FailsBeforeReadingOrSending()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream(Png);
        foreach (var name in new[] { "", "../image.png", "folder\\image.png", "file\r\nname.png", "file\"name.png", new string('x', 513) })
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.UploadFileAsync(source, name));
        foreach (var expiration in new[] { 0, 3599, 2592001 })
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Service.UploadFileAsync(source, "image.png", expiration));
        foreach (var limit in new[] { 0, 1001 })
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Service.ListFilesAsync(new DeepSeekFileListOptions { Limit = limit }));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Service.ListFilesAsync(new DeepSeekFileListOptions { Order = (DeepSeekFileOrder)999 }));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    public async Task UnsupportedDocument_EmptyImage_AndOversizedSourceFailBeforeTransport()
    {
        using var fixture = new Fixture();
        using var pdf = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7"));
        using var empty = new MemoryStream();
        using var oversized = new OversizedStream();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.UploadFileAsync(pdf, "document.png"));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.UploadFileAsync(empty, "empty.png"));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.UploadFileAsync(oversized, "image.png"));
        Assert.IsTrue(pdf.CanRead);
        Assert.IsTrue(empty.CanRead);
        Assert.IsFalse(oversized.ReadCalled);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    public async Task PreCancellation_DoesNotTouchStreamsOrTransport()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream(Png);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.UploadFileAsync(source, "image.png", cancellationToken: cts.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.UploadFileAsync("unused.png", cancellationToken: cts.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.GetFileAsync(FileId, cts.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.ListFilesAsync(cancellationToken: cts.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.DeleteFileAsync(FileId, cts.Token));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    public async Task Cancellation_StopsSourceReadingWithoutDisposingCallerStream()
    {
        using var fixture = new Fixture();
        using var source = new BlockingReadStream();
        using var cts = new CancellationTokenSource();
        var upload = fixture.Service.UploadFileAsync(source, "image.png", cancellationToken: cts.Token);
        await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => upload.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(source.Disposed);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    public async Task Cancellation_StopsTransportAndDisposesUploadContent()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream(Png);
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HttpContent? content = null;
        fixture.Handler.Respond = async (request, token) =>
        {
            content = request.Content;
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertFailedException("The upload was not canceled.");
        };
        var upload = fixture.Service.UploadFileAsync(source, "image.png", cancellationToken: cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => upload.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(source.CanRead);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => content!.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Cancellation_StopsResponseReadingAndDisposesResponseStream()
    {
        using var fixture = new Fixture();
        using var body = new BlockingReadStream();
        using var cts = new CancellationTokenSource();
        fixture.Handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        var get = fixture.Service.GetFileAsync(FileId, cts.Token);
        await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => get.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(body.Disposed);
    }

    [TestMethod]
    public async Task InvalidMetadataSchemas_DoNotProducePartialSuccess_AndDisposeResponses()
    {
        using var fixture = new Fixture();
        var valid = JsonNode.Parse(FileJson())!.AsObject();
        foreach (var property in new[] { "id", "object", "bytes", "created_at", "filename", "purpose" })
        {
            var broken = valid.DeepClone().AsObject();
            broken.Remove(property);
            await AssertInvalidMetadata(fixture, broken.ToJsonString());
        }
        foreach (var json in new[]
        {
            "not json", "null", "[]",
            FileJson(id: NextFileId),
            FileJson().Replace("\"bytes\":" + Png.Length, "\"bytes\":-1"),
            FileJson().Replace("\"created_at\":1700000000", "\"created_at\":\"1700000000\""),
            FileJson().Replace("\"purpose\":\"user_data\"", "\"purpose\":\"assistants\"")
        }) await AssertInvalidMetadata(fixture, json);
        Assert.IsTrue(fixture.Handler.Responses.All(response => response.Disposed));
    }

    [TestMethod]
    public async Task InvalidPaginationOrDeletionSchemas_DoNotSilentlySucceed()
    {
        using var fixture = new Fixture();
        foreach (var json in new[]
        {
            "{\"object\":\"list\",\"data\":[],\"has_more\":true}",
            "{\"object\":\"list\",\"data\":[],\"has_more\":\"false\"}",
            "{\"object\":\"list\",\"has_more\":false}",
            ListJson(FileId, false).Replace("\"last_id\":\"" + FileId, "\"last_id\":\"" + NextFileId)
        })
        {
            fixture.Handler.Respond = (_, _) => Task.FromResult(JsonResponse(json));
            await Assert.ThrowsExactlyAsync<AIServiceException>(() => fixture.Service.ListFilesAsync());
        }
        foreach (var json in new[]
        {
            $"{{\"id\":\"{FileId}\",\"object\":\"file\"}}",
            $"{{\"id\":\"{NextFileId}\",\"object\":\"file\",\"deleted\":true}}",
            $"{{\"id\":\"{FileId}\",\"object\":\"file\",\"deleted\":\"true\"}}"
        })
        {
            fixture.Handler.Respond = (_, _) => Task.FromResult(JsonResponse(json));
            await Assert.ThrowsExactlyAsync<AIServiceException>(() => fixture.Service.DeleteFileAsync(FileId));
        }
        Assert.IsTrue(fixture.Handler.Responses.All(response => response.Disposed));
    }

    [TestMethod]
    public async Task HttpFailure_DisposesResponseAndPreservesSourceOwnership()
    {
        using var fixture = new Fixture();
        using var source = new MemoryStream(Png);
        fixture.Handler.Respond = (_, _) => Task.FromResult(JsonResponse("{\"error\":{\"message\":\"Synthetic rejection\"}}", HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<AIServiceException>(() => fixture.Service.UploadFileAsync(source, "image.png"));
        Assert.IsTrue(source.CanRead);
        Assert.IsTrue(fixture.Handler.Responses.Single().Disposed);
    }

    [TestMethod]
    public void UploadedImageContent_IsImmutableAndProviderSpecific()
    {
        var image = new DeepSeekImageFileContent(FileId);
        Assert.AreEqual(FileId, image.FileId);
        Assert.AreEqual("file", image.Type);
        Assert.AreEqual(1024u, image.EstimateTokens());
        var wire = JsonSerializer.SerializeToNode(image.ToRequestFormat(nameof(AIProvider.DeepSeek)))!;
        Assert.AreEqual("file", wire["type"]!.GetValue<string>());
        Assert.AreEqual(FileId, wire["file_id"]!.GetValue<string>());
        foreach (var provider in new[] { nameof(AIProvider.OpenAI), nameof(AIProvider.Anthropic), nameof(AIProvider.Google), nameof(AIProvider.xAI) })
            Assert.ThrowsExactly<NotSupportedException>(() => image.ToRequestFormat(provider));
    }

    private static async Task AssertInvalidMetadata(Fixture fixture, string json)
    {
        fixture.Handler.Respond = (_, _) => Task.FromResult(JsonResponse(json));
        await Assert.ThrowsExactlyAsync<AIServiceException>(() => fixture.Service.GetFileAsync(FileId));
    }

    private static string FileJson(string id = FileId, int? expiration = null)
    {
        var json = new JsonObject
        {
            ["id"] = id, ["object"] = "file", ["bytes"] = Png.Length, ["created_at"] = 1700000000L,
            ["filename"] = "image.png", ["purpose"] = "user_data"
        };
        if (expiration.HasValue) json["expires_at"] = 1700000000L + expiration.Value;
        return json.ToJsonString();
    }

    private static string ListJson(string id, bool hasMore)
        => new JsonObject
        {
            ["object"] = "list", ["data"] = new JsonArray(JsonNode.Parse(FileJson(id))),
            ["first_id"] = id, ["last_id"] = id, ["has_more"] = hasMore
        }.ToJsonString();

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new TrackingContent(json) };

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;
        public Handler Handler { get; } = new();
        public DeepSeekService Service { get; }
        public Fixture()
        {
            _client = new HttpClient(Handler);
            Service = new DeepSeekService("offline-test-key", _client);
        }
        public void Dispose() => _client.Dispose();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int RequestCount => Requests.Count;
        public List<string> Requests { get; } = new();
        public List<TrackingContent> Responses { get; } = new();
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; }
            = (_, _) => Task.FromResult(JsonResponse(FileJson()));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("Bearer", request.Headers.Authorization!.Scheme);
            Assert.AreEqual("offline-test-key", request.Headers.Authorization.Parameter);
            Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            var response = await Respond(request, cancellationToken);
            if (response.Content is TrackingContent content) Responses.Add(content);
            return response;
        }
    }

    private sealed class TrackingContent(string content) : StringContent(content, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class OversizedStream : MemoryStream
    {
        public bool ReadCalled { get; private set; }
        public override long Length => 64L * 1024 * 1024 + 1;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ReadCalled = true;
            throw new AssertFailedException("The oversized stream should not be read.");
        }
    }

    private sealed class BlockingReadStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => new(ReadAsync(Array.Empty<byte>(), 0, 0, cancellationToken));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
