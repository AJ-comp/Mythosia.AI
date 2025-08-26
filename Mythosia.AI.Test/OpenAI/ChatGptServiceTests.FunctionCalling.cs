using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mythosia.AI.Attributes;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests;

public partial class ChatGptServiceTests
{
    #region Test Functions

    // 테스트용 함수들
    public class TestFunctions
    {
        [AiFunction("Gets the current weather for a given location")]
        public static async Task<string> GetWeather(
            [AiParameter("The city name", required: true)] string city,
            [AiParameter("Temperature unit (celsius or fahrenheit)", required: false)] string unit = "celsius")
        {
            await Task.Delay(10); // Simulate API call
            return JsonSerializer.Serialize(new
            {
                location = city,
                temperature = 22,
                unit = unit,
                condition = "sunny",
                humidity = 65
            });
        }

        [AiFunction("Performs a calculation")]
        public static string Calculate(
            [AiParameter("The mathematical expression", required: true)] string expression)
        {
            // Simple calculator simulation
            if (expression.Contains("+"))
            {
                var parts = expression.Split('+');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0].Trim(), out var a) &&
                    double.TryParse(parts[1].Trim(), out var b))
                {
                    return (a + b).ToString();
                }
            }
            return "Unable to calculate";
        }

        [AiFunction("Sends an email")]
        public static async Task<string> SendEmail(
            [AiParameter("Recipient email address", required: true)] string to,
            [AiParameter("Email subject", required: true)] string subject,
            [AiParameter("Email body content", required: true)] string body)
        {
            await Task.Delay(100); // Simulate sending
            return JsonSerializer.Serialize(new
            {
                success = true,
                messageId = Guid.NewGuid().ToString(),
                recipient = to,
                sentAt = DateTime.UtcNow
            });
        }
    }

    #endregion

    /// <summary>
    /// 기본 Function Calling 테스트 - 사용자가 OpenAI 스키마를 몰라도 됨
    /// </summary>
    [TestMethod]
    public async Task BasicFunctionCallingTest()
    {
        try
        {
            // 방법 1: 가장 간단한 function 등록 (파라미터 없음)
            AI.WithFunction(
                "get_current_time",
                "Gets the current time",
                () => $"Current time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
            );

            await TestFunctionCall(
                "What time is it now?",
                "get_current_time",
                "Parameterless function"
            );

            // 방법 2: 1개 파라미터 function
            AI.WithFunction<string>(
                "get_weather",
                "Gets weather for a city",
                ("city", "The city name", true),
                city => $"Weather in {city}: Sunny, 25°C"
            );

            await TestFunctionCall(
                "What's the weather in Seoul?",
                "get_weather",
                "Single parameter function"
            );

            // 방법 3: 2개 파라미터 function
            AI.WithFunction<int, int>(
                "calculate_sum",
                "Adds two numbers",
                ("num1", "First number", true),
                ("num2", "Second number", true),
                (a, b) => $"The sum of {a} and {b} is {a + b}"
            );

            await TestFunctionCall(
                "What is 15 plus 27?",
                "calculate_sum",
                "Two parameter function"
            );

            // 방법 4: 3개 파라미터 function
            AI.WithFunction<string, string, string>(
                "send_message",
                "Send a message to someone",
                ("recipient", "Person to send to", true),
                ("subject", "Message subject", true),
                ("body", "Message content", true),
                (to, subject, body) => $"Message sent to {to} with subject '{subject}': {body}"
            );

            await TestFunctionCall(
                "Send a message to John with subject 'Meeting' saying 'Let's meet at 3pm'",
                "send_message",
                "Three parameter function"
            );

            // 선택적 파라미터 테스트
            AI.WithFunction<string>(
                "greet_user",
                "Greets a user",
                ("name", "User's name", false),  // optional parameter
                name => string.IsNullOrEmpty(name) ? "Hello, stranger!" : $"Hello, {name}!"
            );

            await TestFunctionCall(
                "Greet me",
                "greet_user",
                "Optional parameter function"
            );

            // 타입 변환 테스트
            AI.WithFunction<double, double>(
                "calculate_percentage",
                "Calculate percentage",
                ("value", "The value", true),
                ("total", "The total", true),
                (value, total) => $"{value} is {(value / total * 100):F2}% of {total}"
            );

            await TestFunctionCall(
                "What percentage is 75 of 300?",
                "calculate_percentage",
                "Type conversion function"
            );

        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Basic Function Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 비동기 Function Calling 테스트
    /// </summary>
    [TestMethod]
    public async Task AsyncFunctionCallingTest()
    {
        try
        {
            // 비동기 파라미터 없는 함수
            AI.WithFunctionAsync(
                "async_time",
                "Gets time asynchronously",
                async () =>
                {
                    await Task.Delay(100);
                    return $"Async time: {DateTime.UtcNow:HH:mm:ss.fff}";
                }
            );

            await TestFunctionCall(
                "Get async time",
                "async_time",
                "Async parameterless"
            );

            // 비동기 1개 파라미터
            AI.WithFunctionAsync<string>(
                "fetch_data",
                "Fetch data from API",
                ("endpoint", "API endpoint", true),
                async endpoint =>
                {
                    await Task.Delay(50);
                    return $"Data fetched from {endpoint}";
                }
            );

            await TestFunctionCall(
                "Fetch data from /users endpoint",
                "fetch_data",
                "Async single parameter"
            );

            // 비동기 2개 파라미터
            AI.WithFunctionAsync<string, int>(
                "delay_message",
                "Send delayed message",
                ("message", "Message to send", true),
                ("delay", "Delay in ms", true),
                async (msg, delay) =>
                {
                    await Task.Delay(delay);
                    return $"Message '{msg}' sent after {delay}ms";
                }
            );

            await TestFunctionCall(
                "Send 'Hello' after 100ms delay",
                "delay_message",
                "Async two parameters"
            );

            // 비동기 3개 파라미터
            AI.WithFunctionAsync<string, string, int>(
                "process_order",
                "Process an order",
                ("product", "Product name", true),
                ("customer", "Customer name", true),
                ("quantity", "Quantity", true),
                async (product, customer, qty) =>
                {
                    await Task.Delay(200);
                    return $"Order processed: {qty}x {product} for {customer}";
                }
            );

            await TestFunctionCall(
                "Process an order for 5 laptops for Alice",
                "process_order",
                "Async three parameters"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Async Function Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 공통 테스트 헬퍼 메서드
    /// </summary>
    private async Task TestFunctionCall(string prompt, string expectedFunctionName, string testDescription)
    {
        Console.WriteLine($"\n[Testing] {testDescription}");
        Console.WriteLine($"[Prompt] {prompt}");

        // 이전 Function 메시지 개수 저장
        var previousFunctionCount = AI.ActivateChat.Messages
            .Count(m => m.Role == ActorRole.Function);

        // Function 호출
        var response = await AI.GetCompletionAsync(prompt);

        // 응답 검증
        Assert.IsNotNull(response, $"{testDescription}: Response should not be null");
        Console.WriteLine($"[Response] {response}");

        // Function이 호출되었는지 확인
        var currentFunctionCount = AI.ActivateChat.Messages
            .Count(m => m.Role == ActorRole.Function);

        Assert.IsTrue(
            currentFunctionCount > previousFunctionCount,
            $"{testDescription}: Function should have been called"
        );

        // 올바른 Function이 호출되었는지 확인 (메타데이터 체크)
        var lastFunctionMessage = AI.ActivateChat.Messages
            .Where(m => m.Role == ActorRole.Function)
            .LastOrDefault();

        if (lastFunctionMessage?.Metadata != null &&
            lastFunctionMessage.Metadata.TryGetValue("function_name", out var calledFunction))
        {
            Console.WriteLine($"[Called Function] {calledFunction}");
            Assert.AreEqual(
                expectedFunctionName,
                calledFunction.ToString(),
                $"{testDescription}: Expected {expectedFunctionName} but {calledFunction} was called"
            );
        }

        Console.WriteLine($"[Test Passed] {testDescription}");
    }

    /// <summary>
    /// 복합 시나리오 테스트
    /// </summary>
    [TestMethod]
    public async Task ComplexFunctionScenarioTest()
    {
        try
        {
            // 여러 함수를 등록하고 AI가 적절한 것을 선택하도록
            AI.WithFunction(
                "get_date",
                "Gets current date",
                () => DateTime.UtcNow.ToString("yyyy-MM-dd")
            )
            .WithFunction<string>(
                "get_user_info",
                "Gets user information",
                ("username", "Username", true),
                username => $"User {username}: Active, Premium member"
            )
            .WithFunctionAsync<string, string>(
                "search_products",
                "Search for products",
                ("category", "Product category", true),
                ("keyword", "Search keyword", false),
                async (category, keyword) =>
                {
                    await Task.Delay(100);
                    var searchTerm = string.IsNullOrEmpty(keyword) ? category : $"{category} - {keyword}";
                    return $"Found 10 products in {searchTerm}";
                }
            );

            // 다양한 프롬프트로 테스트
            await TestFunctionCall("What's today's date?", "get_date", "Date function selection");
            await TestFunctionCall("Get info about user John", "get_user_info", "User info selection");
            await TestFunctionCall("Search for laptops", "search_products", "Product search selection");

            // 연속 Function 호출 테스트 - 각각 다른 컨텍스트로
            Console.WriteLine("\n[Sequential Function Calls - Testing Context Behavior]");

            // 이미 날짜를 알고 있으므로 function을 호출하지 않을 것
            var beforeCount = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            var response1 = await AI.GetCompletionAsync("First, tell me the date");
            var afterCount1 = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            Console.WriteLine($"[Date Request] Function called: {afterCount1 > beforeCount} - Response: {response1}");

            // 새로운 사용자 정보는 function 호출 필요
            beforeCount = afterCount1;
            var response2 = await AI.GetCompletionAsync("Now get info about Alice");  // John이 아닌 Alice
            var afterCount2 = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            Console.WriteLine($"[Alice Info] Function called: {afterCount2 > beforeCount} - Response: {response2}");
            Assert.IsTrue(afterCount2 > beforeCount, "Should call function for new user Alice");

            // 새로운 검색은 function 호출 필요
            beforeCount = afterCount2;
            var response3 = await AI.GetCompletionAsync("Search for phones with keyword 'samsung'");
            var afterCount3 = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            Console.WriteLine($"[Phone Search] Function called: {afterCount3 > beforeCount} - Response: {response3}");
            Assert.IsTrue(afterCount3 > beforeCount, "Should call function for new search");

            // 같은 정보 다시 요청 시 function 호출 안함
            Console.WriteLine("\n[Testing Cached Information]");
            beforeCount = afterCount3;
            var response4 = await AI.GetCompletionAsync("What was the date again?");
            var afterCount4 = AI.ActivateChat.Messages.Count(m => m.Role == ActorRole.Function);
            Console.WriteLine($"[Date Again] Function called: {afterCount4 > beforeCount} - Response: {response4}");
            Assert.IsFalse(afterCount4 > beforeCount, "Should NOT call function for already known date");

            // 전체 Function 호출 횟수 확인
            var totalFunctionCalls = AI.ActivateChat.Messages
                .Count(m => m.Role == ActorRole.Function);

            Console.WriteLine($"\n[Total Function Calls] {totalFunctionCalls}");
            Console.WriteLine("[Message History]");
            foreach (var msg in AI.ActivateChat.Messages.TakeLast(10))
            {
                Console.WriteLine($"  {msg.Role}: {msg.GetDisplayText().Truncate(100)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Complex Scenario Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Attribute 기반 Function 등록 테스트
    /// </summary>
    [TestMethod]
    public async Task AttributeBasedFunctionTest()
    {
        try
        {
            // TestFunctions 클래스의 모든 AiFunction 등록
            var functions = new TestFunctions();
            AI.WithFunctions(functions);

            // Static functions도 등록
            AI.WithStaticFunctions<TestFunctions>();

            // Weather function 테스트
            var response = await AI.GetCompletionAsync("What's the weather in Tokyo?");
            Assert.IsNotNull(response);
            Console.WriteLine($"[Weather Function] {response}");

            // Email function 테스트
            var emailResponse = await AI.GetCompletionAsync(
                "Send an email to test@example.com with subject 'Test' and body 'Hello World'");
            Assert.IsNotNull(emailResponse);
            Console.WriteLine($"[Email Function] {emailResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Attribute Function Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Function Call Mode 테스트
    /// </summary>
    [TestMethod]
    public async Task FunctionCallModeTest()
    {
        try
        {
            // Function 등록
            AI.WithFunction(
                "test_function",
                "A test function",
                () => "Function was called!");

            // Auto mode (기본값)
            AI.ActivateChat.FunctionCallMode = FunctionCallMode.Auto;
            var autoResponse = await AI.GetCompletionAsync("Hello, how are you?");
            Console.WriteLine($"[Auto Mode] {autoResponse}");

            // Force mode - 특정 함수 강제 호출
            AI.ActivateChat.FunctionCallMode = FunctionCallMode.Force;
            AI.ActivateChat.ForceFunctionName = "test_function";
            var forceResponse = await AI.GetCompletionAsync("Any message");
            Console.WriteLine($"[Force Mode] {forceResponse}");

            // Function이 호출되었는지 확인
            var functionMessage = AI.ActivateChat.Messages.LastOrDefault(m => m.Role == ActorRole.Function);
            Assert.IsNotNull(functionMessage, "Function should be called in Force mode");

            // None mode - Function 호출 비활성화
            AI.ActivateChat.FunctionCallMode = FunctionCallMode.None;
            var noneResponse = await AI.GetCompletionAsync("Call test function");
            Console.WriteLine($"[None Mode] {noneResponse}");

            // Reset to Auto
            AI.ActivateChat.FunctionCallMode = FunctionCallMode.Auto;
            AI.ActivateChat.ForceFunctionName = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Function Mode Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Function Calling with Streaming 테스트
    /// </summary>
    [TestMethod]
    public async Task FunctionCallingWithStreamingTest()
    {
        try
        {
            // FunctionBuilder를 사용하여 Function 생성
            var infoFunction = FunctionBuilder.Create("get_info")
                .WithDescription("Get information about a topic")
                .AddParameter("topic", "string", "The topic to get info about", required: true)
                .WithHandler(args =>
                {
                    var topic = args["topic"].ToString();
                    return $"Here's information about {topic}: It's a fascinating subject with many aspects to explore.";
                })
                .Build();

            AI.ActivateChat.AddFunction(infoFunction);

            var options = new StreamOptions
            {
                IncludeMetadata = true,
                IncludeFunctionCalls = true
            };

            var functionCallDetected = false;
            var functionResultReceived = false;
            var textContent = new System.Text.StringBuilder();

            await foreach (var content in ((AIService)AI).StreamAsync(
                "Tell me about quantum computing",
                options))
            {
                Console.WriteLine($"[Stream] Type: {content.Type}");

                switch (content.Type)
                {
                    case StreamingContentType.FunctionCall:
                        functionCallDetected = true;
                        if (content.Metadata != null)
                        {
                            Console.WriteLine($"[Function Call] Metadata: {JsonSerializer.Serialize(content.Metadata)}");
                        }
                        break;

                    case StreamingContentType.FunctionResult:
                        functionResultReceived = true;
                        if (content.Metadata != null)
                        {
                            Console.WriteLine($"[Function Result] {content.Metadata.GetValueOrDefault("result")}");
                        }
                        break;

                    case StreamingContentType.Text:
                        if (content.Content != null)
                        {
                            textContent.Append(content.Content);
                            Console.Write(content.Content);
                        }
                        break;
                }
            }

            Console.WriteLine($"\n[Summary]");
            Console.WriteLine($"  Function Called: {functionCallDetected}");
            Console.WriteLine($"  Function Result: {functionResultReceived}");
            Console.WriteLine($"  Text Length: {textContent.Length}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Streaming Function Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Function 비활성화 테스트
    /// </summary>
    [TestMethod]
    public async Task DisableFunctionsTest()
    {
        try
        {
            // Function 등록
            AI.WithFunction(
                "always_called",
                "This function should always be called for any query",
                () => "This shouldn't be called when disabled!");

            // Functions 일시적으로 비활성화
            AI.FunctionsDisabled = true;
            var response = await AI.GetCompletionAsync("Call the always_called function");
            Console.WriteLine($"[Disabled] {response}");

            // Function이 호출되지 않았는지 확인
            var functionCalls = AI.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).Count();
            var initialCount = functionCalls;

            // Functions 다시 활성화
            AI.FunctionsDisabled = false;
            var enabledResponse = await AI.GetCompletionAsync("Call the always_called function");
            Console.WriteLine($"[Enabled] {enabledResponse}");

            // Function이 호출되었는지 확인
            functionCalls = AI.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).Count();
            Assert.IsTrue(functionCalls >= initialCount, "Function should be called when enabled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Disable Functions Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 복잡한 Function 파라미터 테스트
    /// </summary>
    [TestMethod]
    public async Task ComplexFunctionParametersTest()
    {
        try
        {
            // 복잡한 파라미터를 가진 Function 생성
            var bookingFunction = FunctionBuilder.Create("book_flight")
                .WithDescription("Book a flight ticket")
                .AddParameter("from", "string", "Departure city", required: true)
                .AddParameter("to", "string", "Destination city", required: true)
                .AddParameter("date", "string", "Travel date (YYYY-MM-DD)", required: true)
                .AddParameter("class", "string", "Seat class", required: false, defaultValue: "economy")
                .AddEnumParameter("meal", "Meal preference",
                    new List<string> { "vegetarian", "non-vegetarian", "vegan", "none" },
                    required: false, defaultValue: "none")
                .WithHandler(args =>
                {
                    var result = new
                    {
                        booking_id = Guid.NewGuid().ToString().Substring(0, 8),
                        from = args["from"],
                        to = args["to"],
                        date = args["date"],
                        seat_class = args.GetValueOrDefault("class", "economy"),
                        meal = args.GetValueOrDefault("meal", "none"),
                        status = "confirmed"
                    };
                    return JsonSerializer.Serialize(result);
                })
                .Build();

            AI.ActivateChat.AddFunction(bookingFunction);

            var response = await AI.GetCompletionAsync(
                "Book a flight from Seoul to Tokyo on 2024-12-25, business class with vegetarian meal");

            Assert.IsNotNull(response);
            Console.WriteLine($"[Booking Response] {response}");

            // Function result 확인
            var functionMessage = AI.ActivateChat.Messages
                .LastOrDefault(m => m.Role == ActorRole.Function);

            if (functionMessage != null)
            {
                Console.WriteLine($"[Function Result] {functionMessage.Content}");
                Assert.IsTrue(functionMessage.Content.Contains("booking_id"));
                Assert.IsTrue(functionMessage.Content.Contains("Seoul") ||
                             functionMessage.Content.Contains("seoul"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Complex Parameters Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Function Chain 테스트 (한 함수의 결과를 다른 함수에 전달)
    /// </summary>
    [TestMethod]
    public async Task FunctionChainingTest()
    {
    }

    /// <summary>
    /// Function 에러 처리 테스트
    /// </summary>
    [TestMethod]
    public async Task FunctionErrorHandlingTest()
    {
    }

    /// <summary>
    /// Extension Method를 사용한 Function Calling 테스트
    /// </summary>
    [TestMethod]
    public async Task FunctionExtensionMethodsTest()
    {
        try
        {
            // Function 등록
            AI.WithFunction(
                "test_func",
                "Test function",
                () => "Function executed!");

            // WithoutFunctions extension 테스트
            var withoutResponse = await AI.AskWithoutFunctionsAsync(
                "Call test_func function");
            Console.WriteLine($"[Without Functions] {withoutResponse}");

            // Function이 호출되지 않았는지 확인
            Assert.IsFalse(withoutResponse.Contains("Function executed"));

            // CallFunctionAsync extension 테스트 - 특정 함수 강제 호출
            var forcedResponse = await AI.CallFunctionAsync(
                "test_func",
                "This message doesn't matter");
            Console.WriteLine($"[Forced Function] {forcedResponse}");

            // Function이 호출되었는지 확인
            var lastFunction = AI.ActivateChat.Messages
                .LastOrDefault(m => m.Role == ActorRole.Function);
            Assert.IsNotNull(lastFunction);
            Assert.IsTrue(lastFunction.Content.Contains("Function executed"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Extension Methods Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }
}