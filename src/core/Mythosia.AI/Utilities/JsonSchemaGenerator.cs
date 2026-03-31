using NJsonSchema;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Utilities
{
    /// <summary>
    /// Generates JSON Schema strings from C# types using NJsonSchema.
    /// Used internally by GetCompletionAsync&lt;T&gt;() for structured output.
    /// </summary>
    internal static class JsonSchemaGenerator
    {
        /// <summary>
        /// Generates a JSON Schema string from the specified type.
        /// </summary>
        public static string Generate(Type type)
        {
            var schema = JsonSchema.FromType(type);
            var json = schema.ToJson();

            // Post-process for OpenAI Structured Outputs compliance:
            // - All properties must be listed in "required"
            // - "additionalProperties" must be false
            // - Convert draft-04 "definitions" to "$defs" and update "$ref" paths
            // - Remove "$schema" (not accepted by OpenAI)
            var node = JsonNode.Parse(json);
            if (node is JsonObject root)
            {
                // Remove $schema field
                root.Remove("$schema");

                // Convert "definitions" to "$defs" and update $ref paths
                if (root.ContainsKey("definitions"))
                {
                    var definitions = root["definitions"];
                    root.Remove("definitions");
                    root["$defs"] = definitions;
                }
                FixRefPaths(root);

                EnforceStrictSchema(root);
            }

            return node!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Recursively enforces that every object with "properties" has
        /// all property keys in "required" and "additionalProperties": false.
        /// This is mandatory for OpenAI Structured Outputs (GPT-4o+ / GPT-5.1+).
        /// </summary>
        private static void EnforceStrictSchema(JsonObject schemaNode)
        {
            if (schemaNode.ContainsKey("properties") && schemaNode["properties"] is JsonObject properties)
            {
                // Set "required" to all property keys
                var requiredArray = new JsonArray();
                foreach (var key in properties)
                {
                    requiredArray.Add(JsonValue.Create(key.Key));
                }
                schemaNode["required"] = requiredArray;

                // Set "additionalProperties" to false
                schemaNode["additionalProperties"] = false;

                // Recurse into each property
                foreach (var kvp in properties)
                {
                    if (kvp.Value is JsonObject propSchema)
                    {
                        EnforceStrictSchema(propSchema);
                    }
                }
            }

            // Handle array items
            if (schemaNode.ContainsKey("items") && schemaNode["items"] is JsonObject items)
            {
                EnforceStrictSchema(items);
            }

            // Handle $defs / definitions entries
            foreach (var key in new[] { "$defs", "definitions" })
            {
                if (schemaNode.ContainsKey(key) && schemaNode[key] is JsonObject defs)
                {
                    foreach (var kvp in defs)
                    {
                        if (kvp.Value is JsonObject defSchema)
                        {
                            EnforceStrictSchema(defSchema);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively replaces "#/definitions/" with "#/$defs/" in all $ref values.
        /// </summary>
        private static void FixRefPaths(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                if (obj.ContainsKey("$ref") && obj["$ref"] is JsonValue refVal)
                {
                    var refStr = refVal.GetValue<string>();
                    if (refStr.Contains("#/definitions/"))
                    {
                        obj["$ref"] = refStr.Replace("#/definitions/", "#/$defs/");
                    }
                }

                foreach (var kvp in obj.ToArray())
                {
                    if (kvp.Value is JsonNode child)
                    {
                        FixRefPaths(child);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonNode child)
                    {
                        FixRefPaths(child);
                    }
                }
            }
        }
    }
}
