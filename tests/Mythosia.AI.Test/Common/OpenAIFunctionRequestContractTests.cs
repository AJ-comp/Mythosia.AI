using Mythosia.AI.Builders;
using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("FunctionCalling")]
public class OpenAIFunctionRequestContractTests
{
    private const string CompletedResponse = """
        {
          "status": "completed",
          "output": [
            {
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "output_text", "text": "ok" }
              ]
            }
          ]
        }
        """;

    [TestMethod]
    public async Task ResponsesTools_PreserveMultimodalInputStructuredOutputAndForcedChoice()
    {
        var handler = new QueueHttpMessageHandler(CompletedResponse);
        var service = new RequestProbeService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        service.WithGpt5_6Parameters(verbosity: Verbosity.High);
        service.ForceFunctionName = "inspect_window";
        service.SetStructuredOutputSchema("""
            {
              "type": "object",
              "properties": { "result": { "type": "string" } },
              "required": ["result"],
              "additionalProperties": false
            }
            """);
        service.Functions.Add(CreateRequiredFunction("inspect_window"));

        var image = new ImageContent("https://example.test/window.jpg")
        {
            IsHighDetail = true
        };
        var message = new Message(
            ActorRole.User,
            new List<MessageContent>
            {
                new TextContent("Inspect this window."),
                image
            });

        var result = await service.GetCompletionAsync(message);

        Assert.AreEqual("ok", result);
        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var root = document.RootElement;

        var content = root.GetProperty("input")[0].GetProperty("content");
        Assert.AreEqual(2, content.GetArrayLength());
        Assert.AreEqual("input_text", content[0].GetProperty("type").GetString());
        Assert.AreEqual("Inspect this window.", content[0].GetProperty("text").GetString());
        Assert.AreEqual("input_image", content[1].GetProperty("type").GetString());
        Assert.AreEqual(
            "https://example.test/window.jpg",
            content[1].GetProperty("image_url").GetString());
        Assert.AreEqual("high", content[1].GetProperty("detail").GetString());

        var text = root.GetProperty("text");
        Assert.AreEqual("high", text.GetProperty("verbosity").GetString());
        var format = text.GetProperty("format");
        Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
        Assert.AreEqual("structured_output", format.GetProperty("name").GetString());
        Assert.IsTrue(format.GetProperty("strict").GetBoolean());
        Assert.AreEqual("object", format.GetProperty("schema").GetProperty("type").GetString());

        var tool = root.GetProperty("tools")[0];
        Assert.IsTrue(tool.GetProperty("strict").GetBoolean());
        CollectionAssert.AreEqual(
            new[] { "image_url" },
            tool.GetProperty("parameters").GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray());

        var toolChoice = root.GetProperty("tool_choice");
        Assert.AreEqual("function", toolChoice.GetProperty("type").GetString());
        Assert.AreEqual("inspect_window", toolChoice.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task ResponsesForcedFunctionName_AppliesOnlyToInitialToolRound()
    {
        const string toolCallResponse = """
            {
              "status": "completed",
              "output": [
                {
                  "type": "function_call",
                  "call_id": "call_inspect_window",
                  "name": "inspect_window",
                  "arguments": "{\"image_url\":\"https://example.test/window.jpg\"}"
                }
              ]
            }
            """;
        var handler = new QueueHttpMessageHandler(toolCallResponse, CompletedResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        service.ForceFunctionName = "inspect_window";
        service.Functions.Add(CreateRequiredFunction("inspect_window"));

        var result = await service.GetCompletionAsync("Inspect it.");

        Assert.AreEqual("ok", result);
        Assert.AreEqual(2, handler.Requests.Count);

        using var firstDocument = JsonDocument.Parse(handler.Requests[0].Body);
        var firstToolChoice = firstDocument.RootElement.GetProperty("tool_choice");
        Assert.AreEqual("function", firstToolChoice.GetProperty("type").GetString());
        Assert.AreEqual("inspect_window", firstToolChoice.GetProperty("name").GetString());

        using var secondDocument = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.AreEqual(
            "auto",
            secondDocument.RootElement.GetProperty("tool_choice").GetString());
    }

    [TestMethod]
    public async Task OptionalFunctionParameter_DisablesStrictAndPreservesRequiredContract()
    {
        var handler = new QueueHttpMessageHandler(CompletedResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);

        var function = FunctionBuilder.Create("get_weather")
            .WithDescription("Gets weather")
            .AddParameter("city", "string", "City", required: true)
            .AddParameter("unit", "string", "Temperature unit", required: false, defaultValue: "celsius")
            .WithHandler(_ => "unused")
            .Build();
        service.Functions.Add(function);

        CollectionAssert.AreEqual(new[] { "city" }, function.Parameters.Required);

        await service.GetCompletionAsync("Get the weather.");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var tool = document.RootElement.GetProperty("tools")[0];
        Assert.IsFalse(tool.GetProperty("strict").GetBoolean());

        var parameters = tool.GetProperty("parameters");
        CollectionAssert.AreEqual(
            new[] { "city" },
            parameters.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray());
        Assert.AreEqual(
            "celsius",
            parameters.GetProperty("properties").GetProperty("unit").GetProperty("default").GetString());
        Assert.IsFalse(parameters.GetProperty("additionalProperties").GetBoolean());
    }

    [TestMethod]
    public async Task NestedObjectParameters_DisableStrictUntilNestedSchemasAreExpressible()
    {
        var handler = new QueueHttpMessageHandler(CompletedResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);

        service.Functions.Add(new FunctionDefinition
        {
            Name = "inspect_payload",
            Description = "Inspects an object payload.",
            Parameters = new FunctionParameters
            {
                Properties = new Dictionary<string, ParameterProperty>
                {
                    ["payload"] = new ParameterProperty { Type = "object" }
                },
                Required = new List<string> { "payload" }
            },
            Handler = _ => Task.FromResult("unused")
        });
        service.Functions.Add(new FunctionDefinition
        {
            Name = "inspect_payloads",
            Description = "Inspects an array of object payloads.",
            Parameters = new FunctionParameters
            {
                Properties = new Dictionary<string, ParameterProperty>
                {
                    ["payloads"] = new ParameterProperty
                    {
                        Type = "array",
                        Items = new ParameterProperty { Type = "object" }
                    }
                },
                Required = new List<string> { "payloads" }
            },
            Handler = _ => Task.FromResult("unused")
        });

        await service.GetCompletionAsync("Inspect the payloads.");

        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var tools = document.RootElement.GetProperty("tools");
        Assert.AreEqual(2, tools.GetArrayLength());
        Assert.IsFalse(tools[0].GetProperty("strict").GetBoolean());
        Assert.IsFalse(tools[1].GetProperty("strict").GetBoolean());
        Assert.AreEqual(
            "object",
            tools[1].GetProperty("parameters")
                .GetProperty("properties")
                .GetProperty("payloads")
                .GetProperty("items")
                .GetProperty("type")
                .GetString());
    }

    [TestMethod]
    public async Task MalformedResponsesFunctionArguments_RejectBeforeHandlerExecution()
    {
        const string malformedToolResponse = """
            {
              "status": "completed",
              "output": [
                {
                  "type": "function_call",
                  "call_id": "call_invalid_json",
                  "name": "get_weather",
                  "arguments": "{\"city\":"
                }
              ]
            }
            """;
        var handler = new QueueHttpMessageHandler(malformedToolResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);

        var invocationCount = 0;
        var function = CreateRequiredFunction("get_weather");
        function.Handler = _ =>
        {
            invocationCount++;
            return Task.FromResult("must not run");
        };
        service.Functions.Add(function);

        AIServiceException? exception = null;
        try
        {
            await service.GetCompletionAsync("Get the weather.");
        }
        catch (AIServiceException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        StringAssert.Contains(exception.Message, "invalid arguments");
        StringAssert.Contains(exception.Message, "handler was not executed");
        Assert.AreEqual(0, invocationCount);
    }

    [TestMethod]
    public async Task LegacyFunctions_SerializeForcedFunctionName()
    {
        const string chatCompletionResponse = """
            {
              "choices": [
                { "message": { "role": "assistant", "content": "ok" } }
              ]
            }
            """;
        var handler = new QueueHttpMessageHandler(chatCompletionResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel("gpt-4o-mini");
        service.ForceFunctionName = "inspect_window";
        service.Functions.Add(CreateRequiredFunction("inspect_window"));

        var result = await service.GetCompletionAsync("Inspect it.");

        Assert.AreEqual("ok", result);
        using var document = JsonDocument.Parse(AssertSingleRequest(handler).Body);
        var toolChoice = document.RootElement.GetProperty("tool_choice");
        Assert.AreEqual("function", toolChoice.GetProperty("type").GetString());
        Assert.AreEqual(
            "inspect_window",
            toolChoice.GetProperty("function").GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task LegacyForcedFunctionName_AppliesOnlyToInitialToolRound()
    {
        const string toolCallResponse = """
            {
              "choices": [
                {
                  "finish_reason": "tool_calls",
                  "message": {
                    "role": "assistant",
                    "content": null,
                    "tool_calls": [
                      {
                        "id": "call_inspect_window",
                        "type": "function",
                        "function": {
                          "name": "inspect_window",
                          "arguments": "{\"image_url\":\"https://example.test/window.jpg\"}"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """;
        const string finalResponse = """
            {
              "choices": [
                {
                  "finish_reason": "stop",
                  "message": { "role": "assistant", "content": "ok" }
                }
              ]
            }
            """;
        var handler = new QueueHttpMessageHandler(toolCallResponse, finalResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel("gpt-4o-mini");
        service.ForceFunctionName = "inspect_window";
        service.Functions.Add(CreateRequiredFunction("inspect_window"));

        var result = await service.GetCompletionAsync("Inspect it.");

        Assert.AreEqual("ok", result);
        Assert.AreEqual(2, handler.Requests.Count);

        using var firstDocument = JsonDocument.Parse(handler.Requests[0].Body);
        var firstToolChoice = firstDocument.RootElement.GetProperty("tool_choice");
        Assert.AreEqual("function", firstToolChoice.GetProperty("type").GetString());
        Assert.AreEqual(
            "inspect_window",
            firstToolChoice.GetProperty("function").GetProperty("name").GetString());

        using var secondDocument = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.AreEqual(
            "auto",
            secondDocument.RootElement.GetProperty("tool_choice").GetString());
    }

    [TestMethod]
    public async Task MalformedLegacyFunctionArguments_RejectBeforeHandlerExecution()
    {
        const string malformedToolResponse = """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "function_call": {
                      "name": "inspect_window",
                      "arguments": "["
                    }
                  }
                }
              ]
            }
            """;
        var handler = new QueueHttpMessageHandler(malformedToolResponse);
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel("gpt-4o-mini");

        var invocationCount = 0;
        var function = CreateRequiredFunction("inspect_window");
        function.Handler = _ =>
        {
            invocationCount++;
            return Task.FromResult("must not run");
        };
        service.Functions.Add(function);

        AIServiceException? exception = null;
        try
        {
            await service.GetCompletionAsync("Inspect it.");
        }
        catch (AIServiceException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        StringAssert.Contains(exception.Message, "invalid arguments");
        Assert.AreEqual(0, invocationCount);
    }

    private static FunctionDefinition CreateRequiredFunction(string name)
    {
        var function = new FunctionDefinition
        {
            Name = name,
            Description = name,
            Handler = _ => Task.FromResult("unused")
        };
        function.Parameters.Properties["image_url"] = new ParameterProperty
        {
            Type = "string",
            Description = "Image URL"
        };
        function.Parameters.Required.Add("image_url");
        return function;
    }

    private static CapturedRequest AssertSingleRequest(QueueHttpMessageHandler handler)
    {
        Assert.AreEqual(1, handler.Requests.Count);
        return handler.Requests[0];
    }

    private sealed class RequestProbeService : OpenAIService
    {
        public RequestProbeService(string apiKey, HttpClient httpClient)
            : base(apiKey, httpClient)
        {
        }

        public void SetStructuredOutputSchema(string schema)
        {
            _structuredOutputSchemaJson = schema;
        }
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued response remains.");

            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Body);
}
