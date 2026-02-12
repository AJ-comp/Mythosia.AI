using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Services.Anthropic;
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
            newService.ActivateChat.ChangeModel(AIModel.Claude3_7SonnetLatest);
            
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
            chatGptService.ActivateChat.ChangeModel(AIModel.Gpt4oMini);
            
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
                chatGptService.ActivateChat.ChangeModel(AIModel.o3);

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

}
