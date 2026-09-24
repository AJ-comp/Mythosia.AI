using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private const int GoogleImageMaxImages = 1;
        private static readonly ImageQuality[] GoogleImageQualities = { ImageQuality.Auto };
        private static readonly ImageBackground[] GoogleImageBackgrounds = { ImageBackground.Auto };
        private static readonly ImageOutputFormat[] GoogleImageOutputFormats = { ImageOutputFormat.Auto, ImageOutputFormat.Jpeg };
        private static readonly ImageSizeKind[] GoogleImageSizeKinds = { ImageSizeKind.Auto, ImageSizeKind.Preset };
        private static readonly Dictionary<ImageResolution, string> GoogleImageResolutions = new Dictionary<ImageResolution, string>
        {
            [ImageResolution.FiveTwelve] = "IMAGE_SIZE_FIVE_TWELVE",
            [ImageResolution.OneK] = "IMAGE_SIZE_ONE_K",
            [ImageResolution.TwoK] = "IMAGE_SIZE_TWO_K",
            [ImageResolution.FourK] = "IMAGE_SIZE_FOUR_K"
        };
        private static readonly Dictionary<ImageAspectRatio, string> GoogleImageAspectRatios = new Dictionary<ImageAspectRatio, string>
        {
            [ImageAspectRatio.OneByOne] = "ASPECT_RATIO_ONE_BY_ONE",
            [ImageAspectRatio.TwoByThree] = "ASPECT_RATIO_TWO_BY_THREE",
            [ImageAspectRatio.ThreeByTwo] = "ASPECT_RATIO_THREE_BY_TWO",
            [ImageAspectRatio.ThreeByFour] = "ASPECT_RATIO_THREE_BY_FOUR",
            [ImageAspectRatio.FourByThree] = "ASPECT_RATIO_FOUR_BY_THREE",
            [ImageAspectRatio.FourByFive] = "ASPECT_RATIO_FOUR_BY_FIVE",
            [ImageAspectRatio.FiveByFour] = "ASPECT_RATIO_FIVE_BY_FOUR",
            [ImageAspectRatio.NineBySixteen] = "ASPECT_RATIO_NINE_BY_SIXTEEN",
            [ImageAspectRatio.SixteenByNine] = "ASPECT_RATIO_SIXTEEN_BY_NINE",
            [ImageAspectRatio.TwentyOneByNine] = "ASPECT_RATIO_TWENTY_ONE_BY_NINE",
            [ImageAspectRatio.OneByEight] = "ASPECT_RATIO_ONE_BY_EIGHT",
            [ImageAspectRatio.EightByOne] = "ASPECT_RATIO_EIGHT_BY_ONE",
            [ImageAspectRatio.OneByFour] = "ASPECT_RATIO_ONE_BY_FOUR",
            [ImageAspectRatio.FourByOne] = "ASPECT_RATIO_FOUR_BY_ONE"
        };

        /// <inheritdoc />
        public string DefaultImageModel => AIModels.Google.Images.Gemini3_1FlashImage;

        /// <inheritdoc />
        public override ImageModelCapabilities GetImageCapabilities(string? model = null)
        {
            var selectedModel = string.IsNullOrWhiteSpace(model) ? DefaultImageModel : model!;
            var isLite = string.Equals(selectedModel, AIModels.Google.Images.Gemini3_1FlashLiteImage, StringComparison.OrdinalIgnoreCase);
            var isPro = string.Equals(selectedModel, AIModels.Google.Images.Gemini3ProImage, StringComparison.OrdinalIgnoreCase);
            var knownModel = isLite || isPro ||
                string.Equals(selectedModel, AIModels.Google.Images.Gemini3_1FlashImage, StringComparison.OrdinalIgnoreCase);
            if (!knownModel)
                return new ImageModelCapabilities(
                    provider: Provider, model: selectedModel,
                    generation: CapabilitySupport.Unknown, editing: CapabilitySupport.Unknown,
                    mask: CapabilitySupport.Unsupported, maxImages: GoogleImageMaxImages);

            // The Lite model card specifies 1K only. Its guide's 512 table conflicts
            // with that explicit limit, so do not promise 512 support for Lite.
            var resolutions = isLite
                ? new[] { ImageResolution.Auto, ImageResolution.OneK }
                : isPro
                    ? new[] { ImageResolution.Auto, ImageResolution.OneK, ImageResolution.TwoK, ImageResolution.FourK }
                    : new[] { ImageResolution.Auto }.Concat(GoogleImageResolutions.Keys);
            var aspectRatios = isPro
                ? GoogleImageAspectRatios.Keys.Where(ratio =>
                    ratio != ImageAspectRatio.OneByFour && ratio != ImageAspectRatio.FourByOne &&
                    ratio != ImageAspectRatio.OneByEight && ratio != ImageAspectRatio.EightByOne)
                : GoogleImageAspectRatios.Keys;

            return new ImageModelCapabilities(
                provider: Provider,
                model: selectedModel,
                generation: CapabilitySupport.Supported,
                editing: CapabilitySupport.Supported,
                mask: CapabilitySupport.Unsupported,
                qualities: GoogleImageQualities,
                backgrounds: GoogleImageBackgrounds,
                outputFormats: GoogleImageOutputFormats,
                sizeKinds: GoogleImageSizeKinds,
                resolutions: resolutions,
                aspectRatios: new[] { ImageAspectRatio.Auto }.Concat(aspectRatios),
                maxImages: GoogleImageMaxImages);
        }

        /// <inheritdoc />
        public Task<ImageGenerationResult> GenerateImagesAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateImageGenerationRequest(request);
            return SendImageGenerationRequestAsync(
                request,
                Array.Empty<ImageInput>(),
                cancellationToken);
        }

        /// <inheritdoc />
        public Task<ImageGenerationResult> EditImagesAsync(
            ImageEditRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateImageGenerationRequest(request);
            if (request.InputImages == null || request.InputImages.Count == 0)
                throw new ArgumentException("At least one reference image is required.", nameof(request));
            if (request.Mask != null)
                throw new NotSupportedException("Gemini image editing does not expose a separate mask input.");

            foreach (var image in request.InputImages)
            {
                if (image == null)
                    throw new ArgumentException("Reference images cannot contain null values.", nameof(request));
            }

            return SendImageGenerationRequestAsync(
                request,
                request.InputImages,
                cancellationToken);
        }

        private async Task<ImageGenerationResult> SendImageGenerationRequestAsync(
            ImageGenerationRequest request,
            IReadOnlyList<ImageInput> inputImages,
            CancellationToken cancellationToken)
        {
            var model = string.IsNullOrWhiteSpace(request.Model)
                ? DefaultImageModel
                : request.Model!;
            var requestBody = BuildImageGenerationRequest(request, inputImages);
            using var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");
            using var httpRequest = CreateGoogleRequest(
                HttpMethod.Post,
                $"v1/models/{model}:generateContent",
                content);
            using var response = await SendGoogleImageRequestAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw AIHttpErrorFactory.FromHttp(
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    responseContent,
                    "Gemini image request failed",
                    includeErrorBodyInMessage: true);
            }

            return ParseImageGenerationResponse(responseContent, model);
        }

        private object BuildImageGenerationRequest(
            ImageGenerationRequest request,
            IReadOnlyList<ImageInput> inputImages)
        {
            var parts = new List<object>
            {
                new Dictionary<string, object> { ["text"] = request.Prompt }
            };
            foreach (var image in inputImages)
            {
                parts.Add(new Dictionary<string, object>
                {
                    ["inlineData"] = new Dictionary<string, object>
                    {
                        ["mimeType"] = image.MediaType,
                        ["data"] = Convert.ToBase64String(image.Data)
                    }
                });
            }

            var imageFormat = BuildGoogleImageFormat(request.Size, request.OutputFormat);
            var generationConfig = new Dictionary<string, object>
            {
                ["responseModalities"] = new[] { "TEXT", "IMAGE" }
            };
            if (imageFormat.Count > 0)
            {
                generationConfig["responseFormat"] = new Dictionary<string, object>
                {
                    ["image"] = imageFormat
                };
            }

            return new Dictionary<string, object>
            {
                ["contents"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["role"] = "user",
                        ["parts"] = parts
                    }
                },
                ["generationConfig"] = generationConfig
            };
        }

        private static Dictionary<string, object> BuildGoogleImageFormat(
            ImageSize size,
            ImageOutputFormat outputFormat)
        {
            var result = new Dictionary<string, object>();
            // Auto preserves the provider's native codec; JPEG is the only explicit selector.
            if (outputFormat == ImageOutputFormat.Jpeg)
            {
                result["mimeType"] = "IMAGE_JPEG";
            }

            if (size == null)
                throw new ArgumentNullException(nameof(size));
            if (size.Kind == ImageSizeKind.Auto)
                return result;
            if (size.Kind == ImageSizeKind.Pixels)
                throw new NotSupportedException("Gemini does not support exact pixel sizes. Use ImageSize.Preset with a resolution and aspect ratio.");
            if (size.Kind != ImageSizeKind.Preset)
                throw new ArgumentOutOfRangeException(nameof(size), "Unknown image size kind.");
            if (size.Resolution != ImageResolution.Auto)
                result["imageSize"] = ToGoogleImageSize(size.Resolution);
            if (size.AspectRatio != ImageAspectRatio.Auto)
                result["aspectRatio"] = ToGoogleAspectRatio(size.AspectRatio);
            return result;
        }

        private static string ToGoogleImageSize(ImageResolution size)
        {
            if (GoogleImageResolutions.TryGetValue(size, out var value))
                return value;
            throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported Gemini image size.");
        }

        private static string ToGoogleAspectRatio(ImageAspectRatio ratio)
        {
            if (GoogleImageAspectRatios.TryGetValue(ratio, out var value))
                return value;
            throw new NotSupportedException($"Gemini does not support the {ratio} image aspect ratio.");
        }

        private async Task<HttpResponseMessage> SendGoogleImageRequestAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;
            var timeoutSeconds = policy?.TimeoutSeconds == FunctionCallingPolicy.Default.TimeoutSeconds
                ? FunctionCallingPolicy.Vision.TimeoutSeconds
                : policy?.TimeoutSeconds;

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutSeconds.HasValue)
                timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));

            try
            {
                return await HttpClient.SendAsync(request, timeoutSource.Token);
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested &&
                timeoutSource.IsCancellationRequested)
            {
                throw new AIServiceException(
                    $"Gemini image request timeout after {timeoutSeconds} seconds",
                    exception);
            }
        }

        private ImageGenerationResult ParseImageGenerationResponse(string responseContent, string model)
        {
            try
            {
                using var document = JsonDocument.Parse(responseContent);
                var root = document.RootElement;
                ValidateCompletedGeminiResponse(root);

                var images = new List<GeneratedImage>();
                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array)
                {
                    foreach (var candidate in candidates.EnumerateArray())
                    {
                        // The chat validator checks the first candidate, while image results
                        // include every candidate. Do not present an interrupted one as success.
                        if (!TryGetFinishReason(candidate, out var finishReason) ||
                            !string.Equals(finishReason, SuccessfulFinishReason, StringComparison.Ordinal))
                        {
                            throw CreateGeminiResponseException(
                                "Gemini image candidate did not complete successfully.",
                                string.IsNullOrEmpty(finishReason) ? "missing_finish_reason" : finishReason,
                                root);
                        }

                        if (!candidate.TryGetProperty("content", out var candidateContent) ||
                            !candidateContent.TryGetProperty("parts", out var parts) ||
                            parts.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var part in parts.EnumerateArray())
                        {
                            if (!part.TryGetProperty("inlineData", out var inlineData))
                                continue;
                            if (inlineData.ValueKind != JsonValueKind.Object ||
                                !inlineData.TryGetProperty("data", out var dataElement) ||
                                dataElement.ValueKind != JsonValueKind.String)
                            {
                                throw new AIServiceException("Gemini returned an image part without valid base64 data.");
                            }

                            var encodedData = dataElement.GetString();
                            if (string.IsNullOrWhiteSpace(encodedData))
                                throw new AIServiceException("Gemini returned an empty image part.");

                            if (!inlineData.TryGetProperty("mimeType", out var mimeType) ||
                                mimeType.ValueKind != JsonValueKind.String)
                                throw new AIServiceException("Gemini returned an image part without an image MIME type.");
                            var declaredMediaType = mimeType.GetString();
                            if (!MediaTypeHeaderValue.TryParse(declaredMediaType, out var parsedMediaType) ||
                                string.IsNullOrEmpty(parsedMediaType.MediaType) ||
                                !parsedMediaType.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                                parsedMediaType.MediaType.IndexOf('*') >= 0 ||
                                parsedMediaType.MediaType.Any(char.IsWhiteSpace) ||
                                !string.Equals(parsedMediaType.MediaType, declaredMediaType!.Split(';')[0].Trim(), StringComparison.OrdinalIgnoreCase))
                                throw new AIServiceException("Gemini returned an invalid or non-image MIME type.");

                            images.Add(new GeneratedImage
                            {
                                Data = Convert.FromBase64String(encodedData),
                                MediaType = parsedMediaType.MediaType
                            });
                        }
                    }
                }

                if (images.Count == 0)
                    throw new AIServiceException("Gemini image generation returned no image data.");

                return new ImageGenerationResult
                {
                    Images = images,
                    Provider = Provider,
                    Model = model,
                    RequestId = root.TryGetProperty("responseId", out var responseId) &&
                                responseId.ValueKind == JsonValueKind.String
                        ? responseId.GetString()
                        : null,
                    Usage = TryParseUsageMetadata(root)
                };
            }
            catch (AIServiceException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is FormatException ||
                exception is InvalidOperationException)
            {
                throw new AIServiceException("Failed to parse Gemini image response", exception);
            }
        }

        private void ValidateImageGenerationRequest(ImageGenerationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("An image prompt is required.", nameof(request));
            if (request.Count != GoogleImageMaxImages)
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "Gemini does not expose a guaranteed image-count request parameter; Count must be one.");
            if (!Enum.IsDefined(typeof(ImageQuality), request.Quality) ||
                !Enum.IsDefined(typeof(ImageBackground), request.Background) ||
                !Enum.IsDefined(typeof(ImageOutputFormat), request.OutputFormat))
                throw new ArgumentOutOfRangeException(nameof(request), "Unknown image option value.");
            if (!GoogleImageQualities.Contains(request.Quality))
                throw new NotSupportedException("Gemini image generation does not expose a quality request parameter.");
            if (!GoogleImageBackgrounds.Contains(request.Background))
                throw new NotSupportedException("Gemini image generation does not expose a background request parameter.");
            if (request.OutputCompression.HasValue)
                throw new NotSupportedException("Gemini image generation does not expose output compression.");
            if (!GoogleImageOutputFormats.Contains(request.OutputFormat))
            {
                throw new NotSupportedException(
                    "Gemini supports ImageOutputFormat.Jpeg or ImageOutputFormat.Auto for its native output format.");
            }

            var size = request.Size ?? throw new ArgumentNullException(nameof(request.Size));
            var capabilities = GetImageCapabilities(request.Model);
            // Custom model IDs keep their existing pass-through behavior. The wire
            // formatter still validates provider-wide size kinds and enum mappings.
            if (capabilities.Generation != CapabilitySupport.Supported)
                return;
            if (!capabilities.SizeKinds.Contains(size.Kind))
                throw new NotSupportedException("Gemini does not support exact pixel sizes. Use ImageSize.Preset with a resolution and aspect ratio.");
            if (size.Kind != ImageSizeKind.Preset)
                return;
            if (!capabilities.Resolutions.Contains(size.Resolution))
                throw new NotSupportedException($"Gemini image model '{capabilities.Model}' does not support resolution '{size.Resolution}'.");
            if (!capabilities.AspectRatios.Contains(size.AspectRatio))
                throw new NotSupportedException($"Gemini image model '{capabilities.Model}' does not support aspect ratio '{size.AspectRatio}'.");
        }
    }
}
