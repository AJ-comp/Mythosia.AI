using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Capabilities;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.xAI
{
    public partial class XAIService
    {
        private const int GrokImageMaxImages = 10;
        private const int GrokImageMaxInputImages = 5;
        private static readonly ImageBackground[] GrokImageBackgrounds = { ImageBackground.Auto };
        private static readonly ImageOutputFormat[] GrokImageOutputFormats = { ImageOutputFormat.Auto };
        private static readonly ImageSizeKind[] GrokImageSizeKinds = { ImageSizeKind.Auto, ImageSizeKind.Preset };
        private static readonly Dictionary<ImageQuality, string> GrokImageQualities = new Dictionary<ImageQuality, string>
        {
            [ImageQuality.Auto] = "auto",
            [ImageQuality.Low] = "low",
            [ImageQuality.Medium] = "medium"
        };
        private static readonly Dictionary<ImageResolution, string> GrokImageResolutions = new Dictionary<ImageResolution, string>
        {
            [ImageResolution.OneK] = "1k",
            [ImageResolution.TwoK] = "2k"
        };
        private static readonly Dictionary<ImageAspectRatio, string> GrokImageAspectRatios = new Dictionary<ImageAspectRatio, string>
        {
            [ImageAspectRatio.OneByOne] = "1:1",
            [ImageAspectRatio.ThreeByFour] = "3:4",
            [ImageAspectRatio.FourByThree] = "4:3",
            [ImageAspectRatio.NineBySixteen] = "9:16",
            [ImageAspectRatio.SixteenByNine] = "16:9",
            [ImageAspectRatio.TwoByThree] = "2:3",
            [ImageAspectRatio.ThreeByTwo] = "3:2",
            [ImageAspectRatio.NineByNineteenPointFive] = "9:19.5",
            [ImageAspectRatio.NineteenPointFiveByNine] = "19.5:9",
            [ImageAspectRatio.NineByTwenty] = "9:20",
            [ImageAspectRatio.TwentyByNine] = "20:9",
            [ImageAspectRatio.OneByTwo] = "1:2",
            [ImageAspectRatio.TwoByOne] = "2:1",
            [ImageAspectRatio.TwentyOneByNine] = "21:9",
            [ImageAspectRatio.FiveByTwo] = "5:2"
        };

        /// <inheritdoc />
        public string DefaultImageModel => AIModels.xAI.GrokImagineImage2_0;

        /// <inheritdoc />
        public override ImageModelCapabilities GetImageCapabilities(string? model = null)
        {
            var selectedModel = string.IsNullOrWhiteSpace(model) ? DefaultImageModel : model!;
            if (!string.Equals(selectedModel, AIModels.xAI.GrokImagineImage2_0, StringComparison.OrdinalIgnoreCase))
                return new ImageModelCapabilities(
                    provider: Provider, model: selectedModel,
                    generation: CapabilitySupport.Unknown, editing: CapabilitySupport.Unknown,
                    mask: CapabilitySupport.Unsupported,
                    maxImages: GrokImageMaxImages, maxInputImages: GrokImageMaxInputImages);

            return new ImageModelCapabilities(
                provider: Provider,
                model: selectedModel,
                generation: CapabilitySupport.Supported,
                editing: CapabilitySupport.Supported,
                mask: CapabilitySupport.Unsupported,
                qualities: GrokImageQualities.Keys,
                backgrounds: GrokImageBackgrounds,
                outputFormats: GrokImageOutputFormats,
                sizeKinds: GrokImageSizeKinds,
                resolutions: new[] { ImageResolution.Auto }.Concat(GrokImageResolutions.Keys),
                aspectRatios: new[] { ImageAspectRatio.Auto }.Concat(GrokImageAspectRatios.Keys),
                maxImages: GrokImageMaxImages,
                maxInputImages: GrokImageMaxInputImages);
        }

        /// <inheritdoc />
        public Task<ImageGenerationResult> GenerateImagesAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            var body = BuildImageRequest(request);
            return SendImageRequestAsync("images/generations", body, cancellationToken);
        }

        /// <inheritdoc />
        public Task<ImageGenerationResult> EditImagesAsync(
            ImageEditRequest request,
            CancellationToken cancellationToken = default)
        {
            var body = BuildImageRequest(request);
            if (request.Mask != null)
                throw new NotSupportedException("Grok Imagine image editing does not support masks.");
            if (request.InputImages == null || request.InputImages.Count < 1 || request.InputImages.Count > GrokImageMaxInputImages)
                throw new ArgumentException("Grok Imagine editing requires between one and five input images.", nameof(request));

            var images = new List<Dictionary<string, object>>();
            foreach (var image in request.InputImages)
            {
                if (image == null || image.Data.Length == 0)
                    throw new ArgumentException("Input images must contain nonempty image data.", nameof(request));
                var mediaType = NormalizeImageMediaType(image.MediaType);
                if (mediaType == null)
                    throw new NotSupportedException("Grok Imagine input images must use JPEG, PNG, or WebP media types.");

                images.Add(new Dictionary<string, object>
                {
                    ["type"] = "image_url",
                    ["url"] = $"data:{mediaType};base64,{Convert.ToBase64String(image.Data)}"
                });
            }

            // The REST API accepts either one image or an ordered images array, never both.
            if (images.Count == 1)
                body["image"] = images[0];
            else
                body["images"] = images;
            return SendImageRequestAsync("images/edits", body, cancellationToken);
        }

        private Dictionary<string, object> BuildImageRequest(ImageGenerationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("An image prompt is required.", nameof(request));
            if (request.Count < 1 || request.Count > GrokImageMaxImages)
                throw new ArgumentOutOfRangeException(nameof(request), "Grok Imagine image count must be between one and ten.");

            var model = string.IsNullOrWhiteSpace(request.Model) ? DefaultImageModel : request.Model!;
            if (!Enum.IsDefined(typeof(ImageQuality), request.Quality) ||
                !Enum.IsDefined(typeof(ImageBackground), request.Background) ||
                !Enum.IsDefined(typeof(ImageOutputFormat), request.OutputFormat))
                throw new ArgumentOutOfRangeException(nameof(request), "Unknown image option value.");
            if (!GrokImageOutputFormats.Contains(request.OutputFormat))
                throw new NotSupportedException("Grok Imagine does not offer output codec selection. Use ImageOutputFormat.Auto to accept its native format.");
            if (!GrokImageBackgrounds.Contains(request.Background))
                throw new NotSupportedException("Grok Imagine does not support explicit background options. Use ImageBackground.Auto.");
            if (request.OutputCompression.HasValue)
                throw new NotSupportedException("Grok Imagine does not support output compression options.");
            if (!GrokImageQualities.TryGetValue(request.Quality, out var quality))
                throw new NotSupportedException("Grok Imagine quality must be Auto, Low, or Medium.");

            var body = new Dictionary<string, object>
            {
                ["model"] = model,
                ["prompt"] = request.Prompt,
                ["n"] = request.Count,
                ["quality"] = quality,
                ["response_format"] = "b64_json"
            };
            ApplyImageDimensions(body, request.Size);
            return body;
        }

        private static void ApplyImageDimensions(Dictionary<string, object> body, ImageSize size)
        {
            if (size == null)
                throw new ArgumentNullException(nameof(size));
            if (size.Kind == ImageSizeKind.Auto)
                return;
            if (size.Kind == ImageSizeKind.Pixels)
                throw new NotSupportedException("Grok Imagine does not support exact pixel sizes. Use ImageSize.Preset with a resolution and aspect ratio.");
            if (size.Kind != ImageSizeKind.Preset)
                throw new ArgumentOutOfRangeException(nameof(size), "Unknown image size kind.");
            if (size.Resolution != ImageResolution.Auto)
            {
                if (!GrokImageResolutions.TryGetValue(size.Resolution, out var resolution))
                    throw new NotSupportedException("Grok Imagine supports Auto, OneK, or TwoK resolution presets.");
                body["resolution"] = resolution;
            }
            if (size.AspectRatio != ImageAspectRatio.Auto)
                body["aspect_ratio"] = ToGrokImageAspectRatio(size.AspectRatio);
        }

        private static string ToGrokImageAspectRatio(ImageAspectRatio ratio)
        {
            if (GrokImageAspectRatios.TryGetValue(ratio, out var value))
                return value;
            throw new NotSupportedException($"Grok Imagine does not support the {ratio} image aspect ratio.");
        }

        private async Task<ImageGenerationResult> SendImageRequestAsync(
            string path,
            Dictionary<string, object> body,
            CancellationToken cancellationToken)
        {
            var policy = CurrentPolicy ?? DefaultPolicy ?? FunctionCallingPolicy.Default;
            CurrentPolicy = null;
            var timeoutSeconds = policy.TimeoutSeconds == FunctionCallingPolicy.Default.TimeoutSeconds
                ? FunctionCallingPolicy.Vision.TimeoutSeconds
                : policy.TimeoutSeconds;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutSeconds.HasValue)
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));

            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            try
            {
                // Buffering keeps the policy cancellation active until the complete image body arrives.
                using var response = await HttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    if (!response.IsSuccessStatusCode)
                        throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase,
                            responseContent, "xAI image request failed", includeErrorBodyInMessage: true);
                    return ParseImageResponse(responseContent, (string)body["model"], GetImageRequestId(response));
                }
                catch (AIServiceException exception)
                {
                    var requestId = GetImageRequestId(response);
                    if (!string.IsNullOrWhiteSpace(requestId))
                        exception.Data["x-request-id"] = requestId;
                    throw;
                }
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && cts.IsCancellationRequested)
            {
                throw new AIServiceException($"Image request timeout after {timeoutSeconds} seconds", exception);
            }
        }

        private ImageGenerationResult ParseImageResponse(string json, string model, string? requestId)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    throw new AIServiceException("xAI image response did not contain an image array.");
                var images = new List<GeneratedImage>();
                foreach (var item in data.EnumerateArray())
                {
                    var encoded = GetImageString(item, "b64_json");
                    if (string.IsNullOrWhiteSpace(encoded))
                        throw new AIServiceException("xAI did not return the requested base64 image data. URL-only image results are not supported.");
                    var bytes = Convert.FromBase64String(encoded);
                    var detectedMediaType = DetectImageMediaType(bytes);
                    if (detectedMediaType == null)
                        throw new AIServiceException("xAI returned empty or unrecognized image data; expected JPEG, PNG, or WebP.");
                    var declaredMediaType = GetImageString(item, "mime_type");
                    if (declaredMediaType != null && NormalizeImageMediaType(declaredMediaType) != detectedMediaType)
                        throw new AIServiceException("xAI image MIME metadata does not match the returned image bytes.");
                    images.Add(new GeneratedImage
                    {
                        Data = bytes,
                        MediaType = detectedMediaType,
                        Url = GetImageString(item, "url"),
                        RevisedPrompt = GetImageString(item, "revised_prompt")
                    });
                }
                if (images.Count == 0)
                    throw new AIServiceException("xAI image response contained no images.");
                return new ImageGenerationResult
                {
                    Images = images,
                    Provider = Provider,
                    Model = GetImageString(root, "model") ?? model,
                    RequestId = requestId,
                    Usage = ParseImageUsage(root)
                };
            }
            catch (AIServiceException) { throw; }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidOperationException)
            {
                throw new AIServiceException("Failed to parse the xAI image response.", exception);
            }
        }

        private static string? GetImageString(JsonElement element, string name)
            => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() : null;

        private static int? GetImageTokenCount(JsonElement element, string name)
            => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
                ? value : (int?)null;

        private static TokenUsage? ParseImageUsage(JsonElement root)
        {
            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return null;
            var input = GetImageTokenCount(usage, "input_tokens");
            var output = GetImageTokenCount(usage, "output_tokens");
            var total = GetImageTokenCount(usage, "total_tokens");
            // Cost-only usage is not a token count and must not manufacture zero-token usage.
            if (!input.HasValue && !output.HasValue && !total.HasValue)
                return null;
            return new TokenUsage
            {
                InputTokens = input ?? 0,
                OutputTokens = output ?? 0,
                TotalTokens = total ?? 0,
                CachedInputTokens = usage.TryGetProperty("input_tokens_details", out var inputDetails) && inputDetails.ValueKind == JsonValueKind.Object
                    ? GetImageTokenCount(inputDetails, "cached_tokens") ?? 0 : 0,
                ReasoningTokens = usage.TryGetProperty("output_tokens_details", out var outputDetails) && outputDetails.ValueKind == JsonValueKind.Object
                    ? GetImageTokenCount(outputDetails, "reasoning_tokens") ?? 0 : 0
            };
        }

        private static string? NormalizeImageMediaType(string mediaType)
        {
            switch (mediaType.Trim().ToLowerInvariant())
            {
                case "image/jpeg":
                case "image/jpg": return "image/jpeg";
                case "image/png": return "image/png";
                case "image/webp": return "image/webp";
                default: return null;
            }
        }

        private static string? DetectImageMediaType(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
                return "image/jpeg";
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47 &&
                bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a && bytes[7] == 0x0a)
                return "image/png";
            if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
                bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
                return "image/webp";
            return null;
        }

        private static string? GetImageRequestId(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("x-request-id", out var values))
                foreach (var value in values)
                    return value;
            return null;
        }
    }
}
