using System.Text.Json;

namespace Mythosia.AI.Mcp.Tests;

[TestClass]
public class McpConnectionTests
{
    #region Initialize

    [TestMethod]
    public async Task InitializeAsync_SendsHandshakeAndDiscoverTools()
    {
        // Arrange
        var transport = new MockTransport();

        // Server will respond to: initialize (id=1), tools/list (id=2)
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { tools = new { } },
            serverInfo = new { name = "test-server", version = "1.0.0" }
        });
        transport.EnqueueResult(2, new
        {
            tools = new[]
            {
                new
                {
                    name = "read_file",
                    description = "Reads a file",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { path = new { type = "string", description = "File path" } },
                        required = new[] { "path" }
                    }
                }
            }
        });

        await using var connection = new McpConnection(transport);

        // Act
        await connection.InitializeAsync();

        // Assert
        Assert.AreEqual("test-server", connection.ServerName);
        Assert.AreEqual("1.0.0", connection.ServerVersion);
        Assert.AreEqual(1, connection.Tools.Count);
        Assert.AreEqual("read_file", connection.Tools[0].Name);
        Assert.AreEqual("Reads a file", connection.Tools[0].Description);
    }

    [TestMethod]
    public async Task InitializeAsync_SendsCorrectJsonRpcMessages()
    {
        var transport = new MockTransport();
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            serverInfo = new { name = "test", version = "1.0" }
        });
        transport.EnqueueResult(2, new { tools = Array.Empty<object>() });

        await using var connection = new McpConnection(transport);
        await connection.InitializeAsync();

        // Should have sent 3 messages: initialize request, initialized notification, tools/list request
        Assert.AreEqual(3, transport.SentMessages.Count);

        // Verify initialize request
        transport.SentMessages.TryDequeue(out var initMsg);
        var initDoc = JsonDocument.Parse(initMsg!);
        Assert.AreEqual("initialize", initDoc.RootElement.GetProperty("method").GetString());
        Assert.AreEqual(1, initDoc.RootElement.GetProperty("id").GetInt32());
        Assert.AreEqual("2.0", initDoc.RootElement.GetProperty("jsonrpc").GetString());

        // Verify initialized notification (no id)
        transport.SentMessages.TryDequeue(out var notifMsg);
        var notifDoc = JsonDocument.Parse(notifMsg!);
        Assert.AreEqual("notifications/initialized", notifDoc.RootElement.GetProperty("method").GetString());

        // Verify tools/list request
        transport.SentMessages.TryDequeue(out var toolsMsg);
        var toolsDoc = JsonDocument.Parse(toolsMsg!);
        Assert.AreEqual("tools/list", toolsDoc.RootElement.GetProperty("method").GetString());
        Assert.AreEqual(2, toolsDoc.RootElement.GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task InitializeAsync_ThrowsOnServerError()
    {
        var transport = new MockTransport();
        transport.EnqueueError(1, -32600, "Invalid request");

        await using var connection = new McpConnection(transport);

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => connection.InitializeAsync());
        Assert.IsTrue(ex.Message.Contains("Invalid request"));
    }

    #endregion

    #region CallToolAsync

    [TestMethod]
    public async Task CallToolAsync_ReturnsTextContent()
    {
        var transport = new MockTransport();
        // initialize + tools/list
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = "test", version = "1.0" }
        });
        transport.EnqueueResult(2, new { tools = Array.Empty<object>() });

        await using var connection = new McpConnection(transport);
        await connection.InitializeAsync();

        // tools/call response (id=3)
        transport.EnqueueResult(3, new
        {
            content = new[]
            {
                new { type = "text", text = "Hello from MCP!" }
            }
        });

        var result = await connection.CallToolAsync("greet");
        Assert.AreEqual("Hello from MCP!", result);
    }

    [TestMethod]
    public async Task CallToolAsync_ConcatenatesMultipleTextBlocks()
    {
        var transport = new MockTransport();
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = "test", version = "1.0" }
        });
        transport.EnqueueResult(2, new { tools = Array.Empty<object>() });

        await using var connection = new McpConnection(transport);
        await connection.InitializeAsync();

        transport.EnqueueResult(3, new
        {
            content = new[]
            {
                new { type = "text", text = "Line 1" },
                new { type = "text", text = "Line 2" }
            }
        });

        var result = await connection.CallToolAsync("multi_output");
        Assert.AreEqual("Line 1\nLine 2", result);
    }

    [TestMethod]
    public async Task CallToolAsync_ReturnsErrorStringOnToolError()
    {
        var transport = new MockTransport();
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = "test", version = "1.0" }
        });
        transport.EnqueueResult(2, new { tools = Array.Empty<object>() });

        await using var connection = new McpConnection(transport);
        await connection.InitializeAsync();

        transport.EnqueueResult(3, new
        {
            isError = true,
            content = new[]
            {
                new { type = "text", text = "File not found" }
            }
        });

        var result = await connection.CallToolAsync("read_file", new Dictionary<string, object> { ["path"] = "/missing" });
        Assert.IsTrue(result.Contains("File not found"));
    }

    [TestMethod]
    public async Task CallToolAsync_ThrowsOnJsonRpcError()
    {
        var transport = new MockTransport();
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = "test", version = "1.0" }
        });
        transport.EnqueueResult(2, new { tools = Array.Empty<object>() });

        await using var connection = new McpConnection(transport);
        await connection.InitializeAsync();

        transport.EnqueueError(3, -32601, "Method not found");

        var ex = await Assert.ThrowsExactlyAsync<McpException>(
            () => connection.CallToolAsync("nonexistent"));
        Assert.IsTrue(ex.Message.Contains("Method not found"));
    }

    #endregion

    #region Server Notifications

    [TestMethod]
    public async Task ReadLoop_IgnoresServerNotifications()
    {
        var transport = new MockTransport();

        // initialize response
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = "test", version = "1.0" }
        });

        // Server sends a notification before tools/list response
        transport.EnqueueNotification("notifications/tools/list_changed");

        // tools/list response
        transport.EnqueueResult(2, new
        {
            tools = new[]
            {
                new { name = "tool_a", description = "Tool A" }
            }
        });

        await using var connection = new McpConnection(transport);
        await connection.InitializeAsync();

        // Should have successfully parsed past the notification
        Assert.AreEqual(1, connection.Tools.Count);
        Assert.AreEqual("tool_a", connection.Tools[0].Name);
    }

    #endregion

    #region RefreshToolsAsync

    [TestMethod]
    public async Task RefreshToolsAsync_UpdatesToolList()
    {
        var transport = new MockTransport();
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = "test", version = "1.0" }
        });
        transport.EnqueueResult(2, new
        {
            tools = new[] { new { name = "tool_v1", description = "Original" } }
        });

        await using var connection = new McpConnection(transport);
        await connection.InitializeAsync();
        Assert.AreEqual(1, connection.Tools.Count);
        Assert.AreEqual("tool_v1", connection.Tools[0].Name);

        // Refresh — server now returns different tools (id=3)
        transport.EnqueueResult(3, new
        {
            tools = new[]
            {
                new { name = "tool_v2a", description = "New A" },
                new { name = "tool_v2b", description = "New B" }
            }
        });

        await connection.RefreshToolsAsync();
        Assert.AreEqual(2, connection.Tools.Count);
        Assert.AreEqual("tool_v2a", connection.Tools[0].Name);
        Assert.AreEqual("tool_v2b", connection.Tools[1].Name);
    }

    #endregion
}
