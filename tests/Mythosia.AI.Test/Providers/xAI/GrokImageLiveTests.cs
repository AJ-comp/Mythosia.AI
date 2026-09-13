using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

namespace Mythosia.AI.Tests.xAI;

[TestClass]
[TestCategory("Live")]
[TestCategory("ImageGeneration")]
[TestCategory("GrokImagineImage2")]
[DoNotParallelize]
public class GrokImageLiveTests
{
    [TestMethod]
    [TestCategory("xAI")]
    public async Task DefaultGeneration_UsesImageModelIndependentOfChat()
    {
        using var probe = await GrokImageLiveProbe.CreateAsync();
        Assert.AreEqual(AIModels.xAI.GrokImagineImage2_0, probe.Images.DefaultImageModel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await probe.Images.GenerateImagesAsync(new ImageGenerationRequest
        {
            Prompt = "A minimal flat illustration of a single red square centered on a plain white square canvas. No text, border, shadows, or other objects.",
            OutputFormat = ImageOutputFormat.Auto
        }, timeout.Token);
        var request = probe.AssertRequest(editing: false, count: 1);
        Assert.IsTrue(request.Field("quality") is null or "auto");
        Assert.IsTrue(request.Field("aspect_ratio") is null or "auto");
        Assert.IsTrue(request.Field("resolution") is null or "1k");
        await probe.SaveAndValidateAsync(result, "xai-default-red-square", 1);
        Console.WriteLine("LIVE_GROK_IMAGE_OK feature=default-generation");
    }

    [TestMethod]
    [TestCategory("xAI")]
    [DataRow(2, ImageResolution.OneK, ImageAspectRatio.OneByOne, ImageQuality.Low, "1k", "1:1", "low", "xai-batch-low-square")]
    [DataRow(1, ImageResolution.TwoK, ImageAspectRatio.SixteenByNine, ImageQuality.Medium, "2k", "16:9", "medium", "xai-medium-2k-wide")]
    public async Task Generation_RespectsCountResolutionQualityAndAspectRatio(
        int count, ImageResolution size, ImageAspectRatio aspectRatio, ImageQuality quality,
        string expectedResolution, string expectedRatio, string expectedQuality, string scenario)
    {
        using var probe = await GrokImageLiveProbe.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await probe.Images.GenerateImagesAsync(new ImageGenerationRequest
        {
            Model = AIModels.xAI.GrokImagineImage2_0,
            Prompt = "A clean flat infographic: a red square on the left, a blue circle in the middle, and a green triangle on the right. " +
                "Use a plain white background, generous margins, and exactly these three separated shapes. No words, borders, shadows, or other objects.",
            Count = count,
            Size = ImageSize.Preset(size, aspectRatio),
            Quality = quality,
            OutputFormat = ImageOutputFormat.Auto
        }, timeout.Token);
        var request = probe.AssertRequest(editing: false, count);
        Assert.AreEqual(expectedResolution, request.Field("resolution"));
        Assert.AreEqual(expectedRatio, request.Field("aspect_ratio"));
        Assert.AreEqual(expectedQuality, request.Field("quality"));
        await probe.SaveAndValidateAsync(result, scenario, count, expectedRatio, twoK: size == ImageResolution.TwoK);
        Console.WriteLine($"LIVE_GROK_IMAGE_OK feature=generation-settings count={count} size={size} quality={quality}");
    }

    [TestMethod]
    [TestCategory("xAI")]
    public async Task SingleImageEdit_UsesJsonImageAndKeepsInputBytes()
    {
        const string scenario = "xai-single-edit-add-blue-circle";
        var input = await GrokImageLiveProbe.CreateShapeAsync(scenario, 1, 0);
        using var probe = await GrokImageLiveProbe.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await probe.Images.EditImagesAsync(new ImageEditRequest
        {
            Prompt = "Preserve the large red square centered on the white background. Add one small blue circle in the upper-right white corner. " +
                "Keep both shapes separate. No text, border, shadows, or other objects.",
            InputImages = new[] { input },
            Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.OneByOne),
            Quality = ImageQuality.Auto,
            OutputFormat = ImageOutputFormat.Auto
        }, timeout.Token);
        var request = probe.AssertRequest(editing: true, count: 1, new[] { input });
        Assert.AreEqual("1k", request.Field("resolution"));
        Assert.AreEqual("1:1", request.Field("aspect_ratio"));
        Assert.IsTrue(request.Field("quality") is null or "auto");
        await probe.SaveAndValidateAsync(result, scenario, 1, "1:1");
        Console.WriteLine("LIVE_GROK_IMAGE_OK feature=single-reference-json-edit");
    }

