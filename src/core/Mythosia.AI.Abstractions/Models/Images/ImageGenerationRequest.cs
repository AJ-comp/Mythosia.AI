namespace Mythosia.AI.Models.Images
{
    /// <summary>
    /// Provider-neutral request for generating images from a text prompt.
    /// </summary>
    public class ImageGenerationRequest
    {
        /// <summary>
        /// Description of the image to generate.
        /// </summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Optional image model override. When omitted, the service default is used.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Number of images requested.
        /// </summary>
        public int Count { get; set; } = 1;

        /// <summary>
        /// Requested output dimensions or a provider-supported automatic value.
        /// </summary>
        public string Size { get; set; } = "auto";

        /// <summary>
        /// Requested output quality or a provider-supported automatic value.
        /// </summary>
        public string Quality { get; set; } = "auto";

        /// <summary>
        /// Requested output format, such as png, jpeg, or webp.
        /// </summary>
        public string OutputFormat { get; set; } = "png";

        /// <summary>
        /// Optional compression level for formats that support compression.
        /// </summary>
        public int? OutputCompression { get; set; }

        /// <summary>
        /// Requested background behavior or a provider-supported automatic value.
        /// </summary>
        public string Background { get; set; } = "auto";
    }
}
