using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using System.Text.Json;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 배열 파라미터 Function Calling 테스트
    /// </summary>
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task ArrayParameterFunctionTest()
    {
        await RunIfSupported(
            () => SupportsArrayParameter(),
            async () =>
            {
                List<string>? handledItems = null;
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
                    Handler = args =>
                    {
                        if (args.TryGetValue("items", out var itemsObj) &&
                            itemsObj is JsonElement jsonElement &&
                            jsonElement.ValueKind == JsonValueKind.Array)
                        {
                            handledItems = jsonElement.EnumerateArray()
                                .Select(item => item.GetString() ?? string.Empty)
                                .ToList();
                        }

                        return Task.FromResult(
                            handledItems == null
                                ? "ARRAY_ERROR:no-array"
                                : $"ARRAY_OK:{string.Join("|", handledItems)}");
                    }
                };

                AI.WithFunction(stringArrayFunction);
                ConfigureRequiredFunctionCall("process_string_array");

                var response = await AI.GetCompletionAsync(
                    "Use process_string_array with these: hello, world, test"
                );

                Assert.IsNotNull(response);
                CollectionAssert.AreEqual(
                    new[] { "hello", "world", "test" },
                    handledItems,
                    "The function handler must receive a real JSON array, not a JSON string.");

                var lastFunction = AI.ActivateChat.Messages.LastOrDefault(m => m.Role == ActorRole.Function);
                Assert.IsNotNull(lastFunction, "process_string_array was not called.");
                Assert.AreEqual(
                    "process_string_array",
                    lastFunction.Metadata?.GetValueOrDefault("function_name")?.ToString());
                Assert.AreEqual("ARRAY_OK:hello|world|test", lastFunction.Content);
            },
            "Array Parameter Functions"
        );
    }

    /// <summary>
    /// 숫자 배열 테스트
    /// </summary>
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task NumberArrayFunctionTest()
    {
        await RunIfSupported(
            () => SupportsArrayParameter(),
            async () =>
            {
                List<double>? handledNumbers = null;
                var numberArrayFunction = new FunctionDefinition
                {
                    Name = "calculate_sum",
                    Description = "Calculate the sum of an array of numbers",
                    Parameters = new FunctionParameters
                    {
                        Type = "object",
                        Properties = new Dictionary<string, ParameterProperty>
                        {
                            ["numbers"] = new ParameterProperty
                            {
                                Type = "array",
                                Description = "Numbers to add",
                                Items = new ParameterProperty { Type = "number" }
                            }
                        },
                        Required = new List<string> { "numbers" }
                    },
                    Handler = args =>
                    {
                        if (args.TryGetValue("numbers", out var numbersObj) &&
                            numbersObj is JsonElement jsonElement &&
                            jsonElement.ValueKind == JsonValueKind.Array)
                        {
                            handledNumbers = jsonElement.EnumerateArray()
                                .Select(item => item.GetDouble())
                                .ToList();
                        }

                        return Task.FromResult(
                            handledNumbers == null
                                ? "ARRAY_ERROR:no-array"
                                : $"ARRAY_SUM:{handledNumbers.Sum():0.################}");
                    }
                };

                AI.WithFunction(numberArrayFunction);
                ConfigureRequiredFunctionCall("calculate_sum");

                var response = await AI.GetCompletionAsync(
                    "Calculate the sum of: 10, 20, 30, 40"
                );

                Assert.IsNotNull(response);
                CollectionAssert.AreEqual(
                    new[] { 10d, 20d, 30d, 40d },
                    handledNumbers,
                    "The function handler must receive a real JSON number array.");

                var lastFunction = AI.ActivateChat.Messages
                    .LastOrDefault(m => m.Role == ActorRole.Function);

                Assert.IsNotNull(lastFunction, "calculate_sum was not called.");
                Assert.AreEqual(
                    "calculate_sum",
                    lastFunction.Metadata?.GetValueOrDefault("function_name")?.ToString());
                Assert.AreEqual("ARRAY_SUM:100", lastFunction.Content);
            },
            "Number Array Function"
        );
    }
}
