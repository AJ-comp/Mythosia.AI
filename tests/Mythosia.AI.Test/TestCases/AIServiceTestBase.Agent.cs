using Mythosia.AI.Extensions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// RunAgentAsync 기본 통합 테스트
    /// FC(get_weather)를 등록한 뒤, 에이전트가 도구를 호출하고 최종 텍스트 답변을 반환하는지 검증
    /// </summary>
    [TestCategory("Agent")]
    [TestMethod]
    public async Task AgentRunAsync_WithFunctionCalling_ReturnsAnswer()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                bool functionWasCalled = false;

                AI.WithFunction<string>(
                    "get_weather",
                    "Gets current weather for a city. Returns JSON with temperature and condition.",
                    ("city", "The city name", true),
                    city =>
                    {
                        functionWasCalled = true;
                        return JsonSerializer.Serialize(new
                        {
                            city,
                            temperature = 24,
                            unit = "celsius",
                            condition = "sunny"
                        });
                    }
                );

                var result = await AI.RunAgentAsync(
                    "What is the current weather in Seoul? Use the get_weather function to find out.",
                    maxSteps: 10);

                Console.WriteLine($"[Agent Result] {result}");

                // 기본 검증: 응답이 비어있지 않아야 한다
                Assert.IsNotNull(result);
                Assert.IsFalse(string.IsNullOrWhiteSpace(result), "Agent should return a non-empty answer");

                // FC가 실제로 호출되었는지 검증
                Assert.IsTrue(functionWasCalled, "Agent should have called the get_weather function");

                // 응답에 서울 관련 내용이 포함되어 있는지 (대소문자 무시)
                var lower = result.ToLowerInvariant();
                Assert.IsTrue(
                    lower.Contains("seoul") || lower.Contains("24") || lower.Contains("sunny"),
                    $"Agent response should reference the weather data. Got: {result}");
            },
            "Agent (RunAgentAsync)"
        );
    }

    /// <summary>
    /// RunAgentStreamAsync 통합 테스트
    /// 실제 provider의 스트리밍 + function calling 라운드에서
    /// 함수 호출 이벤트, 함수 결과 이벤트, 최종 completion 및 답변 텍스트가 모두 관찰되는지 검증한다.
    /// </summary>
    [TestCategory("Agent")]
    [TestMethod]
    public async Task AgentRunStreamAsync_WithFunctionCalling_StreamsAnswerAndEvents()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                bool functionWasCalled = false;

                AI.WithFunction<string>(
                    "get_weather",
                    "Gets current weather for a city. Returns JSON with temperature and condition.",
                    ("city", "The city name", true),
                    city =>
                    {
                        functionWasCalled = true;
                        return JsonSerializer.Serialize(new
                        {
                            city,
                            temperature = 24,
                            unit = "celsius",
                            condition = "sunny"
                        });
                    }
                );

                var events = new List<StreamingContent>();

                await foreach (var content in AI.RunAgentStreamAsync(
                    "What is the current weather in Seoul? Use the get_weather function before answering.",
                    maxSteps: 10,
                    options: StreamOptions.WithFunctions))
                {
                    events.Add(content);

                    if (content.Type == StreamingContentType.Text && content.Content != null)
                        Console.Write(content.Content);
                    else if (content.Type == StreamingContentType.FunctionCall)
                        Console.WriteLine($"[AgentStream] FunctionCall: {content.Metadata?["function_name"]}");
                    else if (content.Type == StreamingContentType.FunctionResult)
                        Console.WriteLine($"[AgentStream] FunctionResult: {content.Metadata?["function_name"]}");
                }

                Assert.IsTrue(functionWasCalled, "Agent stream should have called the get_weather function");
                Assert.IsTrue(events.Any(e => e.Type == StreamingContentType.FunctionCall),
                    "Agent stream should emit at least one FunctionCall event");
                Assert.IsTrue(events.Any(e => e.Type == StreamingContentType.FunctionResult),
                    "Agent stream should emit at least one FunctionResult event");
                Assert.IsTrue(events.Any(e => e.Type == StreamingContentType.Completion),
                    "Agent stream should emit a final Completion event");

                var result = string.Concat(events
                    .Where(e => e.Type == StreamingContentType.Text)
                    .Select(e => e.Content));

                Console.WriteLine($"\n[Agent Stream Result] {result}");

                Assert.IsFalse(string.IsNullOrWhiteSpace(result),
                    "Agent stream should produce non-empty final text");

                var lower = result.ToLowerInvariant();
                Assert.IsTrue(
                    lower.Contains("seoul") || lower.Contains("24") || lower.Contains("sunny"),
                    $"Agent stream response should reference the weather data. Got: {result}");
            },
            "Agent Streaming (RunAgentStreamAsync)"
        );
    }
}
