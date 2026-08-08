using Mythosia.AI.Models.Images;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services
{
    /// <summary>
    /// Optional contract for AI services that can generate or edit images.
    /// </summary>
    public interface IImageGenerationService
    {
        /// <summary>
        /// The image model used when a request does not specify one explicitly.
        /// This is independent from the chat model exposed by <see cref="IAIService.Model"/>.
        /// </summary>
        string DefaultImageModel { get; }

        /// <summary>
        /// Generates one or more images from a text prompt.
        /// </summary>
        Task<ImageGenerationResult> GenerateImagesAsync(
            ImageGenerationRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates one or more images using existing images as references.
        /// </summary>
        Task<ImageGenerationResult> EditImagesAsync(
            ImageEditRequest request,
            CancellationToken cancellationToken = default);
    }
}
