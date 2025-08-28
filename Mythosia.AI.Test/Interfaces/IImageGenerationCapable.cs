namespace Mythosia.AI.Tests.Interfaces;

public interface IImageGenerationCapable
{
    Task<byte[]> GenerateImageAsync(string prompt, string size = "1024x1024");
    Task<string> GenerateImageUrlAsync(string prompt, string size = "1024x1024");
}