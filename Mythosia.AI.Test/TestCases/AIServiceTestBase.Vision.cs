using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// Vision 기능 테스트 (지원하는 서비스만)
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Vision")]
    [TestMethod]
    public async Task VisionTest()
    {
        await RunIfSupported(
            () => SupportsMultimodal(),
            async () =>
            {
                var response = await AI.GetCompletionWithImageAsync(
                    "What do you see in this image? Be brief.",
                    TestImagePath
                );

                Assert.IsNotNull(response);
                Console.WriteLine($"[Vision Response] {response}");

                // 스트리밍과 함께 이미지 사용
                string streamedResponse = "";
                await AI.BeginMessage()
                    .AddText("Describe the colors in this image")
                    .AddImage(TestImagePath)
                    .StreamAsync(chunk =>
                    {
                        streamedResponse += chunk;
                        Console.Write(chunk);
                    });

                Console.WriteLine();
                Assert.IsTrue(streamedResponse.Length > 0);
            },
            "Vision"
        );
    }

    /// <summary>
    /// 멀티모달 메시지 빌더 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Vision")]
    [TestMethod]
    public async Task MultimodalMessageBuilderTest()
    {
        await RunIfSupported(
            () => SupportsMultimodal(),
            async () =>
            {
                var multimodalMessage = MessageBuilder.Create()
                    .AddText("What do you see in this image?")
                    .AddImage(TestImagePath)
                    .Build();

                string imageResponse = await AI.GetCompletionAsync(multimodalMessage);
                Assert.IsNotNull(imageResponse);
                Console.WriteLine($"[MessageBuilder Multimodal] {imageResponse}");

                // High detail 이미지
                var detailMessage = MessageBuilder.Create()
                    .AddText("Describe this image in detail")
                    .AddImage(TestImagePath)
                    .WithHighDetail()
                    .Build();

                string detailResponse = await AI.GetCompletionAsync(detailMessage);
                Assert.IsNotNull(detailResponse);
                Console.WriteLine($"[High Detail Response] {detailResponse}");
            },
            "Multimodal MessageBuilder"
        );
    }

    /// <summary>
    /// 다중 이미지 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Vision")]
    [TestMethod]
    public async Task MultipleImagesTest()
    {
        await RunIfSupported(
            () => SupportsMultimodal(),
            async () =>
            {
                try
                {
                    // 같은 이미지를 두 번 사용 (테스트용)
                    var multiImageResponse = await AI
                        .BeginMessage()
                        .AddText("Compare these two images:")
                        .AddImage(TestImagePath)
                        .AddImage(TestImagePath)
                        .SendAsync();

                    Assert.IsNotNull(multiImageResponse);
                    Console.WriteLine($"[Multi-Image] {multiImageResponse}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Multi-Image Error] {ex.Message}");
                    Assert.Fail(ex.Message);
                }
            },
            "Multiple Images"
        );
    }

    /// <summary>
    /// 이미지와 함께 스트리밍 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("Vision")]
    [TestMethod]
    public async Task StreamingWithImageTest()
    {
        await RunIfSupported(
            () => SupportsMultimodal(),
            async () =>
            {
                string fullResponse = "";
                int chunkCount = 0;

                await foreach (var chunk in AI
                    .BeginMessage()
                    .AddText("Describe this image step by step")
                    .AddImage(TestImagePath)
                    .StreamAsync())
                {
                    fullResponse += chunk;
                    chunkCount++;
                    Console.Write(chunk);
                }

                Console.WriteLine($"\n[Image Stream] Chunks: {chunkCount}, Total: {fullResponse.Length}");
                Assert.IsTrue(chunkCount >= 1, $"Expected at least 1 chunk but got {chunkCount}.");
                Assert.IsTrue(fullResponse.Length > 50);
            },
            "Streaming with Image"
        );
    }
}