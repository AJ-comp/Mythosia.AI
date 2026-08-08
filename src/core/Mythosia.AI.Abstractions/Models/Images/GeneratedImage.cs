using System;

namespace Mythosia.AI.Models.Images
{
    /// <summary>
    /// A single image returned by an image generation service.
    /// </summary>
    public sealed class GeneratedImage
    {
        /// <summary>
        /// Raw generated image bytes when returned inline by the provider.
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// MIME type corresponding to <see cref="Data"/>.
        /// </summary>
        public string MediaType { get; set; } = "image/png";

        /// <summary>
        /// Optional provider-hosted URL when the provider returns one.
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// Optional provider-revised prompt associated with this image.
        /// </summary>
        public string? RevisedPrompt { get; set; }
    }
}
