# Basic Completions

For independent settings and reusable variations, use [the request builder](request-building.md). Call `CreateRequest(...)` before `With...`; service-level setters and fluent methods retain their existing behavior.

Use `GetCompletionAsync` when your application sends a question and processes the answer after work finishes. Typed and RAG overloads remain supported. If you also need progress or control while work is ongoing, use the selection advice in the [Run guide](execution-api-transition.md).

<a id="completion-cancellation"></a>

## Cancel an answer that is no longer needed

When a user closes a screen, presses Stop, or the application reaches its waiting limit, the answer may no longer be useful. Pass a `CancellationToken` to stop the current client operation and avoid unnecessary tool calls and later model rounds. A completed answer can still be awaited with `GetCompletionAsync`; cancellation alone does not require a Run.

### Before: no cancellation signal from the caller

```csharp
string answer = await service.CreateRequest("Summarize this document.")
    .GetCompletionAsync();
```

### After: cancel from the caller or after 30 seconds

```csharp
using System;
using System.Threading;

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    string answer = await service.CreateRequest("Summarize this document.")
        .GetCompletionAsync(cancellationToken: cancellation.Token);
    Console.WriteLine(answer);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Cancelled.");
}
```

Keep the token source while the call is running; connect a Stop button or screen-close event to `cancellation.Cancel()`. The example also schedules cancellation after 30 seconds. Caller cancellation raises `OperationCanceledException` after cleanup. This includes a deadline set with `CancellationTokenSource`; the existing `FunctionCallingPolicy.TimeoutSeconds` policy retains its timeout-error behavior.

The service overloads for string and `Message`, typed service completion, the request builder, and `MessageChain.SendAsync` / `SendOnceAsync` accept the token. Existing calls that omit it keep working. These are alternative entry points:

```csharp
using System.Collections.Generic;
using System.Threading;
using Mythosia.AI.Extensions;

using var cancellation = new CancellationTokenSource();
CancellationToken token = cancellation.Token;

string answer = await service.GetCompletionAsync(
    "Summarize this document.", cancellationToken: token);

Dictionary<string, string> data = await service.GetCompletionAsync<Dictionary<string, string>>(
    "Return the title and author as JSON.", cancellationToken: token);

string messageAnswer = await service.BeginMessage().AddText("Summarize this document.")
    .SendAsync(cancellationToken: token);

string oneOffAnswer = await service.BeginMessage().AddText("Translate this sentence.")
    .SendOnceAsync(cancellationToken: token);
```

