using Mythosia.AI.Attributes;
using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System.Text.Json;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 배열 파라미터 Function Calling 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task ArrayParameterFunctionTest()
    {
        await RunIfSupported(
            () => SupportsArrayParameter(),
            async () =>
            {
                // 1. JSON 문자열로 배열 받기 (현재 작동하는 방법)
                AI.WithFunction<string>(
                    "process_items_json",
                    "Process a list of items",
                    ("items_json", "JSON array of items", true),
                    itemsJson => {
                        try
                        {
                            var items = JsonSerializer.Deserialize<List<string>>(itemsJson);
                            return $"Successfully processed {items?.Count ?? 0} items";
                        }
                        catch
                        {
                            return "Failed to parse items";
                        }
                    }
                );

                var response1 = await AI.GetCompletionAsync(
                    "Process these items: apple, banana, orange"
                );

                Assert.IsNotNull(response1);
                Assert.IsTrue(response1.Contains("processed") || response1.Contains("items"));
                Console.WriteLine($"[JSON Array Test] {response1}");

                // 2. 직접 배열 파라미터 정의 (Items 속성 추가 필요)
                var stringArrayFunction = new FunctionDefinition
                {
                    Name = "process_string_array",
                    Description = "Process an array of strings",
                    Parameters = new FunctionParameters
                    {
                        Type = "object",
                        Properties = new Dictionary<string, ParameterProperty>
                        {
                            ["items"] = new ParameterProperty
                            {
                                Type = "array",
                                Description = "List of items to process",
                                Items = new ParameterProperty { Type = "string" }
                            }
                        },
                        Required = new List<string> { "items" }
                    },
                    Handler = async (args) => {
                        if (args.TryGetValue("items", out var itemsObj))
                        {
                            if (itemsObj is JsonElement jsonElement)
                            {
                                var count = jsonElement.GetArrayLength();
                                return $"Received array with {count} items";
                            }
                            else if (itemsObj is List<object> list)
                            {
                                return $"Received list with {list.Count} items";
                            }
                        }
                        return "No items received";
                    }
                };

                AI.WithFunction(stringArrayFunction);

                var response2 = await AI.GetCompletionAsync(
                    "Use process_string_array with these: hello, world, test"
                );

                Assert.IsNotNull(response2);
                Console.WriteLine($"[Direct Array Test] {response2}");

                // Function이 실제로 호출되었는지 확인
                var functionMessages = AI.ActivateChat.Messages
                    .Where(m => m.Role == ActorRole.Function)
                    .ToList();

                Assert.IsTrue(functionMessages.Count > 0, "At least one function should have been called");

                Console.WriteLine($"\n[Test Summary]");
                Console.WriteLine($"  Functions called: {functionMessages.Count}");
                foreach (var msg in functionMessages)
                {
                    if (msg.Metadata?.TryGetValue("function_name", out var fname) == true)
                    {
                        Console.WriteLine($"  - {fname}: {msg.Content?.Substring(0, Math.Min(50, msg.Content.Length))}");
                    }
                }
            },
            "Array Parameter Functions"
        );
    }

    /// <summary>
    /// 숫자 배열 테스트
    /// </summary>
    [TestCategory("Common")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task NumberArrayFunctionTest()
    {
        await RunIfSupported(
            () => SupportsArrayParameter(),
            async () =>
            {
                AI.WithFunction<string>(
                    "calculate_sum",
                    "Calculate sum of numbers",
                    ("numbers_json", "JSON array of numbers", true),
                    numbersJson => {
                        try
                        {
                            var numbers = JsonSerializer.Deserialize<List<double>>(numbersJson);
                            var sum = numbers?.Sum() ?? 0;
                            return $"Sum of {numbers?.Count ?? 0} numbers is {sum}";
                        }
                        catch
                        {
                            return "Invalid number array";
                        }
                    }
                );

                var response = await AI.GetCompletionAsync(
                    "Calculate the sum of: 10, 20, 30, 40"
                );

                Assert.IsNotNull(response);
                Assert.IsTrue(response.Contains("100") || response.Contains("sum"));
                Console.WriteLine($"[Number Array] {response}");

                // Verify function was called
                var lastFunction = AI.ActivateChat.Messages
                    .LastOrDefault(m => m.Role == ActorRole.Function);

                Assert.IsNotNull(lastFunction);
                Assert.IsTrue(lastFunction.Content.Contains("100"));
            },
            "Number Array Function"
        );
    }
}