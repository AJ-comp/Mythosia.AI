using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class OpenAIImage25ContractTests
{
    private static readonly (string Model, string Wire)[] Models =
    [
        (AIModels.OpenAI.GptImage2_5Sunburst, "gpt-image-2.5-sunburst"),
        (AIModels.OpenAI.GptImage2_5Sunburst_260908, "gpt-image-2.5-sunburst-2026-09-08"),
        (AIModels.OpenAI.GptImage2_5Flare, "gpt-image-2.5-flare"),
        (AIModels.OpenAI.GptImage2_5Flare_260908, "gpt-image-2.5-flare-2026-09-08")
    ];

    public static IEnumerable<object[]> ModelQualityCases => Models.SelectMany(model =>
        Enum.GetValues<ImageQuality>().SelectMany(quality =>
            new[] { false, true }.Select(editing => new object[] { model.Model, model.Wire, quality, editing })));

    [TestMethod]
    [DynamicData(nameof(ModelQualityCases))]
    public async Task BothEndpoints_PreserveAllModelAliasesSnapshotsAndQualitySettings(string model, string wire, ImageQuality quality, bool editing)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var service = new OpenAIService("offline-key", AIModels.OpenAI.Gpt5_6Sol, http);
        service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Existing synthetic conversation."));
        var before = JsonSerializer.Serialize(service.ActivateChat);
        var request = MakeRequest(editing, model);
        request.Quality = quality;
        request.Count = 2;
        var result = await SendAsync(service, request);
        var sent = handler.Requests.Single();
        Assert.AreEqual(wire, sent.Fields["model"]);
        Assert.AreEqual(quality.ToString().ToLowerInvariant(), sent.Fields["quality"]);
        Assert.AreEqual("2", sent.Fields["n"]);
        Assert.AreEqual("1024x1024", sent.Fields["size"]);
        Assert.AreEqual("png", sent.Fields["output_format"]);
        Assert.AreEqual("auto", sent.Fields["background"]);
        Assert.AreEqual("Draw one synthetic red square.", sent.Fields["prompt"]);
        Assert.AreEqual(editing ? "/v1/images/edits" : "/v1/images/generations", sent.Path);
        Assert.AreEqual(editing ? "multipart/form-data" : "application/json", sent.ContentType);
        Assert.AreEqual("POST", sent.Method);
        Assert.AreEqual("Bearer", sent.AuthenticationScheme);
        Assert.IsFalse(sent.Fields.ContainsKey("response_format"));
        Assert.IsFalse(sent.Fields.ContainsKey("messages"));
        Assert.IsFalse(sent.Fields.ContainsKey("output_compression"));
        Assert.AreEqual(wire, result.Model);
        Assert.AreEqual("OpenAI", result.Provider);
        Assert.AreEqual("req_image25_offline", result.RequestId);
        Assert.HasCount(2, result.Images);
        Assert.AreEqual(12, result.Usage!.InputTokens);
        Assert.AreEqual(24, result.Usage.OutputTokens);
        Assert.AreEqual(36, result.Usage.TotalTokens);
        Assert.IsTrue(result.Images.All(image => image.MediaType == "image/png"));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Images[0].Data);
        Assert.AreEqual(before, JsonSerializer.Serialize(service.ActivateChat));
        Assert.AreEqual(AIModels.OpenAI.Gpt5_6Sol, service.Model);
        Assert.AreEqual(AIModels.OpenAI.GptImage2, service.DefaultImageModel);
    }

    public static IEnumerable<object[]> ValidPixelSizes =>
        new[] { (1024, 640), (640, 1024), (1440, 480), (480, 1440), (1536, 864), (3840, 2160), (2160, 3840) }
            .SelectMany(size => new[] { false, true }.Select(editing => new object[] { size.Item1, size.Item2, editing }));

    [TestMethod]
    [DynamicData(nameof(ValidPixelSizes))]
    public async Task CustomSize_InclusivePixelRatioAndEdgeBoundariesReachBothEndpoints(int width, int height, bool editing)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = MakeRequest(editing);
        request.Size = ImageSize.Pixels(width, height);
        await SendAsync(new OpenAIService("key", http), request);
        Assert.AreEqual($"{width}x{height}", handler.Requests.Single().Fields["size"]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AutomaticOptions_SendAutomaticSizingQualityBackgroundAndProviderDefaultPng(bool editing)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = MakeRequest(editing);
        request.Size = ImageSize.Auto;
        request.Quality = ImageQuality.Auto;
        request.Background = ImageBackground.Auto;
        request.OutputFormat = ImageOutputFormat.Auto;
        var result = await SendAsync(new OpenAIService("key", http), request);
        var sent = handler.Requests.Single();
        Assert.AreEqual("auto", sent.Fields["size"]);
        Assert.AreEqual("auto", sent.Fields["quality"]);
        Assert.AreEqual("auto", sent.Fields["background"]);
        Assert.AreEqual("png", sent.Fields["output_format"]);
        Assert.AreEqual("image/png", result.Images.Single().MediaType);
    }

    public static IEnumerable<object[]> UnsupportedPresetCases => Enum.GetValues<ImageResolution>()
        .SelectMany(resolution => new[] { ImageAspectRatio.Auto, ImageAspectRatio.ThreeByTwo }.SelectMany(ratio =>
            new[] { false, true }.Select(editing => new object[] { resolution, ratio, editing })));

    [TestMethod]
    [DynamicData(nameof(UnsupportedPresetCases))]
    public async Task ResolutionPresets_AreNeverSilentlyConvertedToPixels(ImageResolution resolution, ImageAspectRatio ratio, bool editing)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = MakeRequest(editing);
        request.Size = ImageSize.Preset(resolution, ratio);
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => SendAsync(new OpenAIService("key", http), request));
        Assert.IsEmpty(handler.Requests);
    }

    public static IEnumerable<object[]> LegacyModelQualityCases =>
        new[] { AIModels.OpenAI.GptImage2, AIModels.OpenAI.GptImage2_260421 }.SelectMany(model =>
            Enum.GetValues<ImageQuality>().SelectMany(quality =>
                new[] { false, true }.Select(editing => new object[] { model, quality, editing })));

    [TestMethod]
    [DynamicData(nameof(LegacyModelQualityCases))]
    public async Task Image2Aliases_KeepTheirSupportedQualityRange(string model, ImageQuality quality, bool editing)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = MakeRequest(editing, model);
        request.Quality = quality;
        if (quality is ImageQuality.XHigh or ImageQuality.Max)
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => SendAsync(new OpenAIService("key", http), request));
            Assert.IsEmpty(handler.Requests);
        }
        else
        {
            await SendAsync(new OpenAIService("key", http), request);
            Assert.AreEqual(quality.ToString().ToLowerInvariant(), handler.Requests.Single().Fields["quality"]);
        }
    }

    public static IEnumerable<object[]> InvalidTypedOptions =>
        new[] { AIModels.OpenAI.GptImage2, AIModels.OpenAI.GptImage2_5Sunburst, "custom-image-deployment" }.SelectMany(model =>
            new[] { "quality-negative", "quality-undefined", "format-negative", "format-undefined", "background-negative", "background-undefined", "size-null" }
                .SelectMany(option => new[] { false, true }.Select(editing => new object[] { model, option, editing })));

    [TestMethod]
    [DynamicData(nameof(InvalidTypedOptions))]
    public async Task InvalidEnumsAndNullSize_AreRejectedForBuiltInAndCustomModelsBeforeHttp(string model, string option, bool editing)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = MakeRequest(editing, model);
        switch (option)
        {
            case "quality-negative": request.Quality = (ImageQuality)(-1); break;
            case "quality-undefined": request.Quality = (ImageQuality)999; break;
            case "format-negative": request.OutputFormat = (ImageOutputFormat)(-1); break;
            case "format-undefined": request.OutputFormat = (ImageOutputFormat)999; break;
            case "background-negative": request.Background = (ImageBackground)(-1); break;
            case "background-undefined": request.Background = (ImageBackground)999; break;
            case "size-null": request.Size = null!; break;
            default: Assert.Fail("Unknown invalid option."); break;
        }
        if (option == "size-null")
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => SendAsync(new OpenAIService("key", http), request));
        else
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => SendAsync(new OpenAIService("key", http), request));
        Assert.IsEmpty(handler.Requests);
    }

    public static IEnumerable<object[]> InvalidSettings =>
        new[] { "size-null", "size-grid", "size-ratio", "size-pixels-low", "size-pixels-high",
            "size-edge", "size-overflow", "quality", "format", "count-zero", "count-high", "compression-low",
            "compression-high", "compression-png", "transparent-jpeg", "background" }
        .SelectMany(scenario => new[] { false, true }.Select(editing => new object[] { scenario, editing }));

    [TestMethod]
    [DynamicData(nameof(InvalidSettings))]
    public async Task InvalidModel25Settings_FailBeforeHttpForGenerationAndEditing(string scenario, bool editing)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = MakeRequest(editing);
        switch (scenario)
        {
            case "size-null": request.Size = null!; break;
            case "size-grid": request.Size = ImageSize.Pixels(1025, 1024); break;
            case "size-ratio": request.Size = ImageSize.Pixels(1536, 496); break;
            case "size-pixels-low": request.Size = ImageSize.Pixels(1024, 624); break;
            case "size-pixels-high": request.Size = ImageSize.Pixels(3840, 2176); break;
            case "size-edge": request.Size = ImageSize.Pixels(3856, 1280); break;
            case "size-overflow": request.Size = ImageSize.Pixels(int.MaxValue, int.MaxValue); break;
            case "quality": request.Quality = (ImageQuality)999; break;
            case "format": request.OutputFormat = (ImageOutputFormat)999; break;
            case "count-zero": request.Count = 0; break;
            case "count-high": request.Count = 11; break;
            case "compression-low": request.OutputFormat = ImageOutputFormat.Jpeg; request.OutputCompression = -1; break;
            case "compression-high": request.OutputFormat = ImageOutputFormat.WebP; request.OutputCompression = 101; break;
            case "compression-png": request.OutputCompression = 50; break;
            case "transparent-jpeg": request.OutputFormat = ImageOutputFormat.Jpeg; request.Background = ImageBackground.Transparent; break;
            case "background": request.Background = (ImageBackground)999; break;
            default: Assert.Fail("Unhandled invalid-settings scenario."); break;
        }
        await Assert.ThrowsAsync<ArgumentException>(() => SendAsync(new OpenAIService("key", http), request));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    [DataRow(ImageOutputFormat.Png, "png", ImageBackground.Transparent, "transparent", null)]
    [DataRow(ImageOutputFormat.WebP, "webp", ImageBackground.Transparent, "transparent", 0)]
    [DataRow(ImageOutputFormat.WebP, "webp", ImageBackground.Transparent, "transparent", 100)]
    [DataRow(ImageOutputFormat.Jpeg, "jpeg", ImageBackground.Opaque, "opaque", 0)]
    [DataRow(ImageOutputFormat.Jpeg, "jpeg", ImageBackground.Auto, "auto", 100)]
    public async Task OutputFormatsTransparencyAndCompression_AreSerializedOnBothEndpoints(
        ImageOutputFormat format, string wireFormat, ImageBackground background, string wireBackground, int? compression)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var service = new OpenAIService("key", http);
        foreach (var editing in new[] { false, true })
        {
            var request = MakeRequest(editing);
            request.OutputFormat = format;
            request.Background = background;
            request.OutputCompression = compression;
            var result = await SendAsync(service, request);
            var sent = handler.Requests.Last();
            Assert.AreEqual(wireFormat, sent.Fields["output_format"]);
            Assert.AreEqual(wireBackground, sent.Fields["background"]);
            Assert.AreEqual(compression?.ToString(System.Globalization.CultureInfo.InvariantCulture), sent.Fields.GetValueOrDefault("output_compression"));
            Assert.AreEqual("image/" + wireFormat, result.Images.Single().MediaType);
        }
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.GptImage2_5Sunburst)]
    [DataRow(AIModels.OpenAI.GptImage2_5Flare)]
    public async Task MultipartEdit_PreservesReferenceOrderMimeFilenamesAndMaskBytes(string model)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var inputs = new[] { new ImageInput([1, 2, 3], "image/png", "first.png"),
            new ImageInput([4, 5], "image/jpeg", "second.jpg"), new ImageInput([6, 7, 8, 9], "image/webp", "third.webp") };
        var mask = new ImageInput([10, 11, 12], "image/png", "selection-mask.png");
        await new OpenAIService("key", http).EditImagesAsync(new ImageEditRequest
        {
            Model = model, Prompt = "Preserve ordered synthetic references.", InputImages = inputs, Mask = mask,
            Size = ImageSize.Pixels(1536, 864), Quality = ImageQuality.Max, OutputFormat = ImageOutputFormat.WebP,
            Background = ImageBackground.Transparent, OutputCompression = 73
        });
        var sent = handler.Requests.Single();
        Assert.AreEqual("max", sent.Fields["quality"]);
        Assert.AreEqual("73", sent.Fields["output_compression"]);
        Assert.AreEqual("1536x864", sent.Fields["size"]);
        Assert.HasCount(4, sent.Files);
        for (var index = 0; index < inputs.Length; index++) AssertFile(sent.Files[index], "image[]", inputs[index]);
        AssertFile(sent.Files[3], "mask", mask);
        Assert.IsFalse(sent.Fields.ContainsKey("input_fidelity"));
    }

    [TestMethod]
    [DataRow("references-empty")]
    [DataRow("references-null")]
    [DataRow("reference-null")]
    [DataRow("reference-count")]
    [DataRow("reference-empty")]
    [DataRow("reference-mime")]
    [DataRow("reference-limit")]
    [DataRow("mask-empty")]
    [DataRow("mask-limit")]
    [DataRow("mask-jpeg")]
    [DataRow("mask-format-mismatch")]
    public async Task InvalidReferencesAndMask_FailBeforeHttp(string scenario)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = (ImageEditRequest)MakeRequest(true);
        switch (scenario)
        {
            case "references-empty": request.InputImages = []; break;
            case "references-null": request.InputImages = null!; break;
            case "reference-null": request.InputImages = [null!]; break;
            case "reference-count": request.InputImages = Enumerable.Repeat(request.InputImages[0], 17).ToArray(); break;
            case "reference-empty": request.InputImages = [new ImageInput([], "image/png")]; break;
            case "reference-mime": request.InputImages = [new ImageInput([1], "image/gif")]; break;
            case "reference-limit": request.InputImages = [new ImageInput(new byte[50 * 1024 * 1024], "image/png")]; break;
            case "mask-empty": request.Mask = new ImageInput([], "image/png"); break;
            case "mask-limit": request.Mask = new ImageInput(new byte[50 * 1024 * 1024], "image/png"); break;
            case "mask-jpeg": request.Mask = new ImageInput([1], "image/jpeg"); break;
            case "mask-format-mismatch": request.Mask = new ImageInput([1], "image/webp"); break;
            default: Assert.Fail("Unhandled input-validation scenario."); break;
        }
        await Assert.ThrowsAsync<ArgumentException>(() => new OpenAIService("key", http).EditImagesAsync(request));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    [DataRow("image/png")]
    [DataRow("image/webp")]
    public async Task MaskAboveOldFourMiBLimitAndSixteenReferences_ArePreserved(string mediaType)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var inputs = Enumerable.Range(0, 16).Select(index => new ImageInput([(byte)index, 1], mediaType, $"reference-{index}.bin")).ToArray();
        var maskBytes = new byte[4 * 1024 * 1024 + 1];
        maskBytes[0] = 23;
        maskBytes[^1] = 42;
        var mask = new ImageInput(maskBytes, mediaType, "large-mask.bin");
        var result = await new OpenAIService("key", http).EditImagesAsync(new ImageEditRequest
        {
            Model = AIModels.OpenAI.GptImage2_5Flare, Prompt = "Synthetic multipart size regression.",
            InputImages = inputs, Mask = mask, Count = 10
        });
        Assert.HasCount(10, result.Images);
        var sent = handler.Requests.Single();
        Assert.HasCount(17, sent.Files);
        for (var index = 0; index < inputs.Length; index++) AssertFile(sent.Files[index], "image[]", inputs[index]);
        AssertFile(sent.Files[16], "mask", mask);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.GptImage2_5Sunburst)]
    [DataRow(AIModels.OpenAI.GptImage2_5Sunburst_260908)]
    [DataRow(AIModels.OpenAI.GptImage2_5Flare)]
    [DataRow(AIModels.OpenAI.GptImage2_5Flare_260908)]
    public async Task EveryAliasAndSnapshot_EnforcesItsNewCountLimit(string model)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var request = MakeRequest(false, model);
        request.Count = 11;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new OpenAIService("key", http).GenerateImagesAsync(request));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task ExplicitModelDoesNotChangeDefaultImageModelAndFollowingRequestUsesTypedOptions()
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var service = new OpenAIService("key", AIModels.OpenAI.Gpt5_6Sol, http);
        await service.GenerateImagesAsync(MakeRequest(false));
        await service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Default image model contract.", Count = 1, Size = ImageSize.Pixels(1536, 1024), Quality = ImageQuality.High,
            OutputFormat = ImageOutputFormat.Jpeg, OutputCompression = 75
        });
        Assert.AreEqual("gpt-image-2", handler.Requests[1].Fields["model"]);
        Assert.AreEqual("1536x1024", handler.Requests[1].Fields["size"]);
        Assert.AreEqual("high", handler.Requests[1].Fields["quality"]);
        Assert.AreEqual("1", handler.Requests[1].Fields["n"]);
        Assert.AreEqual("75", handler.Requests[1].Fields["output_compression"]);
        Assert.AreEqual(AIModels.OpenAI.GptImage2, service.DefaultImageModel);
        Assert.AreEqual(AIModels.OpenAI.Gpt5_6Sol, service.Model);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("true")]
    [DataRow("0")]
    public void LiveProbe_RequiresExplicitOptInBeforeCredentialAccess(string? value)
        => Assert.Throws<InvalidOperationException>(() => OpenAIImage25LiveProbe.RequireOptIn(value));

    private static ImageGenerationRequest MakeRequest(bool editing, string model = AIModels.OpenAI.GptImage2_5Sunburst)
    {
        ImageGenerationRequest request = editing ? new ImageEditRequest { InputImages = [new ImageInput([1, 2, 3], "image/png", "source.png")] }
            : new ImageGenerationRequest();
        request.Prompt = "Draw one synthetic red square.";
        request.Model = model;
        request.Size = ImageSize.Pixels(1024, 1024);
        request.Quality = ImageQuality.Low;
        return request;
    }

    private static Task<ImageGenerationResult> SendAsync(IImageGenerationService service, ImageGenerationRequest request)
        => request is ImageEditRequest edit ? service.EditImagesAsync(edit) : service.GenerateImagesAsync(request);

    private static void AssertFile(FilePart actual, string name, ImageInput expected)
    {
        Assert.AreEqual(name, actual.Name);
        Assert.AreEqual(expected.FileName, actual.FileName);
        Assert.AreEqual(expected.MediaType, actual.MediaType);
        CollectionAssert.AreEqual(expected.Data, actual.Data);
    }

    private sealed record FilePart(string Name, string FileName, string MediaType, byte[] Data);
    private sealed class CapturedRequest
    {
        internal string Path { get; init; } = "";
        internal string Method { get; init; } = "";
        internal string? ContentType { get; init; }
        internal string? AuthenticationScheme { get; init; }
        internal Dictionary<string, string> Fields { get; } = new();
        internal List<FilePart> Files { get; } = new();
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        internal List<CapturedRequest> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = new CapturedRequest { Path = request.RequestUri!.AbsolutePath, Method = request.Method.Method,
                ContentType = request.Content!.Headers.ContentType?.MediaType, AuthenticationScheme = request.Headers.Authorization?.Scheme };
            if (request.Content is MultipartContent multipart)
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    var filename = part.Headers.ContentDisposition.FileName?.Trim('"');
                    if (filename != null) sent.Files.Add(new FilePart(name, filename, part.Headers.ContentType!.MediaType!, await part.ReadAsByteArrayAsync(cancellationToken)));
                    else sent.Fields[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            else
            {
                using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                foreach (var property in document.RootElement.EnumerateObject()) sent.Fields[property.Name] = property.Value.ToString();
            }
            Requests.Add(sent);
            var count = int.Parse(sent.Fields["n"], System.Globalization.CultureInfo.InvariantCulture);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                output_format = sent.Fields["output_format"], data = Enumerable.Range(0, count).Select(_ => new { b64_json = "AQID" }).ToArray(),
                usage = new { input_tokens = 12, output_tokens = 24, total_tokens = 36 }
            }), Encoding.UTF8, "application/json") };
            response.Headers.Add("x-request-id", "req_image25_offline");
            return response;
        }
    }
}
