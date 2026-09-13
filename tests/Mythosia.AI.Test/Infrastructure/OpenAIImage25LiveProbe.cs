using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mythosia.AI.Tests;

// All input images are generated synthetic fixtures. Only protocol metadata and artifact
// paths are logged; credentials, prompts and encoded images remain out of diagnostics.
internal sealed class OpenAIImage25LiveProbe : IDisposable
{
    private readonly CaptureHandler _handler = new();
    private readonly HttpClient _http;
    private readonly OpenAIService _chat;
    private readonly string _history;
    internal IImageGenerationService Images => _chat;
    private static readonly string ArtifactDirectory = Path.Combine(FindRepositoryRoot(), "artifacts", "test-results", "openai-images-live",
        "images-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));

    private OpenAIImage25LiveProbe(string key)
    {
        _http = new HttpClient(_handler) { Timeout = TimeSpan.FromMinutes(6) };
        _chat = new OpenAIService(key, AIModels.OpenAI.Gpt5_6Sol, _http);
        _chat.DefaultPolicy.TimeoutSeconds = 300;
        _chat.ActivateChat.Messages.Add(new Message(ActorRole.User, "Existing synthetic conversation must survive image operations."));
        _history = JsonSerializer.Serialize(_chat.ActivateChat);
    }

    internal static async Task<OpenAIImage25LiveProbe> CreateAsync()
    {
        RequireOptIn(Environment.GetEnvironmentVariable("MYTHOSIA_OPENAI_IMAGE25_LIVE"));
        return new OpenAIImage25LiveProbe(await LiveTestSecrets.GetAsync("momedit-openai-secret"));
    }

    internal static void RequireOptIn(string? value)
    {
        if (value != "1") throw new InvalidOperationException("Set MYTHOSIA_OPENAI_IMAGE25_LIVE=1 explicitly before image live tests; they incur API charges.");
    }

    internal async Task ValidateAndSaveAsync(ImageGenerationResult result, string model, bool editing, ImageInput? input)
    {
        Assert.HasCount(1, _handler.Requests, "A single image operation must make exactly one provider request.");
        var sent = _handler.Requests[0];
        Assert.AreEqual("api.openai.com", sent.Host);
        Assert.AreEqual("https", sent.Scheme);
        Assert.AreEqual("POST", sent.Method);
        Assert.AreEqual(editing ? "/v1/images/edits" : "/v1/images/generations", sent.Path);
        Assert.AreEqual(editing ? "multipart/form-data" : "application/json", sent.ContentType);
        Assert.AreEqual(200, sent.StatusCode);
        Assert.IsTrue(sent.HasBearerAuthentication);
        Assert.AreEqual(model, sent.Fields["model"]);
        Assert.AreEqual("1", sent.Fields["n"]);
        Assert.AreEqual("1024x1024", sent.Fields["size"]);
        Assert.AreEqual("low", sent.Fields["quality"]);
        Assert.AreEqual("png", sent.Fields["output_format"]);
        Assert.AreEqual(editing ? 1 : 0, sent.InputHashes.Count);
        if (input != null)
        {
            Assert.AreEqual(Hash(input.Data), sent.InputHashes.Single());
            Assert.AreEqual(input.MediaType, sent.InputMediaTypes.Single());
        }
        Assert.HasCount(1, sent.ImageHashes);
        Assert.HasCount(1, result.Images);
        Assert.AreEqual("OpenAI", result.Provider);
        Assert.AreEqual(model, result.Model);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.RequestId));
        Assert.AreEqual(sent.RequestId, result.RequestId);
        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(sent.InputTokens, result.Usage.InputTokens);
        Assert.AreEqual(sent.OutputTokens, result.Usage.OutputTokens);
        Assert.AreEqual(sent.TotalTokens, result.Usage.TotalTokens);
        Assert.IsTrue(result.Usage.InputTokens > 0 && result.Usage.OutputTokens > 0);
        Assert.AreEqual(result.Usage.InputTokens + result.Usage.OutputTokens, result.Usage.TotalTokens);
        Assert.AreEqual(_history, JsonSerializer.Serialize(_chat.ActivateChat));
        Assert.AreEqual(AIModels.OpenAI.Gpt5_6Sol, _chat.Model);
        Assert.AreEqual(AIModels.OpenAI.GptImage2, Images.DefaultImageModel);

