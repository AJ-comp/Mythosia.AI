using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Anthropic;
using NJsonSchema;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AnthropicToolSchemaContractTests
{
    private const string ValidArguments = """
        {"Type":"report","URL":"https://example.invalid/report","OrderID":17,"unit":"celsius","matrix":[[1,2],[3]]}
        """;

    [TestMethod]
    [DataRow("claude-sonnet-4-6")]
    [DataRow("claude-opus-5")]
    [DataRow("claude-sonnet-5-5")]
    [DataRow("claude-opus-5-5")]
    public async Task CompletionAndCounting_EmitEffectiveJsonSchemaWithoutRenamingApplicationData(string model)
    {
        using var fixture = new Fixture(model);
        var function = CreateFunction();
        var original = JsonSerializer.Serialize(function.Parameters);
        fixture.Service.Functions.Add(function);

        Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("Describe the report."));
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());

        Assert.AreEqual(2, fixture.Requests.Count);
        CollectionAssert.AreEqual(new[] { "/v1/messages", "/v1/messages/count_tokens" }, fixture.Paths);
        foreach (var request in fixture.Requests)
        {
            var schema = AssertSchemaContract(request);
            await AssertArgumentValidationAsync(schema);
        }
        Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["tools"], fixture.Requests[1]["tools"]));
        Assert.AreEqual(original, JsonSerializer.Serialize(function.Parameters), "Wire projection must not mutate caller-owned definitions.");
        Assert.AreEqual(0, fixture.HandlerCalls, "Generation without a tool call and counting must not invoke handlers.");
    }

    [TestMethod]
    [DataRow("claude-sonnet-4-6", false)]
    [DataRow("claude-sonnet-4-6", true)]
    [DataRow("claude-opus-5", false)]
    [DataRow("claude-opus-5", true)]
    [DataRow("claude-sonnet-5-5", false)]
    [DataRow("claude-sonnet-5-5", true)]
    [DataRow("claude-opus-5-5", false)]
    [DataRow("claude-opus-5-5", true)]
    public async Task StreamingAndRun_ToolContinuationRetainsSchemaAndCaseSensitiveArguments(string model, bool useRun)
    {
        using var fixture = new Fixture(model) { ReturnToolRound = true };
        var function = CreateFunction(arguments =>
        {
            fixture.HandlerCalls++;
            CollectionAssert.AreEquivalent(new[] { "Type", "URL", "OrderID", "unit", "matrix" }, arguments.Keys.ToArray());
            Assert.AreEqual("report", ((JsonElement)arguments["Type"]).GetString());
            Assert.AreEqual(17, ((JsonElement)arguments["OrderID"]).GetInt32());
            return Task.FromResult("local result");
        });
        fixture.Service.Functions.Add(function);

        if (useRun)
        {
            // Output filtering must not disable execution or the second model round.
            await using var run = await fixture.Service.StartRunAsync("Read the report.", options: StreamOptions.TextOnlyOptions);
            var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual("answer", result.Text);
            Assert.AreEqual(2, result.RoundCount);
        }
        else
        {
            var chunks = new List<StreamingContent>();
            await foreach (var chunk in fixture.Service.StreamAsync("Read the report.", StreamOptions.WithFunctions))
                chunks.Add(chunk);
            Assert.AreEqual("answer", string.Concat(chunks.Where(chunk => chunk.Type == StreamingContentType.Text).Select(chunk => chunk.Content)));
            Assert.AreEqual(1, chunks.Count(chunk => chunk.Type == StreamingContentType.FunctionResult));
            Assert.AreEqual(1, chunks.Count(chunk => chunk.Type == StreamingContentType.Completion));
            Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Error));
        }

        Assert.AreEqual(1, fixture.HandlerCalls);
        Assert.AreEqual(2, fixture.Requests.Count);
        Assert.IsTrue(fixture.Requests.All(request => request["stream"]!.GetValue<bool>()));
        await fixture.Service.GetInputTokenCountAsync();
        Assert.AreEqual(1, fixture.HandlerCalls, "Counting completed tool history must not repeat the tool.");
        Assert.AreEqual(3, fixture.Requests.Count);
        foreach (var request in fixture.Requests)
        {
            var schema = AssertSchemaContract(request);
            await AssertArgumentValidationAsync(schema);
            Assert.IsTrue(JsonNode.DeepEquals(fixture.Requests[0]["tools"], request["tools"]));
        }

        var continuationBlocks = fixture.Requests[1]["messages"]!.AsArray()
            .Where(message => message?["content"] is JsonArray)
            .SelectMany(message => message!["content"]!.AsArray()).ToArray();
        var call = continuationBlocks.Single(block => block?["type"]?.GetValue<string>() == "tool_use");
        Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(ValidArguments), call!["input"]));
        var resultBlock = continuationBlocks.Single(block => block?["type"]?.GetValue<string>() == "tool_result");
        Assert.AreEqual("toolu_report", resultBlock!["tool_use_id"]!.GetValue<string>());
        Assert.AreEqual("local result", resultBlock["content"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow("claude-sonnet-4-6", "null")]
    [DataRow("claude-sonnet-4-6", "cycle")]
    [DataRow("claude-sonnet-4-6", "65-levels")]
    [DataRow("claude-sonnet-5-5", "null")]
    [DataRow("claude-sonnet-5-5", "cycle")]
    [DataRow("claude-sonnet-5-5", "65-levels")]
    public async Task Counting_InvalidParameterGraphFailsBeforeHttpAndServiceRemainsUsable(string model, string malformed)
    {
        using var fixture = new Fixture(model);
        var function = new FunctionDefinition { Name = "guarded", Description = "Test parameter graph validation." };
        ParameterProperty? property;
        if (malformed == "null")
            property = null;
        else if (malformed == "cycle")
        {
            property = new ParameterProperty { Type = "array" };
            property.Items = property;
        }
        else
        {
            property = new ParameterProperty { Type = "integer" };
            // Include the root: one scalar leaf and 64 array ancestors are 65 levels.
            for (var depth = 1; depth < 65; depth++)
                property = new ParameterProperty { Type = "array", Items = property };
        }
        function.Parameters.Properties["candidate"] = property!;
        fixture.Service.Functions.Add(function);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.GetInputTokenCountAsync());
        Assert.IsEmpty(fixture.Requests, "Invalid schemas must fail before sending the token-count request.");
        Assert.IsEmpty(fixture.Service.ActivateChat.Messages, "A failed count must not add conversation messages.");

        function.Parameters.Properties["candidate"] = new ParameterProperty { Type = "string" };
        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("Try the repaired schema."));
        Assert.AreEqual(2, fixture.Requests.Count);
        CollectionAssert.AreEqual(new[] { "/v1/messages/count_tokens", "/v1/messages" }, fixture.Paths);
        foreach (var request in fixture.Requests)
        {
            var repaired = request["tools"]![0]!["input_schema"]!["properties"]!["candidate"]!.AsObject();
            CollectionAssert.AreEqual(new[] { "type" }, repaired.Select(pair => pair.Key).ToArray());
            Assert.AreEqual("string", repaired["type"]!.GetValue<string>());
        }
    }

    [TestMethod]
    public async Task Counting_SharedItemsLeafIsNotTreatedAsACycle()
    {
        using var fixture = new Fixture("claude-sonnet-5-5");
        var sharedLeaf = new ParameterProperty { Type = "string", Enum = ["north", "south"], Default = "north" };
        var first = new ParameterProperty { Type = "array", Items = sharedLeaf };
        var second = new ParameterProperty { Type = "array", Items = sharedLeaf };
        var function = new FunctionDefinition { Name = "shared_leaf", Description = "Two independent arrays share a schema leaf." };
        function.Parameters.Properties["first"] = first;
        function.Parameters.Properties["second"] = second;
        fixture.Service.Functions.Add(function);

        Assert.AreEqual(42u, await fixture.Service.GetInputTokenCountAsync());
        var properties = fixture.Requests.Single()["tools"]![0]!["input_schema"]!["properties"]!.AsObject();
        Assert.IsTrue(JsonNode.DeepEquals(properties["first"]!["items"], properties["second"]!["items"]));
        var leaf = properties["first"]!["items"]!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "type", "enum", "default" }, leaf.Select(pair => pair.Key).ToArray());
        Assert.AreEqual("string", leaf["type"]!.GetValue<string>());
        CollectionAssert.AreEqual(new[] { "north", "south" }, leaf["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.AreEqual("north", leaf["default"]!.GetValue<string>());
        Assert.AreSame(sharedLeaf, first.Items);
        Assert.AreSame(sharedLeaf, second.Items);
        CollectionAssert.AreEqual(new[] { "north", "south" }, sharedLeaf.Enum);
    }

    [TestMethod]
    public void PublicParameterDtos_StandaloneSerializationRetainsPascalCaseContract()
    {
        var parameters = new FunctionParameters
        {
            Properties = new Dictionary<string, ParameterProperty>
            {
                ["OrderID"] = new()
                {
                    Type = "array", Description = "Caller serialized data", Default = new { OrderID = 7 },
                    Items = new ParameterProperty { Type = "string", Enum = ["first", "second"] }
                }
            },
            Required = ["OrderID"]
        };

        var serialized = JsonSerializer.SerializeToNode(parameters)!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "Type", "Properties", "Required" }, serialized.Select(pair => pair.Key).ToArray());
        var property = serialized["Properties"]!["OrderID"]!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "Type", "Description", "Enum", "Default", "Items" }, property.Select(pair => pair.Key).ToArray());
        Assert.AreEqual("array", property["Type"]!.GetValue<string>());
        Assert.AreEqual("Caller serialized data", property["Description"]!.GetValue<string>());
        Assert.AreEqual(7, property["Default"]!["OrderID"]!.GetValue<int>());
        Assert.AreEqual("string", property["Items"]!["Type"]!.GetValue<string>());
        CollectionAssert.AreEqual(new[] { "first", "second" }, property["Items"]!["Enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.IsTrue(property.ContainsKey("Enum"));
        Assert.IsNull(property["Enum"], "Standalone DTO serialization retains its existing null-field behavior.");
        Assert.IsFalse(property.ContainsKey("type"));
        Assert.IsFalse(serialized.ContainsKey("properties"));
    }

    private static FunctionDefinition CreateFunction(Func<Dictionary<string, object>, Task<string>>? handler = null) => new()
    {
        Name = "read_report",
        Description = "Read a report with case-sensitive application fields.",
        Handler = handler ?? (_ => Task.FromResult("unused")),
        Parameters = new FunctionParameters
        {
            Properties = new Dictionary<string, ParameterProperty>
            {
                ["Type"] = new() { Type = "string", Description = "Report category", Enum = ["report", "summary"] },
                ["URL"] = new() { Type = "string" },
                ["OrderID"] = new() { Type = "integer", Description = "" },
                ["unit"] = new() { Type = "string", Enum = ["celsius", "fahrenheit"], Default = "celsius" },
                ["matrix"] = new()
                {
                    Type = "array", Description = "Rows of integer values",
                    Items = new() { Type = "array", Items = new() { Type = "integer", Description = "Cell value", Default = 0 } }
                },
                ["enabled"] = new() { Type = "boolean", Default = false },
                ["retryCount"] = new() { Type = "integer", Default = 0 },
                ["label"] = new() { Type = "string", Default = "" },
                ["nullableDefault"] = new() { Type = "string", Default = JsonSerializer.SerializeToElement<object?>(null) },
                ["unconstrained"] = new() { Description = "Caller omitted a type" },
                ["whitespaceType"] = new() { Type = " \t", Default = 1 },
                ["options"] = new()
                {
                    Type = "object", Default = new
                    {
                        Type = "KeepCase", URL = "https://example.invalid/default", OrderID = 9,
                        Nested = new { Type = "NestedCase", OrderID = 10 }
                    }
                }
            },
            Required = ["OrderID", "Type", "URL"]
        }
    };

    private static JsonObject AssertSchemaContract(JsonObject request)
    {
        var tool = request["tools"]!.AsArray().Single()!.AsObject();
        Assert.AreEqual("read_report", tool["name"]!.GetValue<string>());
        Assert.AreEqual("Read a report with case-sensitive application fields.", tool["description"]!.GetValue<string>());
        var schema = tool["input_schema"]!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "type", "properties", "required" }, schema.Select(pair => pair.Key).ToArray());
        Assert.AreEqual("object", schema["type"]!.GetValue<string>());
        CollectionAssert.AreEqual(new[] { "OrderID", "Type", "URL" }, schema["required"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        var properties = schema["properties"]!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "Type", "URL", "OrderID", "unit", "matrix", "enabled", "retryCount", "label", "nullableDefault", "unconstrained", "whitespaceType", "options" }, properties.Select(pair => pair.Key).ToArray());

        foreach (var property in properties)
            AssertLowercaseSchemaKeywords(property.Value!.AsObject());
        Assert.AreEqual("string", properties["Type"]!["type"]!.GetValue<string>());
        Assert.AreEqual("Report category", properties["Type"]!["description"]!.GetValue<string>());
        CollectionAssert.AreEqual(new[] { "report", "summary" }, properties["Type"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        CollectionAssert.AreEqual(new[] { "celsius", "fahrenheit" }, properties["unit"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray());
        Assert.AreEqual("celsius", properties["unit"]!["default"]!.GetValue<string>());

        // Null optional fields are omitted; empty/falsy values are actual supplied values.
        CollectionAssert.AreEqual(new[] { "type" }, properties["URL"]!.AsObject().Select(pair => pair.Key).ToArray());
        Assert.AreEqual("", properties["OrderID"]!["description"]!.GetValue<string>());
        Assert.AreEqual("integer", properties["OrderID"]!["type"]!.GetValue<string>());
        Assert.IsFalse(properties["enabled"]!["default"]!.GetValue<bool>());
        Assert.AreEqual(0, properties["retryCount"]!["default"]!.GetValue<int>());
        Assert.AreEqual("", properties["label"]!["default"]!.GetValue<string>());
        Assert.IsTrue(properties["nullableDefault"]!.AsObject().ContainsKey("default"), "An explicit JsonElement null default is distinct from an absent Default value.");
        Assert.IsNull(properties["nullableDefault"]!["default"]);
        CollectionAssert.AreEqual(new[] { "description" }, properties["unconstrained"]!.AsObject().Select(pair => pair.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "default" }, properties["whitespaceType"]!.AsObject().Select(pair => pair.Key).ToArray());
        Assert.AreEqual(1, properties["whitespaceType"]!["default"]!.GetValue<int>());

        var matrix = properties["matrix"]!;
        Assert.AreEqual("array", matrix["type"]!.GetValue<string>());
        Assert.AreEqual("array", matrix["items"]!["type"]!.GetValue<string>());
        Assert.AreEqual("integer", matrix["items"]!["items"]!["type"]!.GetValue<string>());
        Assert.AreEqual("Cell value", matrix["items"]!["items"]!["description"]!.GetValue<string>());
        Assert.AreEqual(0, matrix["items"]!["items"]!["default"]!.GetValue<int>());

        // Default payloads are application data, not schema nodes or naming-policy targets.
        var expectedDefault = JsonNode.Parse("""
            {"Type":"KeepCase","URL":"https://example.invalid/default","OrderID":9,"Nested":{"Type":"NestedCase","OrderID":10}}
            """);
        Assert.IsTrue(JsonNode.DeepEquals(expectedDefault, properties["options"]!["default"]));
        return schema;
    }

    private static void AssertLowercaseSchemaKeywords(JsonObject schema)
    {
        var allowed = new HashSet<string>(["type", "description", "enum", "default", "items"], StringComparer.Ordinal);
        foreach (var pair in schema)
            Assert.IsTrue(allowed.Contains(pair.Key), $"'{pair.Key}' is not an emitted JSON Schema keyword with the required casing.");
        if (schema["items"] is JsonObject items)
            AssertLowercaseSchemaKeywords(items);
    }

    private static async Task AssertArgumentValidationAsync(JsonObject wireSchema)
    {
        // Exact keyword assertions above are essential: schema readers may accept case-insensitive aliases.
        var schema = await JsonSchema.FromJsonAsync(wireSchema.ToJsonString());
        Assert.IsEmpty(schema.Validate(ValidArguments));
        Assert.IsEmpty(schema.Validate("""{"Type":"report","URL":"https://example.invalid","OrderID":17,"unconstrained":{"free":true},"whitespaceType":[false,42]}"""));
        foreach (var invalid in new[]
        {
            """{"Type":7,"URL":"https://example.invalid","OrderID":17}""",
            """{"Type":"unknown","URL":"https://example.invalid","OrderID":17}""",
            """{"Type":"report","URL":"https://example.invalid","OrderID":17,"unit":"kelvin"}""",
            """{"Type":"report","URL":"https://example.invalid","OrderID":17,"matrix":[["tomorrow"]]}""",
            """{"Type":"report","URL":"https://example.invalid"}"""
        })
            Assert.IsNotEmpty(schema.Validate(invalid), $"The wire schema must reject {invalid}");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        private int _generationRequests;
        public AnthropicService Service { get; }
        public List<JsonObject> Requests { get; } = [];
        public List<string> Paths { get; } = [];
        public bool ReturnToolRound { get; init; }
        public int HandlerCalls { get; set; }

        public Fixture(string model)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Service = new AnthropicService("offline", model, _http);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            Requests.Add(body);
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal))
                return Response("{\"input_tokens\":42}", "application/json");

            var toolRound = ReturnToolRound && ++_generationRequests == 1;
            if (body["stream"]!.GetValue<bool>())
                return Response(Stream(body["model"]!.GetValue<string>(), toolRound), "text/event-stream");
            return Response("{\"id\":\"answer\",\"content\":[{\"type\":\"text\",\"text\":\"answer\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":8,\"output_tokens\":2}}", "application/json");
        }

        private static HttpResponseMessage Response(string body, string contentType) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };

        private static string Stream(string model, bool toolRound)
        {
            var events = new List<object>
            {
                new { type = "message_start", message = new { id = "schema-fixture", model, usage = new { input_tokens = 8 } } }
            };
            if (toolRound)
            {
                events.Add(new { type = "content_block_start", index = 0, content_block = new { type = "tool_use", id = "toolu_report", name = "read_report", input = new { } } });
                events.Add(new { type = "content_block_delta", index = 0, delta = new { type = "input_json_delta", partial_json = ValidArguments } });
            }
            else
            {
                events.Add(new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } });
                events.Add(new { type = "content_block_delta", index = 0, delta = new { type = "text_delta", text = "answer" } });
            }
            events.Add(new { type = "content_block_stop", index = 0 });
            events.Add(new { type = "message_delta", delta = new { stop_reason = toolRound ? "tool_use" : "end_turn" }, usage = new { output_tokens = 2 } });
            events.Add(new { type = "message_stop" });
            return string.Concat(events.Select(value => "data: " + JsonSerializer.Serialize(value) + "\n\n"));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _http.Dispose();
            base.Dispose(disposing);
        }
    }
}
