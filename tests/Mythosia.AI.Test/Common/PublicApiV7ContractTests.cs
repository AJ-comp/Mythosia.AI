using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using System.Reflection;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class PublicApiV7ContractTests
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    [TestMethod]
    public void RemovedLegacyMembers_AreNotPartOfTheV7Surface()
    {
        Assert.IsNull(typeof(AIService).GetProperty("MaxMessageCount", PublicInstance));
        Assert.IsNull(typeof(AIService).GetMethod("GenerateImageAsync", PublicInstance));
        Assert.IsNull(typeof(AIService).GetMethod("GenerateImageUrlAsync", PublicInstance));
        Assert.IsNull(typeof(ChatBlock).GetMethod("RemoveFunctionMessages", PublicInstance));
        Assert.IsNull(typeof(AIModels.Anthropic).GetField(
            "ClaudeOpus4_250514",
            BindingFlags.Public | BindingFlags.Static));
        Assert.IsNull(typeof(AIModels.Anthropic).GetField(
            "ClaudeOpus4_1_250805",
            BindingFlags.Public | BindingFlags.Static));
    }

    [TestMethod]
    public void CurrentAnthropicModelIds_ArePartOfTheV7Surface()
    {
        Assert.AreEqual("claude-mythos-5", GetPublicConstant(nameof(AIModels.Anthropic.ClaudeMythos5)));
        Assert.AreEqual("claude-opus-5", GetPublicConstant(nameof(AIModels.Anthropic.ClaudeOpus5)));
        Assert.AreEqual("claude-sonnet-5", GetPublicConstant(nameof(AIModels.Anthropic.ClaudeSonnet5)));
    }

    [TestMethod]
    public void CurrentAnthropicThinkingEnums_ArePartOfTheV7Surface()
    {
        CollectionAssert.AreEqual(
            new[] { "Auto", "Low", "Medium", "High", "XHigh", "Max" },
            Enum.GetNames<ClaudeReasoningEffort>());
        CollectionAssert.AreEqual(
            new[] { "Omitted", "Summarized" },
            Enum.GetNames<ClaudeThinkingDisplay>());
    }

    [TestMethod]
    public void FunctionBatchContract_IsPartOfTheV7Surface()
    {
        CollectionAssert.AreEqual(
            new[] { "Sequential", "Parallel" },
            Enum.GetNames<FunctionExecutionMode>());
        Assert.AreEqual(
            FunctionExecutionMode.Sequential,
            FunctionCallingPolicy.Default.ExecutionMode);

        Assert.AreEqual(
            typeof(FunctionCallBatch),
            typeof(Message).GetProperty(nameof(Message.FunctionCallBatch))?.PropertyType);
        Assert.AreEqual(
            typeof(FunctionCallResultBatch),
            typeof(Message).GetProperty(nameof(Message.FunctionCallResultBatch))?.PropertyType);
        Assert.AreEqual(
            typeof(FunctionCall),
            typeof(StreamingContent).GetProperty(nameof(StreamingContent.FunctionCall))?.PropertyType);
        Assert.AreEqual(
            typeof(FunctionCallResult),
            typeof(StreamingContent).GetProperty(nameof(StreamingContent.FunctionResult))?.PropertyType);
    }

    [TestMethod]
    public void CustomProviderFunctionExtensionPoints_UseTypedV7Contracts()
    {
        const BindingFlags protectedInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        Assert.IsNull(typeof(AIService).GetMethod("ExtractFunctionCall", protectedInstance));

        var extract = typeof(AIService).GetMethod(
            "ExtractFunctionCalls",
            protectedInstance);
        Assert.IsNotNull(extract);
        Assert.AreEqual(typeof(ValueTuple<string, FunctionCallBatch>), extract.ReturnType);

        var process = typeof(AIService).GetMethod(
            "ProcessFunctionCallAsync",
            protectedInstance,
            binder: null,
            types: new[] { typeof(FunctionCall) },
            modifiers: null);
        Assert.IsNotNull(process);
        Assert.AreEqual(typeof(Task<FunctionCallResult>), process.ReturnType);
    }

    [TestMethod]
    public void ImageGenerationContract_LivesInAbstractionsAndIsImplementedByOpenAI()
    {
        Assert.AreSame(typeof(IAIService).Assembly, typeof(IImageGenerationService).Assembly);
        Assert.IsTrue(typeof(IImageGenerationService).IsAssignableFrom(typeof(OpenAIService)));

        AssertMethod(
            nameof(IImageGenerationService.GenerateImagesAsync),
            typeof(ImageGenerationRequest),
            typeof(CancellationToken));
        AssertMethod(
            nameof(IImageGenerationService.EditImagesAsync),
            typeof(ImageEditRequest),
            typeof(CancellationToken));
    }

    private static void AssertMethod(string name, params Type[] parameterTypes)
    {
        var method = typeof(IImageGenerationService).GetMethod(
            name,
            PublicInstance,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.IsNotNull(method, $"Expected {name} to be part of IImageGenerationService.");
        Assert.AreEqual(typeof(Task<ImageGenerationResult>), method.ReturnType);
    }

    private static string? GetPublicConstant(string name)
        => typeof(AIModels.Anthropic)
            .GetField(name, BindingFlags.Public | BindingFlags.Static)?
            .GetRawConstantValue() as string;
}
