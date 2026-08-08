using System.Text.Json;
using Mythosia.AI.Models.Functions;

namespace Mythosia.AI.Mcp.Tests;

[TestClass]
public class McpToolAdapterTests
{
    #region Helper

    private static async Task<McpConnection> CreateInitializedConnectionAsync(
        MockTransport transport, params object[] tools)
    {
        transport.EnqueueResult(1, new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = "test", version = "1.0" }
        });
        transport.EnqueueResult(2, new { tools });

        var connection = new McpConnection(transport);
        await connection.InitializeAsync();
        return connection;
    }

    #endregion

    #region ToFunctionDefinitions

    [TestMethod]
    public async Task ToFunctionDefinitions_ConvertsAllTools()
    {
        var transport = new MockTransport();
        await using var connection = await CreateInitializedConnectionAsync(transport,
            new { name = "tool_a", description = "Tool A" },
            new { name = "tool_b", description = "Tool B" });

        var functions = McpToolAdapter.ToFunctionDefinitions(connection);

        Assert.AreEqual(2, functions.Count);
        Assert.AreEqual("tool_a", functions[0].Name);
        Assert.AreEqual("Tool A", functions[0].Description);
        Assert.AreEqual("tool_b", functions[1].Name);
        Assert.AreEqual("Tool B", functions[1].Description);
    }

    [TestMethod]
    public async Task ToFunctionDefinitions_AppliesToolFilter()
    {
        var transport = new MockTransport();
        await using var connection = await CreateInitializedConnectionAsync(transport,
            new { name = "read_file", description = "Read" },
            new { name = "write_file", description = "Write" },
            new { name = "delete_file", description = "Delete" });

        var functions = McpToolAdapter.ToFunctionDefinitions(
            connection, toolFilter: name => name == "read_file");

        Assert.AreEqual(1, functions.Count);
        Assert.AreEqual("read_file", functions[0].Name);
    }

    [TestMethod]
    public async Task ToFunctionDefinitions_AppliesNamePrefix()
    {
        var transport = new MockTransport();
        await using var connection = await CreateInitializedConnectionAsync(transport,
            new { name = "search", description = "Search" });

        var functions = McpToolAdapter.ToFunctionDefinitions(
            connection, namePrefix: "gh_");

        Assert.AreEqual(1, functions.Count);
        Assert.AreEqual("gh_search", functions[0].Name);
    }

    [TestMethod]
    public async Task ToFunctionDefinitions_HandlerCallsToolOnServer()
    {
        var transport = new MockTransport();
        await using var connection = await CreateInitializedConnectionAsync(transport,
            new { name = "greet", description = "Greet someone" });

        var functions = McpToolAdapter.ToFunctionDefinitions(connection);
        Assert.IsNotNull(functions[0].Handler);

        // Enqueue tool call response (next id = 3)
        transport.EnqueueResult(3, new
        {
            content = new[] { new { type = "text", text = "Hello, World!" } }
        });

        var result = await functions[0].Handler!(new Dictionary<string, object>());
        Assert.AreEqual("Hello, World!", result);
    }

    [TestMethod]
    public async Task ToFunctionDefinitions_HandlerReturnsErrorOnFailure()
    {
        var transport = new MockTransport();
        await using var connection = await CreateInitializedConnectionAsync(transport,
            new { name = "fail_tool", description = "Always fails" });

        var functions = McpToolAdapter.ToFunctionDefinitions(connection);
        Assert.IsNotNull(functions[0].Handler);

        transport.EnqueueError(3, -32000, "Internal error");

        var result = await functions[0].Handler!(new Dictionary<string, object>());
        Assert.IsTrue(result.StartsWith("Error:"));
    }

    [TestMethod]
    public void ToFunctionDefinitions_ThrowsOnNullConnection()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => McpToolAdapter.ToFunctionDefinitions(null!));
    }

    #endregion

    #region ConvertInputSchema

    [TestMethod]
    public void ConvertInputSchema_ParsesPropertiesAndRequired()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path" },
                "encoding": { "type": "string", "description": "Encoding", "enum": ["utf-8", "ascii"] }
            },
            "required": ["path"]
        }
        """).RootElement;

        var result = McpToolAdapter.ConvertInputSchema(schema);

        Assert.AreEqual("object", result.Type);
        Assert.AreEqual(2, result.Properties.Count);
        Assert.AreEqual(1, result.Required.Count);
        Assert.AreEqual("path", result.Required[0]);

        Assert.AreEqual("string", result.Properties["path"].Type);
        Assert.AreEqual("File path", result.Properties["path"].Description);

        Assert.AreEqual("string", result.Properties["encoding"].Type);
        Assert.IsNotNull(result.Properties["encoding"].Enum);
        Assert.AreEqual(2, result.Properties["encoding"].Enum!.Count);
        Assert.AreEqual("utf-8", result.Properties["encoding"].Enum![0]);
    }

    [TestMethod]
    public void ConvertInputSchema_ParsesDefaultValues()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "count": { "type": "integer", "default": 10 },
                "verbose": { "type": "boolean", "default": false },
                "label": { "type": "string", "default": "test" }
            }
        }
        """).RootElement;

        var result = McpToolAdapter.ConvertInputSchema(schema);

        Assert.AreEqual(10L, result.Properties["count"].Default);
        Assert.AreEqual(false, result.Properties["verbose"].Default);
        Assert.AreEqual("test", result.Properties["label"].Default);
    }

    [TestMethod]
    public void ConvertInputSchema_ParsesArrayItems()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "tags": {
                    "type": "array",
                    "items": { "type": "string", "description": "A tag" }
                }
            }
        }
        """).RootElement;

        var result = McpToolAdapter.ConvertInputSchema(schema);

        Assert.AreEqual("array", result.Properties["tags"].Type);
        Assert.IsNotNull(result.Properties["tags"].Items);
        Assert.AreEqual("string", result.Properties["tags"].Items!.Type);
        Assert.AreEqual("A tag", result.Properties["tags"].Items!.Description);
    }

    [TestMethod]
    public void ConvertInputSchema_ReturnsEmptyForNull()
    {
        var result = McpToolAdapter.ConvertInputSchema(null);

        Assert.AreEqual("object", result.Type);
        Assert.AreEqual(0, result.Properties.Count);
        Assert.AreEqual(0, result.Required.Count);
    }

    #endregion
}
