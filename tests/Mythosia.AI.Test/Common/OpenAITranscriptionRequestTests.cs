using Mythosia.AI.Exceptions;
using Mythosia.AI.Services.OpenAI;
using System.Net;
using System.Text;

namespace Mythosia.AI.Tests.Common;

[TestClass]
[TestCategory("Unit")]
public class OpenAITranscriptionRequestTests
{
    [TestMethod]
    [DataRow(null, 0)]
    [DataRow("", 0)]
    [DataRow("en", 1)]
    [DataRow("ko", 1)]
    public async Task TranscribeAudioAsync_UsesGptTranscribeMultipartContract(
        string? language,
        int expectedLanguageCount)
    {
        var handler = new CaptureHandler("""{"text":"hello","languages":[{"code":"en"}]}""");
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-test-key", client);
        byte[] audioData = [73, 68, 51, 1, 2, 3];

        var text = await service.TranscribeAudioAsync(audioData, "recording.mp3", language: language);

        Assert.AreEqual("hello", text);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("/v1/audio/transcriptions", handler.Uri!.AbsolutePath);
        Assert.AreEqual("multipart/form-data", handler.MediaType);
        Assert.AreEqual("gpt-transcribe", GetFormValue(handler, "model"));
        Assert.AreEqual("json", GetFormValue(handler, "response_format"));
        Assert.IsFalse(handler.Parts.Any(part => part.Name == "language"),
            "GPT-Transcribe uses languages[] instead of Whisper's singular language field.");

        var languages = handler.Parts.Where(part => part.Name == "languages[]").ToArray();
        Assert.AreEqual(expectedLanguageCount, languages.Length);
        if (expectedLanguageCount != 0)
        {
            Assert.AreEqual(language, Encoding.UTF8.GetString(languages[0].Data));
            Assert.IsNull(languages[0].FileName);
        }

        var file = handler.Parts.Single(part => part.Name == "file");
        Assert.AreEqual("recording.mp3", file.FileName);
        CollectionAssert.AreEqual(audioData, file.Data);
        Assert.AreEqual(3 + expectedLanguageCount, handler.Parts.Count);
    }

    [TestMethod]
    [DataRow("""{"text":"Bonjour","languages":[{"code":"fr"}]}""", "Bonjour")]
    [DataRow("""{"text":"uncertain language","languages":[]}""", "uncertain language")]
    [DataRow("""{"text":"text only"}""", "text only")]
    public async Task TranscribeAudioAsync_PreservesTextReturnWhenLanguageMetadataVaries(
        string responseBody,
        string expectedText)
    {
        var handler = new CaptureHandler(responseBody);
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-test-key", client);

        var text = await service.TranscribeAudioAsync([1, 2, 3], "recording.mp3");

        Assert.AreEqual(expectedText, text);
    }

    [TestMethod]
    public async Task TranscribeAudioAsync_ProviderFailurePreservesErrorBody()
    {
        const string errorBody = """{"error":{"message":"Invalid language code","type":"invalid_request_error"}}""";
        var handler = new CaptureHandler(errorBody, HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var service = new OpenAIService("offline-test-key", client);

        var exception = await Assert.ThrowsExactlyAsync<AIServiceException>(() =>
            service.TranscribeAudioAsync([1, 2, 3], "recording.mp3", language: "invalid"));

        StringAssert.Contains(exception.Message, "Audio transcription failed (400)");
        Assert.AreEqual(errorBody, exception.ErrorDetails);
        Assert.AreEqual("invalid", GetFormValue(handler, "languages[]"));
    }

    private static string GetFormValue(CaptureHandler handler, string name)
    {
        var part = handler.Parts.Single(part => part.Name == name);
        Assert.IsNull(part.FileName);
        return Encoding.UTF8.GetString(part.Data);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public CaptureHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        public int RequestCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? MediaType { get; private set; }
        public List<CapturedPart> Parts { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            Uri = request.RequestUri;
            MediaType = request.Content?.Headers.ContentType?.MediaType;
            Assert.IsInstanceOfType<MultipartFormDataContent>(request.Content);

            // Capture the upload before the service disposes the multipart content.
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var disposition = part.Headers.ContentDisposition!;
                Parts.Add(new CapturedPart(
                    disposition.Name!.Trim('"'),
                    disposition.FileName?.Trim('"'),
                    await part.ReadAsByteArrayAsync(cancellationToken)));
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedPart(string Name, string? FileName, byte[] Data);
}
