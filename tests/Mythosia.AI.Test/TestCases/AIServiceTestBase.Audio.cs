using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests;

public abstract partial class AIServiceTestBase
{
    /// <summary>
    /// 오디오 기능 테스트 (지원하는 서비스만)
    /// </summary>
    [TestCategory("Common")]
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
                    // Text-to-Speech
                    var audioData = await gptService.GetSpeechAsync(
                        "Hello, this is a test of the speech synthesis.",
                        voice: "alloy",
                        model: "tts-1"
                    );

                    Assert.IsNotNull(audioData);
                    Assert.IsTrue(audioData.Length > 0);
                    Console.WriteLine($"[TTS] Generated {audioData.Length} bytes");

                    // Speech-to-Text
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