namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 이미지 생성 테스트 (지원하는 서비스만)
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("ImageGeneration")]
    [TestMethod]
    public async Task ImageGenerationTest()
    {
        await RunIfSupported(
            () => SupportsImageGeneration(),
            async () =>
            {
                // 이미지 바이트 배열 생성
                var imageData = await AI.GenerateImageAsync(
                    "A simple test pattern with geometric shapes",
                    "1024x1024"
                );

                Assert.IsNotNull(imageData);
                Assert.IsTrue(imageData.Length > 0);
                Console.WriteLine($"[Image Generation] Generated {imageData.Length} bytes");

                // 이미지 URL 생성
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