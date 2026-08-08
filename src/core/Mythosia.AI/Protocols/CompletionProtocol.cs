using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System.Collections.Generic;
using System.Net.Http;

namespace Mythosia.AI.Protocols
{
    /// <summary>
    /// Abstract base class defining the wire-format contract for AI completion APIs.
    /// Each implementation handles a specific API format
    /// (e.g., /chat/completions, /messages, /generateContent).
    /// </summary>
    public abstract class CompletionProtocol
    {
        /// <summary>
        /// Extracts the text response content from the API response JSON.
        /// </summary>
        public abstract string ExtractResponse(string responseJson);

        /// <summary>
        /// Extracts a text delta from a single SSE stream chunk.
        /// </summary>
        public abstract string ParseStreamChunk(string chunkJson);

        /// <summary>
        /// Creates the HTTP request for a completion call.
        /// </summary>
        public abstract HttpRequestMessage CreateRequest(string apiKey, object requestBody);

        /// <summary>
        /// Creates the HTTP request for a function-calling completion call.
        /// </summary>
        public abstract HttpRequestMessage CreateFunctionRequest(string apiKey, object requestBody);

        /// <summary>
        /// Extracts every function call from one API response JSON payload.
        /// </summary>
        public abstract (string content, FunctionCallBatch functionCalls) ExtractFunctionCalls(string responseJson);

        /// <summary>
        /// Builds the request body for a standard completion request.
        /// </summary>
        public abstract object BuildRequestBody(
            ProtocolRequestParams requestParams,
            System.Func<Message, object>? messageConverter = null);

        /// <summary>
        /// Builds the request body including function/tool definitions.
        /// </summary>
        public abstract object BuildFunctionRequestBody(
            ProtocolRequestParams requestParams,
            IReadOnlyList<FunctionDefinition> functions,
            FunctionCallMode mode,
            System.Func<Message, object>? messageConverter = null);

        /// <summary>
        /// Converts a regular message to the wire format.
        /// Default implementation returns a simple {role, content} object for text-only messages.
        /// </summary>
        public virtual object ConvertMessage(Message message)
        {
            return new { role = message.Role.ToDescription(), content = message.Content };
        }
    }
}
