using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.xAI;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class XAIImageRequestShapeTests
{
    private static readonly byte[] JpegBytes = { 0xff, 0xd8, 0xff, 0xe0, 0, 2, 0xff, 0xd9 };
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
    private static readonly byte[] WebpBytes = { 82, 73, 70, 70, 4, 0, 0, 0, 87, 69, 66, 80 };

    [TestMethod]
    public async Task Generation_UsesImageInterfaceAndJsonContractWithoutChangingChatConfiguration()
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        var service = new XAIService("offline-key", AIModels.xAI.Grok4_6, client);
        service.SystemMessage = "Keep the chat configuration";
        service.Temperature = 0.17f;
        service.MaxTokens = 2468;
        service.ReasoningEffort = GrokReasoning.XHigh;
        var originalChat = service.ActivateChat;
        IImageGenerationService images = service;
        Assert.AreEqual("grok-imagine-image-2.0", typeof(AIModels.xAI).GetField(nameof(AIModels.xAI.GrokImagineImage2_0))!.GetRawConstantValue());
        Assert.AreEqual(AIModels.xAI.GrokImagineImage2_0, images.DefaultImageModel);

        var result = await images.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "A product photograph",
            Count = 10,
            Size = ImageSize.Preset(ImageResolution.TwoK, ImageAspectRatio.SixteenByNine),
            Quality = ImageQuality.Medium,
            OutputFormat = ImageOutputFormat.Auto
        });

        var captured = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, captured.Method);
        Assert.AreEqual("https://api.x.ai/v1/images/generations", captured.Uri.AbsoluteUri);
        Assert.AreEqual("Bearer offline-key", captured.Authorization);
        Assert.AreEqual("application/json", captured.MediaType);
        using var json = JsonDocument.Parse(captured.Body);
        var body = json.RootElement;
        Assert.AreEqual(AIModels.xAI.GrokImagineImage2_0, body.GetProperty("model").GetString());
        Assert.AreEqual("A product photograph", body.GetProperty("prompt").GetString());
        Assert.AreEqual(10, body.GetProperty("n").GetInt32());
        Assert.AreEqual("medium", body.GetProperty("quality").GetString());
        Assert.AreEqual("2k", body.GetProperty("resolution").GetString());
        Assert.AreEqual("16:9", body.GetProperty("aspect_ratio").GetString());
        Assert.AreEqual("b64_json", body.GetProperty("response_format").GetString());
        foreach (var excluded in new[] { "size", "output_format", "background", "output_compression", "messages", "temperature", "reasoning_effort", "max_tokens", "image", "images" })
            Assert.IsFalse(body.TryGetProperty(excluded, out _), excluded);
        Assert.AreEqual("xAI", result.Provider);
        Assert.AreEqual(AIModels.xAI.GrokImagineImage2_0, result.Model);
        Assert.AreEqual("req-image", result.RequestId);
        CollectionAssert.AreEqual(JpegBytes, result.Images.Single().Data);
        Assert.AreEqual("image/jpeg", result.Images.Single().MediaType);
        Assert.AreEqual("revised", result.Images.Single().RevisedPrompt);
        Assert.AreSame(originalChat, service.ActivateChat);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.AreEqual(AIModels.xAI.Grok4_6, service.Model);
        Assert.AreEqual("Keep the chat configuration", service.SystemMessage);
        Assert.AreEqual(0.17f, service.Temperature);
        Assert.AreEqual(2468u, service.MaxTokens);
        Assert.AreEqual(GrokReasoning.XHigh, service.ReasoningEffort);
        Assert.AreEqual(TimeSpan.FromMinutes(5), client.Timeout);
    }

    [TestMethod]
    public async Task ImageOperation_DoesNotConsumePendingChatReasoning()
    {
        var handler = new CaptureHandler((request, _) => Task.FromResult(Response(
            request.Uri.AbsolutePath.EndsWith("chat/completions", StringComparison.Ordinal)
                ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}"
                : ImageResponse(JpegBytes))));
        var service = Create(handler);
        service.WithReasoning(ReasoningLevel.Low);
        await service.GenerateImagesAsync(Request());
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.AreEqual("answer", await service.GetCompletionAsync("hello"));
        using var chat = JsonDocument.Parse(handler.Requests.Last().Body);
        Assert.AreEqual("low", chat.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [TestMethod]
    [DataRow(ImageQuality.Auto, "auto")]
    [DataRow(ImageQuality.Low, "low")]
    [DataRow(ImageQuality.Medium, "medium")]
    public async Task QualityAndModelOverride_AreForwardedForBothOperations(ImageQuality quality, string expectedQuality)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        var generation = Request();
        generation.Model = "grok-imagine-image-future-snapshot";
        generation.Quality = quality;
        generation.OutputFormat = ImageOutputFormat.Auto;
        var generationResult = await service.GenerateImagesAsync(generation);
        await service.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "edit", Model = generation.Model, Quality = quality, OutputFormat = ImageOutputFormat.Auto,
            Count = 10, InputImages = new[] { new ImageInput(JpegBytes, "image/jpeg") }
        });
        foreach (var captured in handler.Requests)
        {
            using var json = JsonDocument.Parse(captured.Body);
            Assert.AreEqual(generation.Model, json.RootElement.GetProperty("model").GetString());
            Assert.AreEqual(expectedQuality, json.RootElement.GetProperty("quality").GetString());
            Assert.IsFalse(json.RootElement.TryGetProperty("aspect_ratio", out _));
            Assert.IsFalse(json.RootElement.TryGetProperty("resolution", out _));
        }
        Assert.AreEqual(generation.Model, generationResult.Model);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(5)]
    public async Task Edits_SendOrderedDataUrisWithExclusiveSingleOrPluralImageField(int inputCount)
    {
        var handler = new CaptureHandler();
        var inputs = Enumerable.Range(0, inputCount)
            .Select(index => new ImageInput(new byte[] { (byte)(index + 1) }, index % 2 == 0 ? "image/png" : "image/webp", $"source-{index}.png"))
            .ToArray();
        await Create(handler).EditImagesAsync(new ImageEditRequest
        {
            Prompt = "combine", InputImages = inputs, OutputFormat = ImageOutputFormat.Auto, Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.ThreeByTwo)
        });
        var captured = handler.Requests.Single();
        Assert.AreEqual("/v1/images/edits", captured.Uri.AbsolutePath);
        Assert.AreEqual("application/json", captured.MediaType);
        using var json = JsonDocument.Parse(captured.Body);
        var body = json.RootElement;
        var images = inputCount == 1 ? new[] { body.GetProperty("image") } : body.GetProperty("images").EnumerateArray().ToArray();
        Assert.IsFalse(body.TryGetProperty(inputCount == 1 ? "images" : "image", out _));
        Assert.AreEqual(inputCount, images.Length);
        for (var index = 0; index < inputCount; index++)
        {
            Assert.AreEqual("image_url", images[index].GetProperty("type").GetString());
            Assert.AreEqual($"data:{inputs[index].MediaType};base64,{Convert.ToBase64String(inputs[index].Data)}", images[index].GetProperty("url").GetString());
            Assert.IsFalse(images[index].TryGetProperty("file_name", out _));
        }
        Assert.AreEqual("b64_json", body.GetProperty("response_format").GetString());
        Assert.AreEqual("1k", body.GetProperty("resolution").GetString());
        Assert.AreEqual("3:2", body.GetProperty("aspect_ratio").GetString());
    }

    [TestMethod]
    [DataRow(ImageResolution.Auto, null)]
    [DataRow(ImageResolution.OneK, "1k")]
    [DataRow(ImageResolution.TwoK, "2k")]
    public async Task Preset_MapsResolutionForGenerationAndEditing(ImageResolution resolution, string? expected)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        foreach (var editing in new[] { false, true })
        {
            var request = NewImageRequest(editing);
            request.Size = ImageSize.Preset(resolution);
            await SendAsync(service, request);
            using var json = JsonDocument.Parse(handler.Requests.Last().Body);
            if (expected == null)
                Assert.IsFalse(json.RootElement.TryGetProperty("resolution", out _));
            else
                Assert.AreEqual(expected, json.RootElement.GetProperty("resolution").GetString());
            Assert.IsFalse(json.RootElement.TryGetProperty("aspect_ratio", out _));
            Assert.IsFalse(json.RootElement.TryGetProperty("size", out _));
        }
    }

    [TestMethod]
    [DataRow(ImageAspectRatio.OneByOne, "1:1")]
    [DataRow(ImageAspectRatio.ThreeByFour, "3:4")]
    [DataRow(ImageAspectRatio.FourByThree, "4:3")]
    [DataRow(ImageAspectRatio.NineBySixteen, "9:16")]
    [DataRow(ImageAspectRatio.SixteenByNine, "16:9")]
    [DataRow(ImageAspectRatio.TwoByThree, "2:3")]
    [DataRow(ImageAspectRatio.ThreeByTwo, "3:2")]
    [DataRow(ImageAspectRatio.NineByNineteenPointFive, "9:19.5")]
    [DataRow(ImageAspectRatio.NineteenPointFiveByNine, "19.5:9")]
    [DataRow(ImageAspectRatio.NineByTwenty, "9:20")]
    [DataRow(ImageAspectRatio.TwentyByNine, "20:9")]
    [DataRow(ImageAspectRatio.OneByTwo, "1:2")]
    [DataRow(ImageAspectRatio.TwoByOne, "2:1")]
    [DataRow(ImageAspectRatio.TwentyOneByNine, "21:9")]
    [DataRow(ImageAspectRatio.FiveByTwo, "5:2")]
    public async Task Preset_MapsEveryDocumentedRatioForGenerationAndEditing(ImageAspectRatio ratio, string expected)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        foreach (var editing in new[] { false, true })
        {
            var request = NewImageRequest(editing);
            request.Size = ImageSize.Preset(ImageResolution.Auto, ratio);
            await SendAsync(service, request);
            using var json = JsonDocument.Parse(handler.Requests.Last().Body);
            Assert.AreEqual(expected, json.RootElement.GetProperty("aspect_ratio").GetString());
            Assert.IsFalse(json.RootElement.TryGetProperty("resolution", out _));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExactPixelsAndNullSize_AreRejectedBeforeHttp(bool editing)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        var request = NewImageRequest(editing);
        request.Size = ImageSize.Pixels(1024, 1024);
        var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() => SendAsync(service, request));
        StringAssert.Contains(exception.Message, "exact pixel");
        request.Size = null!;
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => SendAsync(service, request));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnsupportedPresetAndInvalidEnumOptions_AreRejectedBeforeHttp(bool editing)
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        foreach (var size in new[]
        {
            ImageSize.Preset(ImageResolution.FiveTwelve),
            ImageSize.Preset(ImageResolution.FourK),
            ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.FourByFive),
            ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.FiveByFour),
            ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.OneByEight),
            ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.EightByOne),
            ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.OneByFour),
            ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.FourByOne)
        })
        {
            var request = NewImageRequest(editing);
            request.Size = size;
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => SendAsync(service, request));
        }
        foreach (var mutate in new Action<ImageGenerationRequest>[]
        {
            request => request.Quality = (ImageQuality)999,
            request => request.Background = (ImageBackground)999,
            request => request.OutputFormat = (ImageOutputFormat)999
        })
        {
            var request = NewImageRequest(editing);
            mutate(request);
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => SendAsync(service, request));
        }
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task UnsupportedOptionsAndInvalidInputs_FailBeforeHttpForBothOperations()
    {
        var handler = new CaptureHandler();
        var service = Create(handler);
        foreach (var mutate in new Action<ImageGenerationRequest>[]
        {
            request => request.OutputFormat = ImageOutputFormat.Jpeg,
            request => request.OutputFormat = ImageOutputFormat.Png,
            request => request.OutputFormat = ImageOutputFormat.WebP,
            request => request.Quality = ImageQuality.High,
            request => request.Quality = ImageQuality.XHigh,
            request => request.Quality = ImageQuality.Max,
            request => request.Background = ImageBackground.Opaque,
            request => request.Background = ImageBackground.Transparent,
            request => request.OutputCompression = 80
        })
        {
            var generation = Request();
            var edit = new ImageEditRequest { Prompt = "edit", OutputFormat = ImageOutputFormat.Auto, InputImages = new[] { new ImageInput(JpegBytes, "image/jpeg") } };
            mutate(generation);
            mutate(edit);
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.GenerateImagesAsync(generation));
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.EditImagesAsync(edit));
        }
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.GenerateImagesAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.EditImagesAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = " ", OutputFormat = ImageOutputFormat.Auto }));
        foreach (var count in new[] { 0, -1, 11 })
        {
            var request = Request();
            request.Count = count;
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.GenerateImagesAsync(request));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.EditImagesAsync(new ImageEditRequest { Prompt = "edit", Count = count, OutputFormat = ImageOutputFormat.Auto }));
        }
        foreach (var inputs in new IReadOnlyList<ImageInput>[]
        {
            null!, Array.Empty<ImageInput>(), new ImageInput[] { null! },
            new[] { new ImageInput(Array.Empty<byte>(), "image/jpeg") },
            Enumerable.Repeat(new ImageInput(JpegBytes, "image/jpeg"), 6).ToArray()
        })
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.EditImagesAsync(new ImageEditRequest { Prompt = "edit", OutputFormat = ImageOutputFormat.Auto, InputImages = inputs }));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "edit", OutputFormat = ImageOutputFormat.Auto, InputImages = new[] { new ImageInput(JpegBytes, "image/gif") }
        }));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "edit", OutputFormat = ImageOutputFormat.Auto, Mask = new ImageInput(PngBytes, "image/png")
        }));
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task Response_UsesActualImageBytesAndOptionalMimeMetadataAndTokenUsage()
    {
        foreach (var item in new[] { (JpegBytes, "image/jpeg"), (PngBytes, "image/png"), (WebpBytes, "image/webp") })
        {
            foreach (var mimeType in new string?[] { null, item.Item2 })
            {
                var handler = new CaptureHandler((_, _) => Task.FromResult(Response(ImageResponse(item.Item1, mimeType,
                    new { input_tokens = 12, output_tokens = 8, total_tokens = 20, input_tokens_details = new { cached_tokens = 3 }, output_tokens_details = new { reasoning_tokens = 2 } }, "resolved-image-model"))));
                var request = Request();
                request.OutputFormat = ImageOutputFormat.Auto;
                var result = await Create(handler).GenerateImagesAsync(request);
                CollectionAssert.AreEqual(item.Item1, result.Images.Single().Data);
                Assert.AreEqual(item.Item2, result.Images.Single().MediaType);
                Assert.AreEqual("resolved-image-model", result.Model);
                Assert.IsNotNull(result.Usage);
                Assert.AreEqual(12, result.Usage.InputTokens);
                Assert.AreEqual(8, result.Usage.OutputTokens);
                Assert.AreEqual(20, result.Usage.TotalTokens);
                Assert.AreEqual(3, result.Usage.CachedInputTokens);
                Assert.AreEqual(2, result.Usage.ReasoningTokens);
            }
        }
    }

    [TestMethod]
    public async Task CostOnlyOrAbsentUsage_DoesNotInventTokenCounts()
    {
        foreach (var usage in new object?[] { null, new { cost_in_usd_ticks = 70000000 }, new { input_tokens = (int?)null, output_tokens = (int?)null, total_tokens = (int?)null } })
        {
            var handler = new CaptureHandler((_, _) => Task.FromResult(Response(ImageResponse(JpegBytes, usage: usage))));
            var result = await Create(handler).GenerateImagesAsync(Request());
            Assert.IsNull(result.Usage);
        }
    }

    [TestMethod]
    [DataRow("{bad-json")]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"data\":null}")]
    [DataRow("{\"data\":[]}")]
    [DataRow("{\"data\":[null]}")]
    [DataRow("{\"data\":[{\"url\":\"https://example.invalid/image.jpg\"}]}")]
    [DataRow("{\"data\":[{\"b64_json\":\"bad-base64!\"}]}")]
    [DataRow("{\"data\":[{\"b64_json\":\"\"}]}")]
    [DataRow("{\"data\":[{\"b64_json\":\"AQID\"}]}")]
    public async Task InvalidOrUrlOnlyResponse_FailsWithoutDownloading(string json)
    {
        var handler = new CaptureHandler((_, _) => Task.FromResult(Response(json)));
        var service = Create(handler);
        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.GenerateImagesAsync(Request()));
        Assert.AreEqual("req-image", exception.Data["x-request-id"]);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests.Single().Method);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task MimeMetadata_MustMatchReturnedBytes()
    {
        foreach (var response in new[] { ImageResponse(JpegBytes, "image/png"), ImageResponse(JpegBytes, "image/gif") })
        {
            var handler = new CaptureHandler((_, _) => Task.FromResult(Response(response)));
            await Assert.ThrowsExactlyAsync<AIServiceException>(() => Create(handler).GenerateImagesAsync(Request()));
        }
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.InternalServerError)]
    public async Task HttpFailure_PreservesStatusBodyAndRequestIdAndDisposesResponse(HttpStatusCode status)
    {
        const string body = "{\"error\":\"image request rejected\"}";
        var content = new TrackingContent(body);
        var handler = new CaptureHandler((_, _) => Task.FromResult(Response(body, status, content)));
        var service = Create(handler);
        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.GenerateImagesAsync(Request()));
        Assert.AreEqual(body, exception.ErrorDetails);
        StringAssert.Contains(exception.Message, ((int)status).ToString());
        Assert.AreEqual("req-image", exception.Data["x-request-id"]);
        Assert.IsTrue(content.IsDisposed);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task CallerCancellationAndPolicyTimeout_RemainDistinctAndPreserveClientTimeout()
    {
        var handler = new CaptureHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        var service = new XAIService("offline-key", client);
        using var cts = new CancellationTokenSource();
        var task = service.GenerateImagesAsync(Request(), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        service.CurrentPolicy = new FunctionCallingPolicy { TimeoutSeconds = 1 };
        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() => service.GenerateImagesAsync(Request()));
        StringAssert.Contains(exception.Message, "timeout after 1 seconds");
        Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
        Assert.IsNull(service.CurrentPolicy);
        Assert.AreEqual(TimeSpan.FromMinutes(5), client.Timeout);
        Assert.AreEqual(AIModels.xAI.Grok4_5, service.Model);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task SuccessfulRequest_DisposesResponseAndConsumesOnlyOneShotPolicy()
    {
        var content = new TrackingContent(ImageResponse(JpegBytes));
        var handler = new CaptureHandler((_, _) => Task.FromResult(Response("", content: content)));
        var service = Create(handler);
        var defaultPolicy = service.DefaultPolicy;
        service.CurrentPolicy = new FunctionCallingPolicy { TimeoutSeconds = null };
        await service.GenerateImagesAsync(Request());
        Assert.IsTrue(content.IsDisposed);
        Assert.IsNull(service.CurrentPolicy);
        Assert.AreSame(defaultPolicy, service.DefaultPolicy);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DefaultOptions_AcceptProviderNativeOutputWithoutCodecSelection(bool editing)
    {
        var handler = new CaptureHandler((_, _) => Task.FromResult(Response(ImageResponse(PngBytes, "image/png"))));
        var result = await SendAsync(Create(handler), NewImageRequest(editing));
        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.AreEqual("auto", json.RootElement.GetProperty("quality").GetString());
        foreach (var field in new[] { "output_format", "aspect_ratio", "resolution", "size" })
            Assert.IsFalse(json.RootElement.TryGetProperty(field, out _));
        Assert.AreEqual("image/png", result.Images.Single().MediaType);
        CollectionAssert.AreEqual(PngBytes, result.Images.Single().Data);
    }

    private static ImageGenerationRequest NewImageRequest(bool editing) => editing
        ? new ImageEditRequest { Prompt = "edit", InputImages = new[] { new ImageInput(JpegBytes, "image/jpeg") } }
        : new ImageGenerationRequest { Prompt = "image" };

    private static Task<ImageGenerationResult> SendAsync(IImageGenerationService service, ImageGenerationRequest request)
        => request is ImageEditRequest edit ? service.EditImagesAsync(edit) : service.GenerateImagesAsync(request);

    private static XAIService Create(CaptureHandler handler) => new("offline-key", AIModels.xAI.Grok4_6, new HttpClient(handler));
    private static ImageGenerationRequest Request() => new() { Prompt = "image", OutputFormat = ImageOutputFormat.Auto };

    private static string ImageResponse(byte[] bytes, string? mimeType = "image/jpeg", object? usage = null, string? model = null)
        => JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(bytes), mime_type = mimeType, revised_prompt = "revised" } }, usage, model });

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK, HttpContent? content = null)
    {
        var response = new HttpResponseMessage(status) { Content = content ?? new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.Add("x-request-id", "req-image");
        return response;
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? MediaType, string Body);

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> _response;
        public List<CapturedRequest> Requests { get; } = new();
        public CaptureHandler(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>>? response = null)
            => _response = response ?? ((_, _) => Task.FromResult(Response(ImageResponse(JpegBytes))));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType, await request.Content!.ReadAsStringAsync(cancellationToken));
            Requests.Add(captured);
            return await _response(captured, cancellationToken);
        }
    }

    private sealed class TrackingContent(string content) : StringContent(content, Encoding.UTF8, "application/json")
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
