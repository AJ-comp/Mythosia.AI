# Mythosia.AI.Mcp

MCP (Model Context Protocol) client integration for [Mythosia.AI](https://github.com/AJ-comp/Mythosia.AI).  
Connect to any MCP server and automatically register its tools as `FunctionDefinition`s — usable by **all** AI providers (OpenAI, Anthropic, Google, xAI, DeepSeek, etc.).

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

## Features

- **Automatic tool discovery** — `tools/list` is called on connect; each tool becomes a `FunctionDefinition`
- **Provider-agnostic** — MCP tools work with OpenAI, Anthropic, Google, xAI, DeepSeek, Perplexity
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
