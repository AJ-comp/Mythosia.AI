using Mythosia.AI.Services.OpenAI;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// 오디오 (TTS/STT) 테스트.
/// 원본: AIServiceTestBase.Audio.cs
/// </summary>
[TestClass]
public abstract class AudioTestModule : TestModuleBase
{
    [TestCategory("Audio")]
    [TestMethod]
    public async Task AudioFeaturesTest()
    {
        await RunIfSupported(
            () => SupportsAudio(),
            async () =>
            {
                if (AI is ChatGptService gptService)
                {
                    var audioData = await gptService.GetSpeechAsync(
                        "Hello, this is a test of the speech synthesis.",
                        voice: "alloy",
                        model: "tts-1"
                    );

                    Assert.IsNotNull(audioData);
                    Assert.IsTrue(audioData.Length > 0);
                    Console.WriteLine($"[TTS] Generated {audioData.Length} bytes");

                    var transcription = await gptService.TranscribeAudioAsync(
                        audioData,
                        "test_speech.mp3",
                        language: "en"
                    );

                    Assert.IsNotNull(transcription);
                    Assert.IsTrue(transcription.Length > 0);
                    Console.WriteLine($"[Transcription] {transcription}");
                }
            },
            "Audio Features"
        );
    }
}
