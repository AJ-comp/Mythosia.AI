using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Tests;

internal static class AnthropicFableAllocationLiveScenario
{
    internal static async Task VerifyAsync(FableExecutionMode mode, string model = AIModels.Anthropic.ClaudeFable5_1)
    {
        using var probe = await AnthropicFableLiveProbe.CreateAsync(model);
        probe.Service.DefaultPolicy.MaxRounds = 6;
        probe.Service.ActivateChat.SystemMessage =
            "Keep the person following this task informed: give a short opening status, then brief public updates at meaningful " +
            "tool-result boundaries describing confirmed findings and the next action. Finish with a concise, self-contained recap.";
        probe.Service.WithThinkingBinding(ClaudeThinkingPrefixMismatchBehavior.Error)
            .WithAdaptiveThinkingParameters(ClaudeReasoningEffort.High, ClaudeThinkingDisplay.Updates)
            .WithTurnInstruction("Tool outputs are hidden from the user. Include the useful findings in your progress updates.");
        var snapshotId = "snapshot_" + Guid.NewGuid().ToString("N");
        var rulesId = "rules_" + Guid.NewGuid().ToString("N");
        var reportReference = "allocation_" + Guid.NewGuid().ToString("N");
        var reads = 0; var rulesReads = 0; var checks = 0;
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "read_inventory", Description = "Read the current synthetic kit-component stock and existing reservations.",
            Handler = _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    snapshot_id = snapshotId,
                    components = new[]
                    {
                        new { item = "blue_notebook", stock = 100, reserved = 16 },
                        new { item = "red_notebook", stock = 70, reserved = 13 },
                        new { item = "label_sheet", stock = 112, reserved = 28 }
                    }
                }));
            }
        });
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "read_allocation_rules", Description = "Read kit composition, carton sizes, and order priorities for this inventory snapshot.",
            Parameters = new FunctionParameters
            {
                Properties = new() { ["snapshot_id"] = new() { Type = "string", Description = "The snapshot_id returned by read_inventory." } },
                Required = new() { "snapshot_id" }
            },
            Handler = arguments =>
            {
                Interlocked.Increment(ref rulesReads);
                Assert.AreEqual(snapshotId, arguments["snapshot_id"].ToString());
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    rules_id = rulesId,
                    per_kit = new { blue_notebook = 2, red_notebook = 1, label_sheet = 3 },
                    kits_per_carton = 4,
                    priority_order_requested_kits = 18,
                    standard_order_requested_kits = 14,
                    policy = "Subtract existing reservations. Ship whole cartons only. Never exceed an order's requested kits. " +
                        "Maximize fulfilled priority-order kits first, then standard-order kits from the remaining stock."
                }));
            }
        });
        probe.Service.Functions.Add(new FunctionDefinition
        {
            Name = "check_allocation", Description = "Validate the proposed synthetic allocation; this operation only calculates and does not change inventory.",
            Parameters = new FunctionParameters
            {
                Properties = new()
                {
                    ["rules_id"] = new() { Type = "string", Description = "The rules_id returned by read_allocation_rules." },
                    ["priority_kits"] = new() { Type = "integer", Description = "The proposed number of kits for the priority order." },
                    ["standard_kits"] = new() { Type = "integer", Description = "The proposed number of kits for the standard order." }
                },
                Required = new() { "rules_id", "priority_kits", "standard_kits" }
            },
            Handler = arguments =>
            {
                Interlocked.Increment(ref checks);
                Assert.AreEqual(rulesId, arguments["rules_id"].ToString());
                Assert.AreEqual("16", arguments["priority_kits"].ToString());
                Assert.AreEqual("12", arguments["standard_kits"].ToString());
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    valid = true, report_reference = reportReference,
                    allocated_kits = 28, remaining_unreserved = new { blue_notebook = 28, red_notebook = 29, label_sheet = 0 }
                }));
            }
        });

        var answer = await probe.ExecuteAsync(
            "Plan fulfillment of the priority and standard kit orders from the synthetic inventory. Read the stock, " +
            "then read its allocation rules, calculate a feasible allocation, and validate it with check_allocation. " +
            "Use each of the three tools once. Explain the confirmed inventory constraints and next action as you work. " +
            "Conclude with the quantities, the binding stock or packaging limits, and the report reference returned by the check.", mode);
        Assert.AreEqual(1, reads);
        Assert.AreEqual(1, rulesReads);
        Assert.AreEqual(1, checks);
        StringAssert.Contains(answer.Text, reportReference);
        var calls = probe.Service.ActivateChat.Messages.Where(message => message.FunctionCallBatch != null)
            .SelectMany(message => message.FunctionCallBatch!.Calls).ToArray();
        var results = probe.Service.ActivateChat.Messages.Where(message => message.FunctionCallResultBatch != null)
            .SelectMany(message => message.FunctionCallResultBatch!.Results).ToArray();
        Assert.AreEqual(3, calls.Length);
        Assert.AreEqual(3, calls.Select(call => call.Id).Distinct().Count());
        CollectionAssert.AreEquivalent(calls.Select(call => call.Id).ToArray(), results.Select(result => result.Call.Id).ToArray());
        Assert.AreEqual(4, probe.Requests.Count);
        foreach (var request in probe.Requests)
        {
            Assert.AreEqual("auto", request.Body["tool_choice"]?["type"]?.GetValue<string>());
            Assert.AreEqual("updates", request.Body["thinking"]?["display"]?.GetValue<string>());
            Assert.AreEqual(200, request.StatusCode);
        }
        probe.AssertTransport(mode, AnthropicFableLiveProbe.BindingBeta, AnthropicFableLiveProbe.UpdatesBeta, AnthropicFableLiveProbe.TurnBeta);
        for (var index = 1; index < probe.Requests.Count; index++)
        {
            var before = probe.Requests[index - 1].Body["messages"]!.AsArray();
            var after = probe.Requests[index].Body["messages"]!.AsArray();
            for (var message = 0; message < before.Count; message++)
                Assert.AreEqual(before[message]!.ToJsonString(), after[message]!.ToJsonString());
        }
        if (model == AIModels.Anthropic.ClaudeOpus5_5)
        {
            var signedBlocks = 0;
            for (var index = 0; index + 1 < probe.Requests.Count; index++)
            {
                var replayed = probe.Requests[index + 1].Body["messages"]!.AsArray().OfType<JsonObject>()
                    .Where(message => message["content"] is JsonArray)
                    .SelectMany(message => message["content"]!.AsArray().OfType<JsonObject>()).ToArray();
                foreach (var original in probe.Requests[index].ResponseBlocks().Where(block => block["type"]?.GetValue<string>() == "thinking"))
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(original["signature"]?.GetValue<string>()));
                    Assert.IsTrue(replayed.Any(block => JsonNode.DeepEquals(original, block)), "A real signed Opus 5.5 thinking block changed before tool continuation.");
                    signedBlocks++;
                }
            }
            Assert.IsTrue(signedBlocks > 0, "The live tool task must exercise signed thinking replay.");
        }
        var readableThinking = probe.Requests.SelectMany(request => request.ResponseBlocks()).Count(block =>
            block["type"]?.GetValue<string>() == "thinking" && !string.IsNullOrWhiteSpace(block["thinking"]?.GetValue<string>()));
        Console.WriteLine($"{(model == AIModels.Anthropic.ClaudeOpus5_5 ? "LIVE_OPUS55_ALLOCATION" : "LIVE_FABLE_ALLOCATION")} handlers={reads + rulesReads + checks} readableThinkingBlocks={readableThinking} publicThinkingPresent={!string.IsNullOrWhiteSpace(probe.Service.LastThinkingContent)}");
        Assert.IsTrue(readableThinking > 0, "The real server did not emit readable thinking; accepted Updates options alone do not prove delivery.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(probe.Service.LastThinkingContent));
        if (mode != FableExecutionMode.Completion)
            Assert.IsTrue(answer.Events.Any(item => item.Type == StreamingContentType.Reasoning && !string.IsNullOrWhiteSpace(item.Content)));
    }
}
