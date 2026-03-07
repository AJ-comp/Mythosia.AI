#if false
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.OpenAI;
using Mythosia.AI.Tests.Modules;

using Microsoft.VisualStudio.TestTools.UnitTesting;

// ── GPT-5 Reasoning Effort Specific Tests ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5
{
    /// <summary>
    /// GPT-5 전용 reasoning effort 파라미터 테스트.
    /// 원본: ChatGptServiceTests.cs → OpenAI_Gpt5_ReasoningTests
    /// </summary>
    [TestClass]
    public class ReasoningSpecific : TestModuleBase
    {
        protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5);
        protected override bool SupportsReasoning() => true;

        /// <summary>
        /// GPT-5 reasoning effort 파라미터 설정 및 응답 테스트
        /// Valid values: minimal, low, medium, high
        /// </summary>
        [TestCategory("ServiceSpecific")]
        [TestMethod]
        public async Task Gpt5_ReasoningEffort_CanBeConfigured()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // minimal reasoning effort로 빠른 응답
                gptService.WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.Minimal);
                var quickResponse = await gptService.GetCompletionAsync("What is 2+2?");
                Assert.IsNotNull(quickResponse);
                Console.WriteLine($"[Minimal Effort] {quickResponse}");

                // high reasoning effort로 심층 응답
                gptService.WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.High);
                var detailedResponse = await gptService.GetCompletionAsync(
                    "Explain briefly why the sky is blue in one sentence."
                );
                Assert.IsNotNull(detailedResponse);
                Console.WriteLine($"[High Effort] {detailedResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPT-5 Reasoning Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// GPT-5 체이닝 설정 테스트 (WithGpt5Parameters + WithSystemMessage 등)
        /// </summary>
        [TestCategory("ServiceSpecific")]
        [TestMethod]
        public async Task Gpt5_ChainingParameters_WorksCorrectly()
        {
            try
            {
                var gptService = (ChatGptService)AI;
                gptService
                    .WithGpt5Parameters(reasoningEffort: Gpt5Reasoning.Low)
                    .WithSystemMessage("You are a concise assistant. Answer in one word if possible.")
                    .WithMaxTokens(100);

                var response = await gptService.GetCompletionAsync(
                    "What color is the sun? Answer in one word."
                );
                Assert.IsNotNull(response);
                Assert.IsTrue(response.Length < 200, "Response should be concise");
                Console.WriteLine($"[Chained GPT-5 Response] {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPT-5 Chaining Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}
// ── GPT-5.3 Codex Reasoning Effort Specific Tests ──
namespace Mythosia.AI.Tests.OpenAI.Gpt5_3Codex
{
    /// <summary>
    /// GPT-5.3 전용 reasoning effort, verbosity 파라미터 테스트.
    /// 원본: ChatGptServiceTests.cs → OpenAI_Gpt5_3_ReasoningTests
    /// </summary>
    [TestClass]
    public class ReasoningSpecific : TestModuleBase
    {
        protected override AIService CreateAIService() => ChatGptServiceFactory.Create(AIModel.Gpt5_3Codex);
        protected override bool SupportsReasoning() => true;

        /// <summary>
        /// GPT-5.3 Codex reasoning effort 파라미터 설정 및 응답 테스트
        /// Valid values: low, medium (default), high, xhigh
        /// </summary>
        [TestCategory("ServiceSpecific")]
        [TestMethod]
        public async Task Gpt5_3Codex_ReasoningEffort_CanBeConfigured()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // medium reasoning effort (Codex default)
                gptService.WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.Medium);
                var mediumResponse = await gptService.GetCompletionAsync("What is 2+2?");
                Assert.IsNotNull(mediumResponse);
                Console.WriteLine($"[Medium Effort] {mediumResponse}");

                // high reasoning effort로 심층 응답
                gptService.WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.High);
                var highResponse = await gptService.GetCompletionAsync(
                    "Explain briefly why the sky is blue in one sentence."
                );
                Assert.IsNotNull(highResponse);
                Console.WriteLine($"[High Effort] {highResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPT-5.3 Reasoning Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// GPT-5.3 체이닝 설정 테스트 (WithGpt5_3Parameters + WithSystemMessage 등)
        /// </summary>
        [TestCategory("ServiceSpecific")]
        [TestMethod]
        public async Task Gpt5_3_ChainingParameters_WorksCorrectly()
        {
            try
            {
                var gptService = (ChatGptService)AI;
                gptService
                    .WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.Low)
                    .WithSystemMessage("You are a concise assistant. Answer in one word if possible.")
                    .WithMaxTokens(100);

                var response = await gptService.GetCompletionAsync(
                    "What color is the sun? Answer in one word."
                );
                Assert.IsNotNull(response);
                Assert.IsTrue(response.Length < 200, "Response should be concise");
                Console.WriteLine($"[Chained GPT-5.3 Response] {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPT-5.3 Chaining Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// GPT-5.3 Codex에서 'none' reasoning effort가 자동으로 'low'로 조정되는지 테스트
        /// </summary>
        [TestCategory("ServiceSpecific")]
        [TestMethod]
        public async Task Gpt5_3Codex_NoneEffort_AdjustsToLow()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // Codex에서 None은 지원되지 않으므로 자동으로 Low로 조정되어야 함
                gptService.WithGpt5_3Parameters(reasoningEffort: Gpt5_3Reasoning.None);
                var response = await gptService.GetCompletionAsync("What is 1+1?");
                Assert.IsNotNull(response);
                Console.WriteLine($"[None→Low Adjusted Response] {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPT-5.3 Codex None Effort Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }

        /// <summary>
        /// GPT-5.3 verbosity 설정 테스트
        /// </summary>
        [TestCategory("ServiceSpecific")]
        [TestMethod]
        public async Task Gpt5_3_Verbosity_CanBeConfigured()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                gptService.WithGpt5_3Parameters(
                    reasoningEffort: Gpt5_3Reasoning.Medium,
                    verbosity: Verbosity.Low
                );
                var lowVerbosityResponse = await gptService.GetCompletionAsync("What is gravity?");
                Assert.IsNotNull(lowVerbosityResponse);
                Console.WriteLine($"[Low Verbosity] {lowVerbosityResponse}");

                gptService.WithGpt5_3Parameters(
                    reasoningEffort: Gpt5_3Reasoning.Medium,
                    verbosity: Verbosity.High
                );
                var highVerbosityResponse = await gptService.GetCompletionAsync("What is gravity?");
                Assert.IsNotNull(highVerbosityResponse);
                Console.WriteLine($"[High Verbosity] {highVerbosityResponse}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPT-5.3 Verbosity Error] {ex.Message}");
                Assert.Fail(ex.Message);
            }
        }
    }
}
#endif
