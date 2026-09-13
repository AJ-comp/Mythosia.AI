using Mythosia.AI.Builders;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using Mythosia.AI.Providers.Alibaba;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.DeepSeek;
using Mythosia.AI.Services.Perplexity;
using Mythosia.AI.Services.xAI;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AIRequestBuilderProviderTests
{
    [TestMethod]
    [DataRow("DeepSeek", false)]
    [DataRow("DeepSeek", true)]
    [DataRow("xAI", false)]
    [DataRow("xAI", true)]
    [DataRow("Perplexity", false)]
    [DataRow("Perplexity", true)]
    [DataRow("Qwen", false)]
    [DataRow("Qwen", true)]
    public async Task PreparedRequest_UsesCapturedProviderSettingsWithoutOverwritingDefaults(string provider, bool run)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        service.Temperature = 0.4f;
        service.MaxTokens = 321;
        service.SystemMessage = "original instructions";
        var originalModel = service.Model;
        var basis = service.CreateRequest("question");
        var request = basis.WithTemperature(0.2f);
        Assert.AreEqual(0.4f, service.Temperature);

        service.Temperature = 0.9f;
        service.MaxTokens = 999;
        service.SystemMessage = "later instructions";
        ChangeProviderDefaults(service);
        var laterModel = service.Model;
        handler.OnRequest = _ =>
        {
            // Check while the request is executing, before any finally/restore path can run.
            Assert.AreEqual(0.9f, service.Temperature);
            Assert.AreEqual((uint)999, service.MaxTokens);
            Assert.AreEqual("later instructions", service.SystemMessage);
            Assert.AreEqual(laterModel, service.Model);
            AssertChangedNativeDefaults(service);
        };

        Assert.AreEqual("answer", await Execute(request, run));
        Assert.AreEqual("answer", await Execute(basis, run));
        Assert.AreEqual(2, handler.Requests.Count);
        for (var index = 0; index < handler.Requests.Count; index++)
        {
            var body = handler.Requests[index];
            Assert.AreEqual(provider == "Qwen" ? "qwen-original-alias" : originalModel, body["model"]!.GetValue<string>());
            Assert.AreEqual(index == 0 ? 0.2f : 0.4f, body["temperature"]!.GetValue<float>());
            Assert.AreEqual(321, body[provider == "Perplexity" ? "max_output_tokens" : "max_tokens"]!.GetValue<int>());
            Assert.IsTrue(body.ToJsonString().Contains("original instructions", StringComparison.Ordinal));
            Assert.IsFalse(body.ToJsonString().Contains("later instructions", StringComparison.Ordinal));
            switch (provider)
            {
                case "DeepSeek":
                    Assert.AreEqual("disabled", body["thinking"]!["type"]!.GetValue<string>());
                    Assert.IsFalse(body.ContainsKey("reasoning_effort"));
                    break;
                case "xAI": Assert.AreEqual("low", body["reasoning_effort"]!.GetValue<string>()); break;
                case "Qwen": Assert.IsTrue(body["enable_thinking"]!.GetValue<bool>()); break;
                case "Perplexity": Assert.AreEqual(3, body["max_steps"]!.GetValue<int>()); break;
            }
        }
        Assert.AreEqual(0.9f, service.Temperature);
        AssertChangedNativeDefaults(service);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeepSeekPreparedRequest_CapturesNativeThinkingEffort(bool run)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new DeepSeekService("offline-key", client)
        {
            ThinkingEnabled = true, ReasoningEffort = DeepSeekReasoning.Low
        };
        var request = service.CreateRequest("question");
        service.ThinkingEnabled = false;
        service.ReasoningEffort = DeepSeekReasoning.Max;
        Assert.AreEqual("answer", await Execute(request, run));
        var body = handler.Requests.Single();
        Assert.AreEqual("enabled", body["thinking"]!["type"]!.GetValue<string>());
        Assert.AreEqual("low", body["reasoning_effort"]!.GetValue<string>());
        Assert.IsFalse(service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.Max, service.ReasoningEffort);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PerplexityPreparedRequest_CopiesNestedHostedToolParameters(bool run)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var filters = new JsonObject { ["search_domain_filter"] = new JsonArray("original.example") };
        var tool = new PerplexityHostedTool
        {
            Type = "web_search", Parameters = new() { ["filters"] = filters }
        };
        var service = new PerplexityService("offline-key", client);
        service.AgentOptions.Tools.Add(tool);
        var request = service.CreateRequest("question");
        filters["search_domain_filter"]![0] = "changed.example";
        tool.Parameters["filters"] = new JsonObject { ["search_recency_filter"] = "day" };
        service.AgentOptions.Tools.Clear();

        Assert.AreEqual("answer", await Execute(request, run));
        Assert.AreEqual("answer", await Execute(request, run));
        foreach (var body in handler.Requests)
        {
            var hostedTool = body["tools"]!.AsArray().Single()!;
            Assert.AreEqual("web_search", hostedTool["type"]!.GetValue<string>());
            Assert.AreEqual("original.example", hostedTool["filters"]!["search_domain_filter"]![0]!.GetValue<string>());
        }
        Assert.AreEqual(0, service.AgentOptions.Tools.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PerplexityPreviousResponse_ValidatesTheCompletedBuilderSettings(bool run)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new PerplexityService("offline-key", client);
        service.AgentOptions.PreviousResponseId = "response-before";

        // Capturing defaults must permit subsequent fluent settings to complete the request.
        var basis = service.CreateRequest("continue");
        var request = basis.WithStatelessMode();
        handler.OnRequest = _ => Assert.IsFalse(service.StatelessMode);
        Assert.AreEqual("answer", await Execute(request, run));
        Assert.AreEqual("response-before", handler.Requests.Single()["previous_response_id"]!.GetValue<string>());
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
        Assert.IsFalse(service.StatelessMode);

        // The original builder still lacks StatelessMode and must fail before sending anything.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(basis, run));
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FunctionSchemaDefaults_DetachJsonNodesAndDocumentOwnedElements(bool run)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new PerplexityService("offline-key", client);
        var originalNode = new JsonObject { ["cities"] = new JsonArray("Seoul") };
        var document = JsonDocument.Parse("{\"region\":\"original\"}");
        var definition = new FunctionDefinition
        {
            Name = "lookup",
            Handler = _ => Task.FromResult("unused"),
            Parameters = new FunctionParameters
            {
                Properties = new()
                {
                    ["node"] = new ParameterProperty { Type = "object", Default = originalNode },
                    ["element"] = new ParameterProperty { Type = "object", Default = document.RootElement }
                }
            }
        };
        var request = service.CreateRequest("question").WithFunctions(definition);
        originalNode["cities"]![0] = "Busan";
        document.Dispose();

        Assert.AreEqual("answer", await Execute(request, run));
        Assert.AreEqual("answer", await Execute(request, run));
        foreach (var body in handler.Requests)
        {
            var tool = body["tools"]!.AsArray().Single(item => item!["type"]!.GetValue<string>() == "function")!;
            var properties = tool["parameters"]!["properties"]!;
            Assert.AreEqual("Seoul", properties["node"]!["default"]!["cities"]![0]!.GetValue<string>());
            Assert.AreEqual("original", properties["element"]!["default"]!["region"]!.GetValue<string>());
        }
        Assert.AreEqual(0, service.Functions.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SummarizationProfile_SuppressesUnsupportedFeaturesLikeLegacyCompletion(bool run)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new XAIService("offline-key", AIModels.xAI.Grok4_6, client);
        service.WithWebSearch();
        Assert.AreEqual("answer", await service.GetCompletionAsync("legacy summary", RequestProfiles.Summarization));

        // Internal profiles do not consume pending user-request features. The prepared request
        // captures the same unsupported search setting, then suppresses it through its profile.
        var request = service.CreateRequest("prepared summary").WithProfile(RequestProfiles.Summarization);
        Assert.AreEqual("answer", await Execute(request, run));
        Assert.AreEqual(2, handler.Requests.Count);
        foreach (var body in handler.Requests)
        {
            Assert.IsFalse(body.ContainsKey("tools"));
            Assert.AreEqual(0.2f, body["temperature"]!.GetValue<float>());
            Assert.AreEqual(256, body["max_tokens"]!.GetValue<int>());
            Assert.AreEqual("low", body["reasoning_effort"]!.GetValue<string>());
        }
        Assert.IsFalse(service.StatelessMode);
        Assert.IsFalse(service.FunctionsDisabled);
        Assert.AreEqual(GrokReasoning.Auto, service.ReasoningEffort);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow("DeepSeek", false)]
    [DataRow("DeepSeek", true)]
    [DataRow("xAI", false)]
    [DataRow("xAI", true)]
    [DataRow("Perplexity", false)]
    [DataRow("Perplexity", true)]
    [DataRow("Qwen", false)]
    [DataRow("Qwen", true)]
    public async Task RepeatedBuilderExecution_AnchorsContextToEachNewInput(string provider, bool run)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        var input = new Message(ActorRole.User, "original question");
        var contextInput = new Message(ActorRole.User, "enriched question");
        var request = service.CreateRequest(input).WithContext(new AIRequestContext { RequestMessageOverride = contextInput });

        Assert.AreEqual("answer", await Execute(request, run));
        Assert.AreEqual("answer", await Execute(request, run));
        var savedInputs = service.ActivateChat.Messages.Where(message => message.Role == ActorRole.User).ToArray();
        Assert.AreEqual(2, savedInputs.Length);
        Assert.AreNotEqual(savedInputs[0].Id, savedInputs[1].Id);
        Assert.IsTrue(savedInputs.All(message => message.Id != input.Id));
        Assert.IsTrue(savedInputs.All(message => message.Content == "original question"));

        var field = provider == "Perplexity" ? "input" : "messages";
        var firstUsers = handler.Requests[0][field]!.AsArray().Where(message => message?["role"]?.GetValue<string>() == "user").ToArray();
        var secondUsers = handler.Requests[1][field]!.AsArray().Where(message => message?["role"]?.GetValue<string>() == "user").ToArray();
        Assert.AreEqual("enriched question", firstUsers.Single()!["content"]!.GetValue<string>());
        Assert.AreEqual(2, secondUsers.Length);
        Assert.AreEqual("original question", secondUsers[0]!["content"]!.GetValue<string>());
        Assert.AreEqual("enriched question", secondUsers[1]!["content"]!.GetValue<string>());
        Assert.AreEqual("original question", input.Content);
        Assert.AreEqual("enriched question", contextInput.Content);
    }

    [TestMethod]
    [DataRow("DeepSeek")]
    [DataRow("xAI")]
    [DataRow("Perplexity")]
    [DataRow("Qwen")]
    public async Task PreparedRequest_CopiesFunctionSchemaAndUsesCapturedHandler(string provider)
    {
        using var handler = new CaptureHandler { ReturnFunctionCall = true };
        using var client = new HttpClient(handler);
        var service = CreateService(provider, client);
        var executions = 0;
        var definition = new FunctionDefinition
        {
            Name = "lookup", Description = "original description",
            Handler = _ => { executions++; return Task.FromResult("original result"); },
            Parameters = new FunctionParameters
            {
                Properties = new()
                {
                    ["city"] = new ParameterProperty { Type = "string", Enum = ["Seoul"] }
                },
                Required = ["city"]
            }
        };
        var request = service.CreateRequest("question").WithFunctions(definition);
        definition.Name = "changed";
        definition.Description = "changed description";
        definition.Parameters.Properties["city"].Enum![0] = "Busan";
        definition.Parameters.Required.Clear();
        definition.Handler = _ => throw new InvalidOperationException("The mutated handler must not be used.");

        Assert.AreEqual("answer", await request.GetCompletionAsync());
        Assert.AreEqual(1, executions);
        Assert.AreEqual(0, service.Functions.Count);
        Assert.AreEqual(2, handler.Requests.Count);
        var tools = handler.Requests[0]["tools"]!.AsArray();
        var function = provider == "Perplexity"
            ? tools.Single(tool => tool!["type"]!.GetValue<string>() == "function")!
            : tools.Single()!["function"]!;
        Assert.AreEqual("lookup", function["name"]!.GetValue<string>());
        Assert.AreEqual("original description", function["description"]!.GetValue<string>());
        Assert.AreEqual("Seoul", function["parameters"]!["properties"]!["city"]!["enum"]![0]!.GetValue<string>());
        Assert.AreEqual("city", function["parameters"]!["required"]![0]!.GetValue<string>());
        StringAssert.Contains(handler.Requests[1].ToJsonString(), "original result");
    }

    private static AIService CreateService(string provider, HttpClient client) => provider switch
    {
        "DeepSeek" => new DeepSeekService("offline-key", client),
        "xAI" => new XAIService("offline-key", AIModels.xAI.Grok4_6, client) { ReasoningEffort = GrokReasoning.Low },
        "Perplexity" => new PerplexityService("offline-key", client) { AgentOptions = new PerplexityAgentOptions { MaxSteps = 3 } },
        "Qwen" => new QwenService("offline-key", client) { ModelIdOverride = "qwen-original-alias", ThinkingMode = QwenThinking.On },
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static void ChangeProviderDefaults(AIService service)
    {
        switch (service)
        {
            case DeepSeekService deepSeek:
                deepSeek.ChangeModel("deepseek-later-model");
                deepSeek.ThinkingEnabled = true;
                deepSeek.ReasoningEffort = DeepSeekReasoning.Max;
                break;
            case XAIService xai:
                xai.ChangeModel(AIModels.xAI.Grok4_3);
                xai.ReasoningEffort = GrokReasoning.High;
                break;
            case PerplexityService perplexity:
                perplexity.ChangeModel("google/gemini-3.8-flash");
                perplexity.AgentOptions.MaxSteps = 9;
                break;
            case QwenService qwen:
                qwen.ChangeModel(AlibabaModels.QwenPlus);
                qwen.ModelIdOverride = "qwen-later-alias";
                qwen.ThinkingMode = QwenThinking.Off;
                break;
        }
    }

    private static void AssertChangedNativeDefaults(AIService service)
    {
        switch (service)
        {
            case DeepSeekService deepSeek:
                Assert.IsTrue(deepSeek.ThinkingEnabled);
                Assert.AreEqual(DeepSeekReasoning.Max, deepSeek.ReasoningEffort);
                break;
            case XAIService xai: Assert.AreEqual(GrokReasoning.High, xai.ReasoningEffort); break;
            case PerplexityService perplexity: Assert.AreEqual(9, perplexity.AgentOptions.MaxSteps); break;
            case QwenService qwen:
                Assert.AreEqual(QwenThinking.Off, qwen.ThinkingMode);
                Assert.AreEqual("qwen-later-alias", qwen.ModelIdOverride);
                break;
        }
    }

    private static async Task<string> Execute(AIRequestBuilder request, bool run)
    {
        if (!run) return await request.GetCompletionAsync();
        await using var execution = await request.StartRunAsync();
        return (await execution.Result).Text;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];
        public Action<JsonObject>? OnRequest { get; set; }
        public bool ReturnFunctionCall { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add(body);
            OnRequest?.Invoke(body);
            var agent = request.RequestUri!.AbsolutePath.EndsWith("/agent", StringComparison.Ordinal);
            var tool = ReturnFunctionCall && Requests.Count == 1;
            var streaming = body["stream"]?.GetValue<bool>() == true;
            var payload = agent ? AgentReply(tool) : ChatReply(tool, streaming);
            string response;
            if (!streaming) response = payload.ToJsonString();
            else if (agent)
                response = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"answer\"}\n\n"
                    + "data: " + new JsonObject { ["type"] = "response.completed", ["response"] = payload }.ToJsonString() + "\n\n";
            else response = "data: " + payload.ToJsonString() + "\n\ndata: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, streaming ? "text/event-stream" : "application/json")
            };
        }

        private static JsonObject ChatReply(bool tool, bool streaming)
        {
            var message = new JsonObject { ["role"] = "assistant", ["content"] = tool ? "" : "answer" };
            if (tool) message["tool_calls"] = new JsonArray(new JsonObject
            {
                ["id"] = "call-original", ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "lookup", ["arguments"] = "{\"city\":\"Seoul\"}" }
            });
            return new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject
                {
                    [streaming ? "delta" : "message"] = message,
                    ["finish_reason"] = tool ? "tool_calls" : "stop"
                })
            };
        }

        private static JsonObject AgentReply(bool tool) => new()
        {
            ["id"] = "response-original", ["status"] = "completed",
            ["output"] = tool
                ? new JsonArray(new JsonObject
                {
                    ["id"] = "item-original", ["type"] = "function_call", ["status"] = "completed",
                    ["call_id"] = "call-original", ["name"] = "lookup", ["arguments"] = "{\"city\":\"Seoul\"}"
                })
                : new JsonArray(new JsonObject
                {
                    ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "answer" })
                })
        };
    }
}
