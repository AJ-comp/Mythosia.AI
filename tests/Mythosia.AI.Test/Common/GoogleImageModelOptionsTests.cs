using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class GoogleImageModelOptionsTests
{
    private static readonly ImageAspectRatio[] StandardRatios =
    {
        ImageAspectRatio.Auto, ImageAspectRatio.OneByOne, ImageAspectRatio.TwoByThree,
        ImageAspectRatio.ThreeByTwo, ImageAspectRatio.ThreeByFour, ImageAspectRatio.FourByThree,
        ImageAspectRatio.FourByFive, ImageAspectRatio.FiveByFour, ImageAspectRatio.NineBySixteen,
        ImageAspectRatio.SixteenByNine, ImageAspectRatio.TwentyOneByNine
    };

    private static readonly ImageAspectRatio[] ExtendedRatios = StandardRatios.Concat(new[]
    {
        ImageAspectRatio.OneByEight, ImageAspectRatio.EightByOne,
        ImageAspectRatio.OneByFour, ImageAspectRatio.FourByOne
    }).ToArray();

    [TestMethod]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashImage)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage)]
    public void KnownModels_ExposeTheirExactSizingChoicesIgnoringModelCasing(string model)
    {
        using var fixture = new ImageFixture();
        foreach (var selectedModel in new[] { model, model.ToUpperInvariant() })
        {
            var capabilities = fixture.Service.GetImageCapabilities(selectedModel);
            Assert.AreEqual(selectedModel, capabilities.Model);
            Assert.AreEqual("Google", capabilities.Provider);
            Assert.AreEqual(CapabilitySupport.Supported, capabilities.Generation);
            Assert.AreEqual(CapabilitySupport.Supported, capabilities.Editing);
            CollectionAssert.AreEquivalent(ExpectedResolutions(model), capabilities.Resolutions.ToArray());
            CollectionAssert.AreEquivalent(ExpectedRatios(model), capabilities.AspectRatios.ToArray());
            CollectionAssert.AreEquivalent(new[] { ImageSizeKind.Auto, ImageSizeKind.Preset }, capabilities.SizeKinds.ToArray());
        }
        Assert.AreEqual(0, fixture.Handler.RequestCount, "Inspecting model choices must not send HTTP requests.");
    }

    [TestMethod]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashImage, false)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashImage, true)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, false)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, true)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, false)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, true)]
    public async Task KnownModels_AdvertisedSizingChoicesMatchGenerationAndEditing(string model, bool editing)
    {
        using var fixture = new ImageFixture();
        var capabilities = fixture.Service.GetImageCapabilities(model);
        foreach (var resolution in Enum.GetValues<ImageResolution>())
        {
            var request = CreateRequest(model, editing, ImageSize.Preset(resolution));
            await AssertRequestSupportAsync(fixture, request, capabilities.Resolutions.Contains(resolution), $"{model}: {resolution}");
        }
        foreach (var ratio in Enum.GetValues<ImageAspectRatio>())
        {
            var request = CreateRequest(model, editing, ImageSize.Preset(ImageResolution.OneK, ratio));
            await AssertRequestSupportAsync(fixture, request, capabilities.AspectRatios.Contains(ratio), $"{model}: {ratio}");
        }
    }

    [TestMethod]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, ImageResolution.FiveTwelve, ImageAspectRatio.Auto)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, ImageResolution.TwoK, ImageAspectRatio.Auto)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, ImageResolution.FourK, ImageAspectRatio.Auto)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.FiveTwelve, ImageAspectRatio.Auto)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.OneK, ImageAspectRatio.OneByEight)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.OneK, ImageAspectRatio.EightByOne)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.OneK, ImageAspectRatio.OneByFour)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.OneK, ImageAspectRatio.FourByOne)]
    public async Task ModelSpecificUnsupportedSizes_ThrowBeforeHttpWithoutSubstitution(
        string model, ImageResolution resolution, ImageAspectRatio ratio)
    {
        using var fixture = new ImageFixture();
        foreach (var selectedModel in new[] { model, model.ToUpperInvariant() })
        foreach (var editing in new[] { false, true })
        {
            var size = ImageSize.Preset(resolution, ratio);
            var request = CreateRequest(selectedModel, editing, size);
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.SendAsync(request));
            Assert.AreSame(size, request.Size, "An unsupported preset must not be replaced with another size.");
            Assert.AreEqual(0, fixture.Handler.RequestCount, "Model-specific rejection must happen before HTTP.");
        }
    }

    [TestMethod]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashImage, ImageResolution.FiveTwelve, ImageAspectRatio.OneByFour, "IMAGE_SIZE_FIVE_TWELVE", "ASPECT_RATIO_ONE_BY_FOUR")]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage, ImageResolution.OneK, ImageAspectRatio.OneByEight, "IMAGE_SIZE_ONE_K", "ASPECT_RATIO_ONE_BY_EIGHT")]
    [DataRow(AIModels.Google.Images.Gemini3ProImage, ImageResolution.FourK, ImageAspectRatio.TwentyOneByNine, "IMAGE_SIZE_FOUR_K", "ASPECT_RATIO_TWENTY_ONE_BY_NINE")]
    public async Task SupportedModelSpecificPresets_PreserveWireValuesAndSelectedEndpoint(
        string model, ImageResolution resolution, ImageAspectRatio ratio, string expectedResolution, string expectedRatio)
    {
        using var fixture = new ImageFixture();
        foreach (var selectedModel in new[] { model, model.ToUpperInvariant() })
        foreach (var editing in new[] { false, true })
        {
            var size = ImageSize.Preset(resolution, ratio);
            var request = CreateRequest(selectedModel, editing, size);
            var result = await fixture.SendAsync(request);
            Assert.AreEqual(selectedModel, result.Model);
            Assert.AreSame(size, request.Size);
            AssertSelectedEndpoint(fixture, selectedModel);
            AssertWirePreset(fixture, expectedResolution, expectedRatio);
        }
    }

    [TestMethod]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashImage)]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage)]
    [DataRow(AIModels.Google.Images.Gemini3ProImage)]
    public async Task AllModels_AutomaticSizeAndAutomaticPresetOmitImageFormat(string model)
    {
        using var fixture = new ImageFixture();
        foreach (var editing in new[] { false, true })
        foreach (var size in new[] { ImageSize.Auto, ImageSize.Preset(ImageResolution.Auto) })
        {
            await fixture.SendAsync(CreateRequest(model, editing, size));
            AssertSelectedEndpoint(fixture, model);
            using var json = JsonDocument.Parse(fixture.Handler.LastBody!);
            Assert.IsFalse(json.RootElement.GetProperty("generationConfig").TryGetProperty("responseFormat", out _),
                "Automatic options must stay omitted rather than being rewritten to an explicit model default.");
        }
    }

    [TestMethod]
    public async Task OmittedModel_StillUsesFlashSizingRegardlessOfTheChatModel()
    {
        using var fixture = new ImageFixture();
        fixture.Service.ChangeModel(AIModels.Google.Images.Gemini3_1FlashLiteImage);
        Assert.AreEqual(AIModels.Google.Images.Gemini3_1FlashImage, fixture.Service.DefaultImageModel);
        var capabilities = fixture.Service.GetImageCapabilities();
        CollectionAssert.AreEquivalent(ExpectedResolutions(AIModels.Google.Images.Gemini3_1FlashImage), capabilities.Resolutions.ToArray());
        CollectionAssert.AreEquivalent(ExtendedRatios, capabilities.AspectRatios.ToArray());

        foreach (var editing in new[] { false, true })
        {
            await fixture.SendAsync(CreateRequest(null, editing, ImageSize.Preset(ImageResolution.FiveTwelve, ImageAspectRatio.EightByOne)));
            AssertSelectedEndpoint(fixture, AIModels.Google.Images.Gemini3_1FlashImage);
            AssertWirePreset(fixture, "IMAGE_SIZE_FIVE_TWELVE", "ASPECT_RATIO_EIGHT_BY_ONE");
        }
        Assert.AreEqual(AIModels.Google.Images.Gemini3_1FlashLiteImage, fixture.Service.Model);
    }

    [TestMethod]
    [DataRow("custom-image-deployment")]
    [DataRow(AIModels.Google.Images.Gemini3_1FlashLiteImage + "-custom")]
    [DataRow(AIModels.Google.Images.Gemini3ProImage + "-custom")]
    public async Task UnknownModels_KeepUnknownCapabilitiesAndProviderWidePresetPassThrough(string model)
    {
        using var fixture = new ImageFixture();
        var capabilities = fixture.Service.GetImageCapabilities(model);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Generation);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Editing);
        Assert.IsEmpty(capabilities.Resolutions);
        Assert.IsEmpty(capabilities.AspectRatios);

        foreach (var editing in new[] { false, true })
        {
            await fixture.SendAsync(CreateRequest(model, editing, ImageSize.Preset(ImageResolution.FiveTwelve, ImageAspectRatio.OneByFour)));
            AssertSelectedEndpoint(fixture, model);
            AssertWirePreset(fixture, "IMAGE_SIZE_FIVE_TWELVE", "ASPECT_RATIO_ONE_BY_FOUR");
            await fixture.SendAsync(CreateRequest(model, editing, ImageSize.Preset(ImageResolution.FourK, ImageAspectRatio.OneByEight)));
            AssertSelectedEndpoint(fixture, model);
            AssertWirePreset(fixture, "IMAGE_SIZE_FOUR_K", "ASPECT_RATIO_ONE_BY_EIGHT");

            var sent = fixture.Handler.RequestCount;
            var unsupported = CreateRequest(model, editing, ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.NineByTwenty));
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.SendAsync(unsupported));
            Assert.AreEqual(sent, fixture.Handler.RequestCount, "Unknown model metadata must retain provider-wide validation.");
        }
    }

    [TestMethod]
    public void ModelCapabilities_AreImmutableIndependentSnapshots()
    {
        using var fixture = new ImageFixture();
        var lite = fixture.Service.GetImageCapabilities(AIModels.Google.Images.Gemini3_1FlashLiteImage);
        var pro = fixture.Service.GetImageCapabilities(AIModels.Google.Images.Gemini3ProImage);
        var flash = fixture.Service.GetImageCapabilities();
        AssertReadOnly(lite.Resolutions, ImageResolution.FourK);
        AssertReadOnly(lite.AspectRatios, ImageAspectRatio.NineByTwenty);
        AssertReadOnly(pro.Resolutions, ImageResolution.FiveTwelve);
        AssertReadOnly(pro.AspectRatios, ImageAspectRatio.OneByEight);

        fixture.Service.ChangeModel(AIModels.Google.Gemini3_6Flash);
        _ = fixture.Service.GetImageCapabilities("custom-image-deployment");
        CollectionAssert.AreEquivalent(ExpectedResolutions(AIModels.Google.Images.Gemini3_1FlashLiteImage), lite.Resolutions.ToArray());
        CollectionAssert.AreEquivalent(ExtendedRatios, lite.AspectRatios.ToArray());
        CollectionAssert.AreEquivalent(ExpectedResolutions(AIModels.Google.Images.Gemini3ProImage), pro.Resolutions.ToArray());
        CollectionAssert.AreEquivalent(StandardRatios, pro.AspectRatios.ToArray());
        CollectionAssert.AreEquivalent(ExpectedResolutions(AIModels.Google.Images.Gemini3_1FlashImage), flash.Resolutions.ToArray());
        CollectionAssert.AreEquivalent(ExtendedRatios, flash.AspectRatios.ToArray());
        Assert.AreEqual(AIModels.Google.Images.Gemini3_1FlashLiteImage, lite.Model);
        Assert.AreEqual(AIModels.Google.Images.Gemini3ProImage, pro.Model);
        Assert.AreEqual(AIModels.Google.Images.Gemini3_1FlashImage, flash.Model);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    private static ImageResolution[] ExpectedResolutions(string model) => model switch
    {
        AIModels.Google.Images.Gemini3_1FlashLiteImage => new[] { ImageResolution.Auto, ImageResolution.OneK },
        AIModels.Google.Images.Gemini3ProImage => new[] { ImageResolution.Auto, ImageResolution.OneK, ImageResolution.TwoK, ImageResolution.FourK },
        _ => new[] { ImageResolution.Auto, ImageResolution.FiveTwelve, ImageResolution.OneK, ImageResolution.TwoK, ImageResolution.FourK }
    };

    private static ImageAspectRatio[] ExpectedRatios(string model) =>
        model == AIModels.Google.Images.Gemini3ProImage ? StandardRatios : ExtendedRatios;

    private static void AssertReadOnly<T>(IReadOnlyList<T> values, T unsupported)
    {
        if (values is IList<T> mutable)
            Assert.ThrowsExactly<NotSupportedException>(() => mutable[0] = unsupported);
    }

    private static async Task AssertRequestSupportAsync(ImageFixture fixture, ImageGenerationRequest request, bool supported, string option)
    {
        var sent = fixture.Handler.RequestCount;
        if (supported)
        {
            await fixture.SendAsync(request);
            Assert.AreEqual(sent + 1, fixture.Handler.RequestCount, option);
            AssertSelectedEndpoint(fixture, request.Model!);
        }
        else
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.SendAsync(request), option);
            Assert.AreEqual(sent, fixture.Handler.RequestCount, option);
        }
    }

    private static void AssertSelectedEndpoint(ImageFixture fixture, string model) =>
        Assert.AreEqual($"/v1/models/{model}:generateContent", fixture.Handler.LastUri!.AbsolutePath);

    private static void AssertWirePreset(ImageFixture fixture, string resolution, string ratio)
    {
        using var json = JsonDocument.Parse(fixture.Handler.LastBody!);
        var image = json.RootElement.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("image");
        Assert.AreEqual(resolution, image.GetProperty("imageSize").GetString());
        Assert.AreEqual(ratio, image.GetProperty("aspectRatio").GetString());
    }

    private static ImageGenerationRequest CreateRequest(string? model, bool editing, ImageSize size)
    {
        ImageGenerationRequest request = editing
            ? new ImageEditRequest { InputImages = new[] { new ImageInput(new byte[] { 1, 2, 3 }, "image/png") } }
            : new ImageGenerationRequest();
        request.Prompt = "Draw a tree";
        request.Model = model;
        request.Size = size;
        return request;
    }

    private sealed class ImageFixture : IDisposable
    {
        private readonly HttpClient _client;
        public CaptureHandler Handler { get; } = new();
        public GoogleAIService Service { get; }

        public ImageFixture()
        {
            _client = new HttpClient(Handler);
            Service = new GoogleAIService("offline-test-key", _client);
        }

        public Task<ImageGenerationResult> SendAsync(ImageGenerationRequest request) => request is ImageEditRequest edit
            ? Service.EditImagesAsync(edit)
            : Service.GenerateImagesAsync(request);

        public void Dispose() => _client.Dispose();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastUri = request.RequestUri;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            const string response = "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AQID\"}}]},\"finishReason\":\"STOP\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
