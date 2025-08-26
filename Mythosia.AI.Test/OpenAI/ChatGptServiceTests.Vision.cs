using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using System;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// Vision 모델을 사용한 이미지 분석 테스트
        /// </summary>
        [TestMethod]
        public async Task VisionModelTest()
        {
            try
            {
                // Vision을 지원하는 모델로 전환
                AI.ActivateChat.ChangeModel(AIModel.Gpt5);
                Console.WriteLine($"[Vision Test] Using model: {AI.ActivateChat.Model}");

                // MessageBuilder를 사용한 이미지 분석
                var response = await AI
                    .BeginMessage()
                    .AddText("What do you see in this image?")
                    .AddImage(TestImagePath)
                    .SendAsync();

                Assert.IsNotNull(response);
                Console.WriteLine($"[Vision Analysis] {response}");

                // 편의 메서드 사용
                var response2 = await AI.GetCompletionWithImageAsync(
                    "Describe the colors in this image",
                    TestImagePath
                );

                Assert.IsNotNull(response2);
                Console.WriteLine($"[Vision Colors] {response2}");

                // 다른 Vision 지원 모델 테스트
                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
                Console.WriteLine($"[Vision Test] Changed to model: {AI.ActivateChat.Model}");

                var response3 = await AI.GetCompletionWithImageAsync(
                    "What objects can you identify in this image?",
                    TestImagePath
                );

                Assert.IsNotNull(response3);
                Console.WriteLine($"[Vision Objects] {response3}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Vision Error] {ex.Message}");
                if (ex is Mythosia.AI.Exceptions.AIServiceException aiEx)
                {
                    Console.WriteLine($"[Error Details] {aiEx.ErrorDetails}");
                }
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 스트리밍과 함께 이미지 사용 테스트
        /// </summary>
        [TestMethod]
        public async Task StreamingWithImageTest()
        {
            try
            {
                string fullResponse = "";

                await AI
                    .BeginMessage()
                    .AddText("Describe this image in detail, step by step:")
                    .AddImage(TestImagePath)
                    .StreamAsync(chunk =>
                    {
                        fullResponse += chunk;
                        Console.Write(chunk);
                    });

                Console.WriteLine(); // New line after streaming
                Assert.IsNotNull(fullResponse);
                Assert.IsTrue(fullResponse.Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Streaming Image Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 현재 사용 가능한 Vision 모델 확인 테스트
        /// </summary>
        [TestMethod]
        public async Task CheckAvailableVisionModelsTest()
        {
            try
            {
                var visionModels = new[]
                {
                    (AIModel.Gpt4Vision, "gpt-4-vision-preview"),
                    (AIModel.Gpt4o240806, "gpt-4o-2024-08-06"),
                    (AIModel.Gpt4oLatest, "chatgpt-4o-latest")
                };

                foreach (var (model, modelName) in visionModels)
                {
                    try
                    {
                        AI.ActivateChat.ChangeModel(model);
                        Console.WriteLine($"\n[Testing Model] {modelName}");

                        var response = await AI.GetCompletionAsync("Say 'Hello' if you can process images.");
                        Console.WriteLine($"[Model {modelName}] Available: YES");
                        Console.WriteLine($"[Response] {response}");
                    }
                    catch (Exception modelEx)
                    {
                        Console.WriteLine($"[Model {modelName}] Available: NO");
                        Console.WriteLine($"[Error] {modelEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Model Check Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}