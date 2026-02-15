using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Anthropic;
using Mythosia.AI.Services.Google;
using Mythosia.AI.Services.OpenAI;
using Mythosia.Azure;
using System.Text.Json;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
/// <summary>
/// Provider 간 Function Call 전환 테스트
/// </summary>
[TestCategory("Common")]
[TestCategory("FunctionCalling")]
[TestMethod]
public async Task CrossProviderToClaude()
{
    await RunIfSupported(
        () => SupportsFunctionCalling(),
        async () =>
        {
            Console.WriteLine($"\n========== Starting with {AI.Provider} ==========");
            
            // Step 1: Function 등록
            AI.WithFunction<string>(
                "get_user_id",
                "Get user ID from username",
                ("username", "Username", true),
                username => $"user_{username}_123"
            )
            .WithFunction<string>(
                "get_user_details",
                "Get user details from ID",
                ("user_id", "User ID", true),
                userId => JsonSerializer.Serialize(new
                {
                    id = userId,
                    name = "Test User",
                    email = $"{userId}@example.com",
                    status = "active"
                })
            );

            // Step 2: 첫 번째 Provider에서 Function 실행
            Console.WriteLine($"\n[Phase 1] Executing functions with {AI.Provider}");
            
            var response1 = await AI.GetCompletionAsync(
                "Get the user ID for username 'john_doe' and then get the details"
            );
            
            Console.WriteLine($"Response: {response1}");
            
            // Function 호출 확인
            var functionCalls = AI.ActivateChat.Messages
                .Where(m => m.Role == ActorRole.Function)
                .ToList();
            
            Assert.IsTrue(functionCalls.Count >= 1, 
                $"Expected at least 1 function call, got {functionCalls.Count}");
            
            // Function call 메타데이터 확인
            Console.WriteLine($"\nFunction calls made:");
            foreach (var fc in functionCalls)
            {
                if (fc.Metadata != null)
                {
                    var funcName = fc.Metadata.GetValueOrDefault("function_name")?.ToString();
                    var callId = fc.Metadata.GetValueOrDefault("function_call_id")?.ToString();
                    Console.WriteLine($"  - {funcName} (ID: {callId?.Substring(0, 8)}...)");
                }
            }

            // Step 3: Claude로 모델 전환
            Console.WriteLine($"\n========== Switching to Claude ==========");
            
            // 현재 메시지 수 저장
            var messageCountBefore = AI.ActivateChat.Messages.Count;

            // Claude 모델로 변경
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret");
            string apiKey = secretFetcher.GetKeyValueAsync().Result;

            var newService = new ClaudeService(apiKey, new HttpClient()).CopyFrom(AI);
            newService.ChangeModel(AIModel.Claude3_7SonnetLatest);
            
            // 메시지가 유지되는지 확인
            var messageCountAfter = newService.ActivateChat.Messages.Count;
            Assert.AreEqual(messageCountBefore, messageCountAfter, 
                "Messages should be preserved after model change");

            try
            {
                // Step 4: Claude에서 이전 컨텍스트를 활용한 질문
                var response2 = await newService.GetCompletionAsync(
                    "Based on the user information we just retrieved, what is the user's email?"
                );

                // 이메일 정보가 포함되어 있는지 확인
                Assert.IsTrue(
                    response2.Contains("@example.com") ||
                    response2.Contains("email") ||
                    response2.Contains("user_john_doe_123"),
                    "Claude should be able to reference the previous function results"
                );
            }
            catch(Exception ex)
            {
                Assert.Fail("Claude context test failed");
            }

            // Step 5: Claude에서 새로운 Function 호출
            Console.WriteLine($"\n[Phase 3] New function call with Claude");
            
            var response3 = await newService.GetCompletionAsync(
                "Now get the details for a different user: 'alice'"
            );
            
            Console.WriteLine($"Claude Function Response: {response3}");
            
            // 새로운 Function 호출 확인
            var claudeFunctionCalls = newService.ActivateChat.Messages
                .Where(m => m.Role == ActorRole.Function)
                .Skip(functionCalls.Count)
                .ToList();
            
            Assert.IsTrue(claudeFunctionCalls.Count > 0, 
                "Claude should have made new function calls");
            
            Console.WriteLine($"\nClaude function calls:");
            foreach (var fc in claudeFunctionCalls)
            {
                if (fc.Metadata != null)
                {
                    var funcName = fc.Metadata.GetValueOrDefault("function_name")?.ToString();
                    var toolUseId = fc.Metadata.GetValueOrDefault("claude_tool_use_id")?.ToString();
                    Console.WriteLine($"  - {funcName} (tool_use_id: {toolUseId?.Substring(0, Math.Min(8, toolUseId?.Length ?? 0))}...)");
                }
            }

            Console.WriteLine($"\n✅ Cross-provider transition to Claude successful!");
        },
        "Cross-Provider Function Transition to Claude"
    );
}

