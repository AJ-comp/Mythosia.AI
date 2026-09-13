using Mythosia.AI.Exceptions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("ImageGeneration")]
public class AdversarialImageContractTests
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];

    [TestMethod]
    [DataRow("OpenAI", false)]
    [DataRow("OpenAI", true)]
    [DataRow("Google", false)]
    [DataRow("xAI", false)]
    public async Task EditRequest_CallerMutationAfterUploadStartsCannotChangeSubmittedImage(string provider, bool includeMask)
    {
        var handler = new HeldUploadHandler(provider);
        using var client = new HttpClient(handler);
        var service = Create(provider, client);
        var original = Png.ToArray();
        var input = new ImageInput(original, "image/png");
        var inputs = new List<ImageInput> { input };
        var request = new ImageEditRequest { Prompt = "original prompt", InputImages = inputs };
        var mask = Png.ToArray();
        if (includeMask) request.Mask = new ImageInput(mask, "image/png");

        var pending = service.EditImagesAsync(request);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // HttpClient has accepted the request, but its transport has not read the content yet.
        Array.Fill(original, (byte)0);
        Array.Fill(mask, (byte)0);
        request.Prompt = "changed prompt";
        request.Size = ImageSize.Pixels(1, 1);
        inputs.Clear();
        handler.Release.TrySetResult(true);

        var result = await pending;
        CollectionAssert.AreEqual(Png, handler.UploadedImage!, "Started requests must own their image bytes.");
        if (includeMask) CollectionAssert.AreEqual(Png, handler.UploadedMask!);
        Assert.AreEqual("original prompt", handler.UploadedPrompt);
        Assert.AreEqual(1, result.Images.Count);
    }

    [TestMethod]
    public async Task Google_CompleteAdditionalCandidatesRemainAvailable()
    {
        var response = new JsonObject { ["candidates"] = new JsonArray(Candidate("STOP"), Candidate("STOP")) };
        using var client = new HttpClient(new ResponseHandler(response.ToJsonString()));
        var service = new GoogleAIService("offline", client);

        var result = await service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "question" });

        Assert.AreEqual(2, result.Images.Count);
        foreach (var image in result.Images) CollectionAssert.AreEqual(Png, image.Data);
    }

    [TestMethod]
    [DataRow("MAX_TOKENS")]
    [DataRow("SAFETY")]
    [DataRow("")]
    public async Task Google_MustNotReturnImagesFromAnIncompleteAdditionalCandidate(string finishReason)
    {
        var complete = Candidate("STOP");
        var incomplete = Candidate(finishReason);
        var response = new JsonObject { ["candidates"] = new JsonArray(complete, incomplete) };
        using var client = new HttpClient(new ResponseHandler(response.ToJsonString()));
        var service = new GoogleAIService("offline", client);

        await Assert.ThrowsAsync<AIServiceException>(() =>
            service.GenerateImagesAsync(new ImageGenerationRequest { Prompt = "question" }));
    }

    private static JsonObject Candidate(string finishReason)
    {
        var candidate = new JsonObject
        {
            ["content"] = new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray(new JsonObject
                {
                    ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = "image/png", ["data"] = Convert.ToBase64String(Png)
                    }
                })
            }
        };
        if (finishReason.Length > 0) candidate["finishReason"] = finishReason;
        return candidate;
    }

    private static IImageGenerationService Create(string provider, HttpClient client) => provider switch
    {
        "OpenAI" => new OpenAIService("offline", client),
        "Google" => new GoogleAIService("offline", client),
        "xAI" => new XAIService("offline", client),
        _ => throw new ArgumentException(provider)
    };

    private static HttpResponseMessage Success(string provider)
    {
        var body = provider == "Google"
            ? new JsonObject { ["candidates"] = new JsonArray(Candidate("STOP")) }.ToJsonString()
            : JsonSerializer.Serialize(new { data = new[] { new { b64_json = Convert.ToBase64String(Png) } } });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class HeldUploadHandler(string provider) : HttpMessageHandler
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[]? UploadedImage { get; private set; }
        public byte[]? UploadedMask { get; private set; }
        public string? UploadedPrompt { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            if (request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name == "image[]") UploadedImage = await part.ReadAsByteArrayAsync(cancellationToken);
                    else if (name == "mask") UploadedMask = await part.ReadAsByteArrayAsync(cancellationToken);
                    else if (name == "prompt") UploadedPrompt = await part.ReadAsStringAsync(cancellationToken);
                }
            }
            else
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                if (provider == "Google")
                {
                    var parts = body["contents"]![0]!["parts"]!;
                    UploadedPrompt = parts[0]!["text"]!.GetValue<string>();
                    UploadedImage = Convert.FromBase64String(parts[1]!["inlineData"]!["data"]!.GetValue<string>());
                }
                else
                {
                    UploadedPrompt = body["prompt"]!.GetValue<string>();
                    var url = body["image"]!["url"]!.GetValue<string>();
                    UploadedImage = Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]);
                }
            }
            return Success(provider);
        }
    }

    private sealed class ResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
    }
}
