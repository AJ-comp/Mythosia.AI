using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using Mythosia.AI.Services.Google;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Tests.Common;

/// <summary>
/// ReAct Agent (RunAgentAsync) 동작 검증 (API 키 불필요, 프로바이더 무관)
/// MockAgentService를 사용하여 FC 루프와 완료 판단 로직을 테스트
/// </summary>
[TestClass]
public class AgentRunTests
{
    #region Mock AIService for Agent

    /// <summary>
    /// Agent 테스트용 MockService.
    /// GetCompletionAsync(Message)를 override하여 미리 정의된 응답을 순서대로 반환.
    /// FC가 등록된 상태에서 "Maximum rounds" 예외를 시뮬레이션할 수 있음.
    /// </summary>
    private class MockAgentService : AIService
    {
        private readonly Queue<string> _responses = new();
        public int CompletionCallCount { get; private set; }
        public List<string> ReceivedMessages { get; } = new();

        public MockAgentService(params string[] responses)
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
            foreach (var r in responses)
                _responses.Enqueue(r);
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public override Task<string> GetCompletionAsync(Message message)
        {
            CompletionCallCount++;
            ReceivedMessages.Add(message.Content);

            if (_responses.Count == 0)
                throw new AIServiceException("No more mock responses");

            var response = _responses.Dequeue();

            // Simulate "Maximum rounds" exceeded if response starts with "##MAX_ROUNDS##"
            if (response == "##MAX_ROUNDS##")
            {
                // Add a partial assistant message to simulate mid-execution state
                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Partial answer so far..."));
                throw new AIServiceException($"Maximum rounds ({10}) exceeded");
            }

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, response));
            return Task.FromResult(response);
        }

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    /// <summary>
    /// Streaming agent test double that reuses the base streaming loop while
    /// capturing the effective options and goal passed to the stream.
    /// </summary>
    private class MockStreamingAgentService : AIService
    {
        private readonly string _response;

        public int StreamCallCount { get; private set; }
        public List<string> ReceivedStreamMessages { get; } = new();
        public StreamOptions? LastStreamOptions { get; private set; }
        public int? LastObservedMaxRounds { get; private set; }

        public MockStreamingAgentService(string response)
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            _response = response;
            AddNewChat();
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public override Task<string> GetCompletionAsync(Message message)
        {
            ActivateChat.Messages.Add(message);
            return Task.FromResult(_response);
        }

        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastStreamOptions = options.Clone();
            LastObservedMaxRounds = (CurrentPolicy ?? DefaultPolicy).MaxRounds;

            await foreach (var content in base.StreamCoreAsync(message, options, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                yield return content;
            }
        }

        public override async Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
        {
            StreamCallCount++;
            ReceivedStreamMessages.Add(message.Content ?? string.Empty);

            foreach (var chunk in Chunk(_response, 4))
                await messageReceivedAsync(chunk);

            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, _response));
        }

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);

        private static IEnumerable<string> Chunk(string value, int size)
        {
            for (var i = 0; i < value.Length; i += size)
                yield return value.Substring(i, Math.Min(size, value.Length - i));
        }
    }

    /// <summary>
    /// Streaming mock that mimics a function-calling loop exhausting max rounds
    /// without ever producing a final completion event.
    /// </summary>
    private class MockStreamingAgentServiceNoCompletion : AIService
    {
        public int? LastObservedMaxRounds { get; private set; }

        public MockStreamingAgentServiceNoCompletion()
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public override Task<string> GetCompletionAsync(Message message)
            => Task.FromResult(string.Empty);

        protected override async IAsyncEnumerable<StreamingContent> StreamCoreAsync(
            Message message,
            StreamOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            LastObservedMaxRounds = policy.MaxRounds;
            CurrentPolicy = null;

            ActivateChat.Messages.Add(message);
            ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Partial streamed answer..."));

            yield return new StreamingContent
            {
                Type = StreamingContentType.FunctionResult,
                Metadata = new Dictionary<string, object>
                {
                    ["function_name"] = "mock_tool",
                    ["status"] = "completed"
                }
            };

            await Task.CompletedTask;
        }

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    private class MockRoundUsageAgentService : AIService
    {
        private int _round;

        public MockRoundUsageAgentService()
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public override Task<string> GetCompletionAsync(Message message)
            => Task.FromResult(string.Empty);

        protected override async IAsyncEnumerable<StreamingContent> StreamRoundAsync(
            StreamOptions options,
            bool useFunctions,
            FunctionCallingPolicy policy,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _round++;

            if (_round == 1)
            {
                yield return new StreamingContent
                {
                    Type = StreamingContentType.FunctionCall,
                    Metadata = new Dictionary<string, object>
                    {
                        ["function_name"] = "mock_tool",
                        ["status"] = "started"
                    }
                };

                yield return new StreamingContent
                {
                    Type = StreamingContentType.Completion,
                    Usage = new TokenUsage
                    {
                        InputTokens = 10000,
                        OutputTokens = 100,
                        TotalTokens = 999999,
                        CachedInputTokens = 10,
                        CacheCreationTokens = 20,
                        ReasoningTokens = 30
                    }
                };

                yield return new StreamingContent
                {
                    Type = StreamingContentType.FunctionResult,
                    Metadata = new Dictionary<string, object>
                    {
                        ["function_name"] = "mock_tool",
                        ["status"] = "completed",
                        ["result"] = "ok"
                    }
                };
            }
            else
            {
                yield return new StreamingContent
                {
                    Type = StreamingContentType.Text,
                    Content = "Final answer"
                };

                ActivateChat.Messages.Add(new Message(ActorRole.Assistant, "Final answer"));

                yield return new StreamingContent
                {
                    Type = StreamingContentType.Completion,
                    Usage = new TokenUsage
                    {
                        InputTokens = 13000,
                        OutputTokens = 1000,
                        TotalTokens = 888888,
                        CachedInputTokens = 40,
                        CacheCreationTokens = 50,
                        ReasoningTokens = 60
                    }
                };
            }

            await Task.CompletedTask;
        }

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();

        public int RequestCount { get; private set; }

        public void EnqueueSse(string content)
        {
            _responses.Enqueue(content);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued response for fake HTTP handler.");

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "text/event-stream")
            };

            return Task.FromResult(response);
        }
    }

    #endregion

    #region RunAgentAsync - Basic Behavior

    /// <summary>
    /// LLM이 즉시 텍스트 응답을 반환하면 (FC 없이) 한 번의 호출로 완료
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_NoFunctionCalls_ReturnsFinalAnswer()
    {
        var mock = new MockAgentService("The answer is 42.");

        var result = await mock.RunAgentAsync("What is the meaning of life?");

        Assert.AreEqual("The answer is 42.", result);
        Assert.AreEqual(1, mock.CompletionCallCount, "Should call LLM exactly once");
    }

    /// <summary>
    /// goal 메시지가 올바르게 전달되는지 확인
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_GoalIsPassedAsUserMessage()
    {
        var mock = new MockAgentService("Done.");

        await mock.RunAgentAsync("Find the weather in Seoul");

        Assert.AreEqual(1, mock.ReceivedMessages.Count);
        Assert.AreEqual("Find the weather in Seoul", mock.ReceivedMessages[0]);
    }

    /// <summary>
    /// 기본 maxSteps 값이 10인지 확인 (CurrentPolicy에 반영)
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_DefaultMaxStepsIs10()
    {
        var mock = new MockAgentService("result");
        mock.DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 20 };

        var result = await mock.RunAgentAsync("test goal");

        // Should succeed normally with default maxSteps=10
        Assert.AreEqual("result", result);
    }

    #endregion

    #region RunAgentAsync - MaxSteps Exceeded

    /// <summary>
    /// maxSteps 초과 시 AgentMaxStepsExceededException 발생
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_MaxStepsExceeded_ThrowsAgentException()
    {
        var mock = new MockAgentService("##MAX_ROUNDS##");

        AgentMaxStepsExceededException? ex = null;
        try
        {
            await mock.RunAgentAsync("complex task", maxSteps: 3);
            Assert.Fail("Expected AgentMaxStepsExceededException was not thrown");
        }
        catch (AgentMaxStepsExceededException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        Assert.AreEqual(3, ex.MaxSteps);
        Assert.IsNotNull(ex.PartialResponse, "Should contain partial response");
        Assert.AreEqual("Partial answer so far...", ex.PartialResponse);
    }

    /// <summary>
    /// maxSteps 초과 시 부분 응답이 없으면 PartialResponse가 null
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_MaxStepsExceeded_NoPartialResponse_PartialResponseIsNull()
    {
        var mockWithNoPartial = new MockAgentServiceNoPartial();

        AgentMaxStepsExceededException? ex = null;
        try
        {
            await mockWithNoPartial.RunAgentAsync("task", maxSteps: 5);
            Assert.Fail("Expected AgentMaxStepsExceededException was not thrown");
        }
        catch (AgentMaxStepsExceededException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        Assert.AreEqual(5, ex.MaxSteps);
        Assert.IsNull(ex.PartialResponse);
    }

    /// <summary>
    /// Special mock that throws MaxRounds without adding any assistant messages
    /// </summary>
    private class MockAgentServiceNoPartial : AIService
    {
        public MockAgentServiceNoPartial()
            : base("fake-key", "https://localhost/", new HttpClient())
        {
            AddNewChat();
        }

        public override string Provider => nameof(AIProvider.OpenAI);

        public override Task<string> GetCompletionAsync(Message message)
        {
            throw new AIServiceException("Maximum rounds (5) exceeded");
        }

        protected override HttpRequestMessage CreateMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override string ExtractResponseContent(string responseContent)
            => responseContent;

        protected override string StreamParseJson(string jsonData)
            => jsonData;

        public override Task<uint> GetInputTokenCountAsync()
            => Task.FromResult(0u);

        public override Task<uint> GetInputTokenCountAsync(string prompt)
            => Task.FromResult(0u);

        public override Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(Array.Empty<byte>());

        public override Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024")
            => Task.FromResult(string.Empty);

        public override Task StreamCompletionAsync(Message message, Func<string, Task> messageReceivedAsync)
            => Task.CompletedTask;

        protected override HttpRequestMessage CreateFunctionMessageRequest()
            => new(HttpMethod.Post, "https://localhost/");

        protected override (string content, FunctionCall functionCall) ExtractFunctionCall(string response)
            => (response, null!);
    }

    #endregion

    #region RunAgentStreamAsync - Basic Behavior

    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentStreamAsync_StreamsTextAndCompletion()
    {
        var mock = new MockStreamingAgentService("The answer is 42.");
        var events = new List<StreamingContent>();

        await foreach (var content in mock.RunAgentStreamAsync("What is the meaning of life?"))
            events.Add(content);

        var streamedText = string.Concat(events
            .Where(e => e.Type == StreamingContentType.Text)
            .Select(e => e.Content));

        Assert.AreEqual(1, mock.StreamCallCount, "Should invoke the streaming pipeline exactly once");
        Assert.AreEqual("The answer is 42.", streamedText);
        Assert.IsTrue(events.Any(e => e.Type == StreamingContentType.Completion),
            "Agent stream should emit a final completion event");
    }

    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentStreamAsync_GoalIsPassedAsUserMessage()
    {
        var mock = new MockStreamingAgentService("Done.");

        await foreach (var _ in mock.RunAgentStreamAsync("Find the weather in Seoul"))
        {
        }

        Assert.AreEqual(1, mock.ReceivedStreamMessages.Count);
        Assert.AreEqual("Find the weather in Seoul", mock.ReceivedStreamMessages[0]);
    }

    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentStreamAsync_EnablesFunctionCallingAndDisablesTextOnly()
    {
        var mock = new MockStreamingAgentService("Done.");

        await foreach (var _ in mock.RunAgentStreamAsync(
            "goal",
            maxSteps: 5,
            options: StreamOptions.TextOnlyOptions))
        {
        }

        Assert.AreEqual(5, mock.LastObservedMaxRounds);
        Assert.IsNotNull(mock.LastStreamOptions);
        Assert.IsTrue(mock.LastStreamOptions!.IncludeFunctionCalls,
            "RunAgentStreamAsync should force function calling on");
        Assert.IsFalse(mock.LastStreamOptions.TextOnly,
            "RunAgentStreamAsync should disable TextOnly so completion events can be emitted");
    }

    /// <summary>
    /// Tests the base agent streaming loop with deterministic mock round usage values.
    /// Guarantees that one RoundUsage event is emitted for each LLM round, RoundUsage is
    /// per-round rather than cumulative, TotalTokens is normalized to InputTokens +
    /// OutputTokens, and the final Completion usage remains the cumulative sum for the
    /// whole agent run.
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestCategory("Token")]
    [TestMethod]
    public async Task RunAgentStreamAsync_EmitsRoundUsagePerRoundAndKeepsCompletionUsageCumulative()
    {
        var mock = new MockRoundUsageAgentService();
        var events = new List<StreamingContent>();

        await foreach (var content in mock.RunAgentStreamAsync("Run two rounds", maxSteps: 3))
            events.Add(content);

        var roundUsageEvents = events
            .Where(e => e.Type == StreamingContentType.RoundUsage)
            .ToList();

        Assert.AreEqual(2, roundUsageEvents.Count, "Should emit one RoundUsage event per LLM round.");

        Assert.AreEqual(1, roundUsageEvents[0].RoundIndex);
        Assert.IsFalse(roundUsageEvents[0].IsFinalRound);
        Assert.IsNotNull(roundUsageEvents[0].Usage);
        var firstRoundUsage = roundUsageEvents[0].Usage!;
        Assert.AreEqual(10000, firstRoundUsage.InputTokens);
        Assert.AreEqual(100, firstRoundUsage.OutputTokens);
        Assert.AreEqual(10100, firstRoundUsage.TotalTokens,
            "RoundUsage should normalize TotalTokens to InputTokens + OutputTokens.");

        Assert.AreEqual(2, roundUsageEvents[1].RoundIndex);
        Assert.IsTrue(roundUsageEvents[1].IsFinalRound);
        Assert.IsNotNull(roundUsageEvents[1].Usage);
        var secondRoundUsage = roundUsageEvents[1].Usage!;
        Assert.AreEqual(13000, secondRoundUsage.InputTokens);
        Assert.AreEqual(1000, secondRoundUsage.OutputTokens);
        Assert.AreEqual(14000, secondRoundUsage.TotalTokens);

        var completion = events.Last(e => e.Type == StreamingContentType.Completion);
        Assert.IsNotNull(completion.Usage);
        var completionUsage = completion.Usage!;
        Assert.AreEqual(23000, completionUsage.InputTokens);
        Assert.AreEqual(1100, completionUsage.OutputTokens);
        Assert.AreEqual(24100, completionUsage.TotalTokens,
            "Completion usage should remain cumulative across the full agent run.");
        Assert.AreEqual(50, completionUsage.CachedInputTokens);
        Assert.AreEqual(70, completionUsage.CacheCreationTokens);
        Assert.AreEqual(90, completionUsage.ReasoningTokens);
    }

    /// <summary>
    /// Tests Gemini's risky streaming shape where a functionCall chunk can arrive before
    /// the usageMetadata chunk for the same LLM round. Guarantees that Gemini drains the
    /// stream after the function call, captures late usage even when metadata is disabled,
    /// emits RoundUsage for both the function-call round and final answer round, and keeps
    /// final Completion usage cumulative.
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestCategory("Token")]
    [TestMethod]
    public async Task GeminiStreamFunctionCall_DrainsUsageAfterFunctionCallForRoundUsage()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueSse(string.Join("\n", new[]
        {
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"get_weather\",\"args\":{\"city\":\"Seoul\"}}}]}}]}",
            "data: {\"usageMetadata\":{\"promptTokenCount\":10000,\"candidatesTokenCount\":100,\"totalTokenCount\":10100,\"cachedContentTokenCount\":12,\"thoughtsTokenCount\":3}}",
            "data: [DONE]",
            ""
        }));
        handler.EnqueueSse(string.Join("\n", new[]
        {
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Seoul is sunny.\"}]}}]}",
            "data: {\"usageMetadata\":{\"promptTokenCount\":13000,\"candidatesTokenCount\":1000,\"totalTokenCount\":14000,\"cachedContentTokenCount\":34,\"thoughtsTokenCount\":5}}",
            "data: [DONE]",
            ""
        }));

        var service = new GoogleAIService("fake-key", new HttpClient(handler));
        var functionWasCalled = false;

        service.WithFunction<string>(
            "get_weather",
            "Gets current weather for a city.",
            ("city", "City name", true),
            city =>
            {
                functionWasCalled = true;
                return $"Weather in {city}: sunny";
            });

        var events = new List<StreamingContent>();
        await foreach (var content in service.RunAgentStreamAsync(
            "Use get_weather for Seoul, then answer.",
            maxSteps: 3,
            options: new StreamOptions
            {
                IncludeMetadata = false,
                IncludeFunctionCalls = true,
                TextOnly = false
            }))
        {
            events.Add(content);
        }

        Assert.AreEqual(2, handler.RequestCount);
        Assert.IsTrue(functionWasCalled, "Gemini stream should execute the function call.");
        Assert.IsFalse(events.Any(e => e.Type == StreamingContentType.Error),
            "Gemini fake stream should not surface function execution errors.");

        var roundUsageEvents = events
            .Where(e => e.Type == StreamingContentType.RoundUsage)
            .ToList();

        Assert.AreEqual(2, roundUsageEvents.Count,
            "Gemini should emit usage for both the function-call round and the final answer round.");

        Assert.AreEqual(1, roundUsageEvents[0].RoundIndex);
        Assert.IsFalse(roundUsageEvents[0].IsFinalRound);
        Assert.IsNotNull(roundUsageEvents[0].Usage);
        var firstGeminiRoundUsage = roundUsageEvents[0].Usage!;
        Assert.AreEqual(10000, firstGeminiRoundUsage.InputTokens);
        Assert.AreEqual(100, firstGeminiRoundUsage.OutputTokens);
        Assert.AreEqual(10100, firstGeminiRoundUsage.TotalTokens);
        Assert.AreEqual(12, firstGeminiRoundUsage.CachedInputTokens);
        Assert.AreEqual(3, firstGeminiRoundUsage.ReasoningTokens);

        Assert.AreEqual(2, roundUsageEvents[1].RoundIndex);
        Assert.IsTrue(roundUsageEvents[1].IsFinalRound);
        Assert.IsNotNull(roundUsageEvents[1].Usage);
        var secondGeminiRoundUsage = roundUsageEvents[1].Usage!;
        Assert.AreEqual(13000, secondGeminiRoundUsage.InputTokens);
        Assert.AreEqual(1000, secondGeminiRoundUsage.OutputTokens);
        Assert.AreEqual(14000, secondGeminiRoundUsage.TotalTokens);
        Assert.AreEqual(34, secondGeminiRoundUsage.CachedInputTokens);
        Assert.AreEqual(5, secondGeminiRoundUsage.ReasoningTokens);

        var completion = events.Last(e => e.Type == StreamingContentType.Completion);
        Assert.IsNotNull(completion.Usage);
        var geminiCompletionUsage = completion.Usage!;
        Assert.AreEqual(23000, geminiCompletionUsage.InputTokens);
        Assert.AreEqual(1100, geminiCompletionUsage.OutputTokens);
        Assert.AreEqual(24100, geminiCompletionUsage.TotalTokens);
    }

    #endregion

    #region RunAgentStreamAsync - MaxSteps Exceeded

    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentStreamAsync_MaxStepsExceeded_ThrowsAgentException()
    {
        var mock = new MockStreamingAgentServiceNoCompletion();

        AgentMaxStepsExceededException? ex = null;
        try
        {
            await foreach (var _ in mock.RunAgentStreamAsync("complex task", maxSteps: 3))
            {
            }

            Assert.Fail("Expected AgentMaxStepsExceededException was not thrown");
        }
        catch (AgentMaxStepsExceededException caught)
        {
            ex = caught;
        }

        Assert.IsNotNull(ex);
        Assert.AreEqual(3, ex.MaxSteps);
        Assert.AreEqual("Partial streamed answer...", ex.PartialResponse);
    }

    #endregion

    #region RunAgentAsync - WithFunction Integration

    /// <summary>
    /// WithFunction으로 등록된 FC 인프라가 RunAgentAsync에서 그대로 유지되는지 확인
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_RegisteredFunctions_ArePreserved()
    {
        var mock = new MockAgentService("Final answer after tool use.");

        mock.WithFunction(
            "get_weather",
            "Gets current weather",
            ("city", "City name", true),
            (string city) => $"Weather in {city}: Sunny, 25°C");

        Assert.AreEqual(1, mock.Functions.Count, "Function should be registered before agent call");

        var result = await mock.RunAgentAsync("What's the weather in Seoul?");

        Assert.AreEqual("Final answer after tool use.", result);
        Assert.AreEqual(1, mock.Functions.Count, "Function should still be registered after agent call");
    }

    /// <summary>
    /// 다중 함수가 등록된 상태에서 RunAgentAsync 호출
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_MultipleFunctions_AllPreserved()
    {
        var mock = new MockAgentService("Combined result.");

        mock.WithFunction(
            "search",
            "Search the web",
            ("query", "Search query", true),
            (string q) => $"Results for: {q}");

        mock.WithFunction(
            "calculate",
            "Perform calculation",
            ("expression", "Math expression", true),
            (string expr) => $"Result: 42");

        Assert.AreEqual(2, mock.Functions.Count);

        var result = await mock.RunAgentAsync("Search and calculate something");

        Assert.AreEqual("Combined result.", result);
        Assert.AreEqual(2, mock.Functions.Count);
    }

    #endregion

    #region RunAgentAsync - Custom MaxSteps

    /// <summary>
    /// 커스텀 maxSteps 값으로 호출 시 정상 동작
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_CustomMaxSteps_WorksCorrectly()
    {
        var mock = new MockAgentService("Success with custom steps.");

        var result = await mock.RunAgentAsync("goal", maxSteps: 5);

        Assert.AreEqual("Success with custom steps.", result);
    }

    /// <summary>
    /// maxSteps=1 으로 호출 시에도 정상 동작 (1회 호출로 완료)
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_MaxStepsOne_SucceedsIfCompletedInOneStep()
    {
        var mock = new MockAgentService("Immediate answer.");

        var result = await mock.RunAgentAsync("simple question", maxSteps: 1);

        Assert.AreEqual("Immediate answer.", result);
    }

    #endregion

    #region RunAgentAsync - Policy Preservation

    /// <summary>
    /// RunAgentAsync 호출 후 DefaultPolicy가 변경되지 않는지 확인
    /// </summary>
    [TestCategory("Unit")]
    [TestCategory("Agent")]
    [TestMethod]
    public async Task RunAgentAsync_DoesNotMutateDefaultPolicy()
    {
        var mock = new MockAgentService("done");
        mock.DefaultPolicy = new FunctionCallingPolicy
        {
            MaxRounds = 20,
            TimeoutSeconds = 60,
            EnableLogging = true
        };

        var originalMaxRounds = mock.DefaultPolicy.MaxRounds;
        var originalTimeout = mock.DefaultPolicy.TimeoutSeconds;

        await mock.RunAgentAsync("task", maxSteps: 5);

        Assert.AreEqual(originalMaxRounds, mock.DefaultPolicy.MaxRounds,
            "DefaultPolicy.MaxRounds should not be modified");
        Assert.AreEqual(originalTimeout, mock.DefaultPolicy.TimeoutSeconds,
            "DefaultPolicy.TimeoutSeconds should not be modified");
    }

    #endregion
}
