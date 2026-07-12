namespace Mythosia.AI.Serving.Vllm
{
    /// <summary>Classified outcome of a <c>GET /health</c> probe (see <see cref="VllmServer.GetHealthAsync"/>).</summary>
    public enum VllmHealthStatus
    {
        /// <summary>Server answered with a success status — engine is alive.</summary>
        Healthy,

        /// <summary>Server answered HTTP 503 — reachable, but the inference engine is dead.</summary>
        EngineDead,

        /// <summary>Server answered HTTP 401/403 — reachable, but the API key is missing or wrong.</summary>
        Unauthorized,

        /// <summary>Connection failure or client-side timeout — no HTTP answer at all.</summary>
        Unreachable,

        /// <summary>Server answered with a status this client does not map (e.g. 404 from a non-vLLM endpoint or a proxy error).</summary>
        Unexpected,
    }

    /// <summary>
    /// Result of a health probe: the classified <see cref="Status"/> plus the raw evidence
    /// (<see cref="StatusCode"/>, <see cref="Detail"/>) an operator needs to act on it.
    /// </summary>
    public class VllmHealthReport
    {
        /// <summary>Classified probe outcome.</summary>
        public VllmHealthStatus Status { get; }

        /// <summary>HTTP status code of the answer, or <c>null</c> when no HTTP answer arrived (<see cref="VllmHealthStatus.Unreachable"/>).</summary>
        public int? StatusCode { get; }

        /// <summary>Reason phrase or exception message, when one is available.</summary>
        public string? Detail { get; }

        public VllmHealthReport(VllmHealthStatus status, int? statusCode, string? detail)
        {
            Status = status;
            StatusCode = statusCode;
            Detail = detail;
        }
    }
}
