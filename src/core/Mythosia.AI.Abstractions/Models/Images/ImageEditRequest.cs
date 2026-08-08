using System;
using System.Collections.Generic;

namespace Mythosia.AI.Models.Images
{
    /// <summary>
    /// Request for generating images from one or more reference images.
    /// </summary>
    public sealed class ImageEditRequest : ImageGenerationRequest
    {
        /// <summary>
        /// Ordered reference images used by the image model.
        /// </summary>
        public IReadOnlyList<ImageInput> InputImages { get; set; } = Array.Empty<ImageInput>();

        /// <summary>
        /// Optional mask that identifies the area to edit.
        /// </summary>
        public ImageInput? Mask { get; set; }
    }
}
