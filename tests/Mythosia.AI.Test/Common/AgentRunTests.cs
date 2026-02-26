using Mythosia.AI.Exceptions;
using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Services.Base;
using System.Net;
using System.Net.Http;
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

        public override AIProvider Provider => AIProvider.OpenAI;

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

        public override AIProvider Provider => AIProvider.OpenAI;

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
