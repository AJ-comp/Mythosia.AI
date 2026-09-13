using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

// Only synthetic image fixtures are used. Request bytes remain in memory; output contains
// protocol metadata and artifact paths, never credentials, base64, or complete request bodies.
internal sealed class GrokImageLiveProbe : IDisposable
{
    private readonly CaptureHandler _handler = new();
    private readonly HttpClient _http;
    private readonly string _historyBefore;
    public AIService ChatService { get; }
    public IImageGenerationService Images { get; }
    public IReadOnlyList<RequestRecord> Requests => _handler.Requests;
    public static string ArtifactDirectory { get; } = Path.Combine(FindRepositoryRoot(),
        "artifacts", "test-results", "xai-images-live", "images-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));

    private GrokImageLiveProbe(string provider, string key)
    {
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(6) };
        ChatService = provider == "xAI"
            ? new XAIService(key, AIModels.xAI.Grok4_6, _http)
            : new OpenAIService(key, AIModels.OpenAI.Gpt5_6Sol, _http);
        ChatService.DefaultPolicy.TimeoutSeconds = 300;
        ChatService.ActivateChat.SystemMessage = "Synthetic existing chat state must survive image operations.";
        ChatService.ActivateChat.Messages.Add(new Message(ActorRole.User, "This is an unrelated synthetic text conversation."));
        _historyBefore = JsonSerializer.Serialize(ChatService.ActivateChat);
        Images = (IImageGenerationService)ChatService;
    }

    public static async Task<GrokImageLiveProbe> CreateAsync(string provider = "xAI")
    {
        if (provider is not ("xAI" or "OpenAI")) throw new ArgumentOutOfRangeException(nameof(provider));
        return new(provider, await LiveTestSecrets.GetAsync(provider == "xAI" ? "xai-secret" : "momedit-openai-secret"));
    }

    public RequestRecord AssertRequest(bool editing, int count, IReadOnlyList<ImageInput>? inputs = null)
    {
        Assert.AreEqual(1, Requests.Count, "One public image operation must make exactly one model request without downloading image URLs.");
        var request = Requests[0];
        Assert.AreEqual(ChatService.Provider == "xAI" ? "api.x.ai" : "api.openai.com", request.Host);
        Assert.IsTrue(request.IsHttps);
        Assert.IsTrue(request.HasBearerAuthentication);
        Assert.IsFalse(request.HasQueryAuthentication);
        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual(editing ? "/v1/images/edits" : "/v1/images/generations", request.Path);
        Assert.AreEqual(200, request.StatusCode);
        Assert.AreEqual(Images.DefaultImageModel, request.Field("model"));
        Assert.AreEqual(count.ToString(System.Globalization.CultureInfo.InvariantCulture), request.Field("n"));
        Assert.AreEqual(count, request.ResponseImages.Count);
        Assert.IsTrue(request.ResponseImages.All(image => image.Length > 0 && image.Hash.Length == 64),
            "The API must return actual inline base64 image bytes for every requested image.");
        Assert.IsFalse(request.Body.ContainsKey("messages"));
        Assert.IsFalse(request.Body.ContainsKey("reasoning_effort"));
        Assert.IsFalse(request.Body.ContainsKey("tools"));
        Assert.AreEqual(_historyBefore, JsonSerializer.Serialize(ChatService.ActivateChat));
        Assert.AreEqual(ChatService.Provider == "xAI" ? AIModels.xAI.Grok4_6 : AIModels.OpenAI.Gpt5_6Sol, ChatService.Model);
        var expectedInputs = inputs ?? Array.Empty<ImageInput>();
        CollectionAssert.AreEqual(expectedInputs.Select(image => Hash(image.Data)).ToArray(), request.InputHashes.ToArray(),
            "Input bytes and their order must survive JSON data-URI or multipart serialization unchanged.");
        CollectionAssert.AreEqual(expectedInputs.Select(image => image.MediaType).ToArray(), request.InputMediaTypes.ToArray(),
            "Every ordered reference must keep its original MIME type.");
        if (ChatService.Provider == "xAI")
        {
            Assert.AreEqual("application/json", request.ContentType);
            Assert.AreEqual("b64_json", request.Field("response_format"));
            Assert.IsFalse(request.Body.ContainsKey("size"));
            Assert.IsFalse(request.Body.ContainsKey("output_format"));
            if (expectedInputs.Count == 1)
            {
                Assert.IsNotNull(request.Body["image"]);
                Assert.IsFalse(request.Body.ContainsKey("images"));
            }
            else if (expectedInputs.Count > 1)
            {
                Assert.IsFalse(request.Body.ContainsKey("image"));
                Assert.AreEqual(expectedInputs.Count, request.Body["images"]!.AsArray().Count);
            }
        }
        else Assert.AreEqual(editing ? "multipart/form-data" : "application/json", request.ContentType);
        return request;
    }

