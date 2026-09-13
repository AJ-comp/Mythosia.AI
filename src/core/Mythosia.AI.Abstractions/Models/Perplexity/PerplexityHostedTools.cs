using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mythosia.AI.Models.Perplexity
{
    /// <summary>Constructors for server-hosted tools. Additional documented settings can be supplied through Parameters.</summary>
    public static class PerplexityHostedTools
    {
        public static PerplexityHostedTool WebSearch() => new PerplexityHostedTool { Type = "web_search" };
        public static PerplexityHostedTool FetchUrl() => new PerplexityHostedTool { Type = "fetch_url" };
        public static PerplexityHostedTool Sandbox() => new PerplexityHostedTool { Type = "sandbox" };
        public static PerplexityHostedTool FinanceSearch() => new PerplexityHostedTool { Type = "finance_search" };
        public static PerplexityHostedTool PeopleSearch() => new PerplexityHostedTool { Type = "people_search" };

        /// <summary>Connects a Streamable HTTP MCP server. Calls execute on the server without an approval pause.</summary>
        public static PerplexityHostedTool Mcp(string serverLabel, Uri serverUrl,
            IEnumerable<string>? allowedTools = null, string? authorization = null, bool deferLoading = false,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            ValidateLabel(serverLabel);
            if (serverUrl == null || !serverUrl.IsAbsoluteUri || serverUrl.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(serverUrl.UserInfo))
                throw new ArgumentException("MCP requires an absolute HTTPS URL without user credentials.", nameof(serverUrl));
            var parameters = new Dictionary<string, object> { ["server_label"] = serverLabel,
                ["server_url"] = serverUrl.AbsoluteUri, ["defer_loading"] = deferLoading };
            AddAllowedTools(parameters, allowedTools);
            if (authorization != null) parameters["authorization"] = authorization;
            if (headers != null) parameters["headers"] = headers.ToDictionary(item => item.Key, item => item.Value);
            return new PerplexityHostedTool { Type = "mcp", Parameters = parameters };
        }

        /// <summary>References an already connected integration in the API portal. This provider feature is in preview.</summary>
        public static PerplexityHostedTool Connector(string id, string serverLabel,
            IEnumerable<string>? allowedTools = null, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A connected integration ID is required.", nameof(id));
            ValidateLabel(serverLabel);
            var parameters = new Dictionary<string, object> { ["id"] = id, ["server_label"] = serverLabel };
            AddAllowedTools(parameters, allowedTools);
            if (description != null) parameters["server_description"] = description;
            return new PerplexityHostedTool { Type = "connector", Parameters = parameters };
        }

        private static void ValidateLabel(string label)
        {
            if (label == null || !Regex.IsMatch(label, "^[a-zA-Z0-9_-]{1,64}$"))
                throw new ArgumentException("A server label must contain 1 to 64 letters, digits, underscores or hyphens.", nameof(label));
        }

        private static void AddAllowedTools(Dictionary<string, object> parameters, IEnumerable<string>? tools)
        {
            if (tools == null) return;
            var names = tools.ToArray();
            if (names.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Tool names cannot be empty.", nameof(tools));
            parameters["allowed_tools"] = names;
        }
    }
}
