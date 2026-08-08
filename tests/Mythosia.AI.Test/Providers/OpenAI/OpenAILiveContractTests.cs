using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Services;
using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests.OpenAI;

[TestClass]
[TestCategory("Live")]
[TestCategory("OpenAI")]
public class OpenAILiveContractTests
{
    private static readonly byte[] PngSignature =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    };

    [TestMethod]
    [TestCategory("Gpt5_6")]
    [DataRow(AIModels.OpenAI.Gpt5_6)]
    [DataRow(AIModels.OpenAI.Gpt5_6Terra)]
    [DataRow(AIModels.OpenAI.Gpt5_6Luna)]
    public async Task Gpt5_6AliasAndTierModels_ReturnCompletion(string model)
    {
        var service = (OpenAIService)OpenAIServiceFactory.Create(model);
        service.MaxTokens = 128;
        service.WithGpt5_6Parameters(
            reasoningEffort: Gpt5_6Reasoning.None,
            verbosity: Verbosity.Low,
            reasoningSummary: null);

        var response = await service.GetCompletionAsync("Return exactly LIVE_OK.");

        Assert.IsFalse(string.IsNullOrWhiteSpace(response));
        StringAssert.Contains(response, "LIVE_OK");
    }

    [TestMethod]
    [TestCategory("Gpt5_6")]
    public async Task Gpt5_6Sol_MaxHighDetailedPro_ReturnsCompletion()
    {
        var service = (OpenAIService)OpenAIServiceFactory.Create(AIModels.OpenAI.Gpt5_6Sol);
        // Max reasoning consumes the same output-token budget as the visible answer.
        // Keep enough headroom so this contract verifies the mode instead of flaking on an
        // otherwise valid `incomplete` response caused by an intentionally tiny caller cap.
        service.MaxTokens = 4096;
        service.WithGpt5_6Parameters(
            reasoningEffort: Gpt5_6Reasoning.Max,
            verbosity: Verbosity.High,
            reasoningSummary: ReasoningSummary.Detailed,
            reasoningMode: Gpt5_6ReasoningMode.Pro);

        var response = await service.GetCompletionAsync(
            "Return exactly LIVE_OK after checking that 19 + 23 equals 42.");

        Assert.IsFalse(string.IsNullOrWhiteSpace(response));
        StringAssert.Contains(response, "LIVE_OK");

        // A successful request can include billed reasoning tokens while omitting the optional
        // reasoning output item, for example when summary access is unavailable to the account.
        // Exact summary parsing and the omitted-item case are deterministic unit-test contracts.
        if (string.IsNullOrWhiteSpace(service.LastReasoningSummary))
        {
            Console.WriteLine(
                "[INFO] The live response omitted the optional reasoning summary output item.");
        }
    }

    [TestMethod]
    [TestCategory("OpenAIImage")]
    public async Task GptImage2_GeneratesPng()
    {
        var imageService = CreateImageService();
        var result = await imageService.GenerateImagesAsync(new ImageGenerationRequest
        {
            Model = AIModels.OpenAI.GptImage2,
            Prompt = "A centered blue circle and a small green square on a plain white background.",
            Count = 1,
            Size = "1024x1024",
            Quality = "low",
            OutputFormat = "png",
            Background = "opaque"
        });

        AssertPngResult(result);
    }

    [TestMethod]
    [TestCategory("OpenAIImage")]
    public async Task GptImage2_EditsReferencePng()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "test_image.png");
        var source = await File.ReadAllBytesAsync(sourcePath);
        var imageService = CreateImageService();

        var result = await imageService.EditImagesAsync(new ImageEditRequest
        {
            Model = AIModels.OpenAI.GptImage2,
            Prompt = "Preserve the composition and subject, but change the white hoodie to pale blue.",
            InputImages = new[]
            {
                new ImageInput(source, "image/png", "test_image.png")
            },
            Count = 1,
            Size = "1024x1024",
            Quality = "low",
            OutputFormat = "png",
            Background = "opaque"
        });

        AssertPngResult(result);
    }

    private static IImageGenerationService CreateImageService()
    {
        return (IImageGenerationService)OpenAIServiceFactory.Create(AIModels.OpenAI.Gpt5_6Sol);
    }

    private static void AssertPngResult(ImageGenerationResult result)
    {
        Assert.AreEqual("OpenAI", result.Provider);
        Assert.AreEqual(AIModels.OpenAI.GptImage2, result.Model);
        Assert.AreEqual(1, result.Images.Count);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.RequestId));

        var image = result.Images.Single();
        Assert.AreEqual("image/png", image.MediaType);
        Assert.IsTrue(image.Data.Length > PngSignature.Length);
        CollectionAssert.AreEqual(PngSignature, image.Data.Take(PngSignature.Length).ToArray());
    }
}
