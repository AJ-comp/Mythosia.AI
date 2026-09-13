using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class AdversarialSecondImageContractTests
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];
    private static readonly byte[] Jpeg = [255, 216, 255, 224, 1, 2, 3];

    [TestMethod]
    [DataRow("missing", false)]
    [DataRow("missing", true)]
    [DataRow("number", false)]
    [DataRow("number", true)]
    [DataRow("whitespace", false)]
    [DataRow("whitespace", true)]
    public async Task Google_DeclaredInlineImageWithoutUsableDataCannotBecomePartialSuccess(string malformedData, bool editing)
    {
        var malformed = Inline(Png, "image/png");
        var blob = malformed["inlineData"]!.AsObject();
        if (malformedData == "missing") blob.Remove("data");
        else if (malformedData == "number") blob["data"] = 123;
        else blob["data"] = " \t\r\n";
        using var client = new HttpClient(new ResponseHandler(Envelope(Inline(Png, "image/png"), malformed)));
        var service = new GoogleAIService("offline", client);

        await Assert.ThrowsAsync<AIServiceException>(() => InvokeAsync(service, editing));
    }

    [TestMethod]
    [DataRow("missing", false)]
    [DataRow("missing", true)]
    [DataRow("number", false)]
    [DataRow("number", true)]
    [DataRow("audio", false)]
    [DataRow("audio", true)]
    [DataRow("empty-subtype", false)]
    [DataRow("wildcard", false)]
    [DataRow("spaced-slash", false)]
    public async Task Google_ImageMediaTypeCannotBeInventedOrDescribeNonImageContent(string malformedType, bool editing)
    {
        var malformed = Inline(Jpeg, "image/jpeg");
        var blob = malformed["inlineData"]!.AsObject();
        if (malformedType == "missing") blob.Remove("mimeType");
        else if (malformedType == "number") blob["mimeType"] = 123;
        else blob["mimeType"] = malformedType switch
        {
            "empty-subtype" => "image/",
            "wildcard" => "image/*",
            "spaced-slash" => "image / png",
            _ => "audio/wav"
        };
        using var client = new HttpClient(new ResponseHandler(Envelope(Inline(Png, "image/png"), malformed)));
        var service = new GoogleAIService("offline", client);

        await Assert.ThrowsAsync<AIServiceException>(() => InvokeAsync(service, editing));
    }

    [TestMethod]
    public async Task Google_ConcreteFutureImageMimeTypeRemainsAvailableWithoutDecodingOrTranscoding()
    {
        using var client = new HttpClient(new ResponseHandler(Envelope(Inline(Png, "image/vnd.example.custom"))));
        var service = new GoogleAIService("offline", client);

        var result = await InvokeAsync(service, false);

        Assert.AreEqual("image/vnd.example.custom", result.Images.Single().MediaType);
        CollectionAssert.AreEqual(Png, result.Images.Single().Data);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Google_MixedTextAndValidImagesRetainAllImageBytesAndDeclaredFormats(bool editing)
    {
        using var client = new HttpClient(new ResponseHandler(Envelope(
            new JsonObject { ["text"] = "Here are the images." },
            Inline(Png, "image/png"),
            new JsonObject { ["text"] = "An alternate view follows." },
            Inline(Jpeg, "image/jpeg"))));
        var service = new GoogleAIService("offline", client);

        var result = await InvokeAsync(service, editing);

        Assert.AreEqual(2, result.Images.Count);
        CollectionAssert.AreEqual(Png, result.Images[0].Data);
        CollectionAssert.AreEqual(Jpeg, result.Images[1].Data);
        Assert.AreEqual("image/png", result.Images[0].MediaType);
        Assert.AreEqual("image/jpeg", result.Images[1].MediaType);
    }

    private static Task<ImageGenerationResult> InvokeAsync(GoogleAIService service, bool editing)
        => editing
            ? service.EditImagesAsync(new ImageEditRequest
            {
                Prompt = "question", InputImages = [new ImageInput(Png, "image/png")]
            })
            : service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "question" });

    private static JsonObject Inline(byte[] bytes, string mediaType) => new()
    {
        ["inlineData"] = new JsonObject { ["mimeType"] = mediaType, ["data"] = Convert.ToBase64String(bytes) }
    };

    private static string Envelope(params JsonObject[] parts) => new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["finishReason"] = "STOP",
            ["content"] = new JsonObject
            {
                ["role"] = "model", ["parts"] = new JsonArray(parts.Cast<JsonNode>().ToArray())
            }
        })
    }.ToJsonString();

    private sealed class ResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
    }
}