    public async Task<IReadOnlyList<SavedImage>> SaveAndValidateAsync(
        ImageGenerationResult result, string scenario, int expectedCount, string? expectedRatio = null, bool twoK = false)
    {
        Assert.AreEqual(ChatService.Provider, result.Provider);
        Assert.AreEqual(Images.DefaultImageModel, result.Model);
        Assert.AreEqual(expectedCount, result.Images.Count);
        var request = Requests.Single();
        Directory.CreateDirectory(ArtifactDirectory);
        var saved = new List<SavedImage>();
        for (var index = 0; index < result.Images.Count; index++)
        {
            var image = result.Images[index];
            Assert.IsTrue(image.Data.Length > 0);
            Assert.AreEqual(request.ResponseImages[index].Hash, Hash(image.Data), "The public image must exactly match the bytes returned by the API.");
            using var encoded = SKData.CreateCopy(image.Data);
            using var codec = SKCodec.Create(encoded);
            Assert.IsNotNull(codec, "The returned bytes must be recognized by a real image codec.");
            Assert.IsTrue(codec.Info.Width >= 512 && codec.Info.Height >= 512, "The generated image must have a usable decoded resolution.");
            Assert.IsTrue(codec.Info.Width <= 8192 && codec.Info.Height <= 8192);
            using var decoded = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            Assert.AreEqual(SKCodecResult.Success, codec.GetPixels(decoded.Info, decoded.GetPixels()),
                "Every returned image must decode completely into pixels, without truncated input or codec errors.");
            var expectedMimeType = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Webp => "image/webp",
                _ => throw new InvalidDataException("The API returned an unexpected encoded image format.")
            };
            Assert.AreEqual(expectedMimeType, image.MediaType, "Reported MIME must describe the actual decoded image bytes.");
            if (twoK) Assert.IsTrue(Math.Max(decoded.Width, decoded.Height) >= 1800 && Math.Min(decoded.Width, decoded.Height) >= 1024);
            if (expectedRatio != null)
            {
                var components = expectedRatio.Split(':').Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var requested = components[0] / components[1];
                Assert.AreEqual(requested, (double)decoded.Width / decoded.Height, requested * 0.04,
                    "Decoded dimensions must follow the requested aspect ratio within image-grid rounding.");
            }
            var extension = expectedMimeType switch { "image/jpeg" => ".jpg", "image/png" => ".png", _ => ".webp" };
            var path = Path.Combine(ArtifactDirectory, scenario + "-output-" + (index + 1) + extension);
            await File.WriteAllBytesAsync(path, image.Data);
            var artifact = new SavedImage(path, decoded.Width, decoded.Height, image.MediaType, image.Data.Length);
            saved.Add(artifact);
            Console.WriteLine("LIVE_GROK_IMAGE_ARTIFACT " + JsonSerializer.Serialize(new
            {
                provider = ChatService.Provider, scenario, artifact.Path, artifact.Width, artifact.Height, artifact.MediaType, artifact.Bytes
            }));
        }
        return saved;
    }

    public static async Task<ImageInput> CreateShapeAsync(string scenario, int ordinal, int shape, string mediaType = "image/png")
    {
        const int side = 256;
        var palette = new[] { new SKColor(230, 35, 35), new SKColor(35, 85, 225), new SKColor(30, 165, 65), new SKColor(245, 195, 25), new SKColor(145, 45, 205) };
        using var bitmap = new SKBitmap(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.White);
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
        {
            var dx = Math.Abs(x - 128);
            var dy = Math.Abs(y - 128);
            var inside = shape switch
            {
                0 => dx <= 60 && dy <= 60,
                1 => dx * dx + dy * dy <= 64 * 64,
                2 => y >= 58 && y <= 198 && dx <= (y - 58) / 2,
                3 => (dx <= 22 && dy <= 68) || (dy <= 22 && dx <= 68),
                _ => dx + dy <= 80
            };
            if (inside) bitmap.SetPixel(x, y, palette[shape]);
        }
        var extension = mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType))
        };
        var format = mediaType switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 95);
        Assert.IsNotNull(encoded, "The synthetic input image must encode successfully.");
        var bytes = encoded.ToArray();
        Directory.CreateDirectory(ArtifactDirectory);
        var name = scenario + "-input-" + ordinal + extension;
        await File.WriteAllBytesAsync(Path.Combine(ArtifactDirectory, name), bytes);
        return new ImageInput(bytes, mediaType, name);
    }

    public void Dispose()
    {
        _http.CancelPendingRequests();
        foreach (var request in Requests)
            Console.WriteLine("LIVE_GROK_IMAGE_REQUEST " + JsonSerializer.Serialize(new
            {
                provider = ChatService.Provider, request.StatusCode, request.Path, request.ContentType,
                model = request.Field("model"), count = request.Field("n"), quality = request.Field("quality"),
                aspectRatio = request.Field("aspect_ratio"), resolution = request.Field("resolution"),
                responseFormat = request.Field("response_format"), inputImages = request.InputHashes.Count,
                inputMediaTypes = request.InputMediaTypes.ToArray(),
                returnedImages = request.ResponseImages.Count
            }));
        _http.Dispose();
    }

    internal sealed record SavedImage(string Path, int Width, int Height, string MediaType, int Bytes);
    internal sealed record ResponseImage(string Hash, int Length);
    internal sealed class RequestRecord
    {
        public required JsonObject Body { get; init; }
        public required string Host { get; init; }
        public required string Path { get; init; }
        public required string Method { get; init; }
        public required string ContentType { get; init; }
        public bool IsHttps { get; init; }
        public bool HasBearerAuthentication { get; init; }
        public bool HasQueryAuthentication { get; init; }
        public int StatusCode { get; set; }
        public List<string> InputHashes { get; } = new();
        public List<string> InputMediaTypes { get; } = new();
        public List<ResponseImage> ResponseImages { get; } = new();
        public string? Field(string name) => Body[name] is JsonValue value ? value.ToString() : null;
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public List<RequestRecord> Requests { get; } = new();
        public CaptureHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = new JsonObject();
            var inputHashes = new List<string>();
            var inputMediaTypes = new List<string>();
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name is "image[]" or "image")
                    {
                        inputHashes.Add(Hash(await part.ReadAsByteArrayAsync(cancellationToken)));
                        inputMediaTypes.Add(part.Headers.ContentType!.MediaType!);
                    }
                    else body[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            }
            else
            {
                body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                var images = body["images"]?.AsArray().OfType<JsonObject>().ToArray() ??
                    (body["image"] is JsonObject singleImage ? new[] { singleImage } : []);
                foreach (var image in images)
                {
                    Assert.AreEqual("image_url", image["type"]?.GetValue<string>());
                    var dataUri = image["url"]!.GetValue<string>();
                    Assert.IsTrue(dataUri.StartsWith("data:image/", StringComparison.Ordinal));
                    var marker = dataUri.IndexOf(";base64,", StringComparison.Ordinal);
                    Assert.IsTrue(marker > 5);
                    var mediaType = dataUri[5..marker];
                    Assert.IsTrue(mediaType is "image/png" or "image/jpeg" or "image/webp");
                    inputMediaTypes.Add(mediaType);
                    inputHashes.Add(Hash(Convert.FromBase64String(dataUri[(dataUri.IndexOf(',') + 1)..])));
                }
            }
            var record = new RequestRecord
            {
                Body = body, Host = uri.Host, Path = uri.AbsolutePath, Method = request.Method.Method,
                ContentType = request.Content!.Headers.ContentType!.MediaType!, IsHttps = uri.Scheme == Uri.UriSchemeHttps,
                HasBearerAuthentication = request.Headers.Authorization?.Scheme == "Bearer" && !string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter),
                HasQueryAuthentication = uri.Query.Contains("key=", StringComparison.OrdinalIgnoreCase)
            };
            record.InputHashes.AddRange(inputHashes);
            record.InputMediaTypes.AddRange(inputMediaTypes);
            Requests.Add(record);
            var response = await base.SendAsync(request, cancellationToken);
            record.StatusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
                {
                    var bytes = Convert.FromBase64String(item.GetProperty("b64_json").GetString()!);
                    record.ResponseImages.Add(new ResponseImage(Hash(bytes), bytes.Length));
                }
            }
            return response;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Mythosia.AI.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate the repository artifact directory for image live tests.");
    }
}
