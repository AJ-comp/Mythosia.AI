using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.Azure;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Mythosia.AI.Services.Perplexity;

namespace Mythosia.AI.Tests
{
    [TestClass]
    public class SonarServiceTests : AIServiceTestBase
    {
        protected override AIService CreateAIService()
        {
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "sonar-secret2");
            string sonarKey = secretFetcher.GetKeyValueAsync().Result;
            var service = new SonarService(sonarKey, new HttpClient());

            // 기본 설정
            service.ActivateChat.SystemMessage = "Be concise and factual.";
            return service;
        }

        protected override bool SupportsMultimodal()
        {
            return false; // Sonar는 텍스트만 지원
        }

        protected override AIModel? GetAlternativeModel()
        {
            return AIModel.PerplexitySonarPro;
        }

        /// <summary>
        /// Sonar 웹 검색 기능 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarWebSearchTest()
        {
            try
            {
                var sonarService = (SonarService)AI;

                // 웹 검색 활성화된 모델로 질의
                var searchResponse = await sonarService.GetCompletionWithSearchAsync(
                    "What are the latest developments in quantum computing as of 2024?",
                    domainFilter: null,
                    recencyFilter: "month"
                );

                Assert.IsNotNull(searchResponse);
                Assert.IsNotNull(searchResponse.Content);
                Assert.IsTrue(searchResponse.Content.Length > 0);

                Console.WriteLine($"[Search Response] {searchResponse.Content.Substring(0, Math.Min(300, searchResponse.Content.Length))}...");

                // 인용 확인
                if (searchResponse.Citations.Any())
                {
                    Console.WriteLine($"\n[Citations] Found {searchResponse.Citations.Count} citations:");
                    foreach (var citation in searchResponse.Citations.Take(3))
                    {
                        Console.WriteLine($"- {citation.Title}");
                        Console.WriteLine($"  URL: {citation.Url}");
                        Console.WriteLine($"  Snippet: {citation.Snippet.Substring(0, Math.Min(100, citation.Snippet.Length))}...");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Web Search Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 모델 변경 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarModelSwitchingTest()
        {
            try
            {
                var sonarService = (SonarService)AI;

                // Sonar Pro 모델 테스트
                sonarService.UseSonarPro();
                var proResponse = await sonarService.GetCompletionAsync(
                    "Explain the difference between machine learning and deep learning"
                );
                Assert.IsNotNull(proResponse);
                Console.WriteLine($"[Sonar Pro] {proResponse.Substring(0, Math.Min(200, proResponse.Length))}...");

                // Sonar Reasoning 모델 테스트
                sonarService.UseSonarReasoning();
                var reasoningResponse = await sonarService.GetCompletionAsync(
                    "If it takes 5 machines 5 minutes to make 5 widgets, how long would it take 100 machines to make 100 widgets?"
                );
                Assert.IsNotNull(reasoningResponse);
                Console.WriteLine($"[Sonar Reasoning] {reasoningResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Model Switching Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 도메인 필터링 검색 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarDomainFilteredSearchTest()
        {
            try
            {
                var sonarService = (SonarService)AI;

                // 특정 도메인에서만 검색
                var filteredResponse = await sonarService.GetCompletionWithSearchAsync(
                    "What are the latest AI research papers?",
                    domainFilter: new[] { "arxiv.org", "openai.com", "deepmind.com" },
                    recencyFilter: "week"
                );

                Assert.IsNotNull(filteredResponse.Content);
                Console.WriteLine($"[Filtered Search] {filteredResponse.Content.Substring(0, Math.Min(300, filteredResponse.Content.Length))}...");

                // 필터링된 도메인 확인
                var domains = filteredResponse.Citations
                    .Select(c => new Uri(c.Url).Host)
                    .Distinct()
                    .ToList();

                Console.WriteLine($"\n[Found Domains] {string.Join(", ", domains)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Domain Filter Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 스트리밍 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarStreamingTest()
        {
            try
            {
                string fullResponse = "";
                int chunkCount = 0;

                await AI.StreamCompletionAsync(
                    "What are the main benefits of renewable energy?",
                    chunk =>
                    {
                        fullResponse += chunk;
                        chunkCount++;
                        Console.Write(chunk);
                    }
                );

                Console.WriteLine($"\n[Streaming Complete] Chunks: {chunkCount}");
                Assert.IsTrue(chunkCount > 0);
                Assert.IsTrue(fullResponse.Length > 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Streaming Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 멀티모달 미지원 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarMultimodalNotSupportedTest()
        {
            try
            {
                // 이미지 분석 시도
                var response = await AI.GetCompletionWithImageAsync(
                    "What's in this image?",
                    TestImagePath
                );

                // Sonar는 이미지를 무시하고 텍스트만 처리
                Assert.IsNotNull(response);
                Console.WriteLine($"[Image Attempt] {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Multimodal Error] {ex.Message}");
            }
        }

        /// <summary>
        /// Sonar 이미지 생성 미지원 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarImageGenerationNotSupportedTest()
        {
            try
            {
                await AI.GenerateImageAsync("test prompt");
                Assert.Fail("Should have thrown MultimodalNotSupportedException");
            }
            catch (MultimodalNotSupportedException ex)
            {
                Assert.AreEqual("Perplexity Sonar", ex.ServiceName);
                Assert.AreEqual("Image Generation", ex.RequestedFeature);
                Console.WriteLine($"[Expected Exception] {ex.Message}");
            }
        }

        /// <summary>
        /// Sonar 시간 기반 검색 필터 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarTimeBasedSearchTest()
        {
            try
            {
                var sonarService = (SonarService)AI;
                var timeFilters = new[] { "day", "week", "month", "year" };

                foreach (var filter in timeFilters)
                {
                    Console.WriteLine($"\n[Testing {filter} filter]");

                    var response = await sonarService.GetCompletionWithSearchAsync(
                        "Latest technology news",
                        recencyFilter: filter
                    );

                    Assert.IsNotNull(response.Content);
                    Console.WriteLine($"[{filter}] {response.Content.Substring(0, Math.Min(150, response.Content.Length))}...");
                    Console.WriteLine($"[{filter}] Citations: {response.Citations.Count}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Time Filter Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 대화 컨텍스트 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarConversationContextTest()
        {
            try
            {
                // 검색 없이 일반 대화
                await AI.GetCompletionAsync("I'm interested in renewable energy");
                await AI.GetCompletionAsync("Especially solar panels");

                var contextResponse = await AI.GetCompletionAsync(
                    "Based on what I mentioned, what specific aspect should I research?"
                );

                Assert.IsNotNull(contextResponse);
                Assert.IsTrue(
                    contextResponse.Contains("solar") ||
                    contextResponse.Contains("renewable") ||
                    contextResponse.Contains("energy")
                );
                Console.WriteLine($"[Context Response] {contextResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Context Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 토큰 카운팅 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarTokenCountingTest()
        {
            try
            {
                // 단일 프롬프트 토큰 카운트
                var prompt = "What is artificial intelligence?";
                var tokenCount = await AI.GetInputTokenCountAsync(prompt);

                Assert.IsTrue(tokenCount > 0);
                Console.WriteLine($"[Token Count - Prompt] {tokenCount}");

                // 대화 후 전체 토큰 카운트
                await AI.GetCompletionAsync("Tell me about machine learning");
                await AI.GetCompletionAsync("What about deep learning?");

                var totalTokens = await AI.GetInputTokenCountAsync();
                Assert.IsTrue(totalTokens > tokenCount);
                Console.WriteLine($"[Token Count - Total] {totalTokens}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Token Counting Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 복잡한 검색 쿼리 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarComplexSearchQueryTest()
        {
            try
            {
                var sonarService = (SonarService)AI;

                // 복잡한 multi-part 질문
                var complexResponse = await sonarService.GetCompletionWithSearchAsync(
                    "Compare the latest iPhone and Samsung Galaxy flagship phones in terms of camera quality, battery life, and price. Include specific model numbers and release dates.",
                    recencyFilter: "month"
                );

                Assert.IsNotNull(complexResponse.Content);
                Assert.IsTrue(complexResponse.Citations.Count > 0);

                Console.WriteLine($"[Complex Query Response] {complexResponse.Content.Substring(0, Math.Min(400, complexResponse.Content.Length))}...");
                Console.WriteLine($"\n[Citations Count] {complexResponse.Citations.Count}");

                // 응답에 구체적인 정보가 포함되어 있는지 확인
                var hasSpecificInfo =
                    complexResponse.Content.Contains("iPhone") &&
                    complexResponse.Content.Contains("Galaxy") &&
                    (complexResponse.Content.Contains("camera") ||
                     complexResponse.Content.Contains("battery") ||
                     complexResponse.Content.Contains("price"));

                Assert.IsTrue(hasSpecificInfo, "Response should contain specific comparison information");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Complex Query Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Sonar 에러 처리 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarErrorHandlingTest()
        {
            try
            {
                var sonarService = (SonarService)AI;

                // 잘못된 모델명으로 변경 시도
                try
                {
                    AI.ActivateChat.ChangeModel("invalid-model-name");
                    await AI.GetCompletionAsync("test");
                    Assert.Fail("Should have thrown an exception");
                }
                catch (Exception modelEx)
                {
                    Console.WriteLine($"[Expected Model Error] {modelEx.Message}");
                }

                // 매우 긴 입력으로 제한 테스트
                var veryLongPrompt = string.Concat(Enumerable.Repeat("This is a very long text. ", 1000));
                AI.ActivateChat.MaxTokens = 50;

                var response = await AI.GetCompletionAsync(veryLongPrompt);
                Assert.IsNotNull(response);
                Console.WriteLine($"[Long Input Response] Length: {response.Length}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error Handling] {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Sonar 검색 파라미터 조합 테스트
        /// </summary>
        [TestMethod]
        public async Task SonarSearchParameterCombinationTest()
        {
            try
            {
                var sonarService = (SonarService)AI;

                // 도메인 필터 + 시간 필터 조합
                var combinedResponse = await sonarService.GetCompletionWithSearchAsync(
                    "Latest breakthroughs in AI research",
                    domainFilter: new[] { "nature.com", "science.org", "arxiv.org" },
                    recencyFilter: "week"
                );

                Assert.IsNotNull(combinedResponse.Content);
                Console.WriteLine($"[Combined Search] {combinedResponse.Content.Substring(0, Math.Min(300, combinedResponse.Content.Length))}...");

                // 검색 결과 분석
                if (combinedResponse.Citations.Any())
                {
                    var uniqueDomains = combinedResponse.Citations
                        .Select(c => new Uri(c.Url).Host)
                        .Distinct()
                        .ToList();

                    Console.WriteLine($"\n[Search Analysis]");
                    Console.WriteLine($"Total Citations: {combinedResponse.Citations.Count}");
                    Console.WriteLine($"Unique Domains: {string.Join(", ", uniqueDomains)}");
                    Console.WriteLine($"First Citation Date: {combinedResponse.Citations.First().Title}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Parameter Combination Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}