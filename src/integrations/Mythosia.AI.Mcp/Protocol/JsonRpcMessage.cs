using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mythosia.AI.Mcp.Protocol
{
    /// <summary>
    /// JSON-RPC 2.0 request message
    /// </summary>
    internal class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Params { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 notification (no id)
    /// </summary>
    internal class JsonRpcNotification
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Params { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 response message
    /// </summary>
    internal class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcResult? Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcError? Error { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 result — loosely typed to handle varying MCP responses
    /// </summary>
    internal class JsonRpcResult
    {
        [JsonPropertyName("protocolVersion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProtocolVersion { get; set; }

        [JsonPropertyName("capabilities")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? Capabilities { get; set; }

        [JsonPropertyName("serverInfo")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public McpServerInfo? ServerInfo { get; set; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<McpToolDefinition>? Tools { get; set; }

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<McpContent>? Content { get; set; }

        [JsonPropertyName("isError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsError { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 error object
    /// </summary>
    internal class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; set; }
    }
}
