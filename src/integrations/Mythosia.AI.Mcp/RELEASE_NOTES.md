# Release Notes

## v0.0.1-preview

### MCP Client Integration (Initial Preview)

- **McpConnection** — MCP server lifecycle management with `IAsyncDisposable`
  - `InitializeAsync()` — protocol handshake (protocol version `2024-11-05`)
  - `RefreshToolsAsync()` — re-fetch tool list from server
  - `CallToolAsync()` — invoke an MCP tool with arguments
- **StdioTransport** — launch MCP servers as child processes, communicate via stdin/stdout
  - Supports custom working directory and environment variables
- **McpToolAdapter** — converts MCP tools into `FunctionDefinition` instances
  - JSON Schema `inputSchema` → `FunctionParameters` / `ParameterProperty` mapping
  - Tool filtering by name
  - Name prefixing to avoid collisions across multiple servers
- **McpServiceExtensions** — `AIService.WithMcpServerAsync()` extension methods
  - Stdio overload: `WithMcpServerAsync(command, args, ...)`
  - Custom transport overload: `WithMcpServerAsync(IMcpTransport, ...)`
- **IMcpTransport** — transport abstraction for future SSE / Streamable HTTP support