The token reaches request preparation, HTTP send/read, cooperative local tools, and later model rounds. Queued tools and future rounds are skipped once cancellation is observed. Cleanup keeps recorded tool calls and results paired; a started tool that ignores its token can delay cleanup. Cancellation does not undo completed actions or erase conversation history. See the [tool contract](function-calling.md#tool-execution-contract).

This contract does not promise that the provider stops inference or billing. OpenAI documents terminating the connection for ordinary foreground Responses; Google explicitly describes its abort signal as client-only and applicable usage remains charged. See [OpenAI Responses](https://developers.openai.com/api/docs/guides/background#limits) and [Google GenerateContentConfig.abortSignal](https://googleapis.github.io/js-genai/release_docs/interfaces/types.GenerateContentConfig.html#abortsignal). A background job needs its explicit `CancelAsync()`; cancelling `WaitForCompletionAsync(cancellationToken: ...)` only stops waiting. Ordinary completion is not converted into background execution. See [Perplexity background jobs](perplexity.md).

<a id="completion-cancellation-migration"></a>

This cancellation addition belongs to Mythosia.AI 8.0.0 / Abstractions 4.0.0. Application source calls that omit the token remain valid, including positional profile/context arguments, but consumers must rebuild. Custom `IAIService` implementations must append `CancellationToken cancellationToken = default` to both completion signatures and propagate it. Custom `AIService` providers retain their existing `GetCompletionAsync(Message)` override and must forward the protected `RequestCancellationToken` into their transport. Builder and Run capabilities alone did not require that interface change. Subclasses that override changed public virtual overloads for string/profile/context completion, image helpers, or `RunAgentAsync` must also append and forward the new `CancellationToken`; only the single-`Message` provider override retains its old signature. Method-group delegates targeting a changed signature may need an explicit lambda that passes or omits the token.

## Single Turn

The simplest usage — send a message, get a response:

```csharp
var response = await service.GetCompletionAsync("What is the capital of France?");
Console.WriteLine(response); // Paris
```

## System Prompt

Set a system prompt to give the model a persona or instructions:

```csharp
service.SystemMessage = "You are a concise assistant. Answer in one sentence.";

var response = await service.GetCompletionAsync("Explain recursion.");
```

## Multi-Turn Conversation

Messages are accumulated automatically. Each call to `GetCompletionAsync` appends to the conversation history:

```csharp
await service.GetCompletionAsync("My name is Alice.");
var response = await service.GetCompletionAsync("What is my name?");
// → "Your name is Alice."
```

To clear the conversation history:

```csharp
service.ActivateChat.ClearMessages();
```

## Building Messages Manually

Use `MessageBuilder` to construct messages explicitly:

```csharp
using Mythosia.AI.Builders;

var message = MessageBuilder.Create().AddText("Summarize this text: ...")
    .Build();

var response = await service.GetCompletionAsync(message);
```

## Multimodal (Image Input)

Providers that support vision accept image content alongside text:

```csharp
var imageBytes = await File.ReadAllBytesAsync("diagram.png");

var message = MessageBuilder.Create().AddText("What does this diagram show?")
    .AddImage(imageBytes, "image/png")
    .Build();

var response = await service.GetCompletionAsync(message);
```

For chart and screenshot analysis, local tool calls, or a quick answer followed by deeper review, use [DeepSeek Flash](providers.md#deepseek-deepseekservice) (`AIModels.DeepSeek.Flash`, V4.1 Flash). Thinking stays off by default; enable it with `WithDeepSeekReasoning(...)` or per-request `WithReasoning(...)`.

## Quick Ask (Static API)

For one-off queries without constructing a service instance, use the static `QuickAskAsync`. The provider is auto-detected from the model name:

```csharp
string answer = await AIService.QuickAskAsync(
    apiKey: "sk-...",
    prompt: "What is the capital of France?",
    model: AIModels.OpenAI.Gpt4oMini  // default
);
```

Image variant:

```csharp
string description = await AIService.QuickAskWithImageAsync(
    apiKey: "sk-...",
    prompt: "Describe this image",
    imagePath: "photo.jpg",
    model: AIModels.OpenAI.Gpt4_1
);
```

## Image Convenience Methods

Analyse images without `MessageBuilder` — the service reads the file and resolves the MIME type automatically:

```csharp
// From file path
var response = await service.GetCompletionWithImageAsync(
    "What does this diagram show?", "diagram.png");

// From URL
var response = await service.GetCompletionWithImageUrlAsync(
    "Describe this photo", "https://example.com/photo.jpg");
```

## Retry Last Message

Remove the last assistant response and resend the last user message:

```csharp
string regenerated = await service.RetryLastMessageAsync();
```

Useful when the previous response was unsatisfactory and you want the model to try again.

## Token Counting

Estimate token usage before sending a request. Available on **all providers**:

```csharp
// Count tokens for the current conversation history
uint conversationTokens = await service.GetInputTokenCountAsync();

// Count tokens for a specific prompt
uint promptTokens = await service.GetInputTokenCountAsync("Your prompt here");
```

OpenAI and most providers use local TikToken-based estimation. Anthropic and Google call their native token counting APIs for exact results.

## Fluent Message Chain

`BeginMessage()` provides a fluent API for building and sending messages in a single chain — including text, images, streaming, and policy configuration:

```csharp
// Simple text + image → send
string response = await service.BeginMessage()
    .AddText("What does this diagram show?")
    .AddImage("diagram.png")
    .SendAsync();

// One-off query (no conversation history)
string answer = await service.BeginMessage()
    .AddText("Translate this to Korean")
    .SendOnceAsync();

// Streaming
await service.BeginMessage()
    .AddText("Write a poem about spring")
    .StreamAsync(chunk => Console.Write(chunk));

// With custom timeout and policy
string result = await service.BeginMessage()
    .AddText("Analyze this image")
    .AddImageUrl("https://example.com/photo.jpg")
    .WithHighDetail()
    .WithTimeout(90)
    .SendAsync();
```

`StreamAsync()` also supports `IAsyncEnumerable`:

```csharp
await foreach (var chunk in service.BeginMessage().AddText("Tell me a story").StreamAsync())
    Console.Write(chunk);
```

## Controlling Output Length and Temperature

```csharp
service.MaxTokens = 512;
service.Temperature = 0.2f;  // lower = more deterministic
```

Perplexity: [Answer with an Agent preset / Sources, images, and structured answers](perplexity.md).
