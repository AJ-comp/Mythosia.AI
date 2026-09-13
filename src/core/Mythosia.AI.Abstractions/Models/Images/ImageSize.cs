using System;

namespace Mythosia.AI.Models.Images
{
    /// <summary>
    /// Immutable image sizing intent. Exact pixels and resolution presets are distinct requests;
    /// providers reject unsupported kinds instead of silently approximating dimensions.
    /// </summary>
    public sealed class ImageSize
    {
        /// <summary>Let the provider select the output size.</summary>
        public static ImageSize Auto { get; } = new ImageSize(ImageSizeKind.Auto);

        /// <summary>The sizing mode selected by the factory.</summary>
        public ImageSizeKind Kind { get; }

        /// <summary>Requested width in Pixels mode; zero for other modes.</summary>
        public int Width { get; }

        /// <summary>Requested height in Pixels mode; zero for other modes.</summary>
        public int Height { get; }

        /// <summary>Requested resolution grade in Preset mode; Auto for other modes.</summary>
        public ImageResolution Resolution { get; }

        /// <summary>Requested aspect ratio in Preset mode; Auto for other modes.</summary>
        public ImageAspectRatio AspectRatio { get; }

        private ImageSize(ImageSizeKind kind, int width = 0, int height = 0,
            ImageResolution resolution = ImageResolution.Auto, ImageAspectRatio aspectRatio = ImageAspectRatio.Auto)
        {
            Kind = kind;
            Width = width;
            Height = height;
            Resolution = resolution;
            AspectRatio = aspectRatio;
        }

        /// <summary>
        /// Request exact positive pixel dimensions. Model-specific bounds are validated by the provider.
        /// Providers that only support resolution grades reject this mode.
        /// </summary>
        public static ImageSize Pixels(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
            return new ImageSize(ImageSizeKind.Pixels, width, height);
        }

        /// <summary>
        /// Request a provider resolution grade and optional aspect ratio. Auto resolution can be used
        /// to request only a ratio. Actual pixel dimensions remain provider-selected.
        /// </summary>
        public static ImageSize Preset(ImageResolution resolution, ImageAspectRatio aspectRatio = ImageAspectRatio.Auto)
        {
            if (!Enum.IsDefined(typeof(ImageResolution), resolution))
                throw new ArgumentOutOfRangeException(nameof(resolution));
            if (!Enum.IsDefined(typeof(ImageAspectRatio), aspectRatio))
                throw new ArgumentOutOfRangeException(nameof(aspectRatio));
            return new ImageSize(ImageSizeKind.Preset, resolution: resolution, aspectRatio: aspectRatio);
        }
    }
}
