using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// 이미지 생성 테스트 (DALL-E)
        /// </summary>
        [TestMethod]
        public async Task ImageGenerationTest()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // 이미지 생성 (byte array)
                var imageData = await gptService.GenerateImageAsync(
                    "A simple test pattern with geometric shapes",
                    "1024x1024"
                );

                Assert.IsNotNull(imageData);
                Assert.IsTrue(imageData.Length > 0);
                Console.WriteLine($"[Image Generation] Generated image size: {imageData.Length} bytes");

                // 이미지 URL 생성
                var imageUrl = await gptService.GenerateImageUrlAsync(
                    "A peaceful landscape for testing",
                    "1024x1024"
                );

                Assert.IsNotNull(imageUrl);
                Assert.IsTrue(imageUrl.StartsWith("http"));
                Console.WriteLine($"[Image URL] {imageUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Image Generation Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}