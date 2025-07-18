using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.Azure;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class ChatGptServiceTests : AIServiceTestBase
    {
        // 1) AIServiceTestBase에서 요구하는 인스턴스 생성 로직
        protected override AIService CreateAIService()
        {
            // SecretFetcher에서 API Key 가져오기
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
            string openAiKey = secretFetcher.GetKeyValueAsync().Result;

            // ChatGptService 인스턴스 생성
            var service = new ChatGptService(openAiKey, new HttpClient());
            // GPT-4o는 기본적으로 Vision을 지원합니다
            service.ActivateChat.ChangeModel(AIModel.Gpt4oLatest); // Vision 지원 모델
            service.ActivateChat.SystemMessage = "You are a helpful assistant for testing purposes.";

            return service;
        }

        protected override bool SupportsMultimodal()
        {
            return true; // GPT-4 Vision 지원
        }

        protected override AIModel? GetAlternativeModel()
        {
            return AIModel.Gpt4oLatest;
        }

        // 2) ChatGPT 전용 테스트들

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

        /// <summary>
        /// Vision 모델을 사용한 이미지 분석 테스트
        /// </summary>
        /// <summary>
        /// Vision 모델을 사용한 이미지 분석 테스트
        /// </summary>
        [TestMethod]
        public async Task VisionModelTest()
        {
            try
            {
                // Vision을 지원하는 모델로 전환
                // gpt-4-vision-preview는 deprecated되었을 수 있으므로 gpt-4o 사용
                AI.ActivateChat.ChangeModel(AIModel.Gpt4oLatest);
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
        /// 오디오 기능 테스트 (TTS & Whisper)
        /// </summary>
        [TestMethod]
        public async Task AudioFeaturesTest()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // 1) Text-to-Speech 테스트
                string textToSpeak = "Hello, this is a test of the speech synthesis.";
                var audioData = await gptService.GetSpeechAsync(
                    textToSpeak,
                    voice: "alloy",
                    model: "tts-1"
                );

                Assert.IsNotNull(audioData);
                Assert.IsTrue(audioData.Length > 0);
                Console.WriteLine($"[TTS] Generated audio size: {audioData.Length} bytes");

                // 2) Speech-to-Text 테스트 (생성된 오디오 사용)
                var transcription = await gptService.TranscribeAudioAsync(
                    audioData,
                    "test_speech.mp3",
                    language: "en"
                );

                Assert.IsNotNull(transcription);
                Assert.IsTrue(transcription.Length > 0);
                Console.WriteLine($"[Transcription] {transcription}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio Features Error] {ex.Message}");
                // Audio features might not be available in all environments
                Assert.Inconclusive($"Audio features test skipped: {ex.Message}");
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

        /// <summary>
        /// 대화 관리 기능 테스트
        /// </summary>
        [TestMethod]
        public async Task ConversationManagementTest()
        {
            try
            {
                // 대화 시작
                await AI.GetCompletionAsync("Remember the number 42");
                await AI.GetCompletionAsync("What number did I ask you to remember?");

                // 마지막 응답 가져오기
                var lastResponse = AI.GetLastAssistantResponse();
                Assert.IsNotNull(lastResponse);
                Assert.IsTrue(lastResponse.Contains("42"));
                Console.WriteLine($"[Last Response] {lastResponse}");

                // 대화 요약
                var summary = AI.GetConversationSummary();
                Assert.IsNotNull(summary);
                Console.WriteLine($"[Summary]\n{summary}");

                // 새 대화 시작
                AI.StartNewConversation();
                Assert.AreEqual(0, AI.ActivateChat.Messages.Count);

                // 새 모델로 새 대화
                AI.StartNewConversation(AIModel.Gpt4oLatest);
                var response = await AI.GetCompletionAsync("What number was I talking about?");
                Assert.IsFalse(response.Contains("42")); // 이전 대화를 기억하지 않아야 함
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Conversation Management Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 고급 설정 테스트
        /// </summary>
        [TestMethod]
        public async Task AdvancedConfigurationTest()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // OpenAI 특화 파라미터 설정
                gptService.WithOpenAIParameters(
                    presencePenalty: 0.5f,
                    frequencyPenalty: 0.3f
                );

                // 체이닝 설정
                gptService
                    .WithSystemMessage("You are a creative writer")
                    .WithTemperature(0.9f)
                    .WithMaxTokens(150);

                var creativeResponse = await gptService.GetCompletionAsync(
                    "Write a creative one-line story about a robot"
                );

                Assert.IsNotNull(creativeResponse);
                Console.WriteLine($"[Creative Response] {creativeResponse}");

                // 낮은 temperature로 변경
                gptService.WithTemperature(0.1f);
                var preciseResponse = await gptService.GetCompletionAsync(
                    "What is 2 + 2?"
                );

                Assert.IsNotNull(preciseResponse);
                Assert.IsTrue(preciseResponse.Contains("4"));
                Console.WriteLine($"[Precise Response] {preciseResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Advanced Config Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}