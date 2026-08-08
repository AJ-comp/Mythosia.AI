using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class OpenAIImageRequestShapeTests
{
    private const string SingleImageResponse =
        "{\"data\":[{\"b64_json\":\"AQID\"}],\"output_format\":\"png\"}";

    [TestMethod]
    public async Task ImageOperations_RejectNullRequestBeforeSending()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var generationException = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            service.GenerateImagesAsync(null!));
        var editException = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            service.EditImagesAsync(null!));

        Assert.AreEqual("request", generationException.ParamName);
        Assert.AreEqual("request", editException.ParamName);
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task GenerateImagesAsync_RejectsBlankPromptBeforeSending(string prompt)
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = prompt }));

        Assert.AreEqual("request", exception.ParamName);
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task GenerateImagesAsync_RejectsNonPositiveCountBeforeSending(int count)
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
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    public async Task EditImagesAsync_RejectsMissingInputsBeforeSending()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var emptyException = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.EditImagesAsync(new ImageEditRequest { Prompt = "test" }));
        var nullException = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.EditImagesAsync(new ImageEditRequest
            {
                Prompt = "test",
                InputImages = null!
            }));

        Assert.AreEqual("request", emptyException.ParamName);
        Assert.AreEqual("request", nullException.ParamName);
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    public async Task EditImagesAsync_RejectsNullInputItemBeforeSending()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.EditImagesAsync(new ImageEditRequest
            {
                Prompt = "test",
                InputImages = new ImageInput[] { null! }
            }));

        Assert.AreEqual("request", exception.ParamName);
        Assert.AreEqual(0, handler.Requests.Count, "Invalid requests must not reach HTTP.");
    }

    [TestMethod]
    public void ImageInput_RejectsNullConstructorArguments()
    {
        var dataException = Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ImageInput(null!, "image/png", "source.png"));
        var mediaTypeException = Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ImageInput(new byte[] { 1 }, null!, "source.png"));
        var fileNameException = Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ImageInput(new byte[] { 1 }, "image/png", null!));

        Assert.AreEqual("data", dataException.ParamName);
        Assert.AreEqual("mediaType", mediaTypeException.ParamName);
        Assert.AreEqual("fileName", fileNameException.ParamName);
    }

    [TestMethod]
    public async Task GenerateImagesAsync_SendsExpectedJsonAndUsesDefaultImageModel()
    {
        var handler = new CaptureHttpMessageHandler();
        var service = CreateService(handler);
        service.ChangeModel("gpt-5.4");
        IImageGenerationService imageService = service;

        await imageService.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create two window photographs",
            Count = 2,
            Size = "1536x1024",
            Quality = "high",
            OutputFormat = "webp",
            OutputCompression = 65,
            Background = "opaque"
        });

        var captured = AssertSingleRequest(handler);
        Assert.AreEqual(HttpMethod.Post, captured.Method);
        Assert.AreEqual("/v1/images/generations", captured.Uri.AbsolutePath);
        Assert.AreEqual("Bearer", captured.AuthorizationScheme);
        Assert.AreEqual("offline-test-key", captured.AuthorizationParameter);
        Assert.IsNotNull(captured.JsonBody);

        using var document = JsonDocument.Parse(captured.JsonBody);
        var root = document.RootElement;
        Assert.AreEqual(AIModels.OpenAI.GptImage2, root.GetProperty("model").GetString());
        Assert.AreEqual("Create two window photographs", root.GetProperty("prompt").GetString());
        Assert.AreEqual(2, root.GetProperty("n").GetInt32());
        Assert.AreEqual("1536x1024", root.GetProperty("size").GetString());
        Assert.AreEqual("high", root.GetProperty("quality").GetString());
        Assert.AreEqual("webp", root.GetProperty("output_format").GetString());
        Assert.AreEqual(65, root.GetProperty("output_compression").GetInt32());
        Assert.AreEqual("opaque", root.GetProperty("background").GetString());
        Assert.IsFalse(root.TryGetProperty("response_format", out _));
        Assert.AreEqual(AIModels.OpenAI.GptImage2, imageService.DefaultImageModel);
        Assert.AreEqual("gpt-5.4", service.Model, "The chat model must remain independent from the image model.");
    }

    [TestMethod]
    public async Task EditImagesAsync_SendsOrderedMultipartInputsAndMask()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);
        var first = new ImageInput(new byte[] { 1, 2 }, "image/png", "first.png");
        var second = new ImageInput(new byte[] { 3, 4, 5 }, "image/jpeg", "second.jpg");
        var mask = new ImageInput(new byte[] { 6, 7 }, "image/png", "mask.png");

        await service.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "Preserve the windows and change the lighting",
            Model = AIModels.OpenAI.GptImage2_260421,
            InputImages = new[] { first, second },
            Mask = mask,
            Count = 3,
            Size = "1024x1024",
            Quality = "medium",
            OutputFormat = "jpeg",
            OutputCompression = 72,
            Background = "auto"
        });

        var captured = AssertSingleRequest(handler);
        Assert.AreEqual(HttpMethod.Post, captured.Method);
        Assert.AreEqual("/v1/images/edits", captured.Uri.AbsolutePath);
        Assert.AreEqual(AIModels.OpenAI.GptImage2_260421, GetFormValue(captured, "model"));
        Assert.AreEqual("Preserve the windows and change the lighting", GetFormValue(captured, "prompt"));
        Assert.AreEqual("3", GetFormValue(captured, "n"));
        Assert.AreEqual("1024x1024", GetFormValue(captured, "size"));
        Assert.AreEqual("medium", GetFormValue(captured, "quality"));
        Assert.AreEqual("jpeg", GetFormValue(captured, "output_format"));
        Assert.AreEqual("72", GetFormValue(captured, "output_compression"));
        Assert.AreEqual("auto", GetFormValue(captured, "background"));

        var inputs = captured.Parts.Where(part => part.Name == "image[]").ToArray();
        Assert.AreEqual(2, inputs.Length);
        AssertPart(inputs[0], "first.png", "image/png", new byte[] { 1, 2 });
        AssertPart(inputs[1], "second.jpg", "image/jpeg", new byte[] { 3, 4, 5 });

        var capturedMask = captured.Parts.Single(part => part.Name == "mask");
        AssertPart(capturedMask, "mask.png", "image/png", new byte[] { 6, 7 });
        Assert.IsFalse(captured.Parts.Any(part => part.Name == "input_fidelity"));
    }

    [TestMethod]
    public async Task EditImagesAsync_OmitsAbsentOptionalParts()
    {
        var handler = new CaptureHttpMessageHandler();
        IImageGenerationService service = CreateService(handler);

        await service.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "Create a variation",
            InputImages = new[]
            {
                new ImageInput(new byte[] { 1 }, "image/png", "source.png")
            }
        });

        var captured = AssertSingleRequest(handler);
        Assert.IsFalse(captured.Parts.Any(part => part.Name == "mask"));
        Assert.IsFalse(captured.Parts.Any(part => part.Name == "output_compression"));
        Assert.IsFalse(captured.Parts.Any(part => part.Name == "input_fidelity"));
    }

    [TestMethod]
    public async Task ImageResponse_ParsesAllImagesUsageAndRequestId()
    {
        const string responseBody =
            "{\"data\":[" +
            "{\"b64_json\":\"AQID\",\"revised_prompt\":\"first revised\"}," +
            "{\"b64_json\":\"BAU=\"}]," +
            "\"output_format\":\"webp\"," +
            "\"usage\":{\"input_tokens\":11,\"output_tokens\":22,\"total_tokens\":33}}";
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, responseBody, "req_image_test")));
        IImageGenerationService service = CreateService(handler);

        var result = await service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create images",
            Model = "custom-image-model",
            Count = 2,
            OutputFormat = "webp"
        });

        Assert.AreEqual("OpenAI", result.Provider);
        Assert.AreEqual("custom-image-model", result.Model);
        Assert.AreEqual("req_image_test", result.RequestId);
        Assert.AreEqual(2, result.Images.Count);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Images[0].Data);
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, result.Images[1].Data);
        Assert.AreEqual("image/webp", result.Images[0].MediaType);
        Assert.AreEqual("first revised", result.Images[0].RevisedPrompt);
        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(11, result.Usage.InputTokens);
        Assert.AreEqual(22, result.Usage.OutputTokens);
        Assert.AreEqual(33, result.Usage.TotalTokens);
    }

    [TestMethod]
    public async Task ImageResponse_ParsesUrlOnlyImageAndFallsBackToRequestedFormat()
    {
        const string responseBody =
            "{\"data\":[{\"url\":\"https://images.example.test/generated.jpg\"," +
            "\"revised_prompt\":\"revised\"}]}";
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, responseBody)));
        IImageGenerationService service = CreateService(handler);

        var result = await service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create an image",
            OutputFormat = "jpeg"
        });

        var image = result.Images.Single();
        Assert.AreEqual(0, image.Data.Length);
        Assert.AreEqual("https://images.example.test/generated.jpg", image.Url);
        Assert.AreEqual("image/jpeg", image.MediaType);
        Assert.AreEqual("revised", image.RevisedPrompt);
    }

    [TestMethod]
    public async Task ImageResponse_UsesBinaryMediaTypeForUnknownOutputFormat()
    {
        const string responseBody =
            "{\"data\":[{\"b64_json\":\"AQID\"}],\"output_format\":\"tiff\"}";
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, responseBody)));
        IImageGenerationService service = CreateService(handler);

        var result = await service.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "Create an image"
        });

        Assert.AreEqual("application/octet-stream", result.Images.Single().MediaType);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"data\":{}}")]
    [DataRow("{\"data\":null}")]
    public async Task ImageResponse_RejectsMissingOrNonArrayData(string responseBody)
    {
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, responseBody)));
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "test" }));

        Assert.AreEqual(
            "Image generation response did not contain an image array",
            exception.Message);
    }

    [TestMethod]
    public async Task ImageResponse_RejectsEmptyDataArray()
    {
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, "{\"data\":[]}")));
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "test" }));

        Assert.AreEqual("Image generation returned no images", exception.Message);
    }

    [TestMethod]
    public async Task ImageResponse_RejectsItemWithoutInlineDataOrUrl()
    {
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, "{\"data\":[{}]}")));
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "test" }));

        Assert.AreEqual("Image generation returned an empty image result", exception.Message);
    }

    [TestMethod]
    public async Task ImageResponse_WrapsInvalidBase64()
    {
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(
                HttpStatusCode.OK,
                "{\"data\":[{\"b64_json\":\"not-base64!\"}]}")));
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "test" }));

        Assert.AreEqual("Failed to parse the image generation response", exception.Message);
        Assert.IsInstanceOfType<FormatException>(exception.InnerException);
    }

    [TestMethod]
    public async Task ImageResponse_WrapsInvalidJson()
    {
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.OK, "{not-json")));
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "test" }));

        Assert.AreEqual("Failed to parse the image generation response", exception.Message);
        Assert.IsInstanceOfType<JsonException>(exception.InnerException);
    }

    [TestMethod]
    public async Task ImageRequest_HttpFailurePreservesResponseBody()
    {
        const string errorBody = "{\"error\":{\"code\":\"invalid_image\",\"message\":\"bad input\"}}";
        var handler = new CaptureHttpMessageHandler(
            _ => Task.FromResult(CreateResponse(HttpStatusCode.BadRequest, errorBody, "req_failed")));
        IImageGenerationService service = CreateService(handler);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "test" }));

        StringAssert.Contains(exception.Message, "400");
        Assert.AreEqual(errorBody, exception.ErrorDetails);
        Assert.AreEqual("req_failed", exception.Data["x-request-id"]);
    }

    [TestMethod]
    public async Task ImageRequest_PropagatesCallerCancellation()
    {
        var handler = new CaptureHttpMessageHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateResponse(HttpStatusCode.OK, SingleImageResponse);
        });
        IImageGenerationService service = CreateService(handler);
        using var cts = new CancellationTokenSource();

        var requestTask = service.GenerateImagesAsync(
            new ImageGenerationRequest { Prompt = "cancel this request" },
            cts.Token);
        cts.Cancel();

        try
        {
            await requestTask;
            Assert.Fail("The image request should have observed caller cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Expected: caller cancellation remains cancellation rather than an AIServiceException.
        }
    }

    [TestMethod]
    public async Task ImageRequest_ReportsInternalTimeoutSeparatelyFromCallerCancellation()
    {
        var handler = new CaptureHttpMessageHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateResponse(HttpStatusCode.OK, SingleImageResponse);
        });
        var service = CreateService(handler);
        service.DefaultPolicy = new FunctionCallingPolicy { TimeoutSeconds = 1 };
        IImageGenerationService imageService = service;

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            imageService.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "time out" }));

        StringAssert.Contains(exception.Message, "Image request timeout after 1 seconds");
        Assert.IsInstanceOfType<OperationCanceledException>(exception.InnerException);
    }

    private static OpenAIService CreateService(CaptureHttpMessageHandler handler)
    {
        return new OpenAIService("offline-test-key", new HttpClient(handler));
    }

    private static CapturedRequest AssertSingleRequest(CaptureHttpMessageHandler handler)
    {
        Assert.AreEqual(1, handler.Requests.Count, "Exactly one request should have been sent.");
        return handler.Requests[0];
    }

    private static string GetFormValue(CapturedRequest request, string name)
    {
        var part = request.Parts.Single(item => item.Name == name);
        Assert.IsNull(part.FileName, $"{name} should be a scalar form field.");
        return Encoding.UTF8.GetString(part.Data);
    }

    private static void AssertPart(
        CapturedPart part,
        string expectedFileName,
        string expectedMediaType,
        byte[] expectedData)
    {
        Assert.AreEqual(expectedFileName, part.FileName);
        Assert.AreEqual(expectedMediaType, part.MediaType);
        CollectionAssert.AreEqual(expectedData, part.Data);
    }

    private static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode,
        string body,
        string requestId = "req_default")
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        response.Headers.Add("x-request-id", requestId);
        return response;
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
                AuthorizationParameter = request.Headers.Authorization?.Parameter
            };

            if (request.Content is MultipartContent multipart)
            {
                foreach (var part in multipart)
                {
                    captured.Parts.Add(new CapturedPart
                    {
                        Name = Unquote(part.Headers.ContentDisposition?.Name),
                        FileName = Unquote(part.Headers.ContentDisposition?.FileName),
                        MediaType = part.Headers.ContentType?.MediaType,
                        Data = await part.ReadAsByteArrayAsync(cancellationToken)
                    });
                }
            }
            else if (request.Content != null)
            {
                captured.JsonBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            Requests.Add(captured);
            return await _responseFactory(cancellationToken);
        }

        private static string? Unquote(string? value)
        {
            return value?.Trim('"');
        }
    }

    private sealed class CapturedRequest
    {
        public HttpMethod Method { get; set; } = HttpMethod.Get;

        public Uri Uri { get; set; } = new Uri("https://example.invalid/");

        public string? AuthorizationScheme { get; set; }

        public string? AuthorizationParameter { get; set; }

        public string? JsonBody { get; set; }

        public List<CapturedPart> Parts { get; } = new();
    }

    private sealed class CapturedPart
    {
        public string? Name { get; set; }

        public string? FileName { get; set; }

        public string? MediaType { get; set; }

        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}
