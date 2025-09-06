using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]

namespace Mythosia.AI.Tests;

/// <summary>
/// AIService 공통 테스트 베이스 클래스
/// </summary>
[TestClass]
public abstract partial class AIServiceTestBase
{
    protected AIService AI { get; private set; }
    protected string TestImagePath { get; private set; }

    #region Abstract Methods - 각 구현체에서 제공

    /// <summary>
    /// 각 구현체(Gemini, Claude 등)에서 인스턴스를 생성해 반환
    /// </summary>
    protected abstract AIService CreateAIService();

    #endregion

    #region Virtual Methods - 기능 지원 여부

    protected virtual bool SupportsMultimodal() => false;
    protected virtual bool SupportsFunctionCalling() => false;
    protected virtual bool SupportsArrayParameter() => false;
    protected virtual bool SupportsAudio() => false;
    protected virtual bool SupportsImageGeneration() => false;
    protected virtual bool SupportsWebSearch() => false;
    protected virtual AIModel? GetAlternativeModel() => null;

    #endregion

    #region Test Initialize & Cleanup

    [TestInitialize]
    public virtual void TestInitialize()
    {
        AI = CreateAIService();
        SetupTestImage();
        Console.WriteLine($"[Test Initialize] Service: {AI.GetType().Name}, Model: {AI.ActivateChat.Model}");
    }

    [TestCleanup]
    public virtual void TestCleanup()
    {
        Console.WriteLine("[Test Cleanup] Completed");
    }

    private void SetupTestImage()
    {
        var testAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAssets", "test_image.png");

        if (!File.Exists(testAssetsPath))
        {
            testAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestAssets", "test_image.jpg");
        }

        if (!File.Exists(testAssetsPath))
        {
            throw new FileNotFoundException(
                $"Test image not found. Please add 'test_image.png' or 'test_image.jpg' to the TestAssets folder.");
        }

        TestImagePath = testAssetsPath;
        Console.WriteLine($"[Test Setup] Using test image: {TestImagePath}");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 조건부 테스트 실행 헬퍼
    /// </summary>
    protected async Task RunIfSupported(
        Func<bool> isSupported,
        Func<Task> testAction,
        string featureName)
    {
        if (!isSupported())
        {
            Console.WriteLine($"[{GetType().Name}] {featureName} not supported, skipping test");
            Assert.Inconclusive($"{featureName} not supported by {AI.GetType().Name}");
            return;
        }

        await testAction();
    }

    /// <summary>
    /// 스트리밍 수집 헬퍼
    /// </summary>
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

    #endregion
}