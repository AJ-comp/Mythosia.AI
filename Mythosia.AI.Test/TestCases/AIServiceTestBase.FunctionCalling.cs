using Mythosia.AI.Attributes;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    #region Test Functions for Attribute-based Testing

    public class TestFunctions
    {
        [AiFunction("Gets the current weather for a given location")]
        public static async Task<string> GetWeather(
            [AiParameter("The city name", required: true)] string city,
            [AiParameter("Temperature unit", required: false)] string unit = "celsius")
        {
            await Task.Delay(10);
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
            await Task.Delay(100);
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
    /// 기본 Function Calling 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task BasicFunctionCallingTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                // 파라미터 없는 function
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

                // 1개 파라미터 function
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

                // 2개 파라미터 function
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

                // 3개 파라미터 function
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
                    "Greets a user. Call this function to greet someone, name is optional.",
                    ("name", "User's name (optional - can be empty)", false),
                    name => string.IsNullOrEmpty(name) ? "Hello, stranger!" : $"Hello, {name}!"
                );

                await TestFunctionCall(
                    "Greet me",
                    "greet_user",
                    "Optional parameter function"
                );
            },
            "Function Calling"
        );
    }

    /// <summary>
    /// 비동기 Function Calling 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task AsyncFunctionCallingTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                // ====== [1] 비동기 파라미터 없는 함수 ======
                bool timeInvoked = false;
                string? timeOutput = null;

                AI.WithFunctionAsync(
                    "async_time",
                    "Gets time asynchronously",
                    async () =>
                    {
                        await Task.Delay(100);
                        timeInvoked = true;

                        var s = $"Async time: {DateTime.UtcNow:HH:mm:ss.fff}";
                        timeOutput = s;       // 🔹 테스트용으로 결과를 캡처
                        return s;
                    }
                );

                await TestFunctionCall(
                    "Get async time",
                    "async_time",
                    "Async parameterless"
                );

                // --- Assert: 실제 호출 + 형식 검증 ---
                Assert.IsTrue(timeInvoked, "[async_time] 함수가 호출되지 않았습니다.");
                Assert.IsNotNull(timeOutput, "[async_time] 결과가 null 입니다.");
                StringAssert.Matches(
                    timeOutput!,
                    new Regex(@"^Async time: \d{2}:\d{2}:\d{2}\.\d{3}$"),
                    "[async_time] 결과 형식이 예상과 다릅니다."
                );

                // ====== [2] 비동기 3개 파라미터 함수 ======
                bool orderInvoked = false;
                string? orderOutput = null;

                AI.WithFunctionAsync<string, string, int>(
                    "process_order",
                    "Process an order",
                    ("product", "Product name", true),
                    ("customer", "Customer name", true),
                    ("quantity", "Quantity", true),
                    async (product, customer, qty) =>
                    {
                        await Task.Delay(200);
                        orderInvoked = true;

                        var s = $"Order processed: {qty}x {product} for {customer}";
                        orderOutput = s;      // 🔹 테스트용으로 결과를 캡처
                        return s;
                    }
                );

                await TestFunctionCall(
                    "Process an order for 5 laptops for Alice",
                    "process_order",
                    "Async three parameters"
                );

                Assert.IsTrue(orderInvoked, "[process_order] 함수가 호출되지 않았습니다.");
            },
            "Async Function Calling"
        );
    }

    /// <summary>
    /// Attribute 기반 Function 등록 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task AttributeBasedFunctionTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                // TestFunctions 클래스의 모든 AiFunction 등록
                var functions = new TestFunctions();
                AI.WithFunctions(functions);
                AI.WithStaticFunctions<TestFunctions>();

                // Weather function 테스트
                var response = await AI.GetCompletionAsync("What's the weather in Tokyo?");
                Assert.IsNotNull(response);
                Console.WriteLine($"[Weather] {response}");

                // Email function 테스트
                var emailResponse = await AI.GetCompletionAsync(
                    "Send an email to test@example.com with subject 'Test' and body 'Hello World'");
                Assert.IsNotNull(emailResponse);
                Console.WriteLine($"[Email] {emailResponse}");
            },
            "Attribute-based Functions"
        );
    }

    /// <summary>
    /// Function Calling with Streaming 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task FunctionCallingStreamEventsTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                // Function 등록
                AI.WithFunction(
                    "test_function",
                    "Test function for streaming",
                    () => "Function executed successfully"
                );

                var eventLog = new List<(StreamingContentType type, string metadata)>();

                var options = StreamOptions.WithFunctions;
                var message = new Message(ActorRole.User, "Call the test_function");

                await foreach (var content in AI.StreamAsync(message, options))
                {
                    eventLog.Add((content.Type, JsonSerializer.Serialize(content.Metadata)));

                    if(content.Type == StreamingContentType.Text)
                    {
                        Console.Write(content.Content);
                    }
                    else if (content.Type == StreamingContentType.FunctionCall)
                    {
                        Assert.IsNotNull(content.Metadata);
                        Assert.IsTrue(content.Metadata.ContainsKey("function_name"));
                        Console.WriteLine($"✅ Function Call Event: {content.Metadata["function_name"]}");
                    }
                    else if (content.Type == StreamingContentType.FunctionResult)
                    {
                        Assert.IsNotNull(content.Metadata);
                        Assert.IsTrue(content.Metadata.ContainsKey("status"));
                        Console.WriteLine($"✅ Function Result Event: {content.Metadata["status"]}");
                    }
                }

                // 검증
                var functionCallEvents = eventLog.Where(e => e.type == StreamingContentType.FunctionCall).ToList();
                var functionResultEvents = eventLog.Where(e => e.type == StreamingContentType.FunctionResult).ToList();

                Assert.IsTrue(functionCallEvents.Count > 0, "No FunctionCall events detected");
                Assert.IsTrue(functionResultEvents.Count > 0, "No FunctionResult events detected");

                var lastMessage = AI.ActivateChat.Messages.LastOrDefault();
                Assert.AreEqual(ActorRole.Assistant, lastMessage?.Role);

                Console.WriteLine($"Event Summary:");
                Console.WriteLine($"  FunctionCall events: {functionCallEvents.Count}");
                Console.WriteLine($"  FunctionResult events: {functionResultEvents.Count}");
            },
            "Function Calling Stream Events"
        );
    }

    /// <summary>
    /// Function 비활성화 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task DisableFunctionsTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                // Function 등록
                AI.WithFunction(
                    "always_called",
                    "This function should always be called for any query",
                    () => JsonSerializer.Serialize(new
                    {
                        status = "success",
                        message = "Function executed successfully",
                        timestamp = DateTime.UtcNow
                    })
                );

                // Functions 일시적으로 비활성화
                AI.FunctionsDisabled = true;
                var response = await AI.GetCompletionAsync("Call the always_called function");
                Console.WriteLine($"[Disabled] {response}");

                var functionCallsBeforeEnable = AI.ActivateChat.Messages
                    .Where(m => m.Role == ActorRole.Function).Count();

                // Functions 다시 활성화
                AI.FunctionsDisabled = false;
                AI.ActivateChat.Messages.Clear();

                var enabledResponse = await AI.GetCompletionAsync("Call the always_called function and tell me the result");
                Console.WriteLine($"[Enabled] {enabledResponse}");

                var functionCallsAfterEnable = AI.ActivateChat.Messages
                    .Where(m => m.Role == ActorRole.Function).Count();

                Assert.AreEqual(0, functionCallsBeforeEnable);
                Assert.IsTrue(functionCallsAfterEnable > 0);
            },
            "Disable Functions"
        );
    }

    /// <summary>
    /// 복잡한 Function 파라미터 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task ComplexFunctionParametersTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
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

                var functionMessage = AI.ActivateChat.Messages
                    .LastOrDefault(m => m.Role == ActorRole.Function);

                if (functionMessage != null)
                {
                    Console.WriteLine($"[Function Result] {functionMessage.Content}");
                    Assert.IsTrue(functionMessage.Content.Contains("booking_id"));
                }
            },
            "Complex Function Parameters"
        );
    }

    /// <summary>
    /// Function Chain 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task FunctionChainingTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                // First function: get user ID
                AI.WithFunction<string>(
                    "get_user_id",
                    "Get user ID from username",
                    ("username", "Username", true),
                    username => "user_123"
                );

                // Second function: get user details
                AI.WithFunction<string>(
                    "get_user_details",
                    "Get user details from ID",
                    ("user_id", "User ID", true),
                    userId => JsonSerializer.Serialize(new
                    {
                        id = userId,
                        name = "Test User",
                        email = "test@example.com",
                        created = DateTime.UtcNow.AddDays(-30)
                    })
                );

                try
                {
                    var response = await AI.GetCompletionAsync(
    "Get the details for username 'john_doe'"
);
                    Assert.IsNotNull(response);
                    Console.WriteLine($"[Chain Response] {response}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Function Chaining Error] {ex.GetType().Name}: {ex.Message}");
                    Assert.Fail("Function chaining failed");
                    return;
                }

                var functionMessages = AI.ActivateChat.Messages
                    .Where(m => m.Role == ActorRole.Function)
                    .ToList();

                Console.WriteLine($"[Functions Called] {functionMessages.Count}");
                foreach (var msg in functionMessages)
                {
                    if (msg.Metadata?.TryGetValue("function_name", out var fname) == true)
                    {
                        Console.WriteLine($"  - {fname}: {msg.Content?.Substring(0, Math.Min(50, msg.Content.Length))}...");
                    }
                }
            },
            "Function Chaining"
        );
    }

    /// <summary>
    /// Function 에러 처리 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task FunctionErrorHandlingTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                try
                {
                    // Error-throwing function
                    AI.WithFunction<string>(
                        "error_function",
                        "A function that throws errors",
                        ("input", "Input value", true),
                        input =>
                        {
                            if (input == "error")
                                throw new InvalidOperationException("Test error");
                            return $"Success: {input}";
                        }
                    );

                    // Test successful case
                    var successResponse = await AI.GetCompletionAsync(
                        "Call error_function with input 'test'"
                    );
                    Assert.IsNotNull(successResponse);
                    Console.WriteLine($"[Success] {successResponse}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Function Error] {ex.GetType().Name}: {ex.Message}");
                    Assert.Fail("Function should not have thrown an error in success case");
                }

                AI.ActivateChat.Messages.Clear();

                // Test error case
                var errorResponse = await AI.GetCompletionAsync(
                    "Call error_function with input 'error'"
                );
                Assert.IsNotNull(errorResponse);
                Console.WriteLine($"[Error Handled] {errorResponse}");

                var functionMessage = AI.ActivateChat.Messages
                    .LastOrDefault(m => m.Role == ActorRole.Function);

                if (functionMessage != null)
                {
                    Console.WriteLine($"[Function Error Result] {functionMessage.Content}");
                    Assert.IsTrue(functionMessage.Content.Contains("Error") ||
                                 functionMessage.Content.Contains("error"));
                }
            },
            "Function Error Handling"
        );
    }

    /// <summary>
    /// 복합 시나리오 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task ComplexFunctionScenarioTest()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                // 여러 함수를 등록
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

                // 전체 Function 호출 횟수 확인
                var totalFunctionCalls = AI.ActivateChat.Messages
                    .Count(m => m.Role == ActorRole.Function);

                Console.WriteLine($"\n[Total Function Calls] {totalFunctionCalls}");
                Assert.IsTrue(totalFunctionCalls >= 3);
            },
            "Complex Function Scenario"
        );
    }

    #region Helper Methods

    private async Task TestFunctionCall(string prompt, string expectedFunctionName, string testDescription)
    {
        Console.WriteLine($"\n[Testing] {testDescription}");
        Console.WriteLine($"[Prompt] {prompt}");

        var previousFunctionCount = AI.ActivateChat.Messages
            .Count(m => m.Role == ActorRole.Function);

        try
        {
            var response = await AI.GetCompletionAsync(prompt);

            Assert.IsNotNull(response, $"{testDescription}: Response should not be null");
            Console.WriteLine($"[Response] {response}");
        }
        catch(Exception ex)
        {
            Assert.Fail($"{testDescription}: Exception occurred - {ex.Message}");
            return;
        }

        var currentFunctionCount = AI.ActivateChat.Messages
            .Count(m => m.Role == ActorRole.Function);

        Assert.IsTrue(
            currentFunctionCount > previousFunctionCount,
            $"{testDescription}: Function should have been called"
        );

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

    #endregion
}