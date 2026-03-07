using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// 모든 테스트 모듈의 공통 베이스.
/// AIServiceTestBase와 동일한 라이프사이클을 제공하되, partial 없이 독립 모듈 단위로 동작.
/// </summary>
[TestClass]
public abstract class TestModuleBase
{
    protected AIService AI { get; private set; } = null!;
    protected string TestImagePath { get; private set; } = null!;

    protected abstract AIService CreateAIService();
    protected virtual AIModel? GetAlternativeModel() => null;
    protected virtual void SetupReasoningEffort() { }

    // Feature support flags — 기본값은 OpenAI 기준. 필요시 override.
    protected virtual bool SupportsMultimodal() => true;
    protected virtual bool SupportsFunctionCalling() => true;
    protected virtual bool SupportsArrayParameter() => false;
    protected virtual bool SupportsAudio() => true;
    protected virtual bool SupportsImageGeneration() => true;
    protected virtual bool SupportsWebSearch() => false;
    protected virtual bool SupportsReasoning() => false;
    protected virtual bool SupportsStructuredOutput() => true;

    protected async Task RunIfSupported(Func<bool> isSupported, Func<Task> testAction, string featureName)
    {
        if (!isSupported())
        {
            Console.WriteLine($"[{GetType().Name}] {featureName} not supported, skipping");
            Assert.Inconclusive($"{featureName} not supported by {AI.GetType().Name}");
            return;
        }
        await testAction();
    }

    [TestInitialize]
    public virtual void TestInitialize()
    {
        AI = CreateAIService();
        SetupTestImage();
        Console.WriteLine($"[Module Init] {GetType().Name} | {AI.GetType().Name} | {AI.Model}");
    }

    [TestCleanup]
    public virtual void TestCleanup()
    {
        Console.WriteLine("[Module Cleanup] Done");
    }

    private void SetupTestImage()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAssets", "test_image.png");
        if (!File.Exists(path))
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAssets", "test_image.jpg");
        if (!File.Exists(path))
            throw new FileNotFoundException("Test image not found in TestAssets folder.");
        TestImagePath = path;
    }

    protected async Task<(string Content, int ChunkCount)> StreamAndCollectAsync(string prompt)
    {
        var content = "";
        var chunkCount = 0;
        await foreach (var chunk in AI.StreamAsync(prompt))
        {
            content += chunk;
            chunkCount++;
        }
        return (content, chunkCount);
    }
}
