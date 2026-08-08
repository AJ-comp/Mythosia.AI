using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        /// <inheritdoc />
        public string DefaultImageModel => AIModels.Google.Images.Gemini3_1FlashImage;

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
            string size,
            string outputFormat)
        {
            var result = new Dictionary<string, object>();
            // The current GenerateContent ImageResponseFormat exposes an explicit JPEG selector
            // but no PNG enum. png/auto therefore leave mimeType unspecified and the response's
            // inlineData.mimeType remains authoritative for the bytes actually returned.
            if (string.Equals(outputFormat, "jpeg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outputFormat, "jpg", StringComparison.OrdinalIgnoreCase))
            {
                result["mimeType"] = "IMAGE_JPEG";
            }

            if (string.IsNullOrWhiteSpace(size) ||
                string.Equals(size, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            var normalized = size.Trim().ToUpperInvariant();
            if (normalized == "512" || normalized == "1K" || normalized == "2K" || normalized == "4K")
            {
                result["imageSize"] = ToGoogleImageSize(normalized);
                return result;
            }

            var dimensions = normalized.Split('X');
            if (dimensions.Length != 2 ||
                !int.TryParse(dimensions[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) ||
                !int.TryParse(dimensions[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) ||
                width <= 0 || height <= 0)
            {
                throw new ArgumentException(
                    "Gemini image size must be auto, 512, 1K, 2K, 4K, or WIDTHxHEIGHT.",
                    nameof(size));
            }

            var divisor = GreatestCommonDivisor(width, height);
            result["aspectRatio"] = ToGoogleAspectRatio(width / divisor, height / divisor, size);
            var longestEdge = Math.Max(width, height);
            result["imageSize"] = ToGoogleImageSize(longestEdge <= 512
                ? "512"
                : longestEdge <= 1024
                    ? "1K"
                    : longestEdge <= 2048
                        ? "2K"
                        : "4K");
            return result;
        }

        private static string ToGoogleImageSize(string size)
        {
            return size switch
            {
                "512" => "IMAGE_SIZE_FIVE_TWELVE",
                "1K" => "IMAGE_SIZE_ONE_K",
                "2K" => "IMAGE_SIZE_TWO_K",
                "4K" => "IMAGE_SIZE_FOUR_K",
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported Gemini image size.")
            };
        }

        private static string ToGoogleAspectRatio(int width, int height, string size)
        {
            var ratio = $"{width}:{height}";
            return ratio switch
            {
                "1:1" => "ASPECT_RATIO_ONE_BY_ONE",
                "2:3" => "ASPECT_RATIO_TWO_BY_THREE",
                "3:2" => "ASPECT_RATIO_THREE_BY_TWO",
                "3:4" => "ASPECT_RATIO_THREE_BY_FOUR",
                "4:3" => "ASPECT_RATIO_FOUR_BY_THREE",
                "4:5" => "ASPECT_RATIO_FOUR_BY_FIVE",
                "5:4" => "ASPECT_RATIO_FIVE_BY_FOUR",
                "9:16" => "ASPECT_RATIO_NINE_BY_SIXTEEN",
                "16:9" => "ASPECT_RATIO_SIXTEEN_BY_NINE",
                "21:9" => "ASPECT_RATIO_TWENTY_ONE_BY_NINE",
                "1:8" => "ASPECT_RATIO_ONE_BY_EIGHT",
                "8:1" => "ASPECT_RATIO_EIGHT_BY_ONE",
                "1:4" => "ASPECT_RATIO_ONE_BY_FOUR",
                "4:1" => "ASPECT_RATIO_FOUR_BY_ONE",
                _ => throw new ArgumentException(
                    $"Gemini does not support the {ratio} image aspect ratio.",
                    nameof(size))
            };
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                var remainder = left % right;
                left = right;
                right = remainder;
            }
            return Math.Abs(left);
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
                        if (!candidate.TryGetProperty("content", out var candidateContent) ||
                            !candidateContent.TryGetProperty("parts", out var parts) ||
                            parts.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var part in parts.EnumerateArray())
                        {
                            if (!part.TryGetProperty("inlineData", out var inlineData) ||
                                !inlineData.TryGetProperty("data", out var dataElement) ||
                                dataElement.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var encodedData = dataElement.GetString();
                            if (string.IsNullOrWhiteSpace(encodedData))
                                continue;

                            images.Add(new GeneratedImage
                            {
                                Data = Convert.FromBase64String(encodedData),
                                MediaType = inlineData.TryGetProperty("mimeType", out var mimeType) &&
                                            mimeType.ValueKind == JsonValueKind.String
                                    ? mimeType.GetString() ?? "image/png"
                                    : "image/png"
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

        private static void ValidateImageGenerationRequest(ImageGenerationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("An image prompt is required.", nameof(request));
            if (request.Count != 1)
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "Gemini does not expose a guaranteed image-count request parameter; Count must be one.");
            if (!string.Equals(request.Quality, "auto", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Gemini image generation does not expose a quality request parameter.");
            if (!string.Equals(request.Background, "auto", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Gemini image generation does not expose a background request parameter.");
            if (request.OutputCompression.HasValue)
                throw new NotSupportedException("Gemini image generation does not expose output compression.");
            if (!string.Equals(request.OutputFormat, "png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.OutputFormat, "auto", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.OutputFormat, "jpeg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.OutputFormat, "jpg", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "Gemini image generation accepts jpeg/jpg, or png/auto as a provider-selected output preference.");
            }
        }
    }
}
