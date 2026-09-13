using Mythosia.AI.Services.Base;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class ImageProviderCapabilitiesTests
{
    [TestMethod]
    [DataRow("Google")]
    [DataRow("xAI")]
    public void DefaultImageCapabilities_AreIndependentOfTheChatModelAndDoNotSendRequests(string provider)
    {
        using var fixture = new ImageFixture(provider);
        var chatModel = fixture.Service.Model;
        var capabilities = fixture.Service.GetImageCapabilities();

        Assert.AreEqual(fixture.Images.DefaultImageModel, capabilities.Model);
        Assert.AreEqual(provider, capabilities.Provider);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Generation);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Editing);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Mask);
        Assert.AreEqual(chatModel, fixture.Service.Model);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    [DataRow("Google", AIModels.Google.Images.Gemini3_1FlashImage)]
    [DataRow("Google", AIModels.Google.Images.Gemini3_1FlashLiteImage)]
    [DataRow("Google", AIModels.Google.Images.Gemini3ProImage)]
    [DataRow("xAI", AIModels.xAI.GrokImagineImage2_0)]
    public void KnownImageModels_ReportGenerationAndEditing(string provider, string model)
    {
        using var fixture = new ImageFixture(provider);
        var capabilities = fixture.Service.GetImageCapabilities(model);

        Assert.AreEqual(model, capabilities.Model);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Generation);
        Assert.AreEqual(CapabilitySupport.Supported, capabilities.Editing);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    [DataRow("Google", "gemini-3.1-flash-image-custom")]
    [DataRow("Google", "gemini-99-image")]
    [DataRow("Google", "my-image-deployment")]
    [DataRow("xAI", "grok-imagine-image-2.0-custom")]
    [DataRow("xAI", "grok-imagine-image-99")]
    [DataRow("xAI", "my-image-deployment")]
    public void UnknownImageModels_DoNotInheritSupportFromTheirNames(string provider, string model)
    {
        using var fixture = new ImageFixture(provider);
        var capabilities = fixture.Service.GetImageCapabilities(model);

        Assert.AreEqual(model, capabilities.Model);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Generation);
        Assert.AreEqual(CapabilitySupport.Unknown, capabilities.Editing);
        Assert.AreEqual(CapabilitySupport.Unsupported, capabilities.Mask);
        Assert.IsEmpty(capabilities.Qualities);
        Assert.IsEmpty(capabilities.Backgrounds);
        Assert.IsEmpty(capabilities.OutputFormats);
        Assert.IsEmpty(capabilities.SizeKinds);
        Assert.IsEmpty(capabilities.Resolutions);
        Assert.IsEmpty(capabilities.AspectRatios);
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    [DataRow("Google", false)]
    [DataRow("Google", true)]
    [DataRow("xAI", false)]
    [DataRow("xAI", true)]
    public async Task DeclaredOptionChoices_MatchActualGenerationAndEditValidation(string provider, bool editing)
    {
        using var fixture = new ImageFixture(provider);
        var capabilities = fixture.Service.GetImageCapabilities();

        foreach (var quality in Enum.GetValues<ImageQuality>())
        {
            var request = CreateRequest(editing);
            request.Quality = quality;
            await AssertRequestSupportAsync(fixture, request, capabilities.Qualities.Contains(quality), $"Quality {quality}");
        }
        foreach (var background in Enum.GetValues<ImageBackground>())
        {
            var request = CreateRequest(editing);
            request.Background = background;
            await AssertRequestSupportAsync(fixture, request, capabilities.Backgrounds.Contains(background), $"Background {background}");
        }
        foreach (var format in Enum.GetValues<ImageOutputFormat>())
        {
            var request = CreateRequest(editing);
            request.OutputFormat = format;
            await AssertRequestSupportAsync(fixture, request, capabilities.OutputFormats.Contains(format), $"Output format {format}");
        }
    }

    [TestMethod]
    [DataRow("Google", false)]
    [DataRow("Google", true)]
    [DataRow("xAI", false)]
    [DataRow("xAI", true)]
    public async Task DeclaredSizingChoices_MatchActualGenerationAndEditValidation(string provider, bool editing)
    {
        using var fixture = new ImageFixture(provider);
        var capabilities = fixture.Service.GetImageCapabilities();

        foreach (var size in new[] { ImageSize.Auto, ImageSize.Pixels(1024, 1024), ImageSize.Preset(ImageResolution.Auto) })
        {
            var request = CreateRequest(editing);
            request.Size = size;
            await AssertRequestSupportAsync(fixture, request, capabilities.SizeKinds.Contains(size.Kind), $"Size kind {size.Kind}");
        }
        foreach (var resolution in Enum.GetValues<ImageResolution>())
        {
            var request = CreateRequest(editing);
            request.Size = ImageSize.Preset(resolution);
            await AssertRequestSupportAsync(fixture, request, capabilities.Resolutions.Contains(resolution), $"Resolution {resolution}");
        }
        foreach (var ratio in Enum.GetValues<ImageAspectRatio>())
        {
            var request = CreateRequest(editing);
            request.Size = ImageSize.Preset(ImageResolution.Auto, ratio);
            await AssertRequestSupportAsync(fixture, request, capabilities.AspectRatios.Contains(ratio), $"Aspect ratio {ratio}");
        }
    }

    [TestMethod]
    public void GoogleAndGrokCapabilities_ExposeTheirDifferentAdapterChoices()
    {
        using var google = new ImageFixture("Google");
        using var grok = new ImageFixture("xAI");
        var googleCapabilities = google.Service.GetImageCapabilities();
        var grokCapabilities = grok.Service.GetImageCapabilities();

        CollectionAssert.AreEquivalent(new[] { ImageQuality.Auto }, googleCapabilities.Qualities.ToArray());
        CollectionAssert.AreEquivalent(new[] { ImageQuality.Auto, ImageQuality.Low, ImageQuality.Medium }, grokCapabilities.Qualities.ToArray());
        CollectionAssert.AreEquivalent(new[] { ImageOutputFormat.Auto, ImageOutputFormat.Jpeg }, googleCapabilities.OutputFormats.ToArray());
        CollectionAssert.AreEquivalent(new[] { ImageOutputFormat.Auto }, grokCapabilities.OutputFormats.ToArray());
        CollectionAssert.AreEquivalent(Enum.GetValues<ImageResolution>(), googleCapabilities.Resolutions.ToArray());
        CollectionAssert.AreEquivalent(new[] { ImageResolution.Auto, ImageResolution.OneK, ImageResolution.TwoK }, grokCapabilities.Resolutions.ToArray());
        Assert.IsTrue(googleCapabilities.AspectRatios.Contains(ImageAspectRatio.OneByEight));
        Assert.IsFalse(grokCapabilities.AspectRatios.Contains(ImageAspectRatio.OneByEight));
        Assert.IsFalse(googleCapabilities.AspectRatios.Contains(ImageAspectRatio.NineByTwenty));
        Assert.IsTrue(grokCapabilities.AspectRatios.Contains(ImageAspectRatio.NineByTwenty));
    }

    [TestMethod]
    [DataRow("Google", 1)]
    [DataRow("xAI", 10)]
    public async Task DeclaredMaximumCount_MatchesTheAcceptedBoundary(string provider, int expectedMaximum)
    {
        using var fixture = new ImageFixture(provider);
        Assert.AreEqual(expectedMaximum, fixture.Service.GetImageCapabilities().MaxImages);
        await fixture.Images.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "Draw a tree", Count = expectedMaximum });
        var sent = fixture.Handler.RequestCount;
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Images.GenerateImagesAsync(
            new ImageGenerationRequest { Prompt = "Draw a tree", Count = expectedMaximum + 1 }));
        Assert.AreEqual(sent, fixture.Handler.RequestCount);
    }

    [TestMethod]
    public async Task GrokMaximumInputImages_MatchesTheAcceptedBoundary()
    {
        using var fixture = new ImageFixture("xAI");
        var capabilities = fixture.Service.GetImageCapabilities();
        Assert.AreEqual(5, capabilities.MaxInputImages);
        var request = new ImageEditRequest
        {
            Prompt = "Combine these images",
            InputImages = Enumerable.Range(0, 5).Select(_ => NewImage()).ToArray()
        };
        await fixture.Images.EditImagesAsync(request);
        var sent = fixture.Handler.RequestCount;
        request.InputImages = Enumerable.Range(0, 6).Select(_ => NewImage()).ToArray();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Images.EditImagesAsync(request));
        Assert.AreEqual(sent, fixture.Handler.RequestCount);
    }

    [TestMethod]
    public void GoogleDoesNotInventAMaximumReferenceCount()
    {
        using var fixture = new ImageFixture("Google");
        Assert.IsNull(fixture.Service.GetImageCapabilities().MaxInputImages);
    }

    [TestMethod]
    [DataRow("Google")]
    [DataRow("xAI")]
    public async Task UnsupportedMask_IsRejectedBeforeSending(string provider)
    {
        using var fixture = new ImageFixture(provider);
        Assert.AreEqual(CapabilitySupport.Unsupported, fixture.Service.GetImageCapabilities().Mask);
        var request = (ImageEditRequest)CreateRequest(true);
        request.Mask = NewImage();
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Images.EditImagesAsync(request));
        Assert.AreEqual(0, fixture.Handler.RequestCount);
    }

    [TestMethod]
    [DataRow("Google")]
    [DataRow("xAI")]
    public async Task UnknownCapabilityMetadata_DoesNotBlockCustomModelRequests(string provider)
    {
        using var fixture = new ImageFixture(provider);
        const string customModel = "custom-image-deployment";
        Assert.AreEqual(CapabilitySupport.Unknown, fixture.Service.GetImageCapabilities(customModel).Generation);
        await fixture.Images.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "Draw a tree", Model = customModel });
        Assert.AreEqual(1, fixture.Handler.RequestCount);
        Assert.IsTrue(fixture.Handler.LastUri!.AbsolutePath.Contains(customModel) || fixture.Handler.LastBody!.Contains(customModel));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Images.GenerateImagesAsync(
            new ImageGenerationRequest { Prompt = "Draw a tree", Model = customModel, Quality = ImageQuality.High }));
        Assert.AreEqual(1, fixture.Handler.RequestCount, "Unknown metadata must preserve the adapter's existing validation.");
    }

    private static async Task AssertRequestSupportAsync(ImageFixture fixture, ImageGenerationRequest request, bool supported, string option)
    {
        var sent = fixture.Handler.RequestCount;
        Task<ImageGenerationResult> Send() => request is ImageEditRequest edit
            ? fixture.Images.EditImagesAsync(edit)
            : fixture.Images.GenerateImagesAsync(request);
        if (supported)
        {
            await Send();
            Assert.AreEqual(sent + 1, fixture.Handler.RequestCount, option);
        }
        else
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(Send, option);
            Assert.AreEqual(sent, fixture.Handler.RequestCount, option);
        }
    }

    private static ImageGenerationRequest CreateRequest(bool editing) => editing
        ? new ImageEditRequest { Prompt = "Draw a tree", InputImages = new[] { NewImage() } }
        : new ImageGenerationRequest { Prompt = "Draw a tree" };

    private static ImageInput NewImage() => new ImageInput(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, "image/png");

    private sealed class ImageFixture : IDisposable
    {
        private readonly HttpClient _client;
        public ImageResponseHandler Handler { get; }
        public AIService Service { get; }
        public IImageGenerationService Images => (IImageGenerationService)Service;

        public ImageFixture(string provider)
        {
            Handler = new ImageResponseHandler(provider);
            _client = new HttpClient(Handler);
            Service = provider == "Google"
                ? new GoogleAIService("offline-test-key", _client)
                : new XAIService("offline-test-key", _client);
        }

        public void Dispose() => _client.Dispose();
    }

    private sealed class ImageResponseHandler(string provider) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastUri = request.RequestUri;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var base64 = Convert.ToBase64String(NewImage().Data);
            var json = provider == "Google"
                ? "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"" + base64 + "\"}}]},\"finishReason\":\"STOP\"}]}"
                : "{\"data\":[{\"b64_json\":\"" + base64 + "\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
