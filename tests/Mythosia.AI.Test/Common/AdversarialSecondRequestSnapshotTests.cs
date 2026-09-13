using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class AdversarialSecondRequestSnapshotTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RectangularJsonArraysKeepShapeAndDocumentIndependence(bool inContext)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        var original = new JsonElement[1, 2] { { document.RootElement, document.RootElement } };
        var input = WithMetadata(original);
        var request = inContext
            ? service.CreateRequest("input").WithContext(new AIRequestContext { RequestMessageOverride = input })
            : service.CreateRequest(input);
        document.Dispose();
        service.Observe = message =>
        {
            var copied = (JsonElement[,])message.Metadata!["value"];
            Assert.AreEqual(1, copied.GetLength(0));
            Assert.AreEqual(2, copied.GetLength(1));
            Assert.AreEqual("captured", copied[0, 1].GetProperty("name").GetString());
            copied[0, 1] = default;
            return "safe";
        };
        Assert.AreEqual(CapabilitySupport.Unknown, request.GetCapabilities().ImageInput);
        Assert.AreEqual("safe", await request.GetCompletionAsync());
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    [DataRow(1, 7)]
    [DataRow(2, -2)]
    public async Task NonZeroBoundArraysKeepTheirIndexRanges(int rank, int lowerBound)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var lengths = Enumerable.Repeat(2, rank).ToArray();
        var lowerBounds = Enumerable.Repeat(lowerBound, rank).ToArray();
        var original = Array.CreateInstance(typeof(JsonNode), lengths, lowerBounds);
        original.SetValue(JsonNode.Parse("{\"name\":\"captured\"}"), lowerBounds);
        var request = service.CreateRequest(WithMetadata(original));
        ((JsonNode)original.GetValue(lowerBounds)!)["name"] = "caller mutation";
        service.Observe = message =>
        {
            var copied = (Array)message.Metadata!["value"];
            Assert.AreEqual(original.GetType(), copied.GetType());
            Assert.AreEqual(rank, copied.Rank);
            for (var dimension = 0; dimension < rank; dimension++)
            {
                Assert.AreEqual(lowerBound, copied.GetLowerBound(dimension));
                Assert.AreEqual(2, copied.GetLength(dimension));
            }
            var value = (JsonNode)copied.GetValue(lowerBounds)!;
            Assert.AreEqual("captured", value["name"]!.GetValue<string>());
            value["name"] = "execution mutation";
            return "safe";
        };
        Assert.AreEqual("safe", await request.GetCompletionAsync());
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    [DataRow("Dictionary")]
    [DataRow("SortedDictionary")]
    [DataRow("SortedList")]
    public async Task TypedStringDictionariesKeepCaseInsensitiveComparer(string kind)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        IDictionary<string, JsonElement> original = kind switch
        {
            "Dictionary" => new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            "SortedDictionary" => new SortedDictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            _ => new SortedList<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        };
        original["TITLE"] = document.RootElement;
        var request = service.CreateRequest(WithMetadata(original));
        document.Dispose();
        service.Observe = message =>
        {
            var copied = (IDictionary<string, JsonElement>)message.Metadata!["value"];
            Assert.AreEqual(original.GetType(), copied.GetType());
            Assert.IsTrue(copied.ContainsKey("title"), "Snapshotting must preserve the dictionary's lookup semantics.");
            Assert.AreEqual("captured", copied["title"].GetProperty("name").GetString());
            copied.Clear();
            return "safe";
        };
        Assert.AreEqual("safe", await request.GetCompletionAsync());
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    public async Task RectangularArraysPreserveCyclesAndSharedChildren()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var node = JsonNode.Parse("{\"name\":\"captured\"}")!;
        var original = new object[2, 2];
        original[0, 0] = original;
        original[0, 1] = node;
        original[1, 0] = node;
        var request = service.CreateRequest(WithMetadata(original));
        node["name"] = "caller mutation";
        service.Observe = message =>
        {
            var copied = (object[,])message.Metadata!["value"];
            Assert.AreSame(copied, copied[0, 0]);
            Assert.AreSame(copied[0, 1], copied[1, 0]);
            Assert.IsNull(copied[1, 1]);
            Assert.AreNotSame(node, copied[0, 1]);
            Assert.AreEqual("captured", ((JsonNode)copied[0, 1])["name"]!.GetValue<string>());
            return "safe";
        };
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    public async Task VectorArrayWithNullableJsonElementsRemainsAVector()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        using var document = JsonDocument.Parse("{\"name\":\"captured\"}");
        var original = new JsonElement?[] { null, document.RootElement, default(JsonElement) };
        var request = service.CreateRequest(WithMetadata(original));
        document.Dispose();
        service.Observe = message =>
        {
            var copied = (JsonElement?[])message.Metadata!["value"];
            Assert.AreEqual(original.GetType(), copied.GetType());
            Assert.IsNull(copied[0]);
            Assert.AreEqual("captured", copied[1]!.Value.GetProperty("name").GetString());
            Assert.AreEqual(JsonValueKind.Undefined, copied[2]!.Value.ValueKind);
            return "safe";
        };
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task EmptyArraysKeepShapeWithoutVisitingElements(int rank)
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var lengths = Enumerable.Repeat(0, rank).ToArray();
        var bounds = Enumerable.Repeat(7, rank).ToArray();
        var original = Array.CreateInstance(typeof(JsonElement), lengths, bounds);
        var request = service.CreateRequest(WithMetadata(original));
        service.Observe = message =>
        {
            var copied = (Array)message.Metadata!["value"];
            Assert.AreEqual(original.GetType(), copied.GetType());
            Assert.AreEqual(rank, copied.Rank);
            Assert.AreEqual(0L, copied.LongLength);
            for (var dimension = 0; dimension < rank; dimension++)
                Assert.AreEqual(7, copied.GetLowerBound(dimension));
            return "safe";
        };
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    public async Task UndefinedJsonElementInContextMetadataRemainsUndefined()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        var request = service.CreateRequest("input").WithContext(new AIRequestContext
        {
            RequestMessageOverride = WithMetadata(default(JsonElement))
        });
        service.Observe = message =>
        {
            Assert.AreEqual(JsonValueKind.Undefined, ((JsonElement)message.Metadata!["value"]).ValueKind);
            return "safe";
        };
        Assert.AreEqual("captured-model", request.GetCapabilities().Model);
        Assert.AreEqual("safe", await request.GetCompletionAsync());
    }

    [TestMethod]
    public void MaximumAllowedSchemaDepthDoesNotRejectDistinctEqualObjects()
    {
        using var client = new HttpClient(new NoNetworkHandler());
        var service = new SnapshotProbe(client);
        ParameterProperty property = new EqualProperty { Type = "string" };
        for (var depth = 1; depth < 64; depth++)
            property = new EqualProperty { Type = "array", Items = property };
        var request = service.CreateRequest("input").WithFunctions(new FunctionDefinition
        {
            Name = "tool",
            Parameters = new FunctionParameters { Properties = new Dictionary<string, ParameterProperty> { ["value"] = property } }
        });
        Assert.AreEqual("captured-model", request.GetCapabilities().Model);
        Assert.AreEqual(0, service.ActivateChat.Messages.Count);
    }

    private static Message WithMetadata(object value) => new(ActorRole.User, "input")
    {
        Metadata = new Dictionary<string, object> { ["value"] = value }
    };

    private sealed class EqualProperty : ParameterProperty
    {
        public override bool Equals(object? other) => other is EqualProperty;
        public override int GetHashCode() => 1;
    }

    private sealed class SnapshotProbe : AIService
    {
        public SnapshotProbe(HttpClient client) : base("offline", "https://localhost/", client)
        {
            Model = "captured-model";
            AddNewChat();
        }
        public override string Provider => "Custom";
        public Func<Message, string>? Observe { get; set; }
        public override Task<string> GetCompletionAsync(Message message)
            => Task.FromResult(Observe?.Invoke(CurrentRequestContext?.RequestMessageOverride ?? message) ?? "safe");
        protected override AIModelCapabilities ResolveRequestCapabilities() => new(provider: Provider, model: RequestModel);
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
}
