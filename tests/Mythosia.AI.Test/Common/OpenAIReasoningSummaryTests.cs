using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAIReasoningSummaryTests
{
    private const string ResponseWithSummary = """
        {
          "status": "completed",
          "output_text": "answer",
          "output": [
            {
              "type": "reasoning",
              "summary": [
                { "type": "summary_text", "text": "first " },
                { "type": "summary_text", "text": "second" }
              ]
            },
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": "answer" }
              ]
            }
          ]
        }
        """;

    private const string ResponseWithoutSummary = """
        {
          "status": "completed",
          "output_text": "next answer",
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": "next answer" }
              ]
            }
          ]
        }
        """;

    private const string ResponseWithAcceptedSummarySettingButNoReasoningItem = """
        {
          "status": "completed",
          "reasoning": {
            "context": "current_turn",
            "effort": "max",
            "mode": "pro",
            "summary": "detailed"
          },
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": "answer without a summary item" }
              ]
            }
          ],
          "usage": {
            "input_tokens": 20,
            "output_tokens": 10,
            "output_tokens_details": { "reasoning_tokens": 5 },
            "total_tokens": 30
          }
        }
        """;

    private const string FunctionCallResponseWithSummary = """
        {
          "status": "completed",
          "output": [
            {
              "type": "reasoning",
              "summary": [
                { "type": "summary_text", "text": "tool-round summary" }
              ]
            },
            {
              "type": "function_call",
              "call_id": "call_lookup",
              "name": "lookup",
              "arguments": "{}"
            }
          ]
        }
        """;

    private const string FunctionFinalResponse = """
        {
          "status": "completed",
          "output_text": "tool answer",
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": "tool answer" }
              ]
            }
          ]
        }
        """;

    [TestMethod]
    public async Task Completion_OutputTextFastPathStillCapturesReasoningSummary()
    {
        var service = CreateService(new QueueHttpMessageHandler(ResponseWithSummary));

        var result = await service.GetCompletionAsync("hello");

        Assert.AreEqual("answer", result);
        Assert.AreEqual("first second", service.LastReasoningSummary);
    }

    [TestMethod]
    public async Task Completion_NewCallClearsStaleReasoningSummary()
    {
        var service = CreateService(new QueueHttpMessageHandler(
            ResponseWithSummary,
            ResponseWithoutSummary));

        await service.GetCompletionAsync("first");
        Assert.AreEqual("first second", service.LastReasoningSummary);

        var result = await service.GetCompletionAsync("second");

        Assert.AreEqual("next answer", result);
        Assert.IsNull(service.LastReasoningSummary);
    }

    [TestMethod]
    public async Task Completion_AcceptedSummarySettingWithoutReasoningItem_LeavesSummaryNull()
    {
        var service = CreateService(new QueueHttpMessageHandler(
            ResponseWithAcceptedSummarySettingButNoReasoningItem));

        var result = await service.GetCompletionAsync("hello");

        Assert.AreEqual("answer without a summary item", result);
        Assert.IsNull(service.LastReasoningSummary);
    }

    [TestMethod]
    public async Task FunctionCompletion_CapturesReasoningSummaryFromToolRound()
    {
        var service = CreateService(new QueueHttpMessageHandler(
            FunctionCallResponseWithSummary,
            FunctionFinalResponse));
        var invocationCount = 0;
        service.Functions.Add(new FunctionDefinition
        {
            Name = "lookup",
            Description = "Returns an offline test result",
            Handler = _ =>
            {
                invocationCount++;
                return Task.FromResult("tool result");
            }
        });

        var result = await service.GetCompletionAsync("use the lookup tool");

        Assert.AreEqual("tool answer", result);
        Assert.AreEqual(1, invocationCount);
        Assert.AreEqual("tool-round summary", service.LastReasoningSummary);
    }

    private static OpenAIService CreateService(QueueHttpMessageHandler handler)
    {
        var service = new OpenAIService("offline-test-key", new HttpClient(handler));
        service.ChangeModel(AIModels.OpenAI.Gpt5_6Sol);
        return service;
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public QueueHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued response remains for the request.");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            });
        }
    }
}
