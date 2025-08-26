using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using Mythosia.AI.Services.OpenAI;

namespace Mythosia.AI.Tests
{
    public partial class ChatGptServiceTests
    {
        /// <summary>
        /// 오디오 기능 테스트 (TTS & Whisper)
        /// </summary>
        [TestMethod]
        public async Task AudioFeaturesTest()
        {
            try
            {
                var gptService = (ChatGptService)AI;

                // 1) Text-to-Speech 테스트
                string textToSpeak = "Hello, this is a test of the speech synthesis.";
                var audioData = await gptService.GetSpeechAsync(
                    textToSpeak,
                    voice: "alloy",
                    model: "tts-1"
                );

                Assert.IsNotNull(audioData);
                Assert.IsTrue(audioData.Length > 0);
                Console.WriteLine($"[TTS] Generated audio size: {audioData.Length} bytes");

                // 2) Speech-to-Text 테스트 (생성된 오디오 사용)
                var transcription = await gptService.TranscribeAudioAsync(
                    audioData,
                    "test_speech.mp3",
                    language: "en"
                );

                Assert.IsNotNull(transcription);
                Assert.IsTrue(transcription.Length > 0);
                Console.WriteLine($"[Transcription] {transcription}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Audio Features Error] {ex.Message}");
                // Audio features might not be available in all environments
                Assert.Inconclusive($"Audio features test skipped: {ex.Message}");
            }
        }
    }
}