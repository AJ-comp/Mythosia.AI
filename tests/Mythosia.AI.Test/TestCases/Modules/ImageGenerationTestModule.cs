using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// 이미지 생성 테스트.
/// 원본: AIServiceTestBase.ImageGeneration.cs
/// </summary>
[TestClass]
public abstract class ImageGenerationTestModule : TestModuleBase
{
    [TestCategory("ImageGeneration")]
    [TestMethod]
    public async Task ImageGenerationTest()
    {
        await RunIfSupported(
            () => SupportsImageGeneration(),
            async () =>
            {
                var imageData = await AI.GenerateImageAsync(
                    "A simple test pattern with geometric shapes",
                    "1024x1024"
                );

                Assert.IsNotNull(imageData);
                Assert.IsTrue(imageData.Length > 0);
                Console.WriteLine($"[Image Generation] Generated {imageData.Length} bytes");

                var imageUrl = await AI.GenerateImageUrlAsync(
                    "A peaceful landscape for testing",
                    "1024x1024"
                );

                Assert.IsNotNull(imageUrl);
                Assert.IsTrue(imageUrl.StartsWith("http"));
                Console.WriteLine($"[Image URL] {imageUrl}");
            },
            "Image Generation"
        );
    }
}
