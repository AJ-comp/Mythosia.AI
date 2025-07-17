using System;

namespace Mythosia.AI.Exceptions
{
    /// <summary>
    /// Base exception for all AI service related errors
    /// </summary>
    public class AIServiceException : Exception
    {
        public string? ErrorDetails { get; }
        public string? ServiceName { get; protected set; }

        public AIServiceException(string message) : base(message)
        {
        }

        public AIServiceException(string message, string errorDetails) : base(message)
        {
            ErrorDetails = errorDetails;
        }

        public AIServiceException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public AIServiceException(string message, string errorDetails, string serviceName) : base(message)
        {
            ErrorDetails = errorDetails;
            ServiceName = serviceName;
        }
    }

    /// <summary>
    /// Exception thrown when multimodal features are not supported by the service
    /// </summary>
    public class MultimodalNotSupportedException : AIServiceException
    {
        public string RequestedFeature { get; }

        public MultimodalNotSupportedException(string serviceName, string requestedFeature)
            : base($"The service '{serviceName}' does not support multimodal feature: {requestedFeature}")
        {
            ServiceName = serviceName;
            RequestedFeature = requestedFeature;
        }

        public MultimodalNotSupportedException(string message) : base(message)
        {
            RequestedFeature = "Unknown";
        }
    }

    /// <summary>
    /// Exception thrown when token limit is exceeded
    /// </summary>
    public class TokenLimitExceededException : AIServiceException
    {
        public uint RequestedTokens { get; }
        public uint MaxTokens { get; }

        public TokenLimitExceededException(uint requestedTokens, uint maxTokens)
            : base($"Token limit exceeded. Requested: {requestedTokens}, Maximum: {maxTokens}")
        {
            RequestedTokens = requestedTokens;
            MaxTokens = maxTokens;
        }

        public TokenLimitExceededException(string message, uint requestedTokens, uint maxTokens)
            : base(message)
        {
            RequestedTokens = requestedTokens;
            MaxTokens = maxTokens;
        }
    }

    /// <summary>
    /// Exception thrown when API rate limit is exceeded
    /// </summary>
    public class RateLimitExceededException : AIServiceException
    {
        public TimeSpan? RetryAfter { get; }

        public RateLimitExceededException(string message) : base(message)
        {
        }

        public RateLimitExceededException(string message, TimeSpan retryAfter) : base(message)
        {
            RetryAfter = retryAfter;
        }
    }

    /// <summary>
    /// Exception thrown when API authentication fails
    /// </summary>
    public class AuthenticationException : AIServiceException
    {
        public AuthenticationException(string serviceName)
            : base($"Authentication failed for service: {serviceName}")
        {
            ServiceName = serviceName;
        }

        public AuthenticationException(string message, string serviceName) : base(message)
        {
            ServiceName = serviceName;
        }
    }

    /// <summary>
    /// Exception thrown when an invalid model is specified
    /// </summary>
    public class InvalidModelException : AIServiceException
    {
        public string ModelName { get; }

        public InvalidModelException(string modelName, string serviceName)
            : base($"Invalid model '{modelName}' for service '{serviceName}'")
        {
            ModelName = modelName;
            ServiceName = serviceName;
        }
    }

    /// <summary>
    /// Exception thrown when content validation fails
    /// </summary>
    public class ContentValidationException : AIServiceException
    {
        public string ContentType { get; }

        public ContentValidationException(string message, string contentType) : base(message)
        {
            ContentType = contentType;
        }
    }

    /// <summary>
    /// Exception thrown when the service is temporarily unavailable
    /// </summary>
    public class ServiceUnavailableException : AIServiceException
    {
        public ServiceUnavailableException(string serviceName)
            : base($"The service '{serviceName}' is temporarily unavailable")
        {
            ServiceName = serviceName;
        }

        public ServiceUnavailableException(string serviceName, string message) : base(message)
        {
            ServiceName = serviceName;
        }
    }
}