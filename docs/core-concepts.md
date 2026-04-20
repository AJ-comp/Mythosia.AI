# Core Concepts

This page collects foundational concepts you will see referenced across the rest of the documentation. More concepts will be added here over time.

## What Is a Round?

> [!NOTE]
> A **round** is one call-and-reply trip between your app and the model — your app sends a prompt, the model responds, and that single exchange is one round. A plain chat message is 1 round. Function calling and agents can chain multiple rounds together for a single user message.

### The simplest case: 1 round

For a normal chat message, the entire conversation is one round.

```
app  →  "What is 2 + 2?"       →  model
app  ←  "It is 4."              ←  model
```

`RoundUsage` fires once with this round's tokens. `Completion.Usage` fires at stream end with the same total, because there is only one round.

### Multiple rounds: function calling

Rounds multiply when the model cannot answer on its own. Say a user asks *"What is the weather in Seoul right now?"* — the model has no access to live weather, so it has to call a tool.

**Round 1 — the model decides to call a tool**

Your app sends the user message plus the list of registered tools (e.g. `GetWeather`). The model sees this conversation:

```
system: You are a weather assistant. You can call GetWeather(city).
user:   What is the weather in Seoul right now?
```

Instead of writing a final answer, the model returns a **tool-call request**:

```
tool_call: GetWeather(city="Seoul")
```

The model's turn ends and so does round 1. `RoundUsage` fires with the tokens consumed in round 1. **There is no final user-facing answer yet.**

**Between rounds — your app runs the function**

This step is **not** an LLM call. The Mythosia.AI runtime invokes your registered `GetWeather` implementation and receives `"15°C, cloudy"`. No tokens are consumed.

**Round 2 — the model writes the final answer**

Your app appends the tool result to the conversation and calls the model **a second time**. The model now sees:

```
system:      You are a weather assistant. You can call GetWeather(city).
user:        What is the weather in Seoul right now?
assistant:   [called GetWeather(city="Seoul")]
tool_result: 15°C, cloudy
```

With the information it needed, the model writes plain text:

```
It is currently 15°C and cloudy in Seoul.
```

Round 2 ends. `RoundUsage` fires a second time — with round 2's tokens only (the input is usually larger than round 1's because the conversation is now longer). When the stream closes, `Completion.Usage` fires once with the **sum of round 1 and round 2**.

### At a glance

| Step | LLM call? | What happens | Event |
|---|---|---|---|
| Round 1 | ✅ | Model decides to call `GetWeather` | `RoundUsage` (`RoundIndex=1`) |
| Between rounds | ❌ | Your app runs the function, gets `"15°C, cloudy"` | `FunctionCall`, `FunctionResult` |
| Round 2 | ✅ | Model sees the result and writes the final answer | `RoundUsage` (`RoundIndex=2`, `IsFinalRound=true`) |
| Stream ends | — | — | `Completion` (Usage = round 1 + round 2) |

### More tools mean more rounds

If the model needs to chain multiple tool calls, rounds add up. For *"Compare the weather in Seoul and Tokyo"*:

1. **Round 1** — model calls `GetWeather("Seoul")`
2. App executes it → `"15°C, cloudy"`
3. **Round 2** — model sees the result and also calls `GetWeather("Tokyo")`
4. App executes it → `"18°C, sunny"`
5. **Round 3** — model combines both results into the final answer

Three rounds in total, and `Completion.Usage` sums all three. A UI context-size meter should use the last round's `RoundUsage.TotalTokens` — in this example, round 3's value.
