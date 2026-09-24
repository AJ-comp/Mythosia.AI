using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Google;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Google;

[TestClass]
[TestCategory("Live")]
[TestCategory("Google")]
[TestCategory("GoogleImageModelOptions")]
[DoNotParallelize]
public class GoogleImageModelOptionsLiveTests
{
    // Paid opt-in regression cases use synthetic inputs only. A failure is not retried,
    // skipped, or redirected to a fallback model. Logs contain metadata, never API keys.
    private static readonly string ArtifactDirectory = Path.Combine(FindRepositoryRoot(),
        "artifacts", "test-results", "google-image-model-options-live",
        "images-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashImage, ImageResolution.FiveTwelve, ImageAspectRatio.FourByOne,
        "IMAGE_SIZE_FIVE_TWELVE", "ASPECT_RATIO_FOUR_BY_ONE", 1024, 256, 4.0)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, ImageResolution.OneK, ImageAspectRatio.SixteenByNine,
        "IMAGE_SIZE_ONE_K", "ASPECT_RATIO_SIXTEEN_BY_NINE", 1376, 768, 16.0 / 9.0)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.TwoK, ImageAspectRatio.FourByThree,
        "IMAGE_SIZE_TWO_K", "ASPECT_RATIO_FOUR_BY_THREE", 2400, 1792, 4.0 / 3.0)]
    public Task Generation_UsesSelectedModelAndSupportedImageOptions(
        string model, ImageResolution resolution, ImageAspectRatio ratio,
        string wireResolution, string wireRatio, int expectedWidth, int expectedHeight, double expectedRatio)
        => ExecuteAsync(false, model, resolution, ratio, wireResolution, wireRatio,
            expectedWidth, expectedHeight, expectedRatio);

    [TestMethod]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashImage, ImageResolution.FiveTwelve, ImageAspectRatio.FourByOne,
        "IMAGE_SIZE_FIVE_TWELVE", "ASPECT_RATIO_FOUR_BY_ONE", 1024, 256, 4.0)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, ImageResolution.OneK, ImageAspectRatio.SixteenByNine,
        "IMAGE_SIZE_ONE_K", "ASPECT_RATIO_SIXTEEN_BY_NINE", 1376, 768, 16.0 / 9.0)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.TwoK, ImageAspectRatio.FourByThree,
        "IMAGE_SIZE_TWO_K", "ASPECT_RATIO_FOUR_BY_THREE", 2400, 1792, 4.0 / 3.0)]
    public Task Editing_UsesSelectedModelAndSupportedImageOptions(
        string model, ImageResolution resolution, ImageAspectRatio ratio,
        string wireResolution, string wireRatio, int expectedWidth, int expectedHeight, double expectedRatio)
        => ExecuteAsync(true, model, resolution, ratio, wireResolution, wireRatio,
            expectedWidth, expectedHeight, expectedRatio);

    private static async Task ExecuteAsync(
        bool editing, string model, ImageResolution resolution, ImageAspectRatio ratio,
        string wireResolution, string wireRatio, int expectedWidth, int expectedHeight, double expectedRatio)
    {
        var operation = editing ? "edit" : "generate";
        var scenario = model + "-" + operation;
        var source = editing ? CreateSyntheticReference() : null;
        using var handler = new ImageOptionsHandler(model, operation, wireResolution, wireRatio, source);
        using var http = new HttpClient(handler);
        var service = new GoogleAIService(await LiveTestSecrets.GetAsync("gemini-secret"), http);
        service.DefaultPolicy.TimeoutSeconds = 300;
        IImageGenerationService images = service;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var result = editing
            ? await images.EditImagesAsync(new ImageEditRequest
            {
                Model = model,
                Prompt = "Change the red square in this reference into a blue circle centered on a plain white canvas. " +
                    "Adapt the canvas to the requested output aspect ratio. Return one image, without text or borders.",
                InputImages = new[] { source! },
                Count = 1,
                Size = ImageSize.Preset(resolution, ratio),
                OutputFormat = ImageOutputFormat.Jpeg
            }, timeout.Token)
            : await images.GenerateImagesAsync(new ImageGenerationRequest
            {
                Model = model,
                Prompt = "Generate one minimal flat illustration of a blue circle centered on a plain white canvas. " +
                    "Use the requested output aspect ratio. No text, borders, shadows, or other objects.",
                Count = 1,
                Size = ImageSize.Preset(resolution, ratio),
                OutputFormat = ImageOutputFormat.Jpeg
            }, timeout.Token);

        Assert.AreEqual(1, handler.RequestCount, "Each image operation must make exactly one HTTP model request.");
        Assert.AreEqual(200, handler.StatusCode);
        Assert.AreEqual("Google", result.Provider);
        Assert.AreEqual(model, result.Model);
        Assert.AreEqual(1, result.Images.Count);
        Assert.AreEqual(1, handler.ResponseImageHashes.Count);
        var output = result.Images.Single();
        Assert.AreEqual(handler.ResponseImageHashes.Single(), Hash(output.Data),
            "The public result must contain the original image bytes returned by the requested model.");
        Assert.AreEqual("image/jpeg", output.MediaType);
        using var encoded = SKData.CreateCopy(output.Data);
        using var codec = SKCodec.Create(encoded);
        Assert.IsNotNull(codec, "A real image codec must recognize the returned bytes.");
        Assert.AreEqual(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        using var decoded = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.AreEqual(SKCodecResult.Success, codec.GetPixels(decoded.Info, decoded.GetPixels()),
            "The entire response image must decode successfully.");

        Directory.CreateDirectory(ArtifactDirectory);
        var path = Path.Combine(ArtifactDirectory, scenario + ".jpg");
        await File.WriteAllBytesAsync(path, output.Data, timeout.Token);
        Console.WriteLine("LIVE_GOOGLE_IMAGE_OPTIONS_ARTIFACT " + JsonSerializer.Serialize(new
        {
            model, operation, resolution = wireResolution, aspectRatio = wireRatio,
            width = decoded.Width, height = decoded.Height, mimeType = output.MediaType,
            bytes = output.Data.Length, path
        }));

        // Presets select resolution tiers, not exact pixel dimensions. The published image
        // grids (https://ai.google.dev/gemini-api/docs/image-generation#aspect_ratios_and_image_size)
        // anchor a narrow range that permits rounding but rejects an ignored size or ratio.
        Assert.AreEqual((double)expectedWidth, decoded.Width, expectedWidth * 0.05,
            "Decoded width must match the requested resolution tier and aspect ratio.");
        Assert.AreEqual((double)expectedHeight, decoded.Height, expectedHeight * 0.05,
            "Decoded height must match the requested resolution tier and aspect ratio.");
        Assert.AreEqual(expectedRatio, (double)decoded.Width / decoded.Height, expectedRatio * 0.02,
            "Decoded dimensions must follow the requested aspect ratio within image-grid rounding.");
        Console.WriteLine("LIVE_GOOGLE_IMAGE_OPTIONS_OK " + JsonSerializer.Serialize(new { model, operation }));
    }

    private static ImageInput CreateSyntheticReference()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(128, 128, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.White);
        for (var y = 40; y < 88; y++)
        for (var x = 40; x < 88; x++)
            bitmap.SetPixel(x, y, SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        Assert.IsNotNull(encoded);
        return new ImageInput(encoded.ToArray(), "image/png", "synthetic-red-square.png");
    }

    private sealed class ImageOptionsHandler(
        string model, string operation, string resolution, string aspectRatio, ImageInput? source)
        : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        private int requestCount;
        public int RequestCount => requestCount;
        public int StatusCode { get; private set; }
        public List<string> ResponseImageHashes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(1, Interlocked.Increment(ref requestCount),
                "Retries or fallback requests are not allowed in this live regression test.");
            var uri = request.RequestUri!;
            Assert.AreEqual("https", uri.Scheme);
            Assert.AreEqual("generativelanguage.googleapis.com", uri.Host);
            Assert.AreEqual($"/v1/models/{model}:generateContent", uri.AbsolutePath);
            Assert.AreEqual(string.Empty, uri.Query, "Credentials must be kept out of the URL.");
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.IsTrue(request.Headers.TryGetValues("x-goog-api-key", out var keys) &&
                keys.Any(key => !string.IsNullOrWhiteSpace(key)), "The existing Key Vault API key must be sent in its header.");
            Assert.AreEqual("application/json", request.Content!.Headers.ContentType!.MediaType);
            var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            var config = body["generationConfig"]!.AsObject();
            CollectionAssert.AreEqual(new[] { "TEXT", "IMAGE" }, config["responseModalities"]!.AsArray()
                .Select(value => value!.GetValue<string>()).ToArray());
            var format = config["responseFormat"]!["image"]!.AsObject();
            Assert.AreEqual(3, format.Count);
            Assert.AreEqual("IMAGE_JPEG", format["mimeType"]!.GetValue<string>());
            Assert.AreEqual(resolution, format["imageSize"]!.GetValue<string>());
            Assert.AreEqual(aspectRatio, format["aspectRatio"]!.GetValue<string>());
            var contents = body["contents"]!.AsArray();
            Assert.AreEqual(1, contents.Count);
            Assert.AreEqual("user", contents[0]!["role"]!.GetValue<string>());
            var parts = contents[0]!["parts"]!.AsArray();
            Assert.AreEqual(source == null ? 1 : 2, parts.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(parts[0]!["text"]!.GetValue<string>()));
            if (source != null)
            {
                var inline = parts[1]!["inlineData"]!;
                Assert.AreEqual(source.MediaType, inline["mimeType"]!.GetValue<string>());
                Assert.AreEqual(Hash(source.Data), Hash(Convert.FromBase64String(inline["data"]!.GetValue<string>())));
            }

            var response = await base.SendAsync(request, cancellationToken);
            StatusCode = (int)response.StatusCode;
            Console.WriteLine("LIVE_GOOGLE_IMAGE_OPTIONS_REQUEST " + JsonSerializer.Serialize(new
            {
                model, operation, path = uri.AbsolutePath, statusCode = StatusCode,
                resolution, aspectRatio, mimeType = "IMAGE_JPEG", inputImages = source == null ? 0 : 1
            }));
            try
            {
                Assert.AreEqual(200, StatusCode, "The requested model and options must succeed without a retry or fallback.");
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                foreach (var candidate in document.RootElement.GetProperty("candidates").EnumerateArray())
                foreach (var part in candidate.GetProperty("content").GetProperty("parts").EnumerateArray())
                {
                    if (!part.TryGetProperty("inlineData", out var inline)) continue;
                    Assert.AreEqual("image/jpeg", inline.GetProperty("mimeType").GetString());
                    ResponseImageHashes.Add(Hash(Convert.FromBase64String(inline.GetProperty("data").GetString()!)));
                }
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
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
