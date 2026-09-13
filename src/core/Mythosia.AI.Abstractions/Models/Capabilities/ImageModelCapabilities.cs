using Mythosia.AI.Models.Images;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models.Capabilities
{
    /// <summary>Immutable capabilities of an independently selected image generation model.</summary>
    /// <remarks>Lists describe individually supported choices, not every valid combination. Exact pixel bounds,
    /// masks, encoding combinations and account access remain subject to request and server validation.</remarks>
    public sealed class ImageModelCapabilities
    {
        public static ImageModelCapabilities Unknown { get; } = new ImageModelCapabilities();
        public string? Provider { get; }
        public string? Model { get; }
        public CapabilitySupport Generation { get; }
        public CapabilitySupport Editing { get; }
        public CapabilitySupport Mask { get; }
        public IReadOnlyList<ImageQuality> Qualities { get; }
        public IReadOnlyList<ImageBackground> Backgrounds { get; }
        public IReadOnlyList<ImageOutputFormat> OutputFormats { get; }
        public IReadOnlyList<ImageSizeKind> SizeKinds { get; }
        public IReadOnlyList<ImageResolution> Resolutions { get; }
        public IReadOnlyList<ImageAspectRatio> AspectRatios { get; }
        public int? MaxImages { get; }
        public int? MaxInputImages { get; }

        public ImageModelCapabilities(string? provider = null, string? model = null,
            CapabilitySupport generation = CapabilitySupport.Unknown,
            CapabilitySupport editing = CapabilitySupport.Unknown,
            CapabilitySupport mask = CapabilitySupport.Unknown,
            IEnumerable<ImageQuality>? qualities = null,
            IEnumerable<ImageBackground>? backgrounds = null,
            IEnumerable<ImageOutputFormat>? outputFormats = null,
            IEnumerable<ImageSizeKind>? sizeKinds = null,
            IEnumerable<ImageResolution>? resolutions = null,
            IEnumerable<ImageAspectRatio>? aspectRatios = null,
            int? maxImages = null, int? maxInputImages = null)
        {
            Provider = provider;
            Model = model;
            Generation = generation;
            Editing = editing;
            Mask = mask;
            Qualities = Copy(qualities);
            Backgrounds = Copy(backgrounds);
            OutputFormats = Copy(outputFormats);
            SizeKinds = Copy(sizeKinds);
            Resolutions = Copy(resolutions);
            AspectRatios = Copy(aspectRatios);
            MaxImages = maxImages;
            MaxInputImages = maxInputImages;
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values)
            => Array.AsReadOnly(values?.Distinct().ToArray() ?? Array.Empty<T>());
    }
}
