# Conversation Summary Mechanism

## Overview

An automatic summarization mechanism for managing token costs and context window limits in long conversations.
When `SummaryConversationPolicy` detects a trigger condition, it compresses old messages into summary text and retains only recent messages.

## Core Design Principles

1. **Summary timing**: Trigger-based summarization fires only after all function-calling rounds complete, never mid-chain. Context-overflow recovery is the one exception, and even there the current question and earlier rounds' results are preserved (see [Context-overflow recovery](#context-overflow-recovery-a-different-path-from-trigger-summarization))
2. **Summary policy is API-agnostic**: `GetMessagesToSummarize` trims by rules only. API constraints like user-first are handled by each provider
3. **Original immutability**: Summary text is stored in `CurrentSummary` and injected into the system prompt. API request message lists are copies

## Trigger Conditions

```csharp
// Message count based
var policy = SummaryConversationPolicy.ByMessage(triggerCount: 10, keepRecentCount: 4);

// Token count based (uses actual API-returned tokens)
var policy = SummaryConversationPolicy.ByToken(triggerTokens: 8000, keepRecentTokens: 2000);

// Both (OR condition)
var policy = SummaryConversationPolicy.ByBoth(triggerTokens: 8000, triggerCount: 20, ...);
```

Token-based triggers prefer the official `InputTokens` value (`LastKnownInputTokens`) returned by the API.
Falls back to local estimation (`EstimateTokens`) only when no actual value is available.

## Complete Flow (with Function Calling)

### Setup

```
triggerCount=3, keepRecentCount=2
Functions: get_user_id, get_user_details
```

### Step 1: User question

```
StreamAsync(User("Get details for john_doe"))

ActivateChat.Messages:
  [0] User: "Get details for john_doe"
```

### Step 2: Round 0 — First function call

LLM decides to call `get_user_id("john_doe")`. After execution:

```
ActivateChat.Messages:
  [0] User: "Get details for john_doe"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
```

`hasFunctionResult = true` → next round

### Step 3: Round 1 — Second function call

LLM calls `get_user_details("user_123")`. After execution:

```
ActivateChat.Messages:
  [0] User: "Get details for john_doe"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, name: Test User, email: test@example.com}"
```

`hasFunctionResult = true` → next round

### Step 4: Round 2 — Final text response

LLM synthesizes function results into a text response:

```
ActivateChat.Messages:
  [0] User: "Get details for john_doe"
  [1] Assistant: function_call(get_user_id)
  [2] Function: "user_123"
  [3] Assistant: function_call(get_user_details)
  [4] Function: "{id: user_123, ...}"
  [5] Assistant: "Here are the details for john_doe..."
```

`hasFunctionResult = false` → all rounds complete

### Step 5: Post-streaming summarization

```
ShouldSummarize: 6 messages > triggerCount(3) → triggered!

GetMessagesToSummarize:
  keepFromIndex = 6 - 2 = 4
  To summarize: [0]~[3] (User, Asst(FC), Func, Asst(FC))
  To keep:      [4]~[5] (Function, Assistant)
```

After summary generation and message removal:

```
CurrentSummary = "User requested john_doe details. get_user_id returned user_123, then get_user_details retrieved full profile."

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "Here are the details for john_doe..."

SystemMessage:
  "You are a helpful assistant.

  [Previous conversation summary]
  User requested john_doe details. get_user_id returned user_123, then get_user_details retrieved full profile."
```

### Step 6: Next user question

```
StreamAsync(User("What's the email?"))

ActivateChat.Messages:
  [0] Function: "{id: user_123, ...}"
  [1] Assistant: "Here are the details for john_doe..."
  [2] User: "What's the email?"
```

During API request building, `EnsureUserFirstMessage` applies:

```
messages[0] = Function → not User → synthetic User inserted

Messages sent to API:
  [0] User: "(Continuing from previous conversation context)"  ← synthetic
  [1] Function: "{id: user_123, ...}"
  [2] Assistant: "Here are the details for john_doe..."
  [3] User: "What's the email?"
```

## User-First Constraint Handling

Some APIs (Gemini, Claude) require the message array to start with a User role.
After summary trimming, the first message may be Assistant/Function, so this is handled in each provider's request builder.

```csharp
// AIService.cs
protected static void EnsureUserFirstMessage(List<Message> messages)
{
    if (messages.Count == 0) return;
    if (messages[0].Role == ActorRole.User) return;
    messages.Insert(0, new Message(ActorRole.User,
        "(Continuing from previous conversation context)"));
}
```

- **Applied to**: Gemini, Claude (4 request builder methods)
- **Not applied to**: OpenAI, Grok, DeepSeek, Sonar, Qwen (no user-first constraint)
- **Original immutability**: Applied only to a copy created via `GetLatestMessages().ToList()`

## Why Summarization Fires After Round Completion

```
X Mid-round summarization:
  Round 0: FC call → result saved
  Round 1: [summary fires here] → FC results deleted! → LLM loses context

O Post-completion summarization:
  Round 0: FC call → result saved
  Round 1: FC call → result saved
  Round 2: LLM generates text using all FC results (complete)
  [summary fires here] → cleanup for next turn, no impact on current response
```

## Context-overflow recovery: a different path from trigger summarization

Trigger-based summarization is housekeeping for the *next* turn, so it has no reason to run mid-chain.
But when the server rejects **round 3** with "context length exceeded", waiting is not an option: without
shrinking the request right now, this turn simply fails.

So recovery compaction does run inside the round loop. What the X case above worries about — deleting the
function-call results — is prevented by clamping the cut point no further than the **last user message**.

```
Round 0: FC call → result saved
Round 1: FC call → result saved
Round 2: [server rejects with 400]
         → fold only the history that precedes the current question
         → the current question and rounds 0-1's FC results stay
         → replay round 2 alone (rounds 0-1 are not re-run, so no tool runs twice)
```

When there is no older history to fold, it gives up **without even issuing the summary request** —
deleting anyway would not shrink the request and would cost history for nothing. Three guards stop it:

| Reason | Meaning |
|---|---|
| `nothing-to-cut` | Nothing before the current question to remove |
| `window-clipped` | What would be cut is already outside the `MaxMessageCount` window, so removing it changes nothing |
| `retries-exhausted` | `ContextRecoveryMaxRetries` used up |

In all three the original error propagates with **no summary call and no deletion**.

> **Non-streaming differs.** A retry there re-enters the provider's round loop from zero, so a tool that
> already ran would run again. Recovery therefore stops with the reason `tool-side-effects` in that case.
> Per-round replay exists only on the streaming path.
