# Release Notes

## v0.1.1-preview

### Changed

- Rebuilds the MCP integration against `Mythosia.AI` 8.1.0 and transitively `Mythosia.AI.Abstractions` 4.1.0, using the updated core execution implementation.

### Compatibility

- Existing tool registration, cancellation, failure and connection-lifecycle contracts remain unchanged. No additional source migration is required from 0.1.0-preview. This package remains a preview.

## v0.1.0-preview

> This prerelease targets Mythosia.AI 8.0.0. Rebuild consumers and review the [v8 migration guide](https://github.com/AJ-comp/Mythosia.AI/blob/main/docs/v8-migration.md) for shared contracts and MCP error handling.

### Changed

- Malformed response bodies no longer remove the matching pending call before successful deserialization; a later valid response, caller cancellation or connection cleanup can still settle it. New operations fail after disposal starts or the reader ends, instead of registering calls behind a stopped reader. Request registration and reader shutdown share the lifecycle gate.

- Concurrent asynchronous `DisposeAsync` callers now await the same in-progress cleanup. Disposal closes the transport before waiting for its read loop, allowing reads that require transport closure to finish; subsequent calls after completed cleanup remain no-ops.

- Connection disposal attempts transport cleanup even when a read-cancellation callback throws. Cancellation and transport cleanup failures are retained together; disposal no longer skips the owned transport on that path.

- Registered MCP tools forward the local execution cancellation token to `CallToolAsync`; the client can stop waiting cooperatively, while remote cancellation depends on transport/server support.
- MCP tool failures and transport exceptions propagate to the shared executor as failed results. A direct `McpConnection.CallToolAsync` now throws `McpException` for `isError: true` instead of returning an error string. Existing direct callers that consumed that string must handle the exception.

### Compatibility

- Requires `Mythosia.AI` 8.0.0 and its updated completion, Run-result and tool contracts. MCP remains a preview package; `CallToolAsync` callers that previously consumed tool error text must handle `McpException`. The built-in transport is stdio; other transports require an `IMcpTransport` implementation.

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
