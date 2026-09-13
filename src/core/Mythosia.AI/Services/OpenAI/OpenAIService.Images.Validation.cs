using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Images;
using System;
using System.Globalization;
using System.Linq;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        private static bool IsImage25Model(string model)
            => model == AIModels.OpenAI.GptImage2_5Sunburst || model == AIModels.OpenAI.GptImage2_5Sunburst_260908 ||
                model == AIModels.OpenAI.GptImage2_5Flare || model == AIModels.OpenAI.GptImage2_5Flare_260908;

        private static void ValidateOpenAIImageOptions(ImageGenerationRequest request, string model)
        {
            var outputFormat = ToOpenAIImageFormat(request.OutputFormat);
            if (request.Background == ImageBackground.Transparent && outputFormat == "jpeg")
                throw new ArgumentException("A transparent background requires PNG or WebP output.", nameof(request));
            if (request.OutputCompression.HasValue)
            {
                if (request.OutputCompression < 0 || request.OutputCompression > 100)
                    throw new ArgumentOutOfRangeException(nameof(request), "Image compression must be between zero and 100.");
                if (outputFormat == "png")
                    throw new ArgumentException("Output compression is available for JPEG or WebP, not PNG.", nameof(request));
            }

            // Unknown deployment names retain model-specific validation at the provider.
            // The public option types and encoding combinations are validated for every model.
            var capabilities = ResolveOpenAIImageCapabilities(model);
            if (capabilities.Generation == CapabilitySupport.Unknown) return;
            if (request.Count > capabilities.MaxImages)
                throw new ArgumentOutOfRangeException(nameof(request), "GPT Image accepts one to ten output images.");
            if (!capabilities.Qualities.Contains(request.Quality))
                throw new NotSupportedException("GPT Image 2 supports quality levels up to High. Select a GPT Image 2.5 model for XHigh or Max.");
            ValidateOpenAIImageSize(request.Size);
        }

        private static void ValidateOpenAIImageSize(ImageSize size)
        {
            if (size.Kind == ImageSizeKind.Auto) return;
            var width = size.Width;
            var height = size.Height;
            if (width > 3840 || height > 3840 || width % 16 != 0 || height % 16 != 0 ||
                (long)width > (long)height * 3 || (long)height > (long)width * 3 ||
                (long)width * height < 655360 || (long)width * height > 8294400)
                throw new ArgumentException("GPT Image pixel sizes require multiples of 16, at most 3840 per edge, aspect ratio 1:3 to 3:1, and 655360 to 8294400 pixels.", nameof(size));
        }

        private static string ToOpenAIImageSize(ImageSize size) => size.Kind switch
        {
            ImageSizeKind.Auto => "auto",
            ImageSizeKind.Pixels => size.Width.ToString(CultureInfo.InvariantCulture) + "x" + size.Height.ToString(CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException("OpenAI image requests do not support resolution presets.")
        };

        private static string ToOpenAIImageFormat(ImageOutputFormat format) => format switch
        {
            ImageOutputFormat.Auto => "png",
            ImageOutputFormat.Png => "png",
            ImageOutputFormat.Jpeg => "jpeg",
            ImageOutputFormat.WebP => "webp",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        private static string ToOpenAIImageQuality(ImageQuality quality) => quality switch
        {
            ImageQuality.Auto => "auto",
            ImageQuality.Low => "low",
            ImageQuality.Medium => "medium",
            ImageQuality.High => "high",
            ImageQuality.XHigh => "xhigh",
            ImageQuality.Max => "max",
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };

        private static string ToOpenAIImageBackground(ImageBackground background) => background switch
        {
            ImageBackground.Auto => "auto",
            ImageBackground.Opaque => "opaque",
            ImageBackground.Transparent => "transparent",
            _ => throw new ArgumentOutOfRangeException(nameof(background))
        };

        private static void ValidateImage25Inputs(ImageEditRequest request, string model)
        {
            if (!IsImage25Model(model)) return;
            if (request.InputImages.Count > ResolveOpenAIImageCapabilities(model).MaxInputImages)
                throw new ArgumentException("GPT Image 2.5 accepts at most 16 reference images.", nameof(request));
            foreach (var image in request.InputImages)
            {
                if (image.MediaType != "image/png" && image.MediaType != "image/jpeg" && image.MediaType != "image/webp")
                    throw new ArgumentException("Reference images must use PNG, JPEG or WebP.", nameof(request));
                if (image.Data.LongLength == 0 || image.Data.LongLength >= 50L * 1024 * 1024)
                    throw new ArgumentException("Reference images must be nonempty and smaller than 50 MiB.", nameof(request));
            }
            if (request.Mask != null)
            {
                if (request.Mask.Data.LongLength == 0 || request.Mask.Data.LongLength >= 50L * 1024 * 1024)
                    throw new ArgumentException("An edit mask must be nonempty and smaller than 50 MiB.", nameof(request));
                if ((request.Mask.MediaType != "image/png" && request.Mask.MediaType != "image/webp") ||
                    request.Mask.MediaType != request.InputImages[0].MediaType)
                    throw new ArgumentException("The mask must use PNG or WebP and match the first reference image's format.", nameof(request));
                // Pixel dimensions and the alpha channel are validated by the provider. The library
                // does not decode or transcode input assets just to inspect their metadata.
            }
        }
    }
}
