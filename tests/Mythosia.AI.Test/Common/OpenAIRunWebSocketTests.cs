using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Streaming;
using Mythosia.AI.Services.OpenAI;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
[TestCategory("OpenAI")]
[TestCategory("Run")]
public class OpenAIRunWebSocketTests
{
    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra)]
    [DataRow(AIModels.OpenAI.Gpt6Sol)]
    [DataRow(AIModels.OpenAI.Gpt6Luna)]
    public async Task ResultOnly_UsesOneSocketRequestWithoutStreamFields(string model)
    {
        using var socket = new ScriptedSocket();
        socket.OnSend = payload =>
        {
            Assert.AreEqual("response.create", Kind(payload));
            Assert.AreEqual(model, payload.GetProperty("model").GetString());
            Assert.IsFalse(payload.TryGetProperty("stream", out _));
            Assert.IsFalse(payload.TryGetProperty("background", out _));
            socket.Push(Created("resp_1"), Text("hello"), Completed("resp_1"));
        };
        var service = new SocketService(socket);
        service.ChangeModel(model);

        await using var run = await service.StartRunAsync("hello");
        Assert.IsTrue(run.CanSteer);
        Assert.AreEqual("hello", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(1, socket.Sent.Count);
        Assert.AreEqual(1, service.Connections);
    }

    [TestMethod]
    [DataRow(AIModels.OpenAI.Gpt6Astra, false)]
    [DataRow(AIModels.OpenAI.Gpt6Astra, true)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, false)]
    [DataRow(AIModels.OpenAI.Gpt6Sol, true)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, false)]
    [DataRow(AIModels.OpenAI.Gpt6Luna, true)]
    public async Task AcceptedSteer_WaitsForAutomaticContinuation(string model, bool interrupted)
    {
        using var socket = new ScriptedSocket();
        var firstText = NewSignal();
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.create")
                socket.Push(Created("resp_1"), Text("before "));
            else
            {
                Assert.AreEqual("response.steer", Kind(payload));
                Assert.AreEqual(3, payload.EnumerateObject().Count());
                Assert.AreEqual("resp_1", payload.GetProperty("previous_response_id").GetString());
                socket.Push(Accepted("resp_1", "steer_1"),
                    interrupted ? Steered("resp_1") : Completed("resp_1"),
                    Created("resp_2"), Text("after"), Completed("resp_2"));
            }
        };
        var service = new SocketService(socket);
        service.ChangeModel(model);
        var observed = new StringBuilder();
        await using var run = await service.StartRunAsync("draft", text => { observed.Append(text); firstText.TrySetResult(); });
        await firstText.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.SteerAsync("revise").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("before after", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(run.Result.Result.Text, observed.ToString());
        Assert.AreEqual(1, socket.Sent.Count(item => Kind(item) == "response.create"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SteeredResult_AggregatesEachResponseUsageAndKeepsOnlyFinalResponseModel(bool finalModelReported)
    {
        using var socket = new ScriptedSocket();
        var firstText = NewSignal();
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.create")
            {
                socket.Push(JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp_1", model = "reported-first-model", status = "in_progress" }
                }), Text("before "));
                return;
            }

            Assert.AreEqual("response.steer", Kind(payload));
            var successor = new Dictionary<string, object>
            {
                ["id"] = "resp_2",
                ["status"] = "in_progress"
            };
            if (finalModelReported) successor["model"] = "reported-final-model";
            socket.Push(
                Accepted("resp_1", "steer_1"),
                JsonSerializer.Serialize(new
                {
                    type = "response.incomplete",
                    response = new
                    {
                        id = "resp_1", status = "incomplete",
                        incomplete_details = new { reason = "steered" },
                        usage = new { input_tokens = 10, output_tokens = 3, total_tokens = 13 },
                        output = Array.Empty<object>()
                    }
                }),
                JsonSerializer.Serialize(new { type = "response.created", response = successor }),
                Text("after"),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new
                    {
                        id = "resp_2", status = "completed",
                        usage = new { input_tokens = 20, output_tokens = 5, total_tokens = 25 },
                        output = Array.Empty<object>()
                    }
                }));
        };
        var service = new SocketService(socket);
        await using var run = await service.StartRunAsync("draft", _ => firstText.TrySetResult());
        await firstText.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.SteerAsync("revise").WaitAsync(TimeSpan.FromSeconds(5));

        var result = await run.Result.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("before after", result.Text);
        Assert.AreEqual(2, result.RoundCount);
        Assert.AreEqual(finalModelReported ? "reported-final-model" : null, result.Model);
        Assert.AreEqual("gpt-6-astra", result.RequestedModel);
        Assert.AreEqual("completed", result.RawFinishReason);
        Assert.AreEqual(AIFinishReason.Stop, result.FinishReason);
        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(30, result.Usage.InputTokens);
        Assert.AreEqual(8, result.Usage.OutputTokens);
        Assert.AreEqual(38, result.Usage.TotalTokens);
        Assert.AreEqual(1, socket.Sent.Count(item => Kind(item) == "response.create"));
    }

    [TestMethod]
    public async Task SubsequentSteer_TargetsTheSuccessorResponse()
    {
        using var socket = new ScriptedSocket();
        var firstText = NewSignal();
        var secondText = NewSignal();
        int steers = 0;
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.create") socket.Push(Created("resp_1"), Text("first "));
            else if (Interlocked.Increment(ref steers) == 1)
                socket.Push(Accepted("resp_1", "steer_1"), Steered("resp_1"), Created("resp_2"), Text("second "));
            else
            {
                Assert.AreEqual("resp_2", payload.GetProperty("previous_response_id").GetString());
                socket.Push(Accepted("resp_2", "steer_2"), Steered("resp_2"), Created("resp_3"), Text("third"), Completed("resp_3"));
            }
        };
        var service = new SocketService(socket);
        await using var run = await service.StartRunAsync("draft", text =>
        {
            if (text == "first ") firstText.TrySetResult();
            if (text == "second ") secondText.TrySetResult();
        });
        await firstText.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.SteerAsync("first change").WaitAsync(TimeSpan.FromSeconds(5));
        await secondText.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.SteerAsync("second change").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("first second third", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(1, socket.Sent.Count(item => Kind(item) == "response.create"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SteeringDuringRequiredTool_SendsOnlyOriginalResultAndResendsSettings(bool pendingNotification)
    {
        using var socket = new ScriptedSocket();
        var started = NewSignal();
        var release = NewSignal();
        int creates = 0;
        int calls = 0;
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.steer")
            {
                socket.Push(Accepted("resp_1", "steer_1"));
                if (pendingNotification) socket.Push(Pending("resp_1"));
                return;
            }
            if (Interlocked.Increment(ref creates) == 1)
                socket.Push(Created("resp_1"), ToolCompleted("resp_1", false));
            else
            {
                Assert.AreEqual("resp_1", payload.GetProperty("previous_response_id").GetString());
                Assert.IsTrue(payload.TryGetProperty("tools", out _));
                Assert.IsTrue(payload.TryGetProperty("instructions", out _));
                var input = payload.GetProperty("input").EnumerateArray().ToArray();
                Assert.AreEqual(1, input.Length);
                Assert.AreEqual("function_call_output", input[0].GetProperty("type").GetString());
                Assert.AreEqual("call_read", input[0].GetProperty("call_id").GetString());
                Assert.AreEqual("tool value", input[0].GetProperty("output").GetString());
                socket.Push(Created("resp_2"), Text("finished"), Completed("resp_2"));
            }
        };
        var service = new SocketService(socket) { SystemMessage = "Preserve these settings." };
        service.Functions.Add(new FunctionDefinition
        {
            Name = "read_value", Handler = async _ =>
            { Interlocked.Increment(ref calls); started.TrySetResult(); await release.Task; return "tool value"; }
        });
        await using var run = await service.StartRunAsync("use tool");
        var events = new List<StreamingContent>();
        var reading = ReadAsync(run.StreamAsync(), events);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await run.SteerAsync("change answer").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(run.Result.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.AreEqual("finished", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        await reading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(2, creates);
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.FunctionResult));
        Assert.AreEqual(1, events.Count(item => item.Type == StreamingContentType.Completion));
    }

    [TestMethod]
    public async Task AsyncResultNeededByQueuedSteer_UsesCoordinatorWithoutLosingResult()
    {
        using var socket = new ScriptedSocket();
        var started = NewSignal();
        var release = NewSignal();
        var waiting = NewSignal();
        int creates = 0;
        int calls = 0;
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.steer")
            {
                socket.Push(Accepted("resp_2", "steer_1"), Completed("resp_2"), Pending("resp_2"));
                return;
            }
            var count = Interlocked.Increment(ref creates);
            if (count == 1) socket.Push(Created("resp_1"), ToolCompleted("resp_1", true));
            else if (count == 2)
            {
                Assert.AreEqual(0, payload.GetProperty("input").GetArrayLength());
                socket.Push(Created("resp_2"), Text("waiting "));
            }
            else
            {
                var result = Assert.ContainsSingle(payload.GetProperty("input").EnumerateArray().ToArray());
                Assert.AreEqual("call_read", result.GetProperty("call_id").GetString());
                socket.Push(Created("resp_3"), Text("complete"), Completed("resp_3"));
            }
        };
        var service = new SocketService(socket);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "read_value", AllowAsync = true, Handler = async _ =>
            { Interlocked.Increment(ref calls); started.TrySetResult(); await release.Task; return "tool value"; }
        });
        await using var run = await service.StartRunAsync("use async tool", text => { if (text == "waiting ") waiting.TrySetResult(); });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await run.SteerAsync("change answer").WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.TrySetResult(); }
        Assert.AreEqual("waiting complete", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(3, creates);
    }

    [TestMethod]
    public Task SteerBetweenAsyncResponses_WaitsForTheNextCreatedId() => VerifyAsyncResponseGapAsync(false);

    [TestMethod]
    public Task SteerWhileWaitingForAsyncResult_WakesTheModelWithoutCancelingTool() => VerifyAsyncResponseGapAsync(true);

    private static async Task VerifyAsyncResponseGapAsync(bool waitingForTool)
    {
        using var socket = new ScriptedSocket();
        var started = NewSignal();
        var release = NewSignal();
        var secondRequested = NewSignal();
        int creates = 0;
        int steers = 0;
        int calls = 0;
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.steer")
            {
                Interlocked.Increment(ref steers);
                var target = waitingForTool ? "resp_3" : "resp_2";
                Assert.AreEqual(target, payload.GetProperty("previous_response_id").GetString());
                socket.Push(Accepted(target, "steer_1"), Steered(target),
                    Created("resp_auto"), Text("revised "), Completed("resp_auto"));
                return;
            }
            var count = Interlocked.Increment(ref creates);
            if (count == 1) socket.Push(Created("resp_1"), ToolCompleted("resp_1", true));
            else if (count == 2)
            {
                secondRequested.TrySetResult();
                if (waitingForTool) socket.Push(Created("resp_2"), Text("independent "), Completed("resp_2"));
            }
            else if (count == 3 && waitingForTool)
                socket.Push(Created("resp_3"), Text("resumed "));
            else
            {
                var result = Assert.ContainsSingle(payload.GetProperty("input").EnumerateArray().ToArray());
                Assert.AreEqual("call_read", result.GetProperty("call_id").GetString());
                Assert.AreEqual("resp_auto", payload.GetProperty("previous_response_id").GetString());
                socket.Push(Created("resp_final"), Text("result"), Completed("resp_final"));
            }
        };
        var service = new SocketService(socket);
        service.Functions.Add(new FunctionDefinition
        {
            Name = "read_value", AllowAsync = true, Handler = async _ =>
            { Interlocked.Increment(ref calls); started.TrySetResult(); await release.Task; return "tool value"; }
        });
        await using var run = await service.StartRunAsync("use async tool");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (waitingForTool) await service.WaitingForToolResults.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var steering = run.SteerAsync("revise");
            if (!waitingForTool)
            {
                Assert.IsFalse(steering.IsCompleted);
                Assert.AreEqual(0, steers, "No steering event may target the already completed response.");
                socket.Push(Created("resp_2"), Text("resumed "));
            }
            await steering.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(release.Task.IsCompleted, "Resuming the model must not release or cancel the pending tool.");
        }
        finally { release.TrySetResult(); }
        var resultText = (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text;
        StringAssert.Contains(resultText, "resumed revised result");
        Assert.AreEqual(1, steers);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(waitingForTool ? 4 : 3, creates);
    }

    [TestMethod]
    public async Task SteeredTerminal_DiscardsIncompleteToolDeltas()
    {
        using var socket = new ScriptedSocket();
        var ready = NewSignal();
        var calls = 0;
        var unfinished = new { type = "function_call", id = "fc_read", call_id = "call_read", name = "read_value", arguments = "{}", status = "in_progress" };
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.create")
                socket.Push(Created("resp_1"), JsonSerializer.Serialize(new { type = "response.output_item.added", output_index = 0, item = unfinished }), Text("draft "));
            else
                socket.Push(Accepted("resp_1", "steer_1"), JsonSerializer.Serialize(new
                {
                    type = "response.incomplete", response = new { id = "resp_1", status = "incomplete", incomplete_details = new { reason = "steered" }, output = new[] { unfinished } }
                }), Created("resp_2"), Text("revised"), Completed("resp_2"));
        };
        var service = new SocketService(socket);
        service.Functions.Add(new FunctionDefinition { Name = "read_value", Handler = _ => { Interlocked.Increment(ref calls); return Task.FromResult("unsafe"); } });
        await using var run = await service.StartRunAsync("draft", _ => ready.TrySetResult());
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.SteerAsync("revise").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("draft revised", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(0, calls, "Syntactically valid partial arguments must not make an unfinished call executable.");
        Assert.IsFalse(service.ActivateChat.Messages.Any(message => message.FunctionCallBatch != null));
    }

    [TestMethod]
    public async Task AsyncPending_WaitsForEveryRequiredIdInsteadOfFirstUnrelatedResult()
    {
        using var socket = new ScriptedSocket();
        var ready = NewSignal();
        var slow = NewSignal();
        var fast = NewSignal();
        var fastFinished = NewSignal();
        var createAfterSteer = NewSignal();
        int creates = 0;
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.steer")
            {
                socket.Push(Accepted("resp_2", "steer_1"), Completed("resp_2"), Pending("resp_2"));
                return;
            }
            var count = Interlocked.Increment(ref creates);
            if (count == 1)
            {
                var calls = new[]
                {
                    new { type = "function_call", id = "fc_slow", call_id = "call_read", name = "slow", arguments = "{}", status = "completed", @async = true },
                    new { type = "function_call", id = "fc_fast", call_id = "call_fast", name = "fast", arguments = "{}", status = "completed", @async = true }
                };
                socket.Push(Created("resp_1"), JsonSerializer.Serialize(new { type = "response.completed", response = new { id = "resp_1", status = "completed", output = calls } }));
            }
            else if (count == 2) socket.Push(Created("resp_2"), Text("waiting "));
            else
            {
                createAfterSteer.TrySetResult();
                var outputs = payload.GetProperty("input").EnumerateArray().ToArray();
                Assert.IsTrue(outputs.Any(item => item.GetProperty("call_id").GetString() == "call_read"), "The slow required result must be included.");
                Assert.IsTrue(outputs.Any(item => item.GetProperty("call_id").GetString() == "call_fast"));
                socket.Push(Created("resp_3"), Text("done"), Completed("resp_3"));
            }
        };
        var service = new SocketService(socket);
        service.Functions.Add(new FunctionDefinition { Name = "slow", AllowAsync = true, Handler = async _ => { await slow.Task; return "slow value"; } });
        service.Functions.Add(new FunctionDefinition { Name = "fast", AllowAsync = true, Handler = async _ => { await fast.Task; fastFinished.TrySetResult(); return "fast value"; } });
        await using var run = await service.StartRunAsync("use tools", _ => ready.TrySetResult());
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await run.SteerAsync("revise").WaitAsync(TimeSpan.FromSeconds(5));
            fast.TrySetResult();
            await fastFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var early = await Task.WhenAny(createAfterSteer.Task, Task.Delay(50));
            Assert.AreNotSame(createAfterSteer.Task, early, "An unrelated ready result must not start the continuation.");
        }
        finally { fast.TrySetResult(); slow.TrySetResult(); }
        Assert.AreEqual("waiting done", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(3, creates);
    }

    [TestMethod]
    public async Task CanceledSteerWait_DoesNotLoseItsAcknowledgementOrCorruptTheNextSubmission()
    {
        using var socket = new ScriptedSocket();
        var ready = NewSignal();
        var firstSent = NewSignal();
        var secondSent = NewSignal();
        int steers = 0;
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.create") socket.Push(Created("resp_1"), Text("draft "));
            else if (Interlocked.Increment(ref steers) == 1) firstSent.TrySetResult();
            else
            {
                secondSent.TrySetResult();
                socket.Push(Accepted("resp_1", "steer_2"), Steered("resp_1"), Created("resp_2"), Text("done"), Completed("resp_2"));
            }
        };
        var service = new SocketService(socket);
        await using var run = await service.StartRunAsync("draft", _ => ready.TrySetResult());
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var first = run.SteerAsync("first", cancellation.Token);
        await firstSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        var second = run.SteerAsync("second");
        Assert.IsFalse(secondSent.Task.IsCompleted);
        socket.Push(Accepted("resp_1", "steer_1"));
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("draft done", (await run.Result.WaitAsync(TimeSpan.FromSeconds(5))).Text);
        Assert.AreEqual(2, steers);
    }

    [TestMethod]
    public async Task RejectedSteer_FailsWithoutReplay()
    {
        using var socket = new ScriptedSocket();
        var ready = NewSignal();
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.create") socket.Push(Created("resp_1"), Text("draft"));
            else socket.Push("{\"type\":\"response.steer.failed\",\"steer\":{\"previous_response_id\":\"resp_1\",\"input\":\"change\"},\"error\":{\"code\":\"steering_not_supported\"}}");
        };
        var service = new SocketService(socket);
        await using var run = await service.StartRunAsync("draft", _ => ready.TrySetResult());
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var failure = await Assert.ThrowsAsync<AIServiceException>(() => run.SteerAsync("change"));
        StringAssert.Contains(failure.ErrorDetails, "steering_not_supported");
        await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(2, socket.Sent.Count);
    }

    [TestMethod]
    public async Task DisconnectAfterAcceptedSteer_DoesNotReturnPartialSuccessOrReconnect()
    {
        using var socket = new ScriptedSocket();
        var ready = NewSignal();
        socket.OnSend = payload =>
        {
            if (Kind(payload) == "response.create") socket.Push(Created("resp_1"), Text("draft"));
            else { socket.Push(Accepted("resp_1", "steer_1"), Steered("resp_1")); socket.Disconnect(); }
        };
        var service = new SocketService(socket);
        await using var run = await service.StartRunAsync("draft", _ => ready.TrySetResult());
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await run.SteerAsync("change").WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<AIServiceException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, service.Connections);
    }

    [TestMethod]
    public async Task CancelWhileWaitingForSocket_StopsTheRun()
    {
        using var socket = new ScriptedSocket();
        var ready = NewSignal();
        socket.OnSend = _ => { socket.Push(Created("resp_1"), Text("draft")); };
        var service = new SocketService(socket);
        await using var run = await service.StartRunAsync("draft", _ => ready.TrySetResult());
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run.Result.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string? Kind(JsonElement payload) => payload.GetProperty("type").GetString();
    private static string Created(string id) => JsonSerializer.Serialize(new { type = "response.created", response = new { id, model = "gpt-6-astra", status = "in_progress" } });
    private static string Text(string text) => JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = text });
    private static string Completed(string id) => JsonSerializer.Serialize(new { type = "response.completed", response = new { id, status = "completed", output = Array.Empty<object>() } });
    private static string Steered(string id) => JsonSerializer.Serialize(new { type = "response.incomplete", response = new { id, status = "incomplete", incomplete_details = new { reason = "steered" }, output = Array.Empty<object>() } });
    private static string Accepted(string id, string steerId) => JsonSerializer.Serialize(new { type = "response.steer.accepted", steer = new { id = steerId, previous_response_id = id } });
    private static string Pending(string id) => JsonSerializer.Serialize(new { type = "response.steer.pending", steer = new { id = "steer_1", previous_response_id = id }, reason = "waiting_for_required_input", required_input = new[] { new { type = "function_call_output", call_id = "call_read" } } });
    private static string ToolCompleted(string id, bool asynchronous) => JsonSerializer.Serialize(new
    {
        type = "response.completed", response = new
        {
            id, status = "completed", output = new[] { new { type = "function_call", id = "fc_read", call_id = "call_read", name = "read_value", arguments = "{}", status = "completed", @async = asynchronous } }
        }
    });
    private static async Task ReadAsync(IAsyncEnumerable<StreamingContent> source, List<StreamingContent> items)
    { await foreach (var item in source) items.Add(item); }

    private sealed class SocketService : OpenAIService
    {
        private readonly ScriptedSocket _socket;
        public int Connections { get; private set; }
        public TaskCompletionSource WaitingForToolResults { get; } = NewSignal();
        public SocketService(ScriptedSocket socket) : base("test-key", AIModels.OpenAI.Gpt6Astra, new HttpClient(new NoHttpHandler()))
        { _socket = socket; DefaultPolicy = new FunctionCallingPolicy { MaxRounds = 8, TimeoutSeconds = 15 }; }
        protected override Task<WebSocket> ConnectRunWebSocketAsync(CancellationToken cancellationToken)
        { Connections++; return Task.FromResult<WebSocket>(_socket); }
        protected override Task<IReadOnlyList<FunctionCallResultBatch>> CollectPendingStreamingFunctionResultsAsync(CancellationToken cancellationToken)
        {
            WaitingForToolResults.TrySetResult();
            return base.CollectPendingStreamingFunctionResultsAsync(cancellationToken);
        }
    }

    private sealed class NoHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new AssertFailedException("An Astra run must use its WebSocket, not HTTP.");
    }

    private sealed class ScriptedSocket : WebSocket
    {
        private readonly Channel<byte[]?> _incoming = Channel.CreateUnbounded<byte[]?>();
        private readonly CancellationTokenSource _aborted = new();
        private WebSocketState _state = WebSocketState.Open;
        public List<JsonElement> Sent { get; } = new();
        public Action<JsonElement>? OnSend { get; set; }
        public void Push(params string[] events) { foreach (var item in events) _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(item)); }
        public void Disconnect() => _incoming.Writer.TryWrite(null);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() { _state = WebSocketState.Aborted; _aborted.Cancel(); }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) { Abort(); return Task.CompletedTask; }
        public override void Dispose() { Abort(); }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _aborted.Token);
            var frame = await _incoming.Reader.ReadAsync(linked.Token);
            if (frame == null) return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            Assert.IsTrue(frame.Length <= buffer.Count);
            frame.CopyTo(buffer.Array!, buffer.Offset);
            return new WebSocketReceiveResult(frame.Length, WebSocketMessageType.Text, true);
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(buffer.AsMemory());
            var payload = document.RootElement.Clone();
            Sent.Add(payload);
            OnSend?.Invoke(payload);
            return Task.CompletedTask;
        }
    }
}
