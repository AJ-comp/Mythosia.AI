using System;

namespace Mythosia.AI.Models.Images
{
    /// <summary>
    /// Binary image input supplied to an image generation service.
    /// </summary>
    public sealed class ImageInput
    {
        public ImageInput(byte[] data, string mediaType, string fileName = "image")
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        }

        /// <summary>
        /// Raw image bytes.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// MIME type of the image, such as image/png or image/jpeg.
        /// </summary>
        public string MediaType { get; }

        /// <summary>
        /// File name supplied to providers that accept multipart uploads.
        /// </summary>
        public string FileName { get; }
    }
}
