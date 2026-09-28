using System;

namespace Mythosia.AI.Serving
{
    public enum ServingFailureKind { Unknown, Http, Transport, Timeout, InvalidResponse }

    /// <summary>A management operation or response validation failure. Transport clients omit raw response bodies and credentials.</summary>
    public class ServingException : Exception
    {
        public int? StatusCode { get; }
        public ServingFailureKind FailureKind { get; }
        public ServingException(string message, int? statusCode = null, Exception? innerException = null,
            ServingFailureKind failureKind = ServingFailureKind.Unknown)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            FailureKind = failureKind == ServingFailureKind.Unknown && statusCode.HasValue ? ServingFailureKind.Http : failureKind;
        }
    }
}