    [TestMethod]
    [TestCategory("xAI")]
    [DataRow(2, ImageQuality.Medium, "medium", "xai-two-reference-composition")]
    [DataRow(5, ImageQuality.Low, "low", "xai-five-reference-composition")]
    public async Task MultiImageEdit_PreservesOrderedReferencesUpToTheSupportedLimit(int count, ImageQuality quality, string expectedQuality, string scenario)
    {
        var inputs = new List<ImageInput>();
        for (var index = 0; index < count; index++)
        {
            var mediaType = count == 5 && index == 1 ? "image/jpeg" : count == 5 && index == 2 ? "image/webp" : "image/png";
            inputs.Add(await GrokImageLiveProbe.CreateShapeAsync(scenario, index + 1, index, mediaType));
        }
        using var probe = await GrokImageLiveProbe.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await probe.Images.EditImagesAsync(new ImageEditRequest
        {
            Prompt = count == 2
                ? "Combine the red square from image 1 and blue circle from image 2 in one horizontal row on a plain white background. " +
                  "Put the red square on the left and blue circle on the right. Preserve their flat colors and shapes, with clear space between them. No text or other objects."
                : "Combine the subjects from all five reference images in a single horizontal row on a plain white background. " +
                  "From left to right show exactly: the red square, blue circle, green triangle, yellow plus sign, purple diamond. " +
                  "Keep the flat colors and shapes, evenly spaced and separate. No words, frames, or additional objects.",
            InputImages = inputs,
            Size = ImageSize.Preset(ImageResolution.OneK, ImageAspectRatio.SixteenByNine),
            Quality = quality,
            OutputFormat = ImageOutputFormat.Auto
        }, timeout.Token);
        var request = probe.AssertRequest(editing: true, count: 1, inputs);
        Assert.AreEqual("1k", request.Field("resolution"));
        Assert.AreEqual("16:9", request.Field("aspect_ratio"));
        Assert.AreEqual(expectedQuality, request.Field("quality"));
        await probe.SaveAndValidateAsync(result, scenario, 1, "16:9");
        Console.WriteLine($"LIVE_GROK_IMAGE_OK feature=multi-reference-json-edit references={count}");
    }

    [TestMethod]
    [TestCategory("OpenAI")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OpenAI_UsesTheSamePublicGenerationAndEditingContract(bool editing)
    {
        var scenario = editing ? "openai-common-edit-add-blue-circle" : "openai-common-generation";
        using var probe = await GrokImageLiveProbe.CreateAsync("OpenAI");
        Assert.AreEqual(AIModels.OpenAI.GptImage2, probe.Images.DefaultImageModel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        ImageGenerationResult result;
        IReadOnlyList<ImageInput> inputs = Array.Empty<ImageInput>();
        if (editing)
        {
            inputs = new[] { await GrokImageLiveProbe.CreateShapeAsync(scenario, 1, 0) };
            result = await probe.Images.EditImagesAsync(new ImageEditRequest
            {
                Prompt = "Preserve the red square centered on the white background. Add one small blue circle in the upper-right white corner. " +
                    "Keep both shapes separate, with no words or other objects.",
                InputImages = inputs,
                Size = ImageSize.Pixels(1024, 1024),
                Quality = ImageQuality.Low,
                OutputFormat = ImageOutputFormat.Jpeg
            }, timeout.Token);
        }
        else
            result = await probe.Images.GenerateImagesAsync(new ImageGenerationRequest
            {
                Prompt = "A minimal flat illustration of a red square and a blue circle side by side on a plain white square canvas. " +
                    "The red square is on the left and the blue circle on the right. No words, shadows, or other objects.",
                Size = ImageSize.Pixels(1024, 1024),
                Quality = ImageQuality.Low,
                OutputFormat = ImageOutputFormat.Jpeg
            }, timeout.Token);
        var request = probe.AssertRequest(editing, count: 1, inputs);
        Assert.AreEqual("1024x1024", request.Field("size"));
        Assert.AreEqual("low", request.Field("quality"));
        Assert.AreEqual("jpeg", request.Field("output_format"));
        await probe.SaveAndValidateAsync(result, scenario, 1, "1:1");
        Console.WriteLine($"LIVE_GROK_IMAGE_OK feature=openai-common-image-contract editing={editing}");
    }
}
