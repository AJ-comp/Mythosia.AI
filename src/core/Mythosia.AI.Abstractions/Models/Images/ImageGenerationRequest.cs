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
        /// Requested sizing mode. Use ImageSize.Pixels for exact dimensions or ImageSize.Preset
        /// for a resolution grade and optional aspect ratio. Unsupported modes are rejected.
        /// </summary>
        public ImageSize Size { get; set; } = ImageSize.Auto;

        /// <summary>
        /// Requested output quality. OpenAI GPT Image 2.5 also supports XHigh and Max;
        /// other providers and models validate their own supported levels.
        /// </summary>
        public ImageQuality Quality { get; set; } = ImageQuality.Auto;

        /// <summary>
        /// Requested encoding. Auto selects the provider default; inspect GeneratedImage.MediaType
        /// when saving. Providers without a matching encoding control reject explicit formats
        /// before making an API call. The library does not transcode returned images.
        /// </summary>
        public ImageOutputFormat OutputFormat { get; set; } = ImageOutputFormat.Auto;

        /// <summary>
        /// Optional compression level for formats that support compression.
        /// </summary>
        public int? OutputCompression { get; set; }

        /// <summary>
        /// Requested background behavior or a provider-supported automatic value.
        /// </summary>
        public ImageBackground Background { get; set; } = ImageBackground.Auto;
    }
}
