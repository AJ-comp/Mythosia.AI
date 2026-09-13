using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialRequestSnapshotTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExcessivelyNestedFunctionSchemaFailsBeforeSnapshotIsPublished(bool addToBuilder)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var original = service.CreateRequest("safe");
        service.ConfigureRequestFeatures(new AIRequestFeatures
        {
            Reasoning = new ReasoningOptions { Level = ReasoningLevel.Low }
        });
        var property = new ParameterProperty { Type = "string" };
        for (var depth = 0; depth < 80; depth++)
            property = new ParameterProperty { Type = "array", Items = property };
        var function = Function(property);

        if (addToBuilder)
            Assert.Throws<ArgumentException>(() => original.WithFunctions(function));
        else
        {
            service.Functions.Add(function);
            Assert.Throws<ArgumentException>(() => service.CreateRequest("invalid"));
            service.Functions.Clear();
        }
        Assert.AreEqual("captured-model", original.GetCapabilities().Model);
        CollectionAssert.AreEqual(new[] { ReasoningLevel.Low }, service.GetCapabilities().ReasoningLevels.ToArray());
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void CyclicFunctionSchemaFailsNormally(bool addToBuilder, bool indirectCycle)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var original = service.CreateRequest("safe");
        var property = new ParameterProperty { Type = "array" };
        property.Items = indirectCycle ? new ParameterProperty { Type = "array", Items = property } : property;
        var function = Function(property);

        if (addToBuilder)
            Assert.Throws<ArgumentException>(() => original.WithFunctions(function));
        else
        {
            service.Functions.Add(function);
            Assert.Throws<ArgumentException>(() => service.CreateRequest("invalid"));
            service.Functions.Clear();
        }
        Assert.AreEqual("captured-model", original.GetCapabilities().Model);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task JsonDocumentMetadataSurvivesCallerDisposal()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        var request = service.CreateRequest(new Message(ActorRole.User, "inspect")
        {
            Metadata = new Dictionary<string, object> { ["value"] = document.RootElement }
        });
        document.Dispose();

        service.Observe = message => ((JsonElement)message.Metadata!["value"]).GetProperty("name").GetString()!;
        Assert.AreEqual("captured", await request.GetCompletionAsync());
        Assert.AreEqual("captured", await request.GetCompletionAsync());
    }

    [TestMethod]
    public async Task JsonDocumentFunctionArgumentsSurviveCallerDisposal()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        var request = service.CreateRequest(new Message(ActorRole.Assistant, "inspect")
        {
            FunctionCallBatch = new FunctionCallBatch(new[]
            {
                new FunctionCall { Name = "lookup", Arguments = new Dictionary<string, object> { ["value"] = document.RootElement } }
            })
        });
        document.Dispose();

        service.Observe = message => ((JsonElement)message.FunctionCallBatch!.Calls[0].Arguments["value"])
            .GetProperty("name").GetString()!;
        Assert.AreEqual("captured", await request.GetCompletionAsync());
    }

    [TestMethod]
    public async Task JsonNodeMetadataIsDetachedFromCallerAndEachExecution()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var node = JsonNode.Parse("{\"name\":\"captured\"}")!;
        var request = service.CreateRequest(new Message(ActorRole.User, "inspect")
        {
            Metadata = new Dictionary<string, object> { ["value"] = node }
        });
        node["name"] = "caller mutation";
        service.Observe = message =>
        {
            var captured = (JsonNode)message.Metadata!["value"];
            var name = captured["name"]!.GetValue<string>();
            captured["name"] = "execution mutation";
            return name;
        };

        Assert.AreEqual("captured", await request.GetCompletionAsync());
        Assert.AreEqual("captured", await request.GetCompletionAsync());
        Assert.AreEqual("caller mutation", node["name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task JsonCloneBridgeLeavesCustomMetadataReferencesUntouched()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var custom = new CustomMetadata();
        var request = service.CreateRequest(new Message(ActorRole.User, "inspect")
        {
            Metadata = new Dictionary<string, object> { ["value"] = custom }
        });
        service.Observe = message =>
        {
            Assert.AreSame(custom, message.Metadata!["value"]);
            return "safe";
        };
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    public void AlreadyDisposedJsonDocumentFailsWithOriginalException()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        var element = document.RootElement;
        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => service.CreateRequest(new Message(ActorRole.User, "inspect")
        {
            Metadata = new Dictionary<string, object> { ["value"] = element }
        }));
    }

    [TestMethod]
    public async Task SharedSchemaChildIsAllowedAndCopiedIndependently()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var child = new ParameterProperty { Type = "string", Enum = new List<string> { "captured" } };
        var function = Function(new ParameterProperty { Type = "array", Items = child });
        function.Parameters.Properties["other"] = new ParameterProperty { Type = "array", Items = child };
        var request = service.CreateRequest("inspect").WithFunctions(function);
        child.Enum[0] = "caller mutation";
        service.Observe = _ =>
        {
            var properties = service.ExecutionFunctions[0].Parameters.Properties;
            Assert.AreNotSame(properties["value"].Items, properties["other"].Items);
            Assert.AreEqual("captured", properties["value"].Items!.Enum![0]);
            Assert.AreEqual("captured", properties["other"].Items!.Enum![0]);
            properties["value"].Items!.Enum![0] = "execution mutation";
            return "safe";
        };
        Assert.AreEqual("safe", await request.GetCompletionAsync());
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    public async Task ConcurrentCapabilityQueriesKeepIndependentCapturedSettings()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var low = service.CreateRequest("low").WithTemperature(0.2f).WithReasoning(ReasoningLevel.Low);
        var high = service.CreateRequest("high").WithTemperature(0.8f).WithReasoning(ReasoningLevel.High);
        var queries = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            var snapshot = (index % 2 == 0 ? low : high).GetCapabilities();
            Assert.AreEqual(index % 2 == 0 ? CapabilitySupport.Supported : CapabilitySupport.Unsupported, snapshot.Temperature);
            CollectionAssert.AreEqual(new[] { index % 2 == 0 ? ReasoningLevel.Low : ReasoningLevel.High }, snapshot.ReasoningLevels.ToArray());
        }));
        await Task.WhenAll(queries);
        Assert.AreEqual(0, service.GetCapabilities().ReasoningLevels.Count);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    private static FunctionDefinition Function(ParameterProperty property) => new()
    {
        Name = "lookup",
        Parameters = new FunctionParameters { Properties = new Dictionary<string, ParameterProperty> { ["value"] = property } },
        Handler = _ => Task.FromResult("safe")
    };

    private sealed class SnapshotProbe : AIService
    {
        // Avoid network, tokenizer and conversation behavior: these tests isolate the request-owned payload.
        public SnapshotProbe(HttpClient client) : base("offline", "https://localhost/", client)
        {
            Model = "captured-model";
            AddNewChat();
        }
        public override string Provider => "Custom";
        public Func<Message, string>? Observe { get; set; }
        public List<FunctionDefinition> ExecutionFunctions => RequestFunctions;
        public override Task<string> GetCompletionAsync(Message message) => Task.FromResult(Observe?.Invoke(message) ?? "safe");
        protected override void ValidateRequestFeatures(AIRequestFeatures features) { }
        protected override AIModelCapabilities ResolveRequestCapabilities() => new(provider: Provider, model: RequestModel,
            temperature: RequestTemperature < 0.5f ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
            reasoning: CapabilitySupport.Supported,
            reasoningLevels: CurrentRequestFeatures.Reasoning is { } reasoning ? new[] { reasoning.Level } : Array.Empty<ReasoningLevel>());
        public override Task StreamCompletionAsync(Message message, Func<string, Task> callback) => throw new NotSupportedException();
        protected override HttpRequestMessage CreateMessageRequest() => throw new AssertFailedException("No request expected");
        protected override HttpRequestMessage CreateFunctionMessageRequest() => throw new AssertFailedException("No request expected");
        protected override (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string response) => (response, new());
        protected override string ExtractResponseContent(string response) => response;
        protected override string StreamParseJson(string response) => response;
        public override Task<uint> GetInputTokenCountAsync() => Task.FromResult(0u);
        public override Task<uint> GetInputTokenCountAsync(string prompt) => Task.FromResult(0u);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("No network expected.");
    }

    private sealed class CustomMetadata
    {
        public object Clone() => throw new AssertFailedException("Do not call arbitrary metadata cloning methods.");
        public object DeepClone() => throw new AssertFailedException("Do not call arbitrary metadata cloning methods.");
        public string Value => throw new AssertFailedException("Do not serialize arbitrary metadata getters.");
    }
}
