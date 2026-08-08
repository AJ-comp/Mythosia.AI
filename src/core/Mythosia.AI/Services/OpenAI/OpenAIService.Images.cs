using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Images;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.OpenAI
{
    public partial class OpenAIService
    {
        /// <inheritdoc />
        public string DefaultImageModel => AIModels.OpenAI.GptImage2;

        #region Image Generation

        /// <inheritdoc />
        public async Task<ImageGenerationResult> GenerateImagesAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateGenerationRequest(request);

            var model = ResolveImageModel(request.Model);
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = model,
                ["prompt"] = request.Prompt,
                ["n"] = request.Count,
                ["size"] = request.Size,
                ["quality"] = request.Quality,
                ["output_format"] = request.OutputFormat,
                ["background"] = request.Background
            };

            if (request.OutputCompression.HasValue)
            {
                requestBody["output_compression"] = request.OutputCompression.Value;
            }

            using var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");
            using var httpRequest = CreateImageRequest(HttpMethod.Post, "images/generations", content);
            using var response = await SendImageRequestAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync();

            EnsureImageRequestSucceeded(response, responseContent, "generation");
            return ParseImageResponse(response, responseContent, model, request.OutputFormat);
        }

        /// <inheritdoc />
        public async Task<ImageGenerationResult> EditImagesAsync(
            ImageEditRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateEditRequest(request);

            var model = ResolveImageModel(request.Model);
            using var form = new MultipartFormDataContent();

            AddFormField(form, "model", model);
            AddFormField(form, "prompt", request.Prompt);
            AddFormField(form, "n", request.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddFormField(form, "size", request.Size);
            AddFormField(form, "quality", request.Quality);
            AddFormField(form, "output_format", request.OutputFormat);
            AddFormField(form, "background", request.Background);

            if (request.OutputCompression.HasValue)
            {
                AddFormField(
                    form,
                    "output_compression",
                    request.OutputCompression.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            foreach (var inputImage in request.InputImages)
            {
                AddImagePart(form, inputImage, "image[]");
            }

            if (request.Mask != null)
            {
                AddImagePart(form, request.Mask, "mask");
            }

            using var httpRequest = CreateImageRequest(HttpMethod.Post, "images/edits", form);
            using var response = await SendImageRequestAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync();

            EnsureImageRequestSucceeded(response, responseContent, "editing");
            return ParseImageResponse(response, responseContent, model, request.OutputFormat);
        }

        private HttpRequestMessage CreateImageRequest(HttpMethod method, string path, HttpContent content)
        {
            var request = new HttpRequestMessage(method, path)
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            return request;
        }

        private async Task<HttpResponseMessage> SendImageRequestAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var policy = CurrentPolicy ?? DefaultPolicy;
            CurrentPolicy = null;

            var timeoutSeconds = ResolveImageRequestTimeoutSeconds(policy);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutSeconds.HasValue)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));
            }

            try
            {
                return await HttpClient.SendAsync(request, cts.Token);
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested &&
                cts.IsCancellationRequested)
            {
                throw new AIServiceException(
                    $"Image request timeout after {timeoutSeconds} seconds",
                    exception);
            }
        }

        private static int? ResolveImageRequestTimeoutSeconds(FunctionCallingPolicy policy)
        {
            var seconds = policy?.TimeoutSeconds;
            return seconds == FunctionCallingPolicy.Default.TimeoutSeconds
                ? FunctionCallingPolicy.Vision.TimeoutSeconds
                : seconds;
        }

        private static void AddFormField(MultipartFormDataContent form, string name, string value)
        {
            form.Add(new StringContent(value, Encoding.UTF8), name);
        }

        private static void AddImagePart(
            MultipartFormDataContent form,
            ImageInput image,
            string fieldName)
        {
            var imageContent = new ByteArrayContent(image.Data);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(image.MediaType);
            form.Add(imageContent, fieldName, image.FileName);
        }

        private string ResolveImageModel(string? model)
        {
            return string.IsNullOrWhiteSpace(model) ? DefaultImageModel : model;
        }

        private static void ValidateGenerationRequest(ImageGenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                throw new ArgumentException("An image prompt is required.", nameof(request));
            }

            if (request.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Image count must be at least one.");
            }
        }

        private static void ValidateEditRequest(ImageEditRequest request)
        {
            ValidateGenerationRequest(request);

            if (request.InputImages == null || request.InputImages.Count == 0)
            {
                throw new ArgumentException("At least one input image is required.", nameof(request));
            }

            for (var index = 0; index < request.InputImages.Count; index++)
            {
                if (request.InputImages[index] == null)
                {
                    throw new ArgumentException("Input images cannot contain null values.", nameof(request));
                }
            }
        }

        private static void EnsureImageRequestSucceeded(
            HttpResponseMessage response,
            string responseContent,
            string operation)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var exception = AIHttpErrorFactory.FromHttp(
                (int)response.StatusCode,
                response.ReasonPhrase,
                responseContent,
                $"Image {operation} failed");
            var requestId = GetRequestId(response);
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                exception.Data["x-request-id"] = requestId;
            }

            throw exception;
        }

        private ImageGenerationResult ParseImageResponse(
            HttpResponseMessage response,
            string responseContent,
            string model,
            string requestedOutputFormat)
        {
            try
            {
                using var document = JsonDocument.Parse(responseContent);
                var root = document.RootElement;

                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    throw new AIServiceException("Image generation response did not contain an image array");
                }

                var outputFormat = GetOptionalString(root, "output_format") ?? requestedOutputFormat;
                var mediaType = GetImageMediaType(outputFormat);
                var images = new List<GeneratedImage>();

                foreach (var item in data.EnumerateArray())
                {
                    var encodedData = GetOptionalString(item, "b64_json");
                    var url = GetOptionalString(item, "url");

                    if (string.IsNullOrWhiteSpace(encodedData) && string.IsNullOrWhiteSpace(url))
                    {
                        throw new AIServiceException("Image generation returned an empty image result");
                    }

                    var bytes = string.IsNullOrWhiteSpace(encodedData)
                        ? Array.Empty<byte>()
                        : Convert.FromBase64String(encodedData);

                    images.Add(new GeneratedImage
                    {
                        Data = bytes,
                        MediaType = mediaType,
                        Url = url,
                        RevisedPrompt = GetOptionalString(item, "revised_prompt")
                    });
                }

                if (images.Count == 0)
                {
                    throw new AIServiceException("Image generation returned no images");
                }

                return new ImageGenerationResult
                {
                    Images = images,
                    Provider = Provider,
                    Model = model,
                    RequestId = GetRequestId(response),
                    Usage = ParseTokenUsage(root)
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
                throw new AIServiceException("Failed to parse the image generation response", exception);
            }
        }

        private static string? GetOptionalString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return property.GetString();
        }

        private static TokenUsage? ParseTokenUsage(JsonElement root)
        {
            if (!root.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new TokenUsage
            {
                InputTokens = GetOptionalInt32(usage, "input_tokens"),
                OutputTokens = GetOptionalInt32(usage, "output_tokens"),
                TotalTokens = GetOptionalInt32(usage, "total_tokens")
            };
        }

        private static int GetOptionalInt32(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.Number &&
                   property.TryGetInt32(out var value)
                ? value
                : 0;
        }

        private static string? GetRequestId(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("x-request-id", out var values))
            {
                return null;
            }

            foreach (var value in values)
            {
                return value;
            }

            return null;
        }

        private static string GetImageMediaType(string? outputFormat)
        {
            switch (outputFormat?.Trim().ToLowerInvariant())
            {
                case "png":
                    return "image/png";
                case "jpeg":
                case "jpg":
                    return "image/jpeg";
                case "webp":
                    return "image/webp";
                default:
                    return "application/octet-stream";
            }
        }

        #endregion
    }
}
