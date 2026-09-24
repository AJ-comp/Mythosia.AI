using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.DeepSeek;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class DeepSeekResponsesSafetyTests
{
    private const string FileId = "file-api-synthetic-image";

    [TestMethod]
    public async Task AutoThinking_OmitsExplicitEffortAndIsCapturedForReusableRequests()
    {
        using var fixture = new Fixture(useResponses: true);
        fixture.Service.ThinkingEnabled = true;
        fixture.Service.ReasoningEffort = DeepSeekReasoning.Auto;
        var captured = fixture.Service.CreateRequest("captured auto request");
        Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("persistent auto request"));
        fixture.Service.ThinkingEnabled = false;
        fixture.Service.ReasoningEffort = DeepSeekReasoning.Max;
        Assert.AreEqual("answer", await captured.GetCompletionAsync());
        Assert.IsFalse(fixture.Handler.Requests[0].Body.ContainsKey("reasoning"));
        Assert.IsFalse(fixture.Handler.Requests[1].Body.ContainsKey("reasoning"));
        Assert.IsFalse(fixture.Service.ThinkingEnabled);
        Assert.AreEqual(DeepSeekReasoning.Max, fixture.Service.ReasoningEffort);
        Assert.AreEqual("answer", await fixture.Service.GetCompletionAsync("next ordinary request"));
        Assert.AreEqual("none", fixture.Handler.Requests[2].Body["reasoning"]?["effort"]?.GetValue<string>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompletedWithIncompleteDetails_IsRejectedWithoutAssistantHistory(bool run)
    {
        using var fixture = new Fixture(useResponses: true);
        fixture.Handler.Response = () =>
        {
            var response = ValidResponse(useResponses: true);
            response["incomplete_details"] = new JsonObject { ["reason"] = "max_output_tokens" };
            return response;
        };
        await Assert.ThrowsAsync<AIServiceException>(() => Execute(fixture.Service, new Message(ActorRole.User, "question"), run));
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CompletedReasoningWithoutAnswerOrTool_IsRejectedWithoutAssistantHistory(bool run)
    {
        using var fixture = new Fixture(useResponses: true);
        fixture.Handler.Response = () => new JsonObject
        {
            ["status"] = "completed", ["model"] = AIModels.DeepSeek.Flash,
            ["output"] = new JsonArray(new JsonObject
            {
                ["type"] = "reasoning", ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "reasoning_text", ["text"] = "Unfinished thought without an answer."
                })
            })
        };
        await Assert.ThrowsAsync<AIServiceException>(() => Execute(fixture.Service, new Message(ActorRole.User, "question"), run));
        Assert.AreEqual(1, fixture.Handler.Requests.Count);
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task TokenUsageSumOverflow_IsRejectedWithoutAssistantHistory(bool useResponses, bool run)
    {
        using var fixture = new Fixture(useResponses);
        fixture.Handler.Response = () =>
        {
            var response = ValidResponse(useResponses);
            response["usage"] = useResponses
                ? new JsonObject { ["input_tokens"] = int.MaxValue, ["output_tokens"] = 1 }
                : new JsonObject { ["prompt_tokens"] = int.MaxValue, ["completion_tokens"] = 1 };
            return response;
        };
        await Assert.ThrowsAsync<AIServiceException>(() => Execute(fixture.Service, new Message(ActorRole.User, "question"), run));
        Assert.IsFalse(fixture.Service.ActivateChat.Messages.Any(message => message.Role == ActorRole.Assistant));
    }

    [TestMethod]
    [DataRow(false, false, ActorRole.User)]
    [DataRow(false, true, ActorRole.User)]
    [DataRow(true, false, ActorRole.User)]
    [DataRow(true, true, ActorRole.User)]
    [DataRow(false, false, ActorRole.Function)]
    [DataRow(false, true, ActorRole.Function)]
    [DataRow(true, false, ActorRole.Function)]
    [DataRow(true, true, ActorRole.Function)]
    public async Task UploadedImageReference_SerializesForUserAndPairedToolMessages(bool useResponses, bool run, ActorRole role)
    {
        using var fixture = new Fixture(useResponses);
        var image = new DeepSeekImageFileContent(FileId);
        var message = new Message(role, new List<MessageContent> { new TextContent("Describe the image."), image });
        if (role == ActorRole.Function)
        {
            fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.User, "Read the generated image."));
            fixture.Service.ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "")
            {
                FunctionCallBatch = new FunctionCallBatch(new[]
                {
                    new FunctionCall { Id = "image-call", Name = "capture_image", Arguments = new Dictionary<string, object>() }
                })
            });
            message.Metadata = new Dictionary<string, object> { [MessageMetadataKeys.FunctionId] = "image-call" };
        }
        Assert.AreEqual("answer", await Execute(fixture.Service, message, run));
        var request = fixture.Handler.Requests.Single();
        Assert.AreEqual(useResponses ? "/responses" : "/chat/completions", request.Path);
        JsonObject part;
        if (useResponses)
        {
            var input = request.Body["input"]!.AsArray().OfType<JsonObject>();
            if (role == ActorRole.Function)
            {
                var output = input.Single(item => item["type"]?.GetValue<string>() == "function_call_output");
                Assert.AreEqual("image-call", output["call_id"]?.GetValue<string>());
                part = output["output"]!.AsArray().OfType<JsonObject>().Single(item => item["type"]?.GetValue<string>() == "input_image");
            }
            else
                part = input.Single(item => item["role"]?.GetValue<string>() == "user")["content"]!
                    .AsArray().OfType<JsonObject>().Single(item => item["type"]?.GetValue<string>() == "input_image");
            Assert.AreEqual("input_image", part["type"]?.GetValue<string>());
        }
        else
        {
            var target = request.Body["messages"]!.AsArray().OfType<JsonObject>()
                .Single(item => item["role"]?.GetValue<string>() == (role == ActorRole.Function ? "tool" : "user"));
            if (role == ActorRole.Function) Assert.AreEqual("image-call", target["tool_call_id"]?.GetValue<string>());
            part = target["content"]!.AsArray().OfType<JsonObject>().Single(item => item["type"]?.GetValue<string>() == "file");
            Assert.AreEqual("file", part["type"]?.GetValue<string>());
        }
        Assert.AreEqual(FileId, part["file_id"]?.GetValue<string>());
        Assert.IsFalse(part.ContainsKey("image_url"));
        Assert.IsFalse(part.ContainsKey("file_data"));
        Assert.IsFalse(part.ContainsKey("filename"));
        Assert.AreEqual(FileId, image.FileId);
        Assert.AreEqual("answer", fixture.Service.ActivateChat.Messages.Last().Content);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ProRejectsImageFilesBeforeTransportAndHistory(bool useResponses, bool run)
    {
        using var fixture = new Fixture(useResponses);
        fixture.Service.ChangeModel(AIModels.DeepSeek.V4Pro);
        var message = new Message(ActorRole.User, new DeepSeekImageFileContent(FileId));
        await Assert.ThrowsAsync<MultimodalNotSupportedException>(() => Execute(fixture.Service, message, run));
        Assert.AreEqual(0, fixture.Handler.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false, ActorRole.Assistant)]
    [DataRow(false, ActorRole.System)]
    [DataRow(true, ActorRole.Assistant)]
    [DataRow(true, ActorRole.System)]
    public async Task InvalidImageFileRole_FailsBeforeTransportAndHistory(bool useResponses, ActorRole role)
    {
        using var fixture = new Fixture(useResponses);
        var message = new Message(role, new DeepSeekImageFileContent(FileId));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => fixture.Service.GetCompletionAsync(message));
        Assert.AreEqual(0, fixture.Handler.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ToolImageFileRequiresCallId_BeforeTransportAndHistory(bool useResponses)
    {
        using var fixture = new Fixture(useResponses);
        var message = new Message(ActorRole.Function, new DeepSeekImageFileContent(FileId));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.GetCompletionAsync(message));
        Assert.AreEqual(0, fixture.Handler.Requests.Count);
        Assert.AreEqual(0, fixture.Service.ActivateChat.Messages.Count);
    }

    private static async Task<string> Execute(DeepSeekService service, Message message, bool run)
    {
        if (!run) return await service.GetCompletionAsync(message);
        await using var operation = await service.StartRunAsync(message);
        return (await operation.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text;
    }

    private static JsonObject ValidResponse(bool useResponses)
        => useResponses ? new JsonObject
        {
            ["status"] = "completed", ["model"] = AIModels.DeepSeek.Flash,
            ["output"] = new JsonArray(new JsonObject
            {
                ["type"] = "message", ["role"] = "assistant", ["status"] = "completed",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "answer" })
            })
        } : new JsonObject
        {
            ["model"] = AIModels.DeepSeek.Flash,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["finish_reason"] = "stop", ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "answer" }
            })
        };

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;
        public Handler Handler { get; }
        public DeepSeekService Service { get; }
        public Fixture(bool useResponses)
        {
            Handler = new Handler { Response = () => ValidResponse(useResponses) };
            _client = new HttpClient(Handler);
            Service = new DeepSeekService("offline-test-key", _client) { UseResponsesApi = useResponses };
        }
        public void Dispose() => _client.Dispose();
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<(string Path, JsonObject Body)> Requests { get; } = new();
        public Func<JsonObject> Response { get; set; } = () => ValidResponse(true);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            var response = Response();
            var stream = body["stream"]?.GetValue<bool>() == true;
            string payload;
            if (!stream) payload = response.ToJsonString();
            else if (request.RequestUri.AbsolutePath == "/responses")
                payload = "data: " + new JsonObject { ["type"] = "response.completed", ["sequence_number"] = 0, ["response"] = response }.ToJsonString() + "\n\n";
            else
            {
                var choice = response["choices"]![0]!.AsObject();
                choice["delta"] = choice["message"]!.DeepClone();
                choice.Remove("message");
                payload = "data: " + response.ToJsonString() + "\n\ndata: [DONE]\n\n";
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
