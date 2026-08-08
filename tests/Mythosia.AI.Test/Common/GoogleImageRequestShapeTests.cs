using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class GoogleImageRequestShapeTests
{
    private const string SingleImageResponse =
        "{\"candidates\":[{\"content\":{\"parts\":[" +
        "{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AQID\"}}]}," +
        "\"finishReason\":\"STOP\"}]}";

    [TestMethod]
    public async Task GenerateImagesAsync_SendsDefaultModelRequestAndImageGenerationConfig()
    {
        var handler = new CaptureHttpMessageHandler();
        var service = CreateService(handler);
        service.ChangeModel(AIModels.Google.Gemini3_6Flash);
        IImageGenerationService imageService = service;

        await imageService.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create a sunlit reading room"
        });

        var captured = AssertSingleRequest(handler);
        Assert.AreEqual(HttpMethod.Post, captured.Method);
        Assert.AreEqual(
            $"/v1/models/{AIModels.Google.Images.Gemini3_1FlashImage}:generateContent",
            captured.Uri.AbsolutePath);
        Assert.AreEqual(string.Empty, captured.Uri.Query);
        Assert.AreEqual("offline-test-key", captured.GoogleApiKey);
        Assert.IsNull(captured.AuthorizationScheme);
        Assert.IsNull(captured.AuthorizationParameter);
        Assert.IsNotNull(captured.JsonBody);

        using var document = JsonDocument.Parse(captured.JsonBody);
        var root = document.RootElement;
        var content = root.GetProperty("contents")[0];
        Assert.AreEqual("user", content.GetProperty("role").GetString());
        Assert.AreEqual(
            "Create a sunlit reading room",
            content.GetProperty("parts")[0].GetProperty("text").GetString());

        var generationConfig = root.GetProperty("generationConfig");
        var modalities = generationConfig
            .GetProperty("responseModalities")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        CollectionAssert.AreEqual(new[] { "TEXT", "IMAGE" }, modalities);
        Assert.IsFalse(generationConfig.TryGetProperty("responseFormat", out _));
        Assert.AreEqual(AIModels.Google.Images.Gemini3_1FlashImage, imageService.DefaultImageModel);
        Assert.AreEqual(
            AIModels.Google.Gemini3_6Flash,
            service.Model,
            "The chat model must remain independent from the default image model.");
    }

    [TestMethod]
    [DataRow("512", "IMAGE_SIZE_FIVE_TWELVE")]
    [DataRow("1K", "IMAGE_SIZE_ONE_K")]
    [DataRow("2K", "IMAGE_SIZE_TWO_K")]
    [DataRow("4K", "IMAGE_SIZE_FOUR_K")]
    public async Task GenerateImagesAsync_MapsPublicSizesToGenerateContentEnums(
        string size,
        string expectedImageSize)
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        await service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create a square editorial illustration",
            Size = size
        });

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).JsonBody!);
        var imageFormat = document.RootElement
            .GetProperty("generationConfig")
            .GetProperty("responseFormat")
            .GetProperty("image");
        Assert.AreEqual(expectedImageSize, imageFormat.GetProperty("imageSize").GetString());
    }

    [TestMethod]
    public async Task GenerateImagesAsync_MapsDimensionsToAspectRatioAndImageSize()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        await service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create a wide editorial illustration",
            Model = AIModels.Google.Images.Gemini3ProImage,
            Size = "1536x1024",
            OutputFormat = "jpeg"
        });

        var captured = AssertSingleRequest(handler);
        Assert.AreEqual(
            $"/v1/models/{AIModels.Google.Images.Gemini3ProImage}:generateContent",
            captured.Uri.AbsolutePath);

        using var document = JsonDocument.Parse(captured.JsonBody!);
        var imageFormat = document.RootElement
            .GetProperty("generationConfig")
            .GetProperty("responseFormat")
            .GetProperty("image");
        Assert.AreEqual("ASPECT_RATIO_THREE_BY_TWO", imageFormat.GetProperty("aspectRatio").GetString());
        Assert.AreEqual("IMAGE_SIZE_TWO_K", imageFormat.GetProperty("imageSize").GetString());
        Assert.AreEqual("IMAGE_JPEG", imageFormat.GetProperty("mimeType").GetString());
    }

    [TestMethod]
    public async Task EditImagesAsync_SendsReferenceImagesAsOrderedInlineData()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        await service.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "Keep the composition and change the time of day",
            InputImages = new[]
            {
                new ImageInput(new byte[] { 1, 2, 3 }, "image/png", "first.png"),
                new ImageInput(new byte[] { 4, 5 }, "image/jpeg", "second.jpg")
            }
        });

        var captured = AssertSingleRequest(handler);
        using var document = JsonDocument.Parse(captured.JsonBody!);
        var parts = document.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.AreEqual(3, parts.GetArrayLength());
        Assert.AreEqual(
            "Keep the composition and change the time of day",
            parts[0].GetProperty("text").GetString());
        AssertInlineData(parts[1], "image/png", new byte[] { 1, 2, 3 });
        AssertInlineData(parts[2], "image/jpeg", new byte[] { 4, 5 });
    }

    [TestMethod]
    public async Task ImageResponse_ParsesAllInlineImagesUsageAndRequestId()
    {
        const string responseBody =
            "{\"candidates\":[{\"content\":{\"parts\":[" +
            "{\"text\":\"Generated images\"}," +
            "{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AQID\"}}," +
            "{\"inlineData\":{\"mimeType\":\"image/webp\",\"data\":\"BAU=\"}}]}," +
            "\"finishReason\":\"STOP\"}]," +
            "\"responseId\":\"gemini-image-response-1\"," +
            "\"usageMetadata\":{" +
            "\"promptTokenCount\":11," +
            "\"toolUsePromptTokenCount\":2," +
            "\"candidatesTokenCount\":22," +
            "\"thoughtsTokenCount\":3," +
            "\"cachedContentTokenCount\":4," +
            "\"totalTokenCount\":38}}";
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, responseBody)));
        IImageGenerationService service = CreateService(handler);

        var result = await service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create two variations",
            Model = AIModels.Google.Images.Gemini3_1FlashLiteImage
        });

        Assert.AreEqual("Google", result.Provider);
        Assert.AreEqual(AIModels.Google.Images.Gemini3_1FlashLiteImage, result.Model);
        Assert.AreEqual("gemini-image-response-1", result.RequestId);
        Assert.AreEqual(2, result.Images.Count);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Images[0].Data);
        Assert.AreEqual("image/png", result.Images[0].MediaType);
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, result.Images[1].Data);
        Assert.AreEqual("image/webp", result.Images[1].MediaType);
        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(13, result.Usage.InputTokens);
        Assert.AreEqual(25, result.Usage.OutputTokens);
        Assert.AreEqual(38, result.Usage.TotalTokens);
        Assert.AreEqual(4, result.Usage.CachedInputTokens);
        Assert.AreEqual(3, result.Usage.ReasoningTokens);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task GenerateImagesAsync_RejectsUnsupportedCountBeforeSending(int count)
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest
            {
                Prompt = "test",
                Count = count
            }));

        Assert.AreEqual("request", exception.ParamName);
        StringAssert.Contains(exception.Message, "Count must be one");
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    public async Task EditImagesAsync_RejectsMaskBeforeSending()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.EditImagesAsync(new ImageEditRequest
            {
                Prompt = "Replace the background",
                InputImages = new[]
                {
                    new ImageInput(new byte[] { 1 }, "image/png", "source.png")
                },
                Mask = new ImageInput(new byte[] { 2 }, "image/png", "mask.png")
            }));

        StringAssert.Contains(exception.Message, "separate mask input");
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    [DataRow("quality")]
    [DataRow("background")]
    [DataRow("compression")]
    [DataRow("format")]
    public async Task GenerateImagesAsync_RejectsUnsupportedOptionsBeforeSending(string option)
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);
        var request = new ImageGenerationRequest { Prompt = "test" };

        switch (option)
        {
            case "quality":
                request.Quality = "high";
                break;
            case "background":
                request.Background = "transparent";
                break;
            case "compression":
                request.OutputCompression = 80;
                break;
            case "format":
                request.OutputFormat = "webp";
                break;
        }

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.GenerateImagesAsync(request));

        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    public async Task GenerateImagesAsync_RejectsInvalidSizeBeforeSending()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest
            {
                Prompt = "test",
                Size = "landscape"
            }));

        Assert.AreEqual("size", exception.ParamName);
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    public async Task GenerateImagesAsync_RejectsUnsupportedAspectRatioBeforeSending()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest
            {
                Prompt = "test",
                Size = "1000x700"
            }));

        Assert.AreEqual("size", exception.ParamName);
        StringAssert.Contains(exception.Message, "10:7");
        Assert.AreEqual(0, handler.Requests.Count, "Unsupported ratios must not reach HTTP.");
    }

    [TestMethod]
    public async Task ImageHttpFailure_ExposesProviderBodyInDetailsAndMessage()
    {
        const string errorBody =
            "{\"error\":{\"code\":400,\"message\":\"invalid image enum\",\"status\":\"INVALID_ARGUMENT\"}}";
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.BadRequest, errorBody)));
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest
            {
                Prompt = "test",
                Size = "1K"
            }));

        Assert.AreEqual(errorBody, exception.ErrorDetails);
        StringAssert.Contains(exception.Message, "invalid image enum");
    }

    [TestMethod]
    public void Constructor_DisablesHttpClientTimeoutSoVisionPolicyIsAuthoritative()
    {
        var handler = new CaptureHttpMessageHandler();
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(100)
        };

        _ = new GoogleAIService("offline-test-key", client);

        Assert.AreEqual(
            Timeout.InfiniteTimeSpan,
            client.Timeout,
            "HttpClient's 100-second default must not cap Gemini's 200-second image policy.");
    }

    [TestMethod]
    public async Task ImageRequest_UsesPolicyTimeoutAndMapsItToAIServiceException()
    {
        var handler = new CaptureHttpMessageHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertFailedException("The timeout token should cancel the handler.");
        });
        var service = CreateService(handler);
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = 1 };

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "test" }));

        StringAssert.Contains(exception.Message, "Gemini image request timeout after 1 seconds");
    }

    private static GoogleAIService CreateService(CaptureHttpMessageHandler handler)
    {
        return new GoogleAIService("offline-test-key", new HttpClient(handler));
    }

    private static CapturedRequest AssertSingleRequest(CaptureHttpMessageHandler handler)
    {
        Assert.AreEqual(1, handler.Requests.Count, "Exactly one request should have been sent.");
        return handler.Requests[0];
    }

    private static void AssertInlineData(
        JsonElement part,
        string expectedMediaType,
        byte[] expectedData)
    {
        var inlineData = part.GetProperty("inlineData");
        Assert.AreEqual(expectedMediaType, inlineData.GetProperty("mimeType").GetString());
        CollectionAssert.AreEqual(
            expectedData,
            Convert.FromBase64String(inlineData.GetProperty("data").GetString()!));
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public CaptureHttpMessageHandler(
            Func<CancellationToken, Task<HttpResponseMessage>>? responseFactory = null)
        {
            _responseFactory = responseFactory ??
                (_ => Task.FromResult(CreateResponse(HttpStatusCode.OK, SingleImageResponse)));
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest
            {
                Method = request.Method,
                Uri = request.RequestUri!,
                AuthorizationScheme = request.Headers.Authorization?.Scheme,
                AuthorizationParameter = request.Headers.Authorization?.Parameter,
                GoogleApiKey = request.Headers.TryGetValues("x-goog-api-key", out var apiKeyValues)
                    ? apiKeyValues.SingleOrDefault()
                    : null,
                JsonBody = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)
            };

            Requests.Add(captured);
            return await _responseFactory(cancellationToken);
        }
    }

    private sealed class CapturedRequest
    {
        public HttpMethod Method { get; set; } = HttpMethod.Get;

        public Uri Uri { get; set; } = new Uri("https://example.invalid/");

        public string? AuthorizationScheme { get; set; }

        public string? AuthorizationParameter { get; set; }

        public string? GoogleApiKey { get; set; }

        public string? JsonBody { get; set; }
    }
}
