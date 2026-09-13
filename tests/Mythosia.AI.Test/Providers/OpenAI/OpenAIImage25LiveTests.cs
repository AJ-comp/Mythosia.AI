using Mythosia.AI.Models;
using Mythosia.AI.Models.Images;

namespace Mythosia.AI.Tests.OpenAI;

[TestClass]
[TestCategory("Live")]
[TestCategory("OpenAI")]
[TestCategory("ImageGeneration")]
[TestCategory("GPTImage25")]
[DoNotParallelize]
public class OpenAIImage25LiveTests
{
    [TestMethod]
    [DataRow(AIModels.OpenAI.GptImage2_5Sunburst, false)]
    [DataRow(AIModels.OpenAI.GptImage2_5Sunburst, true)]
    [DataRow(AIModels.OpenAI.GptImage2_5Flare, false)]
    [DataRow(AIModels.OpenAI.GptImage2_5Flare, true)]
    public async Task GenerationAndEditing_ReturnOneDecodedImageFromTheSelectedModel(string model, bool editing)
    {
        using var probe = await OpenAIImage25LiveProbe.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        ImageInput? input = null;
        ImageGenerationResult result;
        if (editing)
        {
            input = OpenAIImage25LiveProbe.CreateSyntheticInput();
            result = await probe.Images.EditImagesAsync(new ImageEditRequest
            {
                Model = model, InputImages = [input], Count = 1, Size = ImageSize.Pixels(1024, 1024), Quality = ImageQuality.Low, OutputFormat = ImageOutputFormat.Png,
                Prompt = "Keep the red square on the white background. Add one small blue circle in the upper-right white corner. No text."
            }, timeout.Token);
        }
        else
            result = await probe.Images.GenerateImagesAsync(new ImageGenerationRequest
            {
                Model = model, Count = 1, Size = ImageSize.Pixels(1024, 1024), Quality = ImageQuality.Low, OutputFormat = ImageOutputFormat.Png,
                Prompt = "A single flat red square centered on a plain white canvas. No words, shadows, frames, or other objects."
            }, timeout.Token);
        await probe.ValidateAndSaveAsync(result, model, editing, input);
    }
}
