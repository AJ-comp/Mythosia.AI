using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.Azure;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// Cross-provider 전환 테스트.
/// 원본: AIServiceTestBase.CrossProvider.cs
/// </summary>
[TestClass]
public abstract class CrossProviderTestModule : TestModuleBase
{
    [TestCategory("CrossProvider"), TestMethod]
    public async Task ToClaude()
    {
        await RunIfSupported(() => SupportsFunctionCalling(), async () =>
        {
            Console.WriteLine($"\n========== Starting with {AI.Provider} ==========");
            AI.WithFunction<string>("get_user_id", "Get user ID from username", ("username", "Username", true), username => $"user_{username}_123")
              .WithFunction<string>("get_user_details", "Get user details from ID", ("user_id", "User ID", true),
                userId => JsonSerializer.Serialize(new { id = userId, name = "Test User", email = $"{userId}@example.com", status = "active" }));

            Console.WriteLine($"\n[Phase 1] Executing functions with {AI.Provider}");
            var response1 = await AI.GetCompletionAsync("Get the user ID for username 'john_doe' and then get the details");
            Console.WriteLine($"Response: {response1}");
            var functionCalls = AI.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).ToList();
            Assert.IsTrue(functionCalls.Count >= 1, $"Expected at least 1 function call, got {functionCalls.Count}");

            Console.WriteLine($"\n========== Switching to Claude ==========");
            var messageCountBefore = AI.ActivateChat.Messages.Count;
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret");
            string apiKey = await secretFetcher.GetKeyValueAsync();
            var newService = new ClaudeService(apiKey, new HttpClient()).CopyFrom(AI);
            newService.ChangeModel(AIModel.ClaudeSonnet4_250514);
            Assert.AreEqual(messageCountBefore, newService.ActivateChat.Messages.Count);

            try
            {
                var response2 = await newService.GetCompletionAsync("Based on the user information we just retrieved, what is the user's email?");
                Assert.IsTrue(response2.Contains("@example.com") || response2.Contains("email") || response2.Contains("user_john_doe_123"));
            }
            catch (Exception ex) { Console.WriteLine($"[Claude Context Error] {ex.GetType().Name}: {ex.Message}"); Assert.Fail($"Claude context test failed: {ex.Message}"); }

            Console.WriteLine($"\n[Phase 3] New function call with Claude");
            var response3 = await newService.GetCompletionAsync("Now get the details for a different user: 'alice'");
            Console.WriteLine($"Claude Function Response: {response3}");
            var claudeFunctionCalls = newService.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).Skip(functionCalls.Count).ToList();
            Assert.IsTrue(claudeFunctionCalls.Count > 0, "Claude should have made new function calls");
        }, "Cross-Provider Function Transition to Claude");
    }

    [TestCategory("CrossProvider"), TestMethod]
    public async Task ToGpt4o()
    {
        await RunIfSupported(() => SupportsFunctionCalling(), async () =>
        {
            Console.WriteLine($"\n========== Starting with {AI.Provider} ==========");
            AI.WithFunction<string>("get_weather", "Get weather for a city", ("city", "City name", true),
                city => JsonSerializer.Serialize(new { city, temperature = 22, condition = "sunny", humidity = 65 }))
              .WithFunction<double, double>("calculate_distance", "Calculate distance between coordinates", ("lat1", "Latitude 1", true), ("lon1", "Longitude 1", true),
                (lat, lon) => $"Distance calculated: {Math.Sqrt(lat * lat + lon * lon):F2} km");

            var response1 = await AI.GetCompletionAsync("What's the weather in Seoul? And calculate the distance from coordinates 37.5, 127.0");
            Console.WriteLine($"Response: {response1}");
            var functionCalls = AI.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).ToList();
            Assert.IsTrue(functionCalls.Count >= 1);

            Console.WriteLine($"\n========== Switching to OpenAI GPT-4 ==========");
            var messageCountBefore = AI.ActivateChat.Messages.Count;
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
            string openAiKey = await secretFetcher.GetKeyValueAsync();
            var chatGptService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
            chatGptService.ChangeModel(AIModel.Gpt4oMini);
            Assert.AreEqual(messageCountBefore, chatGptService.ActivateChat.Messages.Count);

            try
            {
                var response2 = await chatGptService.GetCompletionAsync("Based on the weather information we got, is it a good day for outdoor activities?");
                Console.WriteLine($"OpenAI Response: {response2}");
                Assert.IsTrue(response2.ToLower().Contains("sunny") || response2.ToLower().Contains("good") || response2.ToLower().Contains("yes") || response2.ToLower().Contains("outdoor"));
            }
            catch (Exception ex) { Console.WriteLine($"[Error during OpenAI context test] {ex.Message}"); Assert.Fail("OpenAI context test failed"); }

            var response3 = await chatGptService.GetCompletionAsync("Now check the weather in Tokyo");
            Console.WriteLine($"OpenAI Function Response: {response3}");
            var openAIFunctionCalls = chatGptService.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).Skip(functionCalls.Count).ToList();
            Assert.IsTrue(openAIFunctionCalls.Count > 0, "OpenAI should have made new function calls");
        }, "Cross-Provider Function Transition to OpenAI");
    }

    [TestCategory("CrossProvider"), TestMethod]
    public async Task ToOpenAIo3()
    {
        await RunIfSupported(() => SupportsFunctionCalling(), async () =>
        {
            Console.WriteLine($"\n========== Starting with {AI.Provider} ==========");
            AI.WithFunction<string>("get_weather", "Get weather for a city", ("city", "City name", true),
                city => JsonSerializer.Serialize(new { city, temperature = 22, condition = "sunny", humidity = 65 }))
              .WithFunction<double, double>("calculate_distance", "Calculate distance between coordinates", ("lat1", "Latitude 1", true), ("lon1", "Longitude 1", true),
                (lat, lon) => $"Distance calculated: {Math.Sqrt(lat * lat + lon * lon):F2} km");

            var response1 = await AI.GetCompletionAsync("What's the weather in Seoul? And calculate the distance from coordinates 37.5, 127.0");
            Console.WriteLine($"Response: {response1}");
            var functionCalls = AI.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).ToList();
            Assert.IsTrue(functionCalls.Count >= 1);

            var messageCountBefore = AI.ActivateChat.Messages.Count;
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
            string openAiKey = await secretFetcher.GetKeyValueAsync();
            var chatGptService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
            chatGptService.ChangeModel(AIModel.o3);
            Assert.AreEqual(messageCountBefore, chatGptService.ActivateChat.Messages.Count);

            try
            {
                var response2 = await chatGptService.GetCompletionAsync("Based on the weather information we got, is it a good day for outdoor activities?");
                Console.WriteLine($"OpenAI Response: {response2}");
                Assert.IsTrue(response2.ToLower().Contains("sunny") || response2.ToLower().Contains("good") || response2.ToLower().Contains("yes") || response2.ToLower().Contains("outdoor"));
            }
            catch (Exception ex) { Console.WriteLine($"[Error during OpenAI context test] {ex.Message}"); Assert.Fail("OpenAI context test failed"); }

            var response3 = await chatGptService.GetCompletionAsync("Now check the weather in Tokyo");
            Console.WriteLine($"OpenAI Function Response: {response3}");
            var openAIFunctionCalls = chatGptService.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).Skip(functionCalls.Count).ToList();
            Assert.IsTrue(openAIFunctionCalls.Count > 0, "OpenAI should have made new function calls");
        }, "Cross-Provider Function Transition to OpenAI");
    }

    [TestCategory("CrossProvider"), TestMethod]
    public async Task ThreeWayRoundTrip_FunctionOn()
    {
        await RunIfSupported(() => SupportsFunctionCalling(), async () =>
        {
            Console.WriteLine($"\n========== [Phase 1] Starting with {AI.Provider} ==========");
            AI.WithFunction<string>("get_stock_price", "Get stock price for a ticker symbol", ("ticker", "Stock ticker symbol", true),
                ticker => JsonSerializer.Serialize(new { ticker, price = 150.25, currency = "USD" }));

            var response1 = await AI.GetCompletionAsync("Get the stock price for AAPL");
            Console.WriteLine($"[Phase 1 Response] {response1}");
            var phase1FuncCount = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            Assert.IsTrue(phase1FuncCount >= 1);
            var totalMessagesPhase1 = AI.ActivateChat.Messages.Count;

            Console.WriteLine($"\n========== [Phase 2] Switching to Claude ==========");
            var claudeKey = await new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret").GetKeyValueAsync();
            var claudeService = new ClaudeService(claudeKey, new HttpClient()).CopyFrom(AI);
            claudeService.ChangeModel(AIModel.ClaudeHaiku4_5_251001);
            Assert.AreEqual(totalMessagesPhase1, claudeService.ActivateChat.Messages.Count);
            var response2 = await claudeService.GetCompletionAsync("Now also get the stock price for MSFT");
            Console.WriteLine($"[Phase 2 Claude Response] {response2}");
            var phase2FuncCount = claudeService.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            Assert.IsTrue(phase2FuncCount > phase1FuncCount);
            var totalMessagesPhase2 = claudeService.ActivateChat.Messages.Count;

            Console.WriteLine($"\n========== [Phase 3] Switching to ChatGPT ==========");
            var openAiKey = await new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret").GetKeyValueAsync();
            var gptService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(claudeService);
            gptService.ChangeModel(AIModel.Gpt4oMini);
            Assert.AreEqual(totalMessagesPhase2, gptService.ActivateChat.Messages.Count);
            var response3 = await gptService.GetCompletionAsync("What were the stock prices we looked up? Summarize them.");
            Console.WriteLine($"[Phase 3 GPT Response] {response3}");
            Assert.IsNotNull(response3);

            Console.WriteLine($"\n========== [Phase 4] Switching to Gemini 2.5 ==========");
            var geminiKey = await new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret").GetKeyValueAsync();
            var geminiService = new GeminiService(geminiKey, new HttpClient()).CopyFrom(gptService);
            geminiService.ChangeModel(AIModel.Gemini2_5Flash);
            var response4 = await geminiService.GetCompletionAsync("Based on the prices, which stock is more expensive?");
            Console.WriteLine($"[Phase 4 Gemini Response] {response4}");
            Assert.IsNotNull(response4);
        }, "Cross-Provider 3-Way Round Trip (Function ON)");
    }

    [TestCategory("CrossProvider"), TestMethod]
    public async Task FunctionOff_WithFunctionHistory()
    {
        await RunIfSupported(() => SupportsFunctionCalling(), async () =>
        {
            var failures = new List<string>();
            Console.WriteLine($"\n========== [Phase 1] Function calls with {AI.Provider} ==========");
            AI.WithFunction<string>("get_time", "Get current time for a timezone", ("timezone", "Timezone name", true),
                tz => JsonSerializer.Serialize(new { timezone = tz, time = "14:30:00", date = "2026-02-14" }));
            var response1 = await AI.GetCompletionAsync("What time is it in Seoul?");
            Console.WriteLine($"[Phase 1] {response1}");
            var funcMsgCount = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            Assert.IsTrue(funcMsgCount >= 1, "Should have function messages in history");

            Console.WriteLine($"\n========== [Phase 2] Switch to Claude with Functions DISABLED ==========");
            var claudeKey = await new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret").GetKeyValueAsync();
            var claudeService = new ClaudeService(claudeKey, new HttpClient()).CopyFrom(AI);
            claudeService.ChangeModel(AIModel.ClaudeHaiku4_5_251001);
            claudeService.FunctionsDisabled = true;
            try { var r = await claudeService.GetCompletionAsync("What did you tell me about the time?"); Console.WriteLine($"[Phase 2 Claude] {r}"); }
            catch (Exception ex) { Console.WriteLine($"Claude FAILED: {ex.Message}"); if (ex is AIServiceException aiEx && aiEx.ErrorDetails != null) Console.WriteLine($"   ErrorDetails: {aiEx.ErrorDetails}"); failures.Add($"Claude: {ex.Message}"); }

            Console.WriteLine($"\n========== [Phase 3] Switch to ChatGPT Legacy with Functions DISABLED ==========");
            var openAiKey = await new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret").GetKeyValueAsync();
            var gptLegacyService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
            gptLegacyService.ChangeModel(AIModel.Gpt4oMini);
            gptLegacyService.FunctionsDisabled = true;
            try { var r = await gptLegacyService.GetCompletionAsync("What did you tell me about the time?"); Console.WriteLine($"[Phase 3 GPT Legacy] {r}"); }
            catch (Exception ex) { Console.WriteLine($"ChatGPT Legacy FAILED: {ex.Message}"); failures.Add($"ChatGPT Legacy: {ex.Message}"); }

            Console.WriteLine($"\n========== [Phase 4] Switch to ChatGPT New API with Functions DISABLED ==========");
            var gptNewService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
            gptNewService.ChangeModel(AIModel.Gpt5Mini);
            gptNewService.FunctionsDisabled = true;
            try { var r = await gptNewService.GetCompletionAsync("What did you tell me about the time?"); Console.WriteLine($"[Phase 4 GPT New API] {r}"); }
            catch (Exception ex) { Console.WriteLine($"ChatGPT New API FAILED: {ex.Message}"); failures.Add($"ChatGPT New API: {ex.Message}"); }

            Console.WriteLine($"\n========== [Phase 5] Switch to Gemini with Functions DISABLED ==========");
            var geminiKey = await new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret").GetKeyValueAsync();
            var geminiService = new GeminiService(geminiKey, new HttpClient()).CopyFrom(AI);
            geminiService.ChangeModel(AIModel.Gemini2_5Flash);
            geminiService.FunctionsDisabled = true;
            try { var r = await geminiService.GetCompletionAsync("What did you tell me about the time?"); Console.WriteLine($"[Phase 5 Gemini] {r}"); }
            catch (Exception ex) { Console.WriteLine($"Gemini FAILED: {ex.Message}"); failures.Add($"Gemini: {ex.Message}"); }

            Console.WriteLine($"\n========== Results: Failures: {failures.Count} / 4 ==========");
            if (failures.Count > 0) Assert.Fail($"{failures.Count} service(s) failed:\n" + string.Join("\n", failures));
        }, "Cross-Provider Function OFF with Function History");
    }

    [TestCategory("CrossProvider"), TestMethod]
    public async Task ToGemini3_ThoughtSignatureMissing()
    {
        await RunIfSupported(() => SupportsFunctionCalling(), async () =>
        {
            Console.WriteLine($"\n========== [Phase 1] Function calls with {AI.Provider} ==========");
            AI.WithFunction<string>("get_weather", "Get weather for a city", ("city", "City name", true),
                city => JsonSerializer.Serialize(new { city, temp = 20, condition = "cloudy" }));
            var response1 = await AI.GetCompletionAsync("What's the weather in Tokyo?");
            Console.WriteLine($"[Phase 1] {response1}");
            Assert.IsTrue(AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function) >= 1);

            Console.WriteLine($"\n========== [Phase 2] Switch to Gemini 3 Flash ==========");
            var geminiKey = await new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret").GetKeyValueAsync();
            var geminiService = new GeminiService(geminiKey, new HttpClient()).CopyFrom(AI);
            geminiService.ChangeModel(AIModel.Gemini3FlashPreview);
            try
            {
                var response2 = await geminiService.GetCompletionAsync("Based on the weather info, should I bring an umbrella?");
                Console.WriteLine($"[Phase 2 Gemini 3] {response2}");
            }
            catch (Exception ex) { Console.WriteLine($"Gemini 3 REJECTED function history without ThoughtSignature: {ex.Message}"); }

            Console.WriteLine($"\n========== [Phase 3] New function call with Gemini 3 ==========");
            try
            {
                var response3 = await geminiService.GetCompletionAsync("Now check the weather in London");
                Console.WriteLine($"[Phase 3 Gemini 3 new func] {response3}");
            }
            catch (Exception ex) { Console.WriteLine($"Gemini 3 new function call failed: {ex.Message}"); }
        }, "Cross-Provider to Gemini 3 ThoughtSignature Test");
    }
}
