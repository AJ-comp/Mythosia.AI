using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Mythosia.AI.Models.Functions;

namespace Mythosia.AI.Mcp
{
    /// <summary>
    /// Converts MCP tools into Mythosia.AI <see cref="FunctionDefinition"/> instances,
    /// bridging MCP tool schemas to the existing function calling infrastructure.
    /// </summary>
    public static class McpToolAdapter
    {
        /// <summary>
        /// Converts all tools from an <see cref="McpConnection"/> into <see cref="FunctionDefinition"/> list.
        /// Each function's handler delegates to <see cref="McpConnection.CallToolAsync"/>.
        /// </summary>
        /// <param name="connection">An initialized MCP connection with discovered tools.</param>
        /// <param name="toolFilter">Optional filter to include only specific tools by name.</param>
        /// <param name="namePrefix">Optional prefix added to each tool name to avoid collisions.</param>
        public static List<FunctionDefinition> ToFunctionDefinitions(
            McpConnection connection,
            Func<string, bool>? toolFilter = null,
            string? namePrefix = null)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            var definitions = new List<FunctionDefinition>();

            foreach (var tool in connection.Tools)
            {
                if (toolFilter != null && !toolFilter(tool.Name))
                    continue;

                var funcName = string.IsNullOrEmpty(namePrefix)
                    ? tool.Name
                    : $"{namePrefix}{tool.Name}";

                var funcDef = new FunctionDefinition
                {
                    Name = funcName,
                    Description = tool.Description ?? $"MCP tool: {tool.Name}",
                    Parameters = ConvertInputSchema(tool.InputSchema),
                    Handler = CreateHandler(connection, tool.Name)
                };

                definitions.Add(funcDef);
            }

            return definitions;
        }

        /// <summary>
        /// Creates an async handler that calls the MCP tool via the connection.
        /// </summary>
        private static Func<Dictionary<string, object>, Task<string>> CreateHandler(
            McpConnection connection, string toolName)
        {
            return async (args) =>
            {
                try
                {
                    return await connection.CallToolAsync(toolName, args).ConfigureAwait(false);
                }
                catch (McpException ex)
                {
                    return $"Error: {ex.Message}";
                }
                catch (Exception ex)
                {
                    return $"Error calling MCP tool '{toolName}': {ex.Message}";
                }
            };
        }

        /// <summary>
        /// Converts an MCP tool's JSON Schema inputSchema into <see cref="FunctionParameters"/>.
        /// </summary>
        internal static FunctionParameters ConvertInputSchema(JsonElement? inputSchema)
        {
            var result = new FunctionParameters();

            if (inputSchema == null || inputSchema.Value.ValueKind == JsonValueKind.Undefined)
                return result;

            var schema = inputSchema.Value;

            // type
            if (schema.TryGetProperty("type", out var typeProp))
                result.Type = typeProp.GetString() ?? "object";

            // properties
            if (schema.TryGetProperty("properties", out var propsProp)
                && propsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in propsProp.EnumerateObject())
                {
                    result.Properties[prop.Name] = ConvertParameterProperty(prop.Value);
                }
            }

            // required
            if (schema.TryGetProperty("required", out var reqProp)
                && reqProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in reqProp.EnumerateArray())
                {
                    var name = item.GetString();
                    if (name != null)
                        result.Required.Add(name);
                }
            }

            return result;
        }

        /// <summary>
        /// Converts a single JSON Schema property into <see cref="ParameterProperty"/>.
        /// </summary>
        private static ParameterProperty ConvertParameterProperty(JsonElement element)
        {
            var param = new ParameterProperty();

            if (element.TryGetProperty("type", out var typeProp))
                param.Type = typeProp.GetString() ?? "string";

            if (element.TryGetProperty("description", out var descProp))
                param.Description = descProp.GetString();

            if (element.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
            {
                param.Enum = new List<string>();
                foreach (var item in enumProp.EnumerateArray())
                {
                    var val = item.GetString();
                    if (val != null)
                        param.Enum.Add(val);
                }
            }

            if (element.TryGetProperty("default", out var defaultProp))
            {
                param.Default = ExtractDefaultValue(defaultProp);
            }

            if (element.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Object)
            {
                param.Items = ConvertParameterProperty(itemsProp);
            }

            return param;
        }

        /// <summary>
        /// Extracts a default value from a JsonElement.
        /// </summary>
        private static object? ExtractDefaultValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
    }
}
