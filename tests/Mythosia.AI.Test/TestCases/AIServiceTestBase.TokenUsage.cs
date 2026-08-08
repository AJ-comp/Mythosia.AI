using Mythosia.AI.Extensions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.Base;
using System.Text.Json;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// Tests that a real provider stream emits <see cref="StreamingContentType.RoundUsage"/>
    /// for a single LLM call even when <see cref="StreamOptions.IncludeMetadata"/> is false.
    /// Guarantees that token usage collection is independent from provider metadata chunks,
    /// and that consumers can read the final single-round token count from
    /// <see cref="StreamingContent.Usage"/> without parsing metadata.
    /// </summary>
    [TestCategory("Token")]
    [TestCategory("StreamingMetadata")]
    [TestMethod]
    public async Task StreamingRoundUsage_WithMetadataDisabled_EmitsUsage()
    {
        if (AI is not AIService aiService)
        {
            Assert.Inconclusive("RoundUsage streaming requires AIService base class");
            return;
        }

        var options = new StreamOptions
        {
            IncludeMetadata = false,
            IncludeFunctionCalls = false,
            IncludeReasoning = false,
            TextOnly = false
        };

        var events = new List<StreamingContent>();

        await foreach (var content in aiService.StreamAsync(
            "Answer in one short sentence: token usage works.",
            options))
        {
            events.Add(content);
        }

        var roundUsageEvents = events
            .Where(e => e.Type == StreamingContentType.RoundUsage)
            .ToList();

        Assert.IsTrue(roundUsageEvents.Count > 0,
            "Stream should emit RoundUsage even when IncludeMetadata is false.");

        AssertRoundUsageSequence(roundUsageEvents);

        var finalRoundUsage = roundUsageEvents.Last();
        Assert.IsTrue(finalRoundUsage.IsFinalRound,
            "The final single-call stream should mark the last RoundUsage as final.");
        Assert.IsNull(finalRoundUsage.Metadata,
            "RoundUsage should not require Metadata when IncludeMetadata is false.");

        var completion = events.LastOrDefault(e => e.Type == StreamingContentType.Completion);
        Assert.IsNotNull(completion, "Stream should emit a Completion event.");
        Assert.IsNotNull(completion!.Usage,
            "Completion should keep existing cumulative usage behavior.");

        var completionUsage = completion.Usage!;
        Assert.IsTrue(completionUsage.InputTokens > 0,
            $"Completion InputTokens should be > 0, got {completionUsage.InputTokens}.");
        Assert.IsTrue(completionUsage.TotalTokens >= completionUsage.InputTokens + completionUsage.OutputTokens,
            $"Completion TotalTokens ({completionUsage.TotalTokens}) should be >= InputTokens + OutputTokens.");
    }

    /// <summary>
    /// Tests that a real provider agent stream with function calling emits one
    /// <see cref="StreamingContentType.RoundUsage"/> per LLM round and marks only the last
    /// round as final. Guarantees that each RoundUsage is per-round, not cumulative, and
    /// that the final <see cref="StreamingContentType.Completion"/> usage equals the sum
    /// of all round usages for the agent run.
    /// </summary>
    [TestCategory("Token")]
    [TestCategory("Agent")]
    [TestCategory("FunctionCalling")]
    [TestMethod]
    public async Task RunAgentStreamAsync_WithFunctionCalling_EmitsRoundUsagePerRound()
    {
        await RunIfSupported(
            () => SupportsFunctionCalling(),
            async () =>
            {
                var functionWasCalled = false;

                AI.WithFunction<string>(
                    "get_token_meter_weather",
                    "Gets deterministic weather for token-meter tests. Call this when asked for Seoul weather.",
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
                        });
                ConfigureRequiredFunctionCall("get_token_meter_weather");

                var events = new List<StreamingContent>();

                await foreach (var content in AI.RunAgentStreamAsync(
                    "You must call get_token_meter_weather for Seoul exactly once before answering. " +
                    "After the tool result, answer in one short sentence.",
                    maxSteps: 5,
                    options: StreamOptions.WithFunctions))
                {
                    events.Add(content);
                }

                var errors = events.Where(e => e.Type == StreamingContentType.Error).ToList();
                Assert.IsFalse(errors.Any(),
                    $"Agent stream should not emit errors. Errors: {string.Join(" | ", errors.Select(e => e.Content))}");

                Assert.IsTrue(functionWasCalled,
                    "Provider should call the registered weather function.");
                Assert.IsTrue(events.Any(e => e.Type == StreamingContentType.FunctionCall),
                    "Agent stream should emit a FunctionCall event.");
                Assert.IsTrue(events.Any(e => e.Type == StreamingContentType.FunctionResult),
                    "Agent stream should emit a FunctionResult event.");

                var roundUsageEvents = events
                    .Where(e => e.Type == StreamingContentType.RoundUsage)
                    .ToList();

                Assert.IsTrue(roundUsageEvents.Count >= 2,
                    "Function-calling agent stream should emit RoundUsage for the function-call round and the final answer round.");
                AssertRoundUsageSequence(roundUsageEvents);

                Assert.IsTrue(roundUsageEvents.Take(roundUsageEvents.Count - 1).Any(e => !e.IsFinalRound),
                    "At least one pre-final RoundUsage should be marked as non-final after a function call.");
                Assert.IsTrue(roundUsageEvents.Last().IsFinalRound,
                    "The last RoundUsage should be marked as final.");

                var completion = events.LastOrDefault(e => e.Type == StreamingContentType.Completion);
                Assert.IsNotNull(completion, "Agent stream should emit a final Completion event.");
                Assert.IsNotNull(completion!.Usage,
                    "Completion should keep cumulative usage for the entire agent run.");

                var completionUsage = completion.Usage!;
                Assert.AreEqual(roundUsageEvents.Sum(e => e.Usage!.InputTokens), completionUsage.InputTokens,
                    "Completion InputTokens should equal the sum of per-round input tokens.");
                Assert.AreEqual(roundUsageEvents.Sum(e => e.Usage!.OutputTokens), completionUsage.OutputTokens,
                    "Completion OutputTokens should equal the sum of per-round output tokens.");
                Assert.AreEqual(completionUsage.InputTokens + completionUsage.OutputTokens, completionUsage.TotalTokens,
                    "Completion TotalTokens should remain cumulative InputTokens + OutputTokens.");
            },
            "Agent function-calling RoundUsage"
        );
    }

    /// <summary>
    /// Verifies the shared RoundUsage contract: sequential round indexes, non-null usage,
    /// positive input tokens, non-negative output tokens, and TotalTokens normalized to
    /// InputTokens + OutputTokens for each individual round.
    /// </summary>
    private static void AssertRoundUsageSequence(IReadOnlyList<StreamingContent> roundUsageEvents)
    {
        for (var i = 0; i < roundUsageEvents.Count; i++)
        {
            var content = roundUsageEvents[i];
            Assert.AreEqual(i + 1, content.RoundIndex,
                $"RoundUsage event {i + 1} should have a sequential RoundIndex.");
            Assert.IsNotNull(content.Usage,
                $"RoundUsage event {i + 1} should include Usage.");

            var usage = content.Usage!;
            Assert.IsTrue(usage.InputTokens > 0,
                $"RoundUsage event {i + 1} InputTokens should be > 0, got {usage.InputTokens}.");
            Assert.IsTrue(usage.OutputTokens >= 0,
                $"RoundUsage event {i + 1} OutputTokens should be >= 0, got {usage.OutputTokens}.");
            Assert.AreEqual(usage.InputTokens + usage.OutputTokens, usage.TotalTokens,
                $"RoundUsage event {i + 1} TotalTokens should be InputTokens + OutputTokens.");
        }
    }
}
