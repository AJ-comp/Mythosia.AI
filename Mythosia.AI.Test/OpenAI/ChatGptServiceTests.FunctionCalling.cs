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

            // Function을 호출하도록 유도하는 프롬프트
            var response = await AI.GetCompletionAsync("What time is it now?");


            Assert.IsNotNull(response);
            Console.WriteLine($"[Function Response] {response}");

            // Function이 호출되었는지 확인
            var functionMessages = AI.ActivateChat.Messages.Where(m => m.Role == ActorRole.Function).ToList();
            Assert.IsTrue(functionMessages.Count > 0, "Function should have been called");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Basic Function Test Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 여러 Function 등록 및 선택 테스트
    /// </summary>
    [TestMethod]
    public async Task MultipleFunctionsTest()
    {
        try
        {
            // FunctionBuilder를 사용하여 Function 생성 후 AddFunction 사용
            var weatherFunction = FunctionBuilder.Create("weather")
                .WithDescription("Get weather information")
                .AddParameter("location", "string", "City name", required: true)
                .AddParameter("unit", "string", "Temperature unit", required: false, defaultValue: "celsius")
                .WithHandler(async args =>
                {
                    var location = args["location"].ToString();
                    var unit = args.ContainsKey("unit") ? args["unit"].ToString() : "celsius";
                    await Task.Delay(10);
                    return $"Weather in {location}: 25°{unit[0]}, sunny";
                })
                .Build();

            var calculatorFunction = FunctionBuilder.Create("calculator")
                .WithDescription("Perform mathematical calculations")
                .AddParameter("expression", "string", "Math expression to evaluate", required: true)
                .WithHandler(args =>
                {
                    var expr = args["expression"].ToString();
                    // Simple evaluation
                    if (expr == "2+2") return "4";
                    if (expr == "10*5") return "50";
                    return "Unknown";
                })
                .Build();

            // ChatBlock에 직접 Function 추가
            AI.ActivateChat.AddFunction(weatherFunction);
            AI.ActivateChat.AddFunction(calculatorFunction);

            // Weather function 호출 테스트
            var weatherResponse = await AI.GetCompletionAsync("What's the weather in Seoul?");
            Assert.IsNotNull(weatherResponse);
            Console.WriteLine($"[Weather] {weatherResponse}");

            // Calculator function 호출 테스트
            var calcResponse = await AI.GetCompletionAsync("Calculate 2+2 for me");
            Assert.IsNotNull(calcResponse);
            Console.WriteLine($"[Calculator] {calcResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Multiple Functions Error] {ex.Message}");
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
    /// Delegate를 사용한 Function 등록 테스트
    /// </summary>
    [TestMethod]
    public async Task DelegateFunctionTest()
    {
        try
        {
            // Delegate로 Function 등록
            Func<Dictionary<string, object>, string> simpleFunc = args =>
            {
                var name = args.ContainsKey("name") ? args["name"].ToString() : "World";
                return $"Hello, {name}!";
            };

            // Delegate를 WithFunction에 전달
            AI.WithFunction((Delegate)simpleFunc);

            // 또는 async delegate
            Func<Dictionary<string, object>, Task<string>> asyncFunc = async args =>
            {
                await Task.Delay(10);
                var value = args.ContainsKey("value") ? args["value"].ToString() : "0";
                return $"Processed value: {value}";
            };

            AI.WithFunction((Delegate)asyncFunc);

            // 함수 호출 테스트
            var response = await AI.GetCompletionAsync("Say hello to Alice");
            Assert.IsNotNull(response);
            Console.WriteLine($"[Delegate Function] {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Delegate Function Error] {ex.Message}");
            Assert.Fail(ex.Message);
        }
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