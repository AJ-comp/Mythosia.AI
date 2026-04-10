using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Extension methods for enabling Agentic RAG on AIService.
    /// Registers the RAG pipeline as a callable tool within the agent's ReAct loop.
    /// </summary>
    public static class AgenticRagExtensions
    {
        /// <summary>
        /// Registers the RAG pipeline as a search tool for use with <c>RunAgentAsync</c>.
        /// <para>
        /// In Agentic RAG mode the agent autonomously decides when to search and what query to use.
        /// The <see cref="RagStore"/>'s QueryRewriter is intentionally bypassed for this tool because
        /// the agent itself is responsible for formulating a clear, self-contained query.
        /// </para>
        /// <para>
        /// The configured retrieval strategy (vector-only or hybrid) and pipeline options of the
        /// <see cref="RagStore"/> are respected as-is.
        /// </para>
        /// </summary>
        public static TService WithAgenticRag<TService>(
            this TService service,
            RagStore ragStore,
            string toolName = "search_documents",
            string? toolDescription = null)
            where TService : IAIService, IFunctionRegisterable
        {
            return RegisterAgenticRag(
                service,
                ragStore,
                queryOptionsCallback: null,
                onTrace: null,
                toolName,
                toolDescription);
        }

        /// <summary>
        /// Registers the RAG pipeline as a search tool for use with <c>RunAgentAsync</c>,
        /// with per-tool-call query overrides and optional structured tracing.
        /// </summary>
        public static TService WithAgenticRag<TService>(
            this TService service,
            RagStore ragStore,
            Func<AgenticRagQueryContext, RagQueryOptions?> queryOptions,
            Action<AgenticRagSearchTrace>? onTrace = null,
            string toolName = "search_documents",
            string? toolDescription = null)
            where TService : IAIService, IFunctionRegisterable
        {
            if (queryOptions == null) throw new ArgumentNullException(nameof(queryOptions));

            return RegisterAgenticRag(
                service,
                ragStore,
                queryOptionsCallback: queryOptions,
                onTrace,
                toolName,
                toolDescription);
        }

        /// <summary>
        /// Registers the RAG pipeline as a search tool for use with <c>RunAgentAsync</c>,
        /// with structured tracing only.
        /// </summary>
        public static TService WithAgenticRag<TService>(
            this TService service,
            RagStore ragStore,
            Action<AgenticRagSearchTrace> onTrace,
            string toolName = "search_documents",
            string? toolDescription = null)
            where TService : IAIService, IFunctionRegisterable
        {
            if (onTrace == null) throw new ArgumentNullException(nameof(onTrace));

            return RegisterAgenticRag(
                service,
                ragStore,
                queryOptionsCallback: null,
                onTrace,
                toolName,
                toolDescription);
        }

        private static TService RegisterAgenticRag<TService>(
            TService service,
            RagStore ragStore,
            Func<AgenticRagQueryContext, RagQueryOptions?>? queryOptionsCallback,
            Action<AgenticRagSearchTrace>? onTrace,
            string toolName,
            string? toolDescription)
            where TService : IAIService, IFunctionRegisterable
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (ragStore == null) throw new ArgumentNullException(nameof(ragStore));
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name must not be empty.", nameof(toolName));

            var description = toolDescription ??
                "Search the indexed knowledge base for relevant information. " +
                "Call this tool whenever you need factual information from documents. " +
                "The query must be self-contained, do not use pronouns or references to " +
                "previous turns (e.g. write 'refund policy details' not 'tell me more about that'). " +
                "You may call this tool multiple times with different queries if the first result is insufficient. " +
                "Return your final answer only after you have retrieved enough context.";

            var funcDef = new FunctionDefinition
            {
                Name = toolName,
                Description = description,
                Parameters = new FunctionParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ParameterProperty>
                    {
                        ["query"] = new ParameterProperty
                        {
                            Type = "string",
                            Description = "A self-contained search query describing the information to retrieve from documents."
                        }
                    },
                    Required = new List<string> { "query" }
                },
                Handler = async args =>
                {
                    string? query = null;

                    if (args.TryGetValue("query", out var raw))
                        query = raw is JsonElement je ? je.GetString() : raw?.ToString();

                    if (string.IsNullOrWhiteSpace(query))
                        return "Search failed: no query was provided. Please specify what to search for.";

                    RagQueryOptions? resolvedQueryOptions = null;

                    try
                    {
                        if (queryOptionsCallback != null)
                        {
                            resolvedQueryOptions = queryOptionsCallback(
                                new AgenticRagQueryContext(toolName, query));
                        }

                        var result = resolvedQueryOptions != null
                            ? await ragStore.QueryAsync(query, resolvedQueryOptions)
                            : await ragStore.QueryAsync(query);

                        TryTrace(
                            onTrace,
                            new AgenticRagSearchTrace(toolName, query, resolvedQueryOptions, result));

                        if (!result.HasReferences)
                            return "No relevant documents found. Try rephrasing the query or using different keywords.";

                        return FormatSearchResult(query, result);
                    }
                    catch (Exception ex)
                    {
                        TryTrace(
                            onTrace,
                            new AgenticRagSearchTrace(toolName, query, resolvedQueryOptions, result: null, exception: ex));

                        return $"Search failed: {ex.Message}";
                    }
                }
            };

            service.AddFunction(funcDef);
            return service;
        }

        private static void TryTrace(
            Action<AgenticRagSearchTrace>? onTrace,
            AgenticRagSearchTrace trace)
        {
            if (onTrace == null)
                return;

            try
            {
                onTrace(trace);
            }
            catch
            {
                // Trace callbacks are observability helpers and should not break agent execution.
            }
        }

        private static string FormatSearchResult(string query, RagProcessedQuery result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Retrieved {result.References.Count} relevant excerpt(s) for query: \"{query}\"");
            sb.AppendLine();

            for (int i = 0; i < result.References.Count; i++)
            {
                var reference = result.References[i];
                sb.Append($"[{i + 1}]");

                if (reference.Record.Metadata.TryGetValue("source", out var source))
                    sb.Append($" (Source: {source})");

                sb.AppendLine();
                sb.AppendLine(reference.Record.Content);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
    }
}
