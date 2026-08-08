using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Google;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class GoogleFunctionBatchTests
{
    [TestMethod]
    public async Task NonStreaming_MultipleCallsExecuteInOrderAndUseOneBatchPerTurn()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Json(MultipleFunctionCalls("STOP")),
            Response.Json(TextCandidate("complete", "STOP")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);

        var result = await service.GetCompletionAsync("Run both tools.");

        Assert.AreEqual("complete", result);
        CollectionAssert.AreEqual(new[] { "first_tool", "second_tool" }, invocationOrder);
        Assert.AreEqual(2, handler.RequestCount);

        var callMessage = service.ActivateChat.Messages.Single(message => message.FunctionCallBatch != null);
        var resultMessage = service.ActivateChat.Messages.Single(message => message.FunctionCallResultBatch != null);
        var batch = callMessage.FunctionCallBatch!;
        var results = resultMessage.FunctionCallResultBatch!;

        Assert.AreEqual(2, batch.Calls.Count);
        Assert.AreEqual("google-call-a", batch.Calls[0].Id);
        Assert.AreEqual("google-call-b", batch.Calls[1].Id);
        Assert.AreEqual(0, batch.Calls[0].Index);
        Assert.AreEqual(1, batch.Calls[1].Index);
        Assert.AreEqual("signature-a", batch.Calls[0].Metadata![MessageMetadataKeys.ThoughtSignature]);
        Assert.AreEqual("signature-b", batch.Calls[1].Metadata![MessageMetadataKeys.ThoughtSignature]);
        Assert.AreEqual(batch.Id, results.FunctionCallBatchId);
        CollectionAssert.AreEqual(
            new[] { "first_tool", "second_tool" },
            results.Results.Select(item => item.Call.Name).ToArray());

        AssertContinuationRequest(handler.RequestBodies[1]);
    }

    [TestMethod]
    public async Task NonStreaming_MalformedSecondCallExecutesNoHandlers()
    {
        const string response = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    { "functionCall": { "id": "valid", "name": "first_tool", "args": {} } },
                    { "functionCall": { "id": "invalid", "name": "second_tool", "args": "not-an-object" } }
                  ]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(Response.Json(response));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);

        await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run both tools."));

        Assert.AreEqual(0, invocationOrder.Count);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
    }

    [TestMethod]
    public async Task Streaming_MultipleCallsEmitTypedEventsAndContinueAsOneBatch()
    {
        var handler = new QueueHttpMessageHandler(
            Response.Sse(Sse(MultipleFunctionCalls("STOP"), "[DONE]")),
            Response.Sse(Sse(TextCandidate("complete", "STOP"), "[DONE]")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);
        var chunks = new List<StreamingContent>();

        await foreach (var chunk in service.StreamAsync("Run both tools.", StreamOptions.WithFunctions))
            chunks.Add(chunk);

        CollectionAssert.AreEqual(new[] { "first_tool", "second_tool" }, invocationOrder);
        Assert.AreEqual(2, handler.RequestCount);

        var callEvents = chunks.Where(chunk => chunk.Type == StreamingContentType.FunctionCall).ToArray();
        var resultEvents = chunks.Where(chunk => chunk.Type == StreamingContentType.FunctionResult).ToArray();
        Assert.AreEqual(2, callEvents.Length);
        Assert.AreEqual(2, resultEvents.Length);
        CollectionAssert.AreEqual(
            new[] { "first_tool", "second_tool" },
            callEvents.Select(chunk => chunk.FunctionCall!.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first_tool", "second_tool" },
            resultEvents.Select(chunk => chunk.FunctionResult!.Call.Name).ToArray());
        Assert.IsTrue(callEvents.All(chunk => chunk.FunctionCallBatchId == callEvents[0].FunctionCallBatchId));
        Assert.IsTrue(resultEvents.All(chunk => chunk.FunctionCallBatchId == callEvents[0].FunctionCallBatchId));
        CollectionAssert.AreEqual(
            new[] { "before", "between", "complete" },
            chunks
                .Where(chunk => chunk.Type == StreamingContentType.Text)
                .Select(chunk => chunk.Content)
                .ToArray());

        Assert.AreEqual(1, service.ActivateChat.Messages.Count(message => message.FunctionCallBatch != null));
        Assert.AreEqual(1, service.ActivateChat.Messages.Count(message => message.FunctionCallResultBatch != null));
        AssertContinuationRequest(handler.RequestBodies[1]);
    }

    [TestMethod]
    public async Task Streaming_EmptySuccessfulContinuationCommitsTerminalAssistantTurn()
    {
        const string emptyTerminalResponse = """
            {
              "candidates": [{
                "content": { "role": "model", "parts": [] },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(Sse(MultipleFunctionCalls("STOP"), "[DONE]")),
            Response.Sse(Sse(emptyTerminalResponse, "[DONE]")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);

        await foreach (var _ in service.StreamAsync("Run both tools.", StreamOptions.WithFunctions))
        {
        }

        CollectionAssert.AreEqual(new[] { "first_tool", "second_tool" }, invocationOrder);
        Assert.AreEqual(2, handler.RequestCount);
        var terminalMessage = service.ActivateChat.Messages.Last();
        Assert.AreEqual(ActorRole.Assistant, terminalMessage.Role);
        Assert.AreEqual(string.Empty, terminalMessage.Content);
    }

    [TestMethod]
    public async Task Streaming_MalformedSecondCallExecutesNoHandlers()
    {
        const string malformedBatch = """
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    { "functionCall": { "id": "valid", "name": "first_tool", "args": {} } },
                    { "functionCall": { "id": "invalid", "name": "second_tool" } }
                  ]
                },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(malformedBatch, "[DONE]")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);
        var chunks = new List<StreamingContent>();

        await foreach (var chunk in service.StreamAsync("Run both tools.", StreamOptions.WithFunctions))
            chunks.Add(chunk);

        Assert.AreEqual(0, invocationOrder.Count);
        Assert.IsTrue(chunks.Any(chunk => chunk.Type == StreamingContentType.Error));
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
    }

    [TestMethod]
    public async Task NonStreaming_DuplicateCallIdsExecuteNoHandlersOrCommitBatch()
    {
        const string duplicateBatch = """
            {
              "candidates": [{
                "content": { "role": "model", "parts": [
                  { "functionCall": { "id": "duplicate", "name": "first_tool", "args": {} } },
                  { "functionCall": { "id": "duplicate", "name": "second_tool", "args": {} } }
                ] },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(Response.Json(duplicateBatch));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run both tools."));

        StringAssert.Contains(exception.Message, "duplicate function-call ID");
        Assert.AreEqual(0, invocationOrder.Count);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
        Assert.AreEqual(ActorRole.User, service.ActivateChat.Messages[0].Role);
    }

    [TestMethod]
    public async Task Streaming_DuplicateCallIdsAreNotMergedOrExecuted()
    {
        const string firstCall = """
            {
              "candidates": [{
                "content": { "role": "model", "parts": [
                  { "functionCall": { "id": "duplicate", "name": "first_tool", "args": { "value": 1 } } }
                ] }
              }]
            }
            """;
        const string conflictingLaterCall = """
            {
              "candidates": [{
                "content": { "role": "model", "parts": [
                  { "functionCall": { "id": "duplicate", "name": "second_tool", "args": { "value": 2 } } }
                ] },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(
            firstCall,
            conflictingLaterCall,
            "[DONE]")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);
        var chunks = new List<StreamingContent>();

        await foreach (var chunk in service.StreamAsync("Run both tools.", StreamOptions.WithFunctions))
            chunks.Add(chunk);

        Assert.AreEqual(0, invocationOrder.Count);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(1, chunks.Count(chunk => chunk.Type == StreamingContentType.Error));
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task Streaming_IdenticalSnapshotRepeatedInLaterChunkExecutesOnce()
    {
        const string firstSnapshot = """
            { "candidates": [{ "content": { "role": "model", "parts": [
              { "functionCall": { "id": "repeated", "name": "first_tool", "args": { "value": 1 } },
                "thoughtSignature": "signed-repeat" }
            ] } }] }
            """;
        const string terminalSnapshot = """
            { "candidates": [{ "content": { "role": "model", "parts": [
              { "functionCall": { "id": "repeated", "name": "first_tool", "args": { "value": 1 } },
                "thoughtSignature": "signed-repeat" }
            ] }, "finishReason": "STOP" }] }
            """;
        var handler = new QueueHttpMessageHandler(
            Response.Sse(Sse(firstSnapshot, terminalSnapshot, "[DONE]")),
            Response.Sse(Sse(TextCandidate("complete", "STOP"), "[DONE]")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);
        var chunks = new List<StreamingContent>();

        await foreach (var chunk in service.StreamAsync("Run the tool.", StreamOptions.WithFunctions))
            chunks.Add(chunk);

        CollectionAssert.AreEqual(new[] { "first_tool" }, invocationOrder);
        Assert.AreEqual(1, chunks.Count(chunk => chunk.Type == StreamingContentType.FunctionCall));
        Assert.AreEqual(1, chunks.Count(chunk => chunk.Type == StreamingContentType.FunctionResult));
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.Error));
        Assert.AreEqual(2, handler.RequestCount);
    }

    [TestMethod]
    public async Task NonStreaming_PreGemini3MissingIdUsesInternalCorrelationWithoutInventingWireId()
    {
        const string callWithoutId = """
            {
              "candidates": [{
                "content": { "role": "model", "parts": [{
                  "functionCall": { "name": "first_tool", "args": { "value": 1 } },
                  "thoughtSignature": "signed-call",
                  "futureProviderField": { "keep": true }
                }] },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(
            Response.Json(callWithoutId),
            Response.Json(TextCandidate("complete", "STOP")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);
        service.ChangeModel(AIModels.Google.Gemini2_5Flash);

        Assert.AreEqual("complete", await service.GetCompletionAsync("Run the tool."));

        var batch = service.ActivateChat.Messages.Single(
            message => message.FunctionCallBatch != null).FunctionCallBatch!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(batch.Calls[0].Id),
            "The internal batch still needs a correlation ID.");

        using var request = JsonDocument.Parse(handler.RequestBodies[1]);
        var contents = request.RootElement.GetProperty("contents").EnumerateArray().ToArray();
        var replayedCall = contents
            .Single(content => content.GetProperty("role").GetString() == "model")
            .GetProperty("parts")[0];
        Assert.IsFalse(replayedCall.GetProperty("functionCall").TryGetProperty("id", out _),
            "A locally generated ID must not be presented as a provider-issued ID.");
        Assert.AreEqual("signed-call", replayedCall.GetProperty("thoughtSignature").GetString());
        Assert.IsTrue(replayedCall.GetProperty("futureProviderField").GetProperty("keep").GetBoolean());

        var functionResponse = contents
            .Single(content => content.GetProperty("role").GetString() == "user" &&
                content.GetProperty("parts")[0].TryGetProperty("functionResponse", out _))
            .GetProperty("parts")[0]
            .GetProperty("functionResponse");
        Assert.IsFalse(functionResponse.TryGetProperty("id", out _));
    }

    [TestMethod]
    public async Task NonStreaming_Gemini3MissingProviderIdExecutesNoHandler()
    {
        const string missingId = """
            {
              "candidates": [{
                "content": { "role": "model", "parts": [{
                  "functionCall": { "name": "first_tool", "args": {} },
                  "thoughtSignature": "signed-call"
                }] },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(Response.Json(missingId));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run the tool."));

        StringAssert.Contains(exception.InnerException?.Message ?? string.Empty, "missing its provider ID");
        Assert.AreEqual(0, invocationOrder.Count);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task NonStreaming_InvalidLaterThoughtSignatureExecutesNoHandlers()
    {
        const string malformedSignature = """
            {
              "candidates": [{
                "content": { "role": "model", "parts": [
                  { "functionCall": { "id": "valid", "name": "first_tool", "args": {} } },
                  { "functionCall": { "id": "invalid", "name": "second_tool", "args": {} },
                    "thoughtSignature": 42 }
                ] },
                "finishReason": "STOP"
              }]
            }
            """;
        var handler = new QueueHttpMessageHandler(Response.Json(malformedSignature));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);

        await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run both tools."));

        Assert.AreEqual(0, invocationOrder.Count);
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task Streaming_ContentAfterTerminalDoesNotExecuteOrCommitTool()
    {
        const string lateFunctionCall = """
            { "candidates": [{ "content": { "role": "model", "parts": [
              { "functionCall": { "id": "late", "name": "first_tool", "args": {} } }
            ] } }] }
            """;
        var handler = new QueueHttpMessageHandler(Response.Sse(Sse(
            TextCandidate("premature", "STOP"),
            lateFunctionCall,
            "[DONE]")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);
        var chunks = new List<StreamingContent>();

        await foreach (var chunk in service.StreamAsync("Run the tool.", StreamOptions.WithFunctions))
            chunks.Add(chunk);

        Assert.AreEqual(0, invocationOrder.Count);
        var error = chunks.Single(chunk => chunk.Type == StreamingContentType.Error);
        Assert.AreEqual("content_after_terminal", error.Metadata?["reason"]?.ToString());
        Assert.IsFalse(chunks.Any(chunk => chunk.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(1, service.ActivateChat.Messages.Count);
    }

    [TestMethod]
    public async Task NonStreaming_MaxRoundsDoesNotSendAnUnobservableExtraRequest()
    {
        var handler = new QueueHttpMessageHandler(Response.Json(MultipleFunctionCalls("STOP")));
        var invocationOrder = new List<string>();
        var service = CreateServiceWithTools(handler, invocationOrder);
        service.WithMaxRounds(1);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Run both tools."));

        StringAssert.Contains(exception.Message, "Maximum rounds (1) exceeded");
        CollectionAssert.AreEqual(new[] { "first_tool", "second_tool" }, invocationOrder);
        Assert.AreEqual(1, handler.RequestCount,
            "The response after the last permitted round could never be observed.");
    }

    [TestMethod]
    public async Task NonStreaming_OneShotPolicyIsConsumedWithoutFunctionsAndOnHttpFailure()
    {
        var successfulHandler = new QueueHttpMessageHandler(
            Response.Json(TextCandidate("complete", "STOP")));
        var successfulService = new GoogleAIService(
            "offline-test-key", new HttpClient(successfulHandler));
        successfulService.WithMaxRounds(1);

        Assert.AreEqual("complete", await successfulService.GetCompletionAsync("No tools."));
        Assert.IsNull(successfulService.CurrentPolicy);

        var failingHandler = new QueueHttpMessageHandler(
            Response.Error(HttpStatusCode.InternalServerError, "failed"));
        var failingService = new GoogleAIService(
            "offline-test-key", new HttpClient(failingHandler));
        failingService.Functions.Add(CreateFunction("first_tool", "unused", new List<string>()));
        failingService.WithMaxRounds(1);

        await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => failingService.GetCompletionAsync("Fail before parsing."));
        Assert.IsNull(failingService.CurrentPolicy);
    }

    [TestMethod]
    public async Task NonStreaming_PolicyTimeoutCancelsInitialHttpRequest()
    {
        var handler = new CancellationAwareHttpMessageHandler();
        var service = new GoogleAIService("offline-test-key", new HttpClient(handler));
        service.WithTimeout(1);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(
            () => service.GetCompletionAsync("Wait forever."));

        StringAssert.Contains(exception.Message, "Request timeout after 1 seconds");
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsTrue(handler.CancellationObserved);
        Assert.IsNull(service.CurrentPolicy);
    }

    private static GoogleAIService CreateServiceWithTools(
        HttpMessageHandler handler,
        List<string> invocationOrder)
    {
        var service = new GoogleAIService("offline-test-key", new HttpClient(handler));
        service.Functions.Add(CreateFunction("first_tool", "first-result", invocationOrder));
        service.Functions.Add(CreateFunction("second_tool", "second-result", invocationOrder));
        return service;
    }

    private static FunctionDefinition CreateFunction(
        string name,
        string result,
        List<string> invocationOrder)
    {
        return new FunctionDefinition
        {
            Name = name,
            Description = $"Executes {name}.",
            Handler = _ =>
            {
                invocationOrder.Add(name);
                return Task.FromResult(result);
            }
        };
    }

    private static void AssertContinuationRequest(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        var contents = document.RootElement.GetProperty("contents").EnumerateArray().ToArray();
        var callContent = contents.Single(content =>
            content.GetProperty("role").GetString() == "model" &&
            content.GetProperty("parts").EnumerateArray().Any(part => part.TryGetProperty("functionCall", out _)));
        var callParts = callContent.GetProperty("parts").EnumerateArray().ToArray();

        Assert.AreEqual(4, callParts.Length);
        Assert.AreEqual("before", callParts[0].GetProperty("text").GetString());
        Assert.AreEqual("first_tool", callParts[1].GetProperty("functionCall").GetProperty("name").GetString());
        Assert.AreEqual("signature-a", callParts[1].GetProperty("thoughtSignature").GetString());
        Assert.AreEqual("between", callParts[2].GetProperty("text").GetString());
        Assert.AreEqual("second_tool", callParts[3].GetProperty("functionCall").GetProperty("name").GetString());
        Assert.AreEqual("signature-b", callParts[3].GetProperty("thoughtSignature").GetString());
        Assert.AreEqual("opaque-a", callParts[1].GetProperty("futureProviderField").GetString());
        Assert.AreEqual("opaque-b", callParts[3].GetProperty("futureProviderField").GetString());

        var resultContent = contents.Single(content =>
            content.GetProperty("role").GetString() == "user" &&
            content.GetProperty("parts").EnumerateArray().Any(part => part.TryGetProperty("functionResponse", out _)));
        var resultParts = resultContent.GetProperty("parts").EnumerateArray().ToArray();
        Assert.AreEqual(2, resultParts.Length);
        Assert.AreEqual(
            "first_tool",
            resultParts[0].GetProperty("functionResponse").GetProperty("name").GetString());
        Assert.AreEqual(
            "first-result",
            resultParts[0].GetProperty("functionResponse").GetProperty("response").GetProperty("content").GetString());
        Assert.AreEqual(
            "second_tool",
            resultParts[1].GetProperty("functionResponse").GetProperty("name").GetString());
        Assert.AreEqual(
            "second-result",
            resultParts[1].GetProperty("functionResponse").GetProperty("response").GetProperty("content").GetString());
    }

    private static string MultipleFunctionCalls(string finishReason)
        => $$"""
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    { "text": "before" },
                    {
                      "functionCall": { "id": "google-call-a", "name": "first_tool", "args": { "value": 1 } },
                      "thoughtSignature": "signature-a",
                      "futureProviderField": "opaque-a"
                    },
                    { "text": "between" },
                    {
                      "functionCall": { "id": "google-call-b", "name": "second_tool", "args": { "value": 2 } },
                      "thoughtSignature": "signature-b",
                      "futureProviderField": "opaque-b"
                    }
                  ]
                },
                "finishReason": "{{finishReason}}"
              }]
            }
            """;

    private static string TextCandidate(string text, string finishReason)
        => "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"" +
           text + "\"}]},\"finishReason\":\"" + finishReason + "\"}]}";

    private static string Sse(params string[] payloads)
        => string.Join("\n\n", payloads.Select(payload => $"data: {NormalizeSsePayload(payload)}")) + "\n\n";

    private static string NormalizeSsePayload(string payload)
    {
        if (payload == "[DONE]")
            return payload;

        using var document = JsonDocument.Parse(payload);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private readonly record struct Response(string Body, string MediaType, HttpStatusCode StatusCode)
    {
        public static Response Json(string body) => new(body, "application/json", HttpStatusCode.OK);

        public static Response Sse(string body) => new(body, "text/event-stream", HttpStatusCode.OK);

        public static Response Error(HttpStatusCode statusCode, string body)
            => new(body, "application/json", statusCode);
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Response> _responses;

        public QueueHttpMessageHandler(params Response[] responses)
        {
            _responses = new Queue<Response>(responses);
        }

        public int RequestCount { get; private set; }

        public List<string> RequestBodies { get; } = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued response remains.");

            var response = _responses.Dequeue();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, response.MediaType)
            };
        }
    }

    private sealed class CancellationAwareHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The timeout fixture unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
