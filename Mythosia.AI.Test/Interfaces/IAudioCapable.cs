namespace Mythosia.AI.Tests.Interfaces;

public interface IAudioCapable
{
    Task<byte[]> GetSpeechAsync(string text, string voice = "alloy", string model = "tts-1");
    Task<string> TranscribeAudioAsync(byte[] audioData, string fileName, string? language = null);
}