using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIStatelessPreservedHistoryTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var model in new[] { AIModels.OpenAI.Gpt6Astra, AIModels.OpenAI.Gpt6_1Sol })
        foreach (var change in new[] { "truncate", "reorder", "model" })
        foreach (var entry in new[] { "rewrite", "summary", "builder", "stream" })
            yield return [model, change, entry];
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task StatelessRequest_DoesNotValidateOrResetUnrelatedPreservedHistory(string model, string change, string entry)
    {
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var service = new OpenAIService("offline", model, http);
        await service.WithReasoning(ReasoningLevel.Low, CachePreservation.Required).GetCompletionAsync("PRIVATE_HISTORY");
        if (change == "truncate") service.ActivateChat.Messages.RemoveAt(1);
        else if (change == "reorder")
            (service.ActivateChat.Messages[0], service.ActivateChat.Messages[1]) =
                (service.ActivateChat.Messages[1], service.ActivateChat.Messages[0]);
        else service.ChangeModel(AIModels.OpenAI.Gpt4_1);
        var before = JsonSerializer.Serialize(service.ActivateChat.Messages);
        if (entry == "rewrite") await service.GetCompletionAsync("HELPER", RequestProfiles.QueryRewrite);
        else if (entry == "summary") await service.GetCompletionAsync("HELPER", RequestProfiles.Summarization);
        else if (entry == "builder") await service.CreateRequest("HELPER").WithStatelessMode().GetCompletionAsync();
        else
        {
            service.StatelessMode = true;
            await foreach (var _ in service.StreamAsync("HELPER")) { }
            service.StatelessMode = false;
        }
        Assert.HasCount(2, wire.Bodies);
        var helper = wire.Bodies.Last().ToJsonString();
        StringAssert.Contains(helper, "HELPER");
        Assert.IsFalse(helper.Contains("PRIVATE_HISTORY"));
        Assert.IsFalse(helper.Contains("configuration_update"));
        Assert.AreEqual(before, JsonSerializer.Serialize(service.ActivateChat.Messages));
        // The helper must not erase the old guard merely to bypass it.
        if (change == "model") await Assert.ThrowsAsync<NotSupportedException>(() => service.GetCompletionAsync("stateful"));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCompletionAsync("stateful"));
        Assert.HasCount(2, wire.Bodies);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            Bodies.Add(body);
            const string response = "{\"id\":\"r\",\"status\":\"completed\",\"output_text\":\"answer\",\"output\":[{\"id\":\"m\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"answer\",\"annotations\":[]}]}]}";
            var streaming = body["stream"]?.GetValue<bool>() == true;
            var text = streaming ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"answer\"}\n\ndata: {\"type\":\"response.completed\",\"response\":" + response + "}\n\n" : response;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, streaming ? "text/event-stream" : "application/json") };
        }
    }
}
