# Mythosia.AI.Mcp

Give an AI workflow access to tools your application already exposes through MCP (Model Context Protocol). Connect through the built-in stdio transport or a custom `IMcpTransport`; the integration discovers tools and registers `FunctionDefinition`s for providers that support local function calling.

## Current release: 0.1.1-preview

This preview patch targets **Mythosia.AI 8.1.0** and transitively **Mythosia.AI.Abstractions 4.1.0**. Existing MCP public APIs and lifecycle behavior remain unchanged; no additional source migration is required from 0.1.0-preview. See the [v0.1.1-preview release notes](https://github.com/AJ-comp/Mythosia.AI/blob/main/src/integrations/Mythosia.AI.Mcp/RELEASE_NOTES.md#v011-preview).

Registered MCP tools share the core execution loop, cancellation and failure contracts. Direct `CallToolAsync` reports `McpException` for `isError: true`. When upgrading from 0.0.1-preview, review the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) for the earlier error-handling and Run contract changes.

When a task needs progress display and a way to stop, keep an `AIRun` handle. `await run.Result` returns an `AIRunResult` containing the final text and reported execution details without requiring a stream reader; use `.Text` for the former string. Cancellation is cooperative and does not undo completed server actions. See the [Run result migration](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md#run-result).

Connection cleanup is coordinated across concurrent disposal calls. Malformed replies no longer lose a pending request, and calls made after disposal or reader shutdown fail instead of waiting indefinitely. The lifecycle behavior and cancellation limits are described below.

## Installation

```bash
dotnet add package Mythosia.AI.Mcp
```

## Quick Start

```csharp
using Mythosia.AI.Mcp;
using Mythosia.AI.Services.OpenAI;

var service = new OpenAIService(apiKey, httpClient);

// Connect to an MCP server (stdio transport)
await using var mcp = await service.WithMcpServerAsync(
    "npx", "-y @modelcontextprotocol/server-filesystem /workspace");

// AI now has access to all MCP tools automatically
var answer = await service.GetCompletionAsync("List all files in /workspace");
```

## Use MCP tools in a run

Registered MCP tools forward the execution cancellation token to `McpConnection.CallToolAsync`. Cancellation stops the client operation cooperatively; remote work stops only if the transport/server honors it. Run cleanup does not undo completed server actions. Tool errors and transport exceptions become failed tool results instead of successful `"Error: ..."` text. Direct `CallToolAsync` callers now receive `McpException` for an MCP result with `isError: true`; handle that exception when migrating.

A task that reads several files or calls multiple MCP tools benefits from visible progress and a way to stop. Keep a run handle to connect those controls while using the tools registered above.

```csharp
// Keep the MCP connection alive for every run that uses its tools.
await using var run = await service.WithMaxRounds(10).StartRunAsync(
    "List files in /workspace and explain the project layout.",
    onText: text => Console.Write(text),
    cancellationToken: cancellationToken);
string answer = (await run.Result).Text;
```

Registered MCP tools use the common function loop; no separate agent mode is required. The library executes tool calls automatically. A run does not own or dispose a shared `McpConnection`; dispose the run before disposing its connection. See the [run guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/execution-api-transition.md).

Concurrent asynchronous calls to `McpConnection.DisposeAsync()` wait for the same cleanup. The connection requests cancellation and closes its transport before waiting for the read loop to exit, so reads that need connection closure can finish. Cancellation callback failures do not skip transport cleanup; callback and cleanup errors are preserved together when necessary. Calling disposal again after cleanup has finished remains a no-op.

To avoid leaving late tool calls waiting during shutdown, once connection disposal starts, new `InitializeAsync`, `RefreshToolsAsync` and `CallToolAsync` operations are rejected with `ObjectDisposedException`. If a response has the matching request ID but a malformed body, it is skipped while the request remains pending: a later valid response, caller cancellation or connection cleanup can still settle the call. If the reader has already stopped because the server closed the stream or a transport read failed, new operations fail with `McpException` instead of waiting for a reply that cannot arrive; create a new connection to continue.

## Features

- **Automatic tool discovery** — `tools/list` is called on connect; each tool becomes a `FunctionDefinition`
- **Provider support** — MCP tools use the provider's client function-calling path. DeepSeek Flash and the Perplexity Agent adapter also support registered local functions.
- **Composable** — mix MCP tools with `[AiFunction]` local functions and `FunctionBuilder` definitions
- **Lifecycle management** — `IAsyncDisposable` pattern; `await using` shuts down the server cleanly
- **Tool filtering** — include only the tools you need with a name filter
- **Name prefixing** — avoid tool name collisions when connecting multiple MCP servers

## Usage

### Stdio Transport (most common)

```csharp
// Basic — command + args
await using var mcp = await service.WithMcpServerAsync(
    "npx", "-y @modelcontextprotocol/server-filesystem /workspace");

// With environment variables
await using var mcp = await service.WithMcpServerAsync(
    "python", "-m my_mcp_server",
    environmentVariables: new Dictionary<string, string>
    {
        ["API_KEY"] = "sk-..."
    });
```

### Tool Filtering

```csharp
// Only include specific tools
await using var mcp = await service.WithMcpServerAsync(
    "npx", "-y @modelcontextprotocol/server-github",
    toolFilter: name => name is "search_repositories" or "get_file_contents");
```

### Multiple MCP Servers

```csharp
// Connect to multiple servers — use namePrefix to avoid collisions
await using var fs = await service.WithMcpServerAsync(
    "npx", "-y @modelcontextprotocol/server-filesystem /workspace",
    namePrefix: "fs_");

await using var gh = await service.WithMcpServerAsync(
    "npx", "-y @modelcontextprotocol/server-github",
    namePrefix: "gh_");

// AI sees tools like: fs_read_file, fs_write_file, gh_search_repositories, etc.
```

### Custom Transport

```csharp
using Mythosia.AI.Mcp.Transports;

// Use any IMcpTransport implementation
var transport = new StdioTransport("node", "my-server.js");
await using var mcp = await service.WithMcpServerAsync(transport);
```

### Direct McpConnection Usage

```csharp
// For lower-level control without AIService integration
var transport = new StdioTransport("npx", "-y @modelcontextprotocol/server-filesystem /tmp");
var connection = new McpConnection(transport);
await connection.InitializeAsync();

// Inspect discovered tools
foreach (var tool in connection.Tools)
    Console.WriteLine($"{tool.Name}: {tool.Description}");

// Call a tool directly
var result = await connection.CallToolAsync("read_file", 
    new Dictionary<string, object> { ["path"] = "/tmp/hello.txt" });

// Convert to FunctionDefinitions manually
var functions = McpToolAdapter.ToFunctionDefinitions(connection);
```

## Architecture

```
Mythosia.AI.Abstractions   ← FunctionDefinition, FunctionParameters
        ↑
    Mythosia.AI             ← AIService, provider implementations
        ↑
  Mythosia.AI.Mcp           ← McpConnection, StdioTransport, McpToolAdapter
```

MCP tools are converted to `FunctionDefinition` at connection time. The existing function calling infrastructure (ReAct loop, tool execution) handles the rest — no changes needed in the core package.

When deciding which model controls to show alongside MCP tools, inspect the concrete AI service’s model capabilities. Native asynchronous LLM tool support is distinct from asynchronous local or MCP handlers; model capability inspection does not probe an MCP server. [Capability guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/model-capabilities.md).

## Supported MCP Protocol Features

| Feature | Status |
|---|---|
| `tools/list` (tool discovery) | Supported |
| `tools/call` (tool execution) | Supported |
| Stdio transport | Supported |
| SSE transport | Planned |
| Streamable HTTP transport | Planned |
| `resources/*` | Planned |
| `prompts/*` | Planned |

## Links

- [Mythosia.AI Documentation](https://aj-comp.github.io/Mythosia.AI/)
- [MCP Specification](https://spec.modelcontextprotocol.io/)
- [GitHub](https://github.com/AJ-comp/Mythosia.AI)
