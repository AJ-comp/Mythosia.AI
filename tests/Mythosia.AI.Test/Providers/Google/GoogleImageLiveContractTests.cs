using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Google;
using Mythosia.Azure;
using SkiaSharp;

namespace Mythosia.AI.Tests.Google;

[TestClass]
[TestCategory("Live")]
[TestCategory("Google")]
[TestCategory("GoogleImage")]
[DoNotParallelize]
public class GoogleImageLiveContractTests
{
    private const string VaultUri = "https://mythosia-key-vault.vault.azure.net/";
    private const string SecretName = "gemini-secret";
    private const string ImageModel = AIModels.Google.Images.Gemini3_1FlashLiteImage;

    private static readonly byte[] JpegStartSignature =
    {
        0xFF, 0xD8, 0xFF
    };

    private static readonly byte[] JpegEndSignature = { 0xFF, 0xD9 };

    private static string? apiKey;
    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        apiKey = await new SecretFetcher(VaultUri, SecretName).GetKeyValueAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        apiKey = null;
    }

    [TestMethod]
    public async Task GeminiImage_GeneratesJpeg()
    {
        using var httpClient = CreateHttpClient();
        var imageService = CreateImageService(httpClient);
        var result = await imageService.GenerateImagesAsync(new ImageGenerationRequest
        {
            Model = ImageModel,
            Prompt = "A single solid blue circle centered on a plain white background.",
            Count = 1,
            Size = ImageSize.Preset(ImageResolution.OneK),
            OutputFormat = ImageOutputFormat.Jpeg
        });

        AssertJpegResult(result);
    }

    [TestMethod]
    public async Task GeminiImage_EditsReferenceImageAsJpeg()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "test_image.png");
        var source = await File.ReadAllBytesAsync(sourcePath);
        using var httpClient = CreateHttpClient();
        var imageService = CreateImageService(httpClient);

        var result = await imageService.EditImagesAsync(new ImageEditRequest
        {
            Model = ImageModel,
            Prompt = "Preserve the image and add one small blue circle in the top-left corner.",
            InputImages = new[]
            {
                new ImageInput(source, "image/png", "test_image.png")
            },
            Count = 1,
            Size = ImageSize.Preset(ImageResolution.OneK),
            OutputFormat = ImageOutputFormat.Jpeg
        });

        AssertJpegResult(result);
    }

    [TestMethod]
    public async Task GeminiImage_MapsExplicitAspectRatio()
    {
        using var httpClient = CreateHttpClient();
        var imageService = CreateImageService(httpClient);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await imageService.GenerateImagesAsync(new ImageGenerationRequest
        {
            Model = ImageModel,
            Prompt = "A wide flat illustration with a red square on the left and a blue circle on the right, plain white background, no text.",
            Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
            OutputFormat = ImageOutputFormat.Jpeg
        }, timeout.Token);

        AssertJpegResult(result);
        using var encoded = SKData.CreateCopy(result.Images[0].Data);
        using var codec = SKCodec.Create(encoded);
        Assert.IsNotNull(codec);
        Assert.AreEqual(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        using var decoded = new SKBitmap(codec.Info);
        Assert.AreEqual(SKCodecResult.Success, codec.GetPixels(decoded.Info, decoded.GetPixels()));
        Assert.AreEqual(16.0 / 9.0, (double)decoded.Width / decoded.Height, 0.05);
        Assert.IsTrue(decoded.Width >= 1024 && decoded.Height >= 512);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Mythosia.AI.slnx")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        var outputDirectory = Path.Combine(directory.FullName, "artifacts", "test-results", "google-image-aspect-ratio-live");
        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, "gemini-wide-" + Guid.NewGuid().ToString("N") + ".jpg");
        await File.WriteAllBytesAsync(output, result.Images[0].Data);
        Console.WriteLine($"LIVE_GOOGLE_IMAGE_ASPECT_RATIO_OK model={result.Model} width={decoded.Width} height={decoded.Height} mime={result.Images[0].MediaType} artifact={output}");
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    private static IImageGenerationService CreateImageService(HttpClient httpClient)
    {
        Assert.IsNotNull(apiKey);
        return new GoogleAIService(apiKey, httpClient);
    }

    private static void AssertJpegResult(ImageGenerationResult result)
    {
        Assert.AreEqual("Google", result.Provider);
        Assert.AreEqual(ImageModel, result.Model);
        Assert.AreEqual(1, result.Images.Count);

        var image = result.Images.Single();
        Assert.AreEqual("image/jpeg", image.MediaType);
        Assert.IsTrue(image.Data.Length > JpegStartSignature.Length + JpegEndSignature.Length);
        CollectionAssert.AreEqual(
            JpegStartSignature,
            image.Data.Take(JpegStartSignature.Length).ToArray());
        CollectionAssert.AreEqual(
            JpegEndSignature,
            image.Data.TakeLast(JpegEndSignature.Length).ToArray());
    }
}
