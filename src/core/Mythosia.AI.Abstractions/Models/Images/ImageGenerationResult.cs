using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Models.Images
{
    /// <summary>
    /// Provider-neutral result containing generated images and request metadata.
    /// </summary>
    public sealed class ImageGenerationResult
    {
        /// <summary>
        /// All images returned for the request.
        /// </summary>
        public IReadOnlyList<GeneratedImage> Images { get; set; } = Array.Empty<GeneratedImage>();

        /// <summary>
        /// Provider that fulfilled the request.
        /// </summary>
        public string Provider { get; set; } = string.Empty;

        /// <summary>
        /// Image model that fulfilled the request.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Optional provider request identifier for diagnostics and provenance.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Optional token usage reported by the provider.
        /// </summary>
        public TokenUsage? Usage { get; set; }
    }
}