/// <summary>
/// Provider 간 Function Call 전환 테스트 - ChatGPT로
/// </summary>
[TestCategory("Common")]
[TestCategory("FunctionCalling")]
[TestMethod]
public async Task CrossProviderToGpt4o()
{
    await RunIfSupported(
        () => SupportsFunctionCalling(),
        async () =>
        {
            Console.WriteLine($"\n========== Starting with {AI.Provider} ==========");
            
            // Step 1: Function 등록
            AI.WithFunction<string>(
                "get_weather",
                "Get weather for a city",
                ("city", "City name", true),
                city => JsonSerializer.Serialize(new
                {
                    city = city,
                    temperature = 22,
                    condition = "sunny",
                    humidity = 65
                })
            )
            .WithFunction<double, double>(
                "calculate_distance",
                "Calculate distance between coordinates",
                ("lat1", "Latitude 1", true),
                ("lon1", "Longitude 1", true),
                (lat, lon) => $"Distance calculated: {Math.Sqrt(lat * lat + lon * lon):F2} km"
            );

            // Step 2: 첫 번째 Provider에서 Function 실행
            Console.WriteLine($"\n[Phase 1] Executing functions with {AI.Provider}");
            
            var response1 = await AI.GetCompletionAsync(
                "What's the weather in Seoul? And calculate the distance from coordinates 37.5, 127.0"
            );
            
            Console.WriteLine($"Response: {response1}");
            
            // Function 호출 확인
            var functionCalls = AI.ActivateChat.Messages
                .Where(m => m.Role == ActorRole.Function)
                .ToList();
            
            Assert.IsTrue(functionCalls.Count >= 1, 
                $"Expected at least 1 function call, got {functionCalls.Count}");
            
            // 메타데이터 로깅
            Console.WriteLine($"\nOriginal function calls:");
            foreach (var fc in functionCalls)
            {
                if (fc.Metadata != null)
                {
                    var funcName = fc.Metadata.GetValueOrDefault("function_name")?.ToString();
                    var callId = fc.Metadata.GetValueOrDefault("function_call_id")?.ToString();
                    Console.WriteLine($"  - {funcName} (ID: {callId?.Substring(0, 8)}...)");
                }
            }

            // Step 3: OpenAI GPT-4로 모델 전환
            Console.WriteLine($"\n========== Switching to OpenAI GPT-4 ==========");
            
            var messageCountBefore = AI.ActivateChat.Messages.Count;

            // OpenAI 모델로 변경 (Legacy API)
            var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
            string openAiKey = secretFetcher.GetKeyValueAsync().Result;

            var chatGptService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
            chatGptService.ChangeModel(AIModel.Gpt4oMini);
            
            var messageCountAfter = chatGptService.ActivateChat.Messages.Count;
            Assert.AreEqual(messageCountBefore, messageCountAfter, 
                "Messages should be preserved after model change");

            // Step 4: OpenAI에서 이전 컨텍스트를 활용한 질문
            Console.WriteLine($"\n[Phase 2] Continuing with OpenAI using previous context");
            
            try
            {
                var response2 = await chatGptService.GetCompletionAsync(
    "Based on the weather information we got, is it a good day for outdoor activities?"
);

                Console.WriteLine($"OpenAI Response: {response2}");

                // 날씨 정보가 참조되는지 확인
                Assert.IsTrue(
                    response2.ToLower().Contains("sunny") ||
                    response2.ToLower().Contains("good") ||
                    response2.ToLower().Contains("yes") ||
                    response2.ToLower().Contains("outdoor"),
                    "OpenAI should reference the previous weather information"
                );

            }
            catch(Exception ex)
            {
                Console.WriteLine($"[Error during OpenAI context test] {ex.Message}");
                Assert.Fail("OpenAI context test failed");
            }

            // Step 5: OpenAI에서 새로운 Function 호출
            Console.WriteLine($"\n[Phase 3] New function call with OpenAI");
            
            var response3 = await chatGptService.GetCompletionAsync(
                "Now check the weather in Tokyo"
            );
            
            Console.WriteLine($"OpenAI Function Response: {response3}");
            
            // 새로운 Function 호출 확인
            var openAIFunctionCalls = chatGptService.ActivateChat.Messages
                .Where(m => m.Role == ActorRole.Function)
                .Skip(functionCalls.Count)
                .ToList();
            
            Assert.IsTrue(openAIFunctionCalls.Count > 0, 
                "OpenAI should have made new function calls");
            
            Console.WriteLine($"\nOpenAI function calls:");
            foreach (var fc in openAIFunctionCalls)
            {
                if (fc.Metadata != null)
                {
                    var funcName = fc.Metadata.GetValueOrDefault("function_name")?.ToString();
                    var openAiCallId = fc.Metadata.GetValueOrDefault("openai_call_id")?.ToString();
                    var unifiedId = fc.Metadata.GetValueOrDefault("function_call_id")?.ToString();
                    
                    Console.WriteLine($"  - {funcName}");
                    Console.WriteLine($"    Unified ID: {unifiedId?.Substring(0, Math.Min(8, unifiedId?.Length ?? 0))}...");
                    if (!string.IsNullOrEmpty(openAiCallId))
                    {
                        Console.WriteLine($"    OpenAI call_id: {openAiCallId}");
                    }
                }
            }

            Console.WriteLine($"\n✅ Cross-provider transition to OpenAI successful!");
        },
        "Cross-Provider Function Transition to OpenAI"
    );
}


    /// <summary>
    /// Provider 간 Function Call 전환 테스트 - ChatGPT로
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task CrossProviderToOpenAIo3()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                Console.WriteLine($"\n========== Starting with {AI.Provider} ==========");

                // Step 1: Function 등록
                AI.WithFunction<string>(
                    "get_weather",
                    "Get weather for a city",
                    ("city", "City name", true),
                    city => JsonSerializer.Serialize(new
                    {
                        city = city,
                        temperature = 22,
                        condition = "sunny",
                        humidity = 65
                    })
                )
                .WithFunction<double, double>(
                    "calculate_distance",
                    "Calculate distance between coordinates",
                    ("lat1", "Latitude 1", true),
                    ("lon1", "Longitude 1", true),
                    (lat, lon) => $"Distance calculated: {Math.Sqrt(lat * lat + lon * lon):F2} km"
                );

                // Step 2: 첫 번째 Provider에서 Function 실행
                Console.WriteLine($"\n[Phase 1] Executing functions with {AI.Provider}");

                var response1 = await AI.GetCompletionAsync(
                    "What's the weather in Seoul? And calculate the distance from coordinates 37.5, 127.0"
                );

                Console.WriteLine($"Response: {response1}");

                // Function 호출 확인
                var functionCalls = AI.ActivateChat.Messages
                    .Where(m => m.Role == ActorRole.Function)
                    .ToList();

                Assert.IsTrue(functionCalls.Count >= 1,
                    $"Expected at least 1 function call, got {functionCalls.Count}");

                // 메타데이터 로깅
                Console.WriteLine($"\nOriginal function calls:");
                foreach (var fc in functionCalls)
                {
                    if (fc.Metadata != null)
                    {
                        var funcName = fc.Metadata.GetValueOrDefault("function_name")?.ToString();
                        var callId = fc.Metadata.GetValueOrDefault("function_call_id")?.ToString();
                        Console.WriteLine($"  - {funcName} (ID: {callId?.Substring(0, 8)}...)");
                    }
                }

                var messageCountBefore = AI.ActivateChat.Messages.Count;

                // OpenAI 모델로 변경 (Legacy API)
                var secretFetcher = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret");
                string openAiKey = secretFetcher.GetKeyValueAsync().Result;

                var chatGptService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
                chatGptService.ChangeModel(AIModel.o3);

                var messageCountAfter = chatGptService.ActivateChat.Messages.Count;
                Assert.AreEqual(messageCountBefore, messageCountAfter,
                    "Messages should be preserved after model change");

                // Step 4: OpenAI에서 이전 컨텍스트를 활용한 질문
                Console.WriteLine($"\n[Phase 2] Continuing with OpenAI using previous context");

                try
                {
                    var response2 = await chatGptService.GetCompletionAsync(
        "Based on the weather information we got, is it a good day for outdoor activities?"
    );

                    Console.WriteLine($"OpenAI Response: {response2}");

                    // 날씨 정보가 참조되는지 확인
                    Assert.IsTrue(
                        response2.ToLower().Contains("sunny") ||
                        response2.ToLower().Contains("good") ||
                        response2.ToLower().Contains("yes") ||
                        response2.ToLower().Contains("outdoor"),
                        "OpenAI should reference the previous weather information"
                    );

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error during OpenAI context test] {ex.Message}");
                    Assert.Fail("OpenAI context test failed");
                }

                // Step 5: OpenAI에서 새로운 Function 호출
                Console.WriteLine($"\n[Phase 3] New function call with OpenAI");

                var response3 = await chatGptService.GetCompletionAsync(
                    "Now check the weather in Tokyo"
                );

                Console.WriteLine($"OpenAI Function Response: {response3}");

                // 새로운 Function 호출 확인
                var openAIFunctionCalls = chatGptService.ActivateChat.Messages
                    .Where(m => m.Role == ActorRole.Function)
                    .Skip(functionCalls.Count)
                    .ToList();

                Assert.IsTrue(openAIFunctionCalls.Count > 0,
                    "OpenAI should have made new function calls");

                Console.WriteLine($"\nOpenAI function calls:");
                foreach (var fc in openAIFunctionCalls)
                {
                    if (fc.Metadata != null)
                    {
                        var funcName = fc.Metadata.GetValueOrDefault("function_name")?.ToString();
                        var openAiCallId = fc.Metadata.GetValueOrDefault("openai_call_id")?.ToString();
                        var unifiedId = fc.Metadata.GetValueOrDefault("function_call_id")?.ToString();

                        Console.WriteLine($"  - {funcName}");
                        Console.WriteLine($"    Unified ID: {unifiedId?.Substring(0, Math.Min(8, unifiedId?.Length ?? 0))}...");
                        if (!string.IsNullOrEmpty(openAiCallId))
                        {
                            Console.WriteLine($"    OpenAI call_id: {openAiCallId}");
                        }
                    }
                }

                Console.WriteLine($"\n✅ Cross-provider transition to OpenAI successful!");
            },
            "Cross-Provider Function Transition to OpenAI"
        );
    }

    /// <summary>
    /// 3-Way 라운드트립: Current → Claude → ChatGPT → Gemini 2.5
    /// Function ON 상태에서 3개 provider를 연속 전환하며 이력과 function 동작 확인
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task CrossProvider_ThreeWayRoundTrip_FunctionOn()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                Console.WriteLine($"\n========== [Phase 1] Starting with {AI.Provider} ==========");

                // Function 등록
                AI.WithFunction<string>(
                    "get_stock_price",
                    "Get stock price for a ticker symbol",
                    ("ticker", "Stock ticker symbol", true),
                    ticker => JsonSerializer.Serialize(new { ticker, price = 150.25, currency = "USD" })
                );

                // Phase 1: 원래 provider에서 function 호출
                var response1 = await AI.GetCompletionAsync("Get the stock price for AAPL");
                Console.WriteLine($"[Phase 1 Response] {response1}");

                var phase1FuncCount = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
                Assert.IsTrue(phase1FuncCount >= 1, $"Phase 1: Expected function call, got {phase1FuncCount}");

                var totalMessagesPhase1 = AI.ActivateChat.Messages.Count;
                Console.WriteLine($"[Phase 1] Messages: {totalMessagesPhase1}, Function calls: {phase1FuncCount}");

                // Phase 2: Claude로 전환
                Console.WriteLine($"\n========== [Phase 2] Switching to Claude ==========");
                var claudeKey = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret")
                    .GetKeyValueAsync().Result;
                var claudeService = new ClaudeService(claudeKey, new HttpClient()).CopyFrom(AI);
                claudeService.ChangeModel(AIModel.Claude3_5Haiku241022);

                Assert.AreEqual(totalMessagesPhase1, claudeService.ActivateChat.Messages.Count,
                    "Messages should be preserved after switch to Claude");

                // Claude에서 컨텍스트 참조 + 새 function 호출
                var response2 = await claudeService.GetCompletionAsync(
                    "Now also get the stock price for MSFT");
                Console.WriteLine($"[Phase 2 Claude Response] {response2}");

                var phase2FuncCount = claudeService.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
                Assert.IsTrue(phase2FuncCount > phase1FuncCount,
                    $"Phase 2: Claude should have made additional function calls ({phase2FuncCount} > {phase1FuncCount})");

                var totalMessagesPhase2 = claudeService.ActivateChat.Messages.Count;

                // Phase 3: ChatGPT로 전환
                Console.WriteLine($"\n========== [Phase 3] Switching to ChatGPT ==========");
                var openAiKey = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret")
                    .GetKeyValueAsync().Result;
                var gptService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(claudeService);
                gptService.ChangeModel(AIModel.Gpt4oMini);

                Assert.AreEqual(totalMessagesPhase2, gptService.ActivateChat.Messages.Count,
                    "Messages should be preserved after switch to ChatGPT");

                var response3 = await gptService.GetCompletionAsync(
                    "What were the stock prices we looked up? Summarize them.");
                Console.WriteLine($"[Phase 3 GPT Response] {response3}");
                Assert.IsNotNull(response3);

                // Phase 4: Gemini 2.5로 전환
                Console.WriteLine($"\n========== [Phase 4] Switching to Gemini 2.5 ==========");
                var geminiKey = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret")
                    .GetKeyValueAsync().Result;
                var geminiService = new GeminiService(geminiKey, new HttpClient()).CopyFrom(gptService);
                geminiService.ChangeModel(AIModel.Gemini2_5Flash);

                var response4 = await geminiService.GetCompletionAsync(
                    "Based on the prices, which stock is more expensive?");
                Console.WriteLine($"[Phase 4 Gemini Response] {response4}");
                Assert.IsNotNull(response4);

                Console.WriteLine($"\n✅ 3-way round-trip (Function ON) successful!");
                Console.WriteLine($"   Total messages at end: {geminiService.ActivateChat.Messages.Count}");
            },
            "Cross-Provider 3-Way Round Trip (Function ON)"
        );
    }

    /// <summary>
    /// Function OFF 상태에서 function 이력이 있는 대화를 non-function 경로로 전환하는 테스트.
    /// 각 서비스의 non-function BuildRequestBody()가 ActorRole.Function 메시지를 처리할 수 있는지 확인.
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task CrossProvider_FunctionOff_WithFunctionHistory()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                var failures = new List<string>();

                Console.WriteLine($"\n========== [Phase 1] Function calls with {AI.Provider} ==========");

                // Function 등록 및 호출
                AI.WithFunction<string>(
                    "get_time",
                    "Get current time for a timezone",
                    ("timezone", "Timezone name", true),
                    tz => JsonSerializer.Serialize(new { timezone = tz, time = "14:30:00", date = "2026-02-14" })
                );

                var response1 = await AI.GetCompletionAsync("What time is it in Seoul?");
                Console.WriteLine($"[Phase 1] {response1}");

                var funcMsgCount = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
                Assert.IsTrue(funcMsgCount >= 1, "Should have function messages in history");
                Console.WriteLine($"[Phase 1] Function messages in history: {funcMsgCount}");

                // Phase 2: Function OFF로 전환 후 Claude로 CopyFrom
                Console.WriteLine($"\n========== [Phase 2] Switch to Claude with Functions DISABLED ==========");
                var claudeKey = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-antropic-secret")
                    .GetKeyValueAsync().Result;
                var claudeService = new ClaudeService(claudeKey, new HttpClient()).CopyFrom(AI);
                claudeService.ChangeModel(AIModel.Claude3_5Haiku241022);
                claudeService.FunctionsDisabled = true;  // Function OFF

                // Debug: dump messages before API call
                Console.WriteLine($"[Phase 2 DEBUG] Messages in chat ({claudeService.ActivateChat.Messages.Count}):");
                foreach (var msg in claudeService.ActivateChat.Messages)
                {
                    var meta = msg.Metadata != null ? $" [meta: {string.Join(", ", msg.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}]" : "";
                    Console.WriteLine($"  {msg.Role}: {(msg.Content?.Length > 80 ? msg.Content.Substring(0, 77) + "..." : msg.Content)}{meta}");
                }

                try
                {
                    var response2 = await claudeService.GetCompletionAsync(
                        "What did you tell me about the time?");
                    Console.WriteLine($"[Phase 2 Claude - Function OFF] {response2}");
                    Console.WriteLine("✅ Claude handled function history in non-function path");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Claude FAILED with function history in non-function path:");
                    Console.WriteLine($"   {ex.Message}");
                    if (ex is AIServiceException aiEx && aiEx.ErrorDetails != null)
                        Console.WriteLine($"   ErrorDetails: {aiEx.ErrorDetails}");
                    failures.Add($"Claude: {ex.Message}");
                }

                // Phase 3: Function OFF로 ChatGPT Legacy API (gpt-4o-mini) 전환
                Console.WriteLine($"\n========== [Phase 3] Switch to ChatGPT Legacy (gpt-4o-mini) with Functions DISABLED ==========");
                var openAiKey = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "momedit-openai-secret")
                    .GetKeyValueAsync().Result;
                var gptLegacyService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
                gptLegacyService.ChangeModel(AIModel.Gpt4oMini);
                gptLegacyService.FunctionsDisabled = true;

                try
                {
                    var response3 = await gptLegacyService.GetCompletionAsync(
                        "What did you tell me about the time?");
                    Console.WriteLine($"[Phase 3 GPT Legacy - Function OFF] {response3}");
                    Console.WriteLine("✅ ChatGPT Legacy handled function history in non-function path");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ ChatGPT Legacy FAILED with function history in non-function path:");
                    Console.WriteLine($"   {ex.Message}");
                    if (ex is AIServiceException aiEx && aiEx.ErrorDetails != null)
                        Console.WriteLine($"   ErrorDetails: {aiEx.ErrorDetails}");
                    failures.Add($"ChatGPT Legacy (gpt-4o-mini): {ex.Message}");
                }

                // Phase 4: Function OFF로 ChatGPT New API (gpt-5-mini) 전환
                Console.WriteLine($"\n========== [Phase 4] Switch to ChatGPT New API (gpt-5-mini) with Functions DISABLED ==========");
                var gptNewService = new ChatGptService(openAiKey, new HttpClient()).CopyFrom(AI);
                gptNewService.ChangeModel(AIModel.Gpt5Mini);
                gptNewService.FunctionsDisabled = true;

                try
                {
                    var response4 = await gptNewService.GetCompletionAsync(
                        "What did you tell me about the time?");
                    Console.WriteLine($"[Phase 4 GPT New API - Function OFF] {response4}");
                    Console.WriteLine("✅ ChatGPT New API handled function history in non-function path");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ ChatGPT New API FAILED with function history in non-function path:");
                    Console.WriteLine($"   {ex.Message}");
                    if (ex is AIServiceException aiEx && aiEx.ErrorDetails != null)
                        Console.WriteLine($"   ErrorDetails: {aiEx.ErrorDetails}");
                    failures.Add($"ChatGPT New API (gpt-5-mini): {ex.Message}");
                }

                // Phase 5: Function OFF로 Gemini 전환
                Console.WriteLine($"\n========== [Phase 5] Switch to Gemini with Functions DISABLED ==========");
                var geminiKey = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret")
                    .GetKeyValueAsync().Result;
                var geminiService = new GeminiService(geminiKey, new HttpClient()).CopyFrom(AI);
                geminiService.ChangeModel(AIModel.Gemini2_5Flash);
                geminiService.FunctionsDisabled = true;

                try
                {
                    var response5 = await geminiService.GetCompletionAsync(
                        "What did you tell me about the time?");
                    Console.WriteLine($"[Phase 5 Gemini - Function OFF] {response5}");
                    Console.WriteLine("✅ Gemini handled function history in non-function path");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Gemini FAILED with function history in non-function path:");
                    Console.WriteLine($"   {ex.Message}");
                    if (ex is AIServiceException aiEx && aiEx.ErrorDetails != null)
                        Console.WriteLine($"   ErrorDetails: {aiEx.ErrorDetails}");
                    failures.Add($"Gemini: {ex.Message}");
                }

                // 결과 종합
                Console.WriteLine($"\n========== Results ==========");
                Console.WriteLine($"Failures: {failures.Count} / 4");
                foreach (var f in failures)
                    Console.WriteLine($"  - {f}");

                if (failures.Count > 0)
                    Assert.Fail($"{failures.Count} service(s) failed:\n" + string.Join("\n", failures));
            },
            "Cross-Provider Function OFF with Function History"
        );
    }

    /// <summary>
    /// 다른 Provider에서 function 호출 후 Gemini 3로 전환 시 ThoughtSignature 누락 문제 테스트.
    /// Gemini 3는 function call 메시지에 ThoughtSignature를 요구할 수 있음 (strict validation).
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task CrossProvider_ToGemini3_ThoughtSignatureMissing()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                Console.WriteLine($"\n========== [Phase 1] Function calls with {AI.Provider} ==========");

                // Function 등록 및 호출
                AI.WithFunction<string>(
                    "get_weather",
                    "Get weather for a city",
                    ("city", "City name", true),
                    city => JsonSerializer.Serialize(new { city, temp = 20, condition = "cloudy" })
                );

                var response1 = await AI.GetCompletionAsync("What's the weather in Tokyo?");
                Console.WriteLine($"[Phase 1] {response1}");

                var funcMsgCount = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
                Assert.IsTrue(funcMsgCount >= 1, "Should have function messages");

                // Phase 2: Gemini 3로 전환 (ThoughtSignature 없는 function 이력 포함)
                Console.WriteLine($"\n========== [Phase 2] Switch to Gemini 3 Flash ==========");
                var geminiKey = new SecretFetcher("https://mythosia-key-vault.vault.azure.net/", "gemini-secret")
                    .GetKeyValueAsync().Result;
                var geminiService = new GeminiService(geminiKey, new HttpClient()).CopyFrom(AI);
                geminiService.ChangeModel(AIModel.Gemini3FlashPreview);

                try
                {
                    // Gemini 3에서 기존 function 이력을 포함한 요청
                    var response2 = await geminiService.GetCompletionAsync(
                        "Based on the weather info, should I bring an umbrella?");
                    Console.WriteLine($"[Phase 2 Gemini 3] {response2}");
                    Console.WriteLine("✅ Gemini 3 accepted function history without ThoughtSignature");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Gemini 3 REJECTED function history without ThoughtSignature:");
                    Console.WriteLine($"   {ex.Message}");
                    // Don't Assert.Fail - this documents the known limitation
                    Console.WriteLine("   → This confirms ThoughtSignature is required for Gemini 3 cross-provider switching");
                }

                // Phase 3: Gemini 3에서 새 function 호출도 시도
                Console.WriteLine($"\n========== [Phase 3] New function call with Gemini 3 ==========");
                try
                {
                    var response3 = await geminiService.GetCompletionAsync(
                        "Now check the weather in London");
                    Console.WriteLine($"[Phase 3 Gemini 3 new func] {response3}");
                    Console.WriteLine("✅ Gemini 3 new function call successful");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Gemini 3 new function call failed: {ex.Message}");
                }
            },
            "Cross-Provider to Gemini 3 ThoughtSignature Test"
        );
    }

}