        var image = result.Images.Single();
        Assert.AreEqual(sent.ImageHashes.Single(), Hash(image.Data));
        Assert.AreEqual("image/png", image.MediaType);
        using var encoded = SKData.CreateCopy(image.Data);
        using var codec = SKCodec.Create(encoded);
        Assert.IsNotNull(codec, "Image bytes must be recognized by a real codec.");
        Assert.AreEqual(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.AreEqual(1024, codec.Info.Width);
        Assert.AreEqual(1024, codec.Info.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(1024, 1024, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.AreEqual(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels()), "Decode every output pixel; truncated images must fail.");
        Directory.CreateDirectory(ArtifactDirectory);
        var scenario = model + (editing ? "-edit" : "-generation");
        var path = Path.Combine(ArtifactDirectory, scenario + ".png");
        await File.WriteAllBytesAsync(path, image.Data);
        if (input != null) await File.WriteAllBytesAsync(Path.Combine(ArtifactDirectory, scenario + "-input.png"), input.Data);
        Console.WriteLine("LIVE_OPENAI_IMAGE25_ARTIFACT " + JsonSerializer.Serialize(new
        { model, editing, Path = path, Width = codec.Info.Width, Height = codec.Info.Height, image.MediaType, Bytes = image.Data.Length }));
        Console.WriteLine("LIVE_OPENAI_IMAGE25_OK " + JsonSerializer.Serialize(new { model, editing }));
    }

    internal static ImageInput CreateSyntheticInput()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Red }) canvas.DrawRect(new SKRect(64, 64, 192, 192), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        Assert.IsNotNull(data);
        return new ImageInput(data.ToArray(), "image/png", "synthetic-red-square.png");
    }

    public void Dispose()
    {
        foreach (var sent in _handler.Requests)
            Console.WriteLine("LIVE_OPENAI_IMAGE25_REQUEST " + JsonSerializer.Serialize(new
            {
                sent.StatusCode, sent.Path, sent.ContentType, model = sent.Fields.GetValueOrDefault("model"), count = sent.Fields.GetValueOrDefault("n"),
                quality = sent.Fields.GetValueOrDefault("quality"), size = sent.Fields.GetValueOrDefault("size"), format = sent.Fields.GetValueOrDefault("output_format"),
                inputImages = sent.InputHashes.Count, returnedImages = sent.ImageHashes.Count, hasRequestId = !string.IsNullOrWhiteSpace(sent.RequestId),
                sent.InputTokens, sent.OutputTokens, sent.TotalTokens
            }));
        _http.Dispose();
    }

    private sealed class RequestRecord
    {
        internal string Host { get; init; } = "";
        internal string Scheme { get; init; } = "";
        internal string Method { get; init; } = "";
        internal string Path { get; init; } = "";
        internal string? ContentType { get; init; }
        internal bool HasBearerAuthentication { get; init; }
        internal Dictionary<string, string> Fields { get; } = new();
        internal List<string> InputHashes { get; } = new();
        internal List<string> InputMediaTypes { get; } = new();
        internal List<string> ImageHashes { get; } = new();
        internal int StatusCode { get; set; }
        internal string? RequestId { get; set; }
        internal int InputTokens { get; set; }
        internal int OutputTokens { get; set; }
        internal int TotalTokens { get; set; }
    }

    private sealed class CaptureHandler() : DelegatingHandler(new HttpClientHandler())
    {
        internal List<RequestRecord> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = new RequestRecord { Host = request.RequestUri!.Host, Scheme = request.RequestUri.Scheme,
                Path = request.RequestUri.AbsolutePath, Method = request.Method.Method, ContentType = request.Content!.Headers.ContentType?.MediaType,
                HasBearerAuthentication = request.Headers.Authorization?.Scheme == "Bearer" && !string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter) };
            if (request.Content is MultipartContent multipart)
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name is "image[]" or "image")
                    {
                        sent.InputHashes.Add(Hash(await part.ReadAsByteArrayAsync(cancellationToken)));
                        sent.InputMediaTypes.Add(part.Headers.ContentType!.MediaType!);
                    }
                    else if (name != "prompt") sent.Fields[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            else
            {
                using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name != "prompt") sent.Fields[property.Name] = property.Value.ToString();
            }
            Requests.Add(sent);
            var response = await base.SendAsync(request, cancellationToken);
            sent.StatusCode = (int)response.StatusCode;
            sent.RequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                foreach (var image in document.RootElement.GetProperty("data").EnumerateArray())
                    sent.ImageHashes.Add(Hash(Convert.FromBase64String(image.GetProperty("b64_json").GetString()!)));
                var usage = document.RootElement.GetProperty("usage");
                sent.InputTokens = usage.GetProperty("input_tokens").GetInt32();
                sent.OutputTokens = usage.GetProperty("output_tokens").GetInt32();
                sent.TotalTokens = usage.GetProperty("total_tokens").GetInt32();
            }
            return response;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Mythosia.AI.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate image-test artifact directory.");
    }
}
