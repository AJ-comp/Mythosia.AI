using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mythosia.AI.Mcp.Protocol
{
    /// <summary>
    /// MCP server info returned during initialization
    /// </summary>
    internal class McpServerInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    /// <summary>
    /// MCP tool definition returned from tools/list
    /// </summary>
    internal class McpToolDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        [JsonPropertyName("inputSchema")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? InputSchema { get; set; }
    }

    /// <summary>
    /// MCP content block returned from tools/call
    /// </summary>
    internal class McpContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("mimeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MimeType { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Data { get; set; }
    }

    /// <summary>
    /// Parameters for the initialize request
    /// </summary>
    internal class McpInitializeParams
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = McpProtocolConstants.ProtocolVersion;

        [JsonPropertyName("capabilities")]
        public Dictionary<string, object> Capabilities { get; set; } = new Dictionary<string, object>();

        [JsonPropertyName("clientInfo")]
        public McpClientInfo ClientInfo { get; set; } = new McpClientInfo();
    }

    /// <summary>
    /// Client info sent during initialization
    /// </summary>
    internal class McpClientInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Mythosia.AI";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";
    }

    /// <summary>
    /// Parameters for tools/call
    /// </summary>
    internal class McpToolCallParams
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("arguments")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? Arguments { get; set; }
    }

    /// <summary>
    /// MCP protocol constants
    /// </summary>
    internal static class McpProtocolConstants
    {
        public const string ProtocolVersion = "2024-11-05";

        // Methods
        public const string Initialize = "initialize";
        public const string Initialized = "notifications/initialized";
        public const string ToolsList = "tools/list";
        public const string ToolsCall = "tools/call";
        public const string Ping = "ping";
    }
}
