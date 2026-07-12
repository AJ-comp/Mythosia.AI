using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace Mythosia.AI.Serving.Vllm
{
    /// <summary>
    /// Thrown when the vLLM server answers a control-plane request with a non-success status.
    /// Carries the OpenAI-style error body (<c>{"error":{"message","type","param","code"}}</c>)
    /// that vLLM returns uniformly, so the diagnostic payload is not lost in a bare
    /// <see cref="System.Net.Http.HttpRequestException"/>.
    /// <para>
    /// Connection-level failures (DNS, refused, timeout) are NOT wrapped — they surface as the
    /// standard <see cref="System.Net.Http.HttpRequestException"/> / timeout exceptions.
    /// </para>
    /// </summary>
    public class VllmException : Exception
    {
        /// <summary>HTTP status code the server answered with.</summary>
        public int StatusCode { get; }

        /// <summary>vLLM error type when the body carried one (e.g. <c>"NotFoundError"</c>).</summary>
        public string? ErrorType { get; }

        /// <summary>vLLM error code when the body carried one (often the numeric status again).</summary>
        public string? ErrorCode { get; }

        /// <summary>Raw response body (truncated to 4 KB) for diagnostics.</summary>
        public string? ResponseBody { get; }

        public VllmException(
            string message,
            int statusCode,
            string? errorType = null,
            string? errorCode = null,
            string? responseBody = null)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorType = errorType;
            ErrorCode = errorCode;
            ResponseBody = responseBody;
        }

        internal static VllmException FromResponse(int statusCode, string? reasonPhrase, string? body)
        {
            string? message = null;
            string? errorType = null;
            string? errorCode = null;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var obj = JObject.Parse(body);
                    var error = obj["error"];
                    if (error is JObject errorObject)
                    {
                        message = errorObject["message"]?.ToString();
                        errorType = errorObject["type"]?.ToString();
                        errorCode = errorObject["code"]?.ToString();
                    }
                    else
                    {
                        // Some middleware/proxy error paths return {"message": ...} or {"detail": ...}.
                        message = obj["message"]?.ToString() ?? obj["detail"]?.ToString();
                    }
                }
                catch (JsonException)
                {
                    // Non-JSON body (HTML error page etc.) — fall through to the status-line message.
                }
            }

            var headline = string.IsNullOrEmpty(message)
                ? (string.IsNullOrWhiteSpace(reasonPhrase)
                    ? $"vLLM server returned HTTP {statusCode}."
                    : $"vLLM server returned HTTP {statusCode} {reasonPhrase}.")
                : $"vLLM server returned HTTP {statusCode}: {message}";

            return new VllmException(headline, statusCode, errorType, errorCode, VllmServer.Truncate(body));
        }
    }
}
